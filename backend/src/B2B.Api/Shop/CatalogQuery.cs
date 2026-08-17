using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Shop;

// Filtros del rail del catálogo (17-catalog-catalog.png). Las facetas de atributo
// no están enumeradas en el código: llegan como "a.{Atributo}={valor,valor}", así
// que si BC publica un atributo nuevo aparece solo en la barra lateral.
public sealed record CatalogQuery
{
    public const int MaxTake = 500;

    public string? Search { get; init; }
    public string? Family { get; init; }
    public IReadOnlySet<string> Availability { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Attributes { get; init; } =
        new Dictionary<string, IReadOnlySet<string>>();
    public string Sort { get; init; } = CatalogSort.Featured;
    public string? Window { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = MaxTake;

    public static CatalogQuery From(IQueryCollection query)
    {
        var attributes = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in query)
        {
            if (!key.StartsWith("a.", StringComparison.Ordinal) || key.Length == 2) continue;
            var values = Split(value.ToString());
            if (values.Count > 0) attributes[key[2..]] = values;
        }

        return new CatalogQuery
        {
            Search = Trimmed(query["q"]),
            Family = Trimmed(query["family"]),
            Availability = Split(query["availability"].ToString()),
            Attributes = attributes,
            Sort = CatalogSort.Normalize(query["sort"]),
            Window = Trimmed(query["window"]),
            Skip = Math.Max(0, Int(query["skip"], 0)),
            Take = Math.Clamp(Int(query["take"], MaxTake), 1, MaxTake)
        };
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int Int(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static HashSet<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

public static class CatalogSort
{
    public const string Featured = "featured";      // Destacados
    public const string Relevance = "relevance";    // Relevancia (vista sin distracciones)
    public const string PriceAsc = "price-asc";
    public const string PriceDesc = "price-desc";
    public const string Name = "name";

    public static string Normalize(string? value) => value switch
    {
        Relevance or PriceAsc or PriceDesc or Name => value,
        _ => Featured
    };
}

public static class Availability
{
    public const string Available = "available";   // Disponible
    public const string Consult = "consult";       // Consultar
    public const string Low = "low";               // < 10u

    /// Sentinela del conector (contrato 03 §7.7): 10000 = ventana programada abierta
    public const decimal Infinite = 10000m;
    public const decimal LowThreshold = 10m;

    public static readonly string[] All = [Available, Consult, Low];
}

/// Una talla del listado: stock por ventana y su precio ya resuelto
public sealed record CatalogVariant(
    CatalogProduct Product,
    IReadOnlyDictionary<string, decimal> Stock,
    decimal? Pvd,
    decimal? Pvp);

/// Una fila del listado, con todo lo que pintan la ficha y la matriz de tallas
public sealed record CatalogRow(
    CatalogModel Model,
    IReadOnlyList<string> Segments,
    IReadOnlyDictionary<string, string> Attributes,
    string? ImageUri,
    IReadOnlyList<CatalogVariant> Variants,
    decimal? Pvd,
    decimal? Pvp,
    string Currency,
    IReadOnlyList<string> Availability,
    bool PricePerSize);

public sealed record CatalogFacetValue(string Value, int Count);
public sealed record CatalogFamilyFacet(string Id, string Label, int Count);
public sealed record CatalogAttributeFacet(string Key, IReadOnlyList<CatalogFacetValue> Values);

public sealed record CatalogPage(
    IReadOnlyList<object> Windows,
    string? Window,
    IReadOnlyList<CatalogRow> Rows,
    int Total,
    IReadOnlyList<CatalogFamilyFacet> Families,
    IReadOnlyList<CatalogFacetValue> AvailabilityFacet,
    IReadOnlyList<CatalogAttributeFacet> AttributeFacets);

// Arma el catálogo comprable en una sola pasada: modelos, tallas, stock por ventana,
// tarifa del cliente y facetas. El volumen del catálogo (centenares de modelos) cabe
// de sobra en memoria, así que se resuelve todo aquí en vez de en cinco consultas.
public static class CatalogService
{
    // Orden de la barra lateral de la referencia; los atributos que BC añada después
    // se ordenan alfabéticamente detrás.
    private static readonly string[] PreferredFacets =
        ["grupo de edad", "silueta", "colección", "coleccion", "temporada"];

    public static async Task<CatalogPage> QueryAsync(
        AppDbContext db, PortalActorPrices prices, CatalogQuery query, DateTimeOffset now)
    {
        var models = await db.CatalogModels.Where(m => m.Active).ToListAsync();
        var products = await db.CatalogProducts.Where(p => p.Active && !p.IsCasePack).ToListAsync();
        var stocks = await db.StockLevels.ToListAsync();
        var offers = await db.Offers.ToListAsync();
        var windows = await db.ServiceWindows.ToListAsync();
        var imageDocs = await db.SyncDocuments.Where(d => d.EntityType == "model-image").ToListAsync();

        var window = PickWindow(windows, query.Window);
        var context = new PriceContext(prices.ClientId, prices.GroupIds, window?.OrderType);

        var stockByProduct = stocks
            .GroupBy(s => s.ProductExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                IReadOnlyDictionary<string, decimal> (g) => g
                    .GroupBy(s => s.ServiceWindowKey)
                    .ToDictionary(w => w.Key, w => w.Max(s => s.Stock)),
                StringComparer.OrdinalIgnoreCase);
        var productsByModel = products
            .GroupBy(p => p.ModelExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var offersByModel = offers
            .GroupBy(o => o.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var imageByModel = imageDocs.ToDictionary(d => d.ExternalId, ImageUri, StringComparer.OrdinalIgnoreCase);

        var windowKey = window?.ExternalId;
        var all = models
            .Select(model => Build(model, productsByModel, offersByModel, stockByProduct, imageByModel,
                context, windowKey, now))
            .ToList();

        var filtered = all.Where(row => Matches(row, query)).ToList();
        var page = Sort(filtered, query).Skip(query.Skip).Take(query.Take).ToList();

        return new CatalogPage(
            Windows: [.. windows.Select(object (w) => new { id = w.ExternalId, name = w.Name, orderType = w.OrderType })],
            Window: windowKey,
            Rows: page,
            Total: filtered.Count,
            Families: FamilyFacet(all, query),
            AvailabilityFacet: AvailabilityFacet(all, query),
            AttributeFacets: AttributeFacets(all, query));
    }

    /// Mismas filas que el listado pero sin paginar: lo que baja "Desc. Stock"
    public static async Task<IReadOnlyList<CatalogRow>> RowsAsync(
        AppDbContext db, PortalActorPrices prices, CatalogQuery query, DateTimeOffset now)
    {
        var page = await QueryAsync(db, prices, query with { Skip = 0, Take = CatalogQuery.MaxTake }, now);
        return page.Rows;
    }

    private static ServiceWindow? PickWindow(List<ServiceWindow> windows, string? requested)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            var match = windows.FirstOrDefault(w =>
                string.Equals(w.ExternalId, requested, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        // Por defecto la ventana del botón azul del header: REPOSICIÓN
        return windows.FirstOrDefault(w => w.OrderType == "REPLENISHMENT") ?? windows.FirstOrDefault();
    }

    private static CatalogRow Build(
        CatalogModel model,
        Dictionary<string, List<CatalogProduct>> productsByModel,
        Dictionary<string, List<Offer>> offersByModel,
        Dictionary<string, IReadOnlyDictionary<string, decimal>> stockByProduct,
        Dictionary<string, string?> imageByModel,
        PriceContext context,
        string? windowKey,
        DateTimeOffset now)
    {
        var modelOffers = offersByModel.GetValueOrDefault(model.ExternalId) ?? [];

        var variants = (productsByModel.GetValueOrDefault(model.ExternalId) ?? [])
            .OrderBy(p => decimal.TryParse(p.Size, out var n) ? n : decimal.MaxValue)
            .ThenBy(p => p.Size, StringComparer.OrdinalIgnoreCase)
            .Select(product => new CatalogVariant(
                product,
                stockByProduct.GetValueOrDefault(product.ExternalId) ?? new Dictionary<string, decimal>(),
                CatalogPricing.Resolve(modelOffers, "PVD", product.ExternalId, context, now),
                CatalogPricing.Resolve(modelOffers, "PVP", product.ExternalId, context, now)))
            .ToList();

        // La cabecera del artículo enseña el precio desde el que arranca la curva;
        // sin tallas cae en la tarifa de modelo.
        var pvdPrices = variants.Select(v => v.Pvd).OfType<decimal>().ToList();
        var pvd = pvdPrices.Count > 0
            ? pvdPrices.Min()
            : CatalogPricing.Resolve(modelOffers, "PVD", null, context, now);
        var pvpPrices = variants.Select(v => v.Pvp).OfType<decimal>().ToList();
        var pvp = pvpPrices.Count > 0
            ? pvpPrices.Max()
            : CatalogPricing.Resolve(modelOffers, "PVP", null, context, now);

        return new CatalogRow(
            Model: model,
            Segments: Strings(model.ProductSegmentsJson),
            Attributes: Attributes(model.AttributesJson),
            ImageUri: imageByModel.GetValueOrDefault(model.ExternalId),
            Variants: variants,
            Pvd: pvd,
            Pvp: pvp,
            Currency: modelOffers.FirstOrDefault()?.PriceCode is { Length: > 0 } code ? code : "EUR",
            Availability: States(variants, windowKey),
            // Kids en la referencia: cuando la curva no tiene un precio único, la
            // matriz enseña el precio en cada talla.
            PricePerSize: pvdPrices.Distinct().Count() > 1);
    }

    public static decimal StockOf(CatalogVariant variant, string? windowKey) =>
        windowKey is null ? 0m : variant.Stock.GetValueOrDefault(windowKey, 0m);

    private static IReadOnlyList<string> States(IReadOnlyList<CatalogVariant> variants, string? windowKey)
    {
        var levels = variants.Select(v => StockOf(v, windowKey)).ToList();
        List<string> states = [];
        if (levels.Any(s => s >= Availability.LowThreshold)) states.Add(Availability.Available);
        if (levels.Any(s => s > 0 && s < Availability.LowThreshold)) states.Add(Availability.Low);
        if (levels.Count == 0 || levels.All(s => s <= 0)) states.Add(Availability.Consult);
        return states;
    }

    // ── Filtros ────────────────────────────────────────────────────────────────

    private static bool Matches(CatalogRow row, CatalogQuery query) =>
        MatchesSearch(row, query) && MatchesFamily(row, query)
        && MatchesAvailability(row, query) && MatchesAttributes(row, query, skip: null);

    private static bool MatchesSearch(CatalogRow row, CatalogQuery query) =>
        query.Search is null
        || row.Model.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
        || row.Model.ExternalReference.Contains(query.Search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFamily(CatalogRow row, CatalogQuery query) =>
        query.Family is null
        || string.Equals(row.Model.FamilyId, query.Family, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAvailability(CatalogRow row, CatalogQuery query) =>
        query.Availability.Count == 0 || row.Availability.Any(query.Availability.Contains);

    private static bool MatchesAttributes(CatalogRow row, CatalogQuery query, string? skip)
    {
        foreach (var (key, wanted) in query.Attributes)
        {
            if (skip is not null && string.Equals(key, skip, StringComparison.OrdinalIgnoreCase)) continue;
            if (!row.Attributes.TryGetValue(key, out var value) || !wanted.Contains(value)) return false;
        }
        return true;
    }

    // ── Facetas ────────────────────────────────────────────────────────────────
    // Cada grupo se cuenta sobre el resto de filtros pero NO sobre el suyo: así el
    // recuento sigue siendo útil después de marcar una casilla de ese mismo grupo.

    private static List<CatalogFamilyFacet> FamilyFacet(List<CatalogRow> all, CatalogQuery query) =>
        [.. all
            .Where(r => MatchesSearch(r, query) && MatchesAvailability(r, query) && MatchesAttributes(r, query, null))
            .GroupBy(r => r.Model.FamilyId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .Select(g => new CatalogFamilyFacet(g.Key, Label(g.Key), g.Count()))
            .OrderBy(f => f.Label, StringComparer.CurrentCultureIgnoreCase)];

    private static List<CatalogFacetValue> AvailabilityFacet(List<CatalogRow> all, CatalogQuery query)
    {
        var pool = all
            .Where(r => MatchesSearch(r, query) && MatchesFamily(r, query) && MatchesAttributes(r, query, null))
            .ToList();
        return [.. Availability.All.Select(state =>
            new CatalogFacetValue(state, pool.Count(r => r.Availability.Contains(state))))];
    }

    private static List<CatalogAttributeFacet> AttributeFacets(List<CatalogRow> all, CatalogQuery query)
    {
        var keys = all.SelectMany(r => r.Attributes.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. keys
            .Select(key =>
            {
                var pool = all.Where(r => MatchesSearch(r, query) && MatchesFamily(r, query)
                                          && MatchesAvailability(r, query) && MatchesAttributes(r, query, key));
                var values = pool
                    .Select(r => r.Attributes.GetValueOrDefault(key))
                    .OfType<string>()
                    .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new CatalogFacetValue(g.Key, g.Count()))
                    .OrderBy(v => v.Value, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                return new CatalogAttributeFacet(key, values);
            })
            .Where(f => f.Values.Count > 0)
            .OrderBy(f => Rank(f.Key))
            .ThenBy(f => f.Key, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static int Rank(string key)
    {
        var index = Array.FindIndex(PreferredFacets, p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? PreferredFacets.Length : index;
    }

    // ── Orden ──────────────────────────────────────────────────────────────────

    private static IEnumerable<CatalogRow> Sort(List<CatalogRow> rows, CatalogQuery query) => query.Sort switch
    {
        CatalogSort.PriceAsc => rows.OrderBy(r => r.Pvd ?? decimal.MaxValue).ThenBy(r => r.Model.Name, Alpha),
        CatalogSort.PriceDesc => rows.OrderByDescending(r => r.Pvd ?? decimal.MinValue).ThenBy(r => r.Model.Name, Alpha),
        // Relevancia: primero lo que se puede comprar hoy, después el nombre
        CatalogSort.Relevance => rows
            .OrderByDescending(r => r.Availability.Contains(Availability.Available))
            .ThenByDescending(r => r.Availability.Contains(Availability.Low))
            .ThenBy(r => r.Model.Name, Alpha),
        _ => rows.OrderBy(r => r.Model.Name, Alpha)
    };

    private static readonly StringComparer Alpha = StringComparer.CurrentCultureIgnoreCase;

    // ── Utilidades ─────────────────────────────────────────────────────────────

    private static string Label(string familyId) =>
        familyId.Length == 0 ? familyId : char.ToUpperInvariant(familyId[0]) + familyId[1..];

    private static string? ImageUri(SyncDocument doc)
    {
        try
        {
            return (JsonNode.Parse(doc.Payload) as JsonObject)?["images"]?[0]?["image"]?["uri"]?.GetValue<string>();
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static List<string> Strings(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Dictionary<string, string> Attributes(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return result;
            foreach (var (key, value) in obj)
            {
                var text = value?.ToString();
                if (!string.IsNullOrWhiteSpace(text)) result[key] = text;
            }
        }
        catch (JsonException) { }
        return result;
    }
}

/// Lo que el catálogo necesita saber del que pregunta para poner precio
public sealed record PortalActorPrices(string? ClientId, IReadOnlyCollection<string> GroupIds)
{
    public static readonly PortalActorPrices Anonymous = new(null, []);
}
