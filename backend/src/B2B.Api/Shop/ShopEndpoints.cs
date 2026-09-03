using System.Security.Claims;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Shop;

// API del portal de clientes: el catálogo comprable en una llamada — modelos con
// variantes (tallas), PVD/PVP del cliente que pregunta, stock por ventana de
// servicio y las facetas del rail lateral (17-catalog-catalog.png).
public static class ShopEndpoints
{
    public static void MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shop/catalog", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var query = CatalogQuery.From(request.Query);
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow, visibility);
            var favorites = await FavoritesAsync(db, actor);
            var settings = await db.IntegrationSettings.FindAsync(1);

            return Results.Ok(new
            {
                page.Windows,
                window = page.Window,
                // M-1: idioma efectivo de este payload (es|en|fr|it). Sin el parámetro
                // locale la respuesta es la de siempre, en español.
                locale = page.Locale,
                page.Total,
                query.Skip,
                query.Take,
                sort = query.Sort,
                // 14a-4 / UX-M1: el actor está restringido por reglas de visibilidad (el
                // front avisa "Catálogo adaptado a tu cuenta" y ajusta los vacíos).
                restricted = visibility.IsRestricted,
                facets = new
                {
                    families = page.Families,
                    availability = page.AvailabilityFacet.Select(f => new { id = f.Value, count = f.Count }),
                    attributes = page.AttributeFacets
                },
                // 14a-4 / UX-M4: la cinta viaja con el catálogo (misma forma que /api/shop/ribbon),
                // computada con las facetas de ESTA respuesta: sin segunda petición ni salto de
                // layout. Las entradas reflejan los filtros activos (recuentos contextuales).
                ribbon = new { entries = RibbonBuilder.Build(page, settings?.CatalogRibbonJson, page.Locale) },
                items = page.Rows.Select(row => CardItem(row, favorites))
            });
        }).RequireAuthorization();

        // ── Productos relacionados ─────────────────────────────────────────────
        // Los modelos llegan de BC con `crossSellingIds`/`upSellingIds` (SystemIds de los
        // modelos hermanos, mismo "Modelo" base). Este endpoint los resuelve con el MISMO
        // pipeline del catálogo (tarifa del cliente, stock por ventana, visibilidad): solo
        // devuelve los relacionados que el cliente puede comprar. `models` admite varios ids
        // (carrito) separados por comas; los modelos de origen nunca se devuelven a sí mismos.
        app.MapGet("/api/shop/related", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var baseQuery = CatalogQuery.From(request.Query);
            // Misma forma de respuesta en TODOS los retornos (con o sin relacionados).
            var empty = new { window = baseQuery.Window, locale = baseQuery.Locale, items = Array.Empty<object>() };
            // Ids de origen normalizados como los arrays (sin llaves): un `models={GUID}` con
            // llaves debe excluirse igual de los resultados.
            var sources = (request.Query["models"].ToString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim('{', '}'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (sources.Count == 0) return Results.Ok(empty);

            // crossSellingIds/upSellingIds de los payloads crudos de los modelos de origen,
            // conservando el ORDEN de aparición (el orden comercial que fijó BC).
            var (cross, up) = await RelatedIdsAsync(db, sources);
            var wanted = cross.Concat(up).Where(id => !sources.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (wanted.Count == 0) return Results.Ok(empty);

            // Sin NINGÚN filtro residual del querystring: los relacionados se resuelven solo
            // por id (un ?availability= o ?a.*= colado no debe vaciar las sugerencias).
            var query = baseQuery with
            {
                Search = null, Family = null, Skip = 0, Take = CatalogQuery.MaxTake,
                Availability = new HashSet<string>(),
                Attributes = new Dictionary<string, IReadOnlySet<string>>(),
                Ids = wanted.ToHashSet(StringComparer.OrdinalIgnoreCase),
            };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow, visibility);
            var favorites = await FavoritesAsync(db, actor);
            var upSet = up.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // En el orden de BC (primero cross, luego up), solo los visibles/comprables.
            // Decisión de negocio: un relacionado SIN tarifa para este cliente no se sugiere
            // (en el catálogo sale como "consultar"; aquí sería ruido sin precio).
            var byId = page.Rows.Where(r => r.Pvd is not null)
                .ToDictionary(r => r.Model.ExternalId, StringComparer.OrdinalIgnoreCase);
            var items = wanted.Where(byId.ContainsKey).Select(id => new
            {
                relation = upSet.Contains(id) ? "up" : "cross",
                card = CardItem(byId[id], favorites),
            });
            return Results.Ok(new { window = page.Window, locale = page.Locale, items });
        }).RequireAuthorization();

        // ── Cinta del catálogo (ribbon) ────────────────────────────────────────
        // Las entradas de la banda bajo CATÁLOGO|LOOKBOOK computadas para el actor
        // (RibbonBuilder) sobre el catálogo COMPLETO filtrado por su visibilidad, sin
        // ningún filtro de la barra. La usa /manage (vista previa del gestor); el portal
        // ya recibe la cinta dentro de /api/shop/catalog (14a-4).
        app.MapGet("/api/shop/ribbon", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var locale = DocumentProjections.Locale(request.Query["locale"]);

            // Take = 1: las facetas (Families/AttributeFacets) se computan sobre TODO el
            // catálogo filtrado por visibilidad ("all" en CatalogService.QueryAsync); Take
            // solo recorta Rows, que aquí no se usan.
            var page = await CatalogService.QueryAsync(db, Prices(actor),
                new CatalogQuery { Take = 1, Locale = locale }, DateTimeOffset.UtcNow, visibility);

            var settings = await db.IntegrationSettings.FindAsync(1);
            var entries = RibbonBuilder.Build(page, settings?.CatalogRibbonJson, locale);
            return Results.Ok(new { locale, entries });
        }).RequireAuthorization();

        // Botón "Desc. Stock" de la toolbar: el listado que se está viendo, con los
        // mismos filtros, en un CSV que Excel abre sin preguntar nada.
        app.MapGet("/api/shop/stock-export.csv", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var query = CatalogQuery.From(request.Query);
            var page = await CatalogService.QueryAsync(db, Prices(actor), query with { Skip = 0, Take = CatalogQuery.ExportTake },
                DateTimeOffset.UtcNow, visibility);

            var rows = page.Rows.SelectMany(row => row.Variants.Select(variant => new object?[]
            {
                row.Model.ExternalReference,
                row.Model.Name,
                Label(row.Model.FamilyId),
                variant.Product.Size,
                variant.Product.Sku,
                variant.Product.Ean,
                page.Window,
                StockText(CatalogService.StockOf(variant, page.Window)),
                variant.Pvd ?? row.Pvd,
                variant.Pvp ?? row.Pvp
            }));

            var csv = Csv.Build(
                ["Referencia", "Modelo", "Línea", "Talla", "SKU", "EAN", "Ventana", "Stock", "PVD", "PVP"],
                rows);

            return Results.File(csv, "text/csv; charset=utf-8", $"stock-{DateTime.Now:yyyyMMdd}.csv");
        }).RequireAuthorization();
    }

    // Proyección de una card de modelo (catálogo, relacionados): SIEMPRE la misma forma,
    // así el front pinta cualquier lista de modelos con el mismo componente.
    private static object CardItem(CatalogRow row, HashSet<string> favorites) => new
    {
        modelId = row.Model.ExternalId,
        name = row.Name,
        reference = row.Model.ExternalReference,
        familyId = row.Model.FamilyId,
        familyLabel = row.FamilyLabel,
        segments = row.Segments,
        attributes = row.Attributes,
        attributeList = row.AttributeList,
        imageUri = row.ImageUri,
        images = row.Images,
        pvd = row.Pvd,
        pvp = row.Pvp,
        currency = row.Currency,
        availability = row.Availability,
        pricePerSize = row.PricePerSize,
        favorite = favorites.Contains(row.Model.ExternalId),
        products = row.Variants.Select(variant => new
        {
            productId = variant.Product.ExternalId,
            size = variant.Product.Size,
            sku = variant.Product.Sku,
            ean = variant.Product.Ean,
            stock = variant.Stock,
            pvd = variant.Pvd,
            pvp = variant.Pvp
        })
    };

    // Lee crossSellingIds/upSellingIds de los payloads CRUDOS de los modelos (el normalizador
    // no los materializa; el documento jsonb sí los conserva). Devuelve los ids en orden de
    // aparición, sin llaves y deduplicados. La comparación de ids es SIEMPRE en memoria y
    // case-insensitive (la traducción SQL de un HashSet ignora el comparer y la collation de
    // Postgres es sensible: un id en otra caja fallaría en silencio).
    //
    // Resolución SIMÉTRICA: en BC la relación es "mismo Modelo base" (simétrica por
    // definición), pero cada artículo solo refresca SU lista al re-enviarse. Si B lista a A
    // pero A aún no fue re-enviado con sus ids, la ficha de A también debe enseñar a B (y a
    // los demás hermanos que B declare). Por eso, además de los arrays de los orígenes, se
    // incorporan los modelos que LISTAN a un origen y sus hermanos declarados.
    private static async Task<(List<string> Cross, List<string> Up)> RelatedIdsAsync(
        AppDbContext db, IReadOnlySet<string> sourceIds)
    {
        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == "model")
            .Select(d => new { d.ExternalId, d.Payload })
            .ToListAsync();

        List<string> cross = [], up = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inverse = new List<string>();
        var inverseSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1ª pasada: los arrays DIRECTOS de los orígenes (mandan en el orden).
        foreach (var doc in docs)
        {
            if (!sourceIds.Contains(doc.ExternalId)) continue;
            var root = ClientIdentity.Parse(doc.Payload);
            Collect(root?["crossSellingIds"], cross, seen);
            Collect(root?["upSellingIds"], up, seen);
        }

        // 2ª pasada: relación inversa — X lista a un origen → X es hermano (y sus hermanos
        // declarados también). Van detrás de los directos, en orden estable de documento.
        // Vale para las DOS relaciones: compartir el valor de un atributo (mismo Modelo →
        // cross; misma Colección → up) es simétrico por definición, aunque BC solo refresque
        // la lista de cada artículo al re-enviarlo. Si un documento lista al origen en ambas,
        // manda cross (misma prioridad que la deduplicación general).
        var inverseUp = new List<string>();
        foreach (var doc in docs)
        {
            if (sourceIds.Contains(doc.ExternalId)) continue;
            var root = ClientIdentity.Parse(doc.Payload);
            if (ContainsAny(root?["crossSellingIds"], sourceIds))
            {
                if (seen.Add(doc.ExternalId)) inverse.Add(doc.ExternalId);
                Collect(root?["crossSellingIds"], inverse, seen);   // hermanos del hermano
            }
            else if (ContainsAny(root?["upSellingIds"], sourceIds))
            {
                if (seen.Add(doc.ExternalId)) inverseUp.Add(doc.ExternalId);
                Collect(root?["upSellingIds"], inverseUp, seen);    // colección del hermano
            }
        }
        cross.AddRange(inverse);
        up.AddRange(inverseUp);
        return (cross, up);

        static void Collect(System.Text.Json.Nodes.JsonNode? node, List<string> into, HashSet<string> seen)
        {
            if (node is not System.Text.Json.Nodes.JsonArray arr) return;
            foreach (var item in arr)
            {
                var id = (item as System.Text.Json.Nodes.JsonValue)?.TryGetValue<string>(out var s) == true
                    ? s.Trim().Trim('{', '}') : null;
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id!)) into.Add(id!);
            }
        }

        static bool ContainsAny(System.Text.Json.Nodes.JsonNode? node, IReadOnlySet<string> ids)
        {
            if (node is not System.Text.Json.Nodes.JsonArray arr) return false;
            foreach (var item in arr)
            {
                if ((item as System.Text.Json.Nodes.JsonValue)?.TryGetValue<string>(out var s) == true
                    && ids.Contains(s.Trim().Trim('{', '}'))) return true;
            }
            return false;
        }
    }

    private static PortalActorPrices Prices(PortalActor? actor) =>
        actor is null ? PortalActorPrices.Anonymous : new PortalActorPrices(actor.ClientId, actor.GroupIds);

    private static async Task<HashSet<string>> FavoritesAsync(AppDbContext db, PortalActor? actor)
    {
        if (actor is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = await db.PortalFavorites
            .Where(f => f.UserId == actor.UserId)
            .Select(f => f.ModelId)
            .ToListAsync();
        return new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);
    }

    // El sentinela de infinito no es una cifra que se pueda llevar a una hoja de cálculo
    private static string StockText(decimal stock) =>
        stock >= Availability.Infinite ? "∞" : stock.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));

    private static string Label(string familyId) =>
        familyId.Length == 0 ? familyId : char.ToUpperInvariant(familyId[0]) + familyId[1..];
}
