using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace B2B.Api.Portal;

// Proyección de los documentos que el conector deja crudos en sync_documents
// (pedidos del contrato 04 §5, albaranes y facturas del contrato 05) a las filas
// que pintan 03-orders.png, 04-delivery-notes.png y 05-invoices.png.
//
// Aquí no hay acceso a datos ni a HTTP: solo JSON del conector → fila de tabla.
// Eso permite probar el mapeo de estados, el signo de las devoluciones y el
// cálculo de la deuda sin levantar nada.

/// Lo que toda fila de listado necesita para filtrarse, contarse y ordenarse
public interface IDocumentRow
{
    string Number { get; }
    DateTimeOffset? Date { get; }
    string Status { get; }
    string Season { get; }
    /// Texto sobre el que trabaja el "Buscar..." de la toolbar
    string SearchBlob { get; }
}

public sealed record OrderRow(
    string Id, string Number, DateTimeOffset? Date, string Reference, string Type,
    decimal Units, decimal Total, string Currency, string Status, string Season) : IDocumentRow
{
    [JsonIgnore] public string SearchBlob => $"{Number} {Reference} {Type}";
}

public sealed record DeliveryNoteRow(
    string Id, string Number, DateTimeOffset? Date, bool IsInvoiced, decimal Total, string Currency,
    string Address, string TransportId, string TransportUrlTrack) : IDocumentRow
{
    public string Status => IsInvoiced ? "invoiced" : "not-invoiced";
    [JsonIgnore] public string Season => "";
    [JsonIgnore] public string SearchBlob => $"{Number} {Address} {TransportId}";
}

public sealed record InvoiceRow(
    string Id, string Number, DateTimeOffset? Date, string PayMethod, decimal Total, decimal Debt,
    string Currency, string Status, DateTimeOffset? DueDate) : IDocumentRow
{
    [JsonIgnore] public string Season => "";
    [JsonIgnore] public string SearchBlob => $"{Number} {PayMethod}";
}

/// Línea de detalle, común a las tres vistas (pedido: cantidad servida y estado;
/// albarán y factura: solo lo entregado/facturado)
public sealed record DocumentLine(
    string Id, string Name, string Reference, string Size, string Sku,
    decimal Quantity, decimal QuantityDelivered, decimal Price, decimal Discount,
    decimal TaxPercent, decimal Amount, string Status);

/// Filtros de la toolbar + paginación. El clientId NO está aquí a propósito:
/// sale del token, nunca de la query (plan, Fase 3).
public sealed record DocumentQuery(
    string Search, string Status, string Season, DateOnly? From, DateOnly? To,
    string Sort, string Locale, int Skip, int Take)
{
    public const int DefaultTake = 12;   // "Mostrar 12" de la referencia
    public const int MaxTake = 500;

    /// Se llama Read y no From porque `From` ya es la propiedad de fecha inicial
    public static DocumentQuery Read(IQueryCollection query)
    {
        var take = Int(query["take"], DefaultTake);
        return new DocumentQuery(
            Search: (query["search"].ToString()).Trim(),
            Status: query["status"].ToString().Trim().ToLowerInvariant(),
            Season: query["season"].ToString().Trim(),
            From: Day(query["from"]),
            To: Day(query["to"]),
            Sort: query["sort"].ToString().Trim().ToLowerInvariant(),
            Locale: DocumentProjections.Locale(query["locale"]),
            Skip: Math.Max(0, Int(query["skip"], 0)),
            Take: Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake));
    }

    private static int Int(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static DateOnly? Day(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}

public sealed record DocumentPage<T>(
    IReadOnlyList<T> Items, int Total, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<string> Seasons);

public static class DocumentProjections
{
    public const string OrderEntity = "order";
    public const string DeliveryNoteEntity = "delivery-note";
    public const string InvoiceEntity = "invoice";

    // Estados de cada rail, EN EL ORDEN de su captura (bajo "Todos"/"Todas")
    public static readonly string[] OrderStatuses =
        ["invoiced", "partially-shipped", "shipped", "canceled", "open"];
    public static readonly string[] DeliveryNoteStatuses = ["invoiced", "not-invoiced"];
    public static readonly string[] InvoiceStatuses = ["overdue", "paid", "partial", "credit", "pending"];

    // ══════════════ Filas de listado ══════════════

    /// Un pedido de venta. Devuelve null para lo que NO es un pedido: los pedidos de
    /// devolución llegan por el mismo endpoint (contrato 05 §6) con `type` NOT_DEFINED
    /// e importes negativos, y su sitio es /sat, no /orders.
    public static OrderRow? Order(string id, JsonObject payload)
    {
        var total = Money(payload["totals"]?["total"]);
        if (string.Equals(Text(payload["type"]), "NOT_DEFINED", StringComparison.OrdinalIgnoreCase) || total < 0)
            return null;

        var items = payload["items"] as JsonArray ?? [];
        var units = items.Sum(item => Decimal(item?["transactionInfo"]?["info"]?["quantity"]));

        // REF. DE CLIENTE: el nº de pedido de compra del cliente y, si BC no lo
        // manda, su "Your Reference" — que es lo que el cliente reconoce.
        var reference = Text(payload["purchaseOrderId"]);
        if (reference.Length == 0) reference = Text(payload["reference"]);

        return new OrderRow(
            Id: id,
            Number: Text(payload["externalReference"]),
            Date: Date(payload["orderedDate"]),
            Reference: reference,
            Type: Text(payload["type"]),
            Units: units,
            Total: total,
            Currency: Currency(payload["totals"]?["total"]),
            Status: OrderStatus(payload),
            Season: Text(payload["seasonId"]));
    }

    private static string OrderStatus(JsonObject payload)
    {
        var status = Text(payload["status"]).ToLowerInvariant();
        return OrderStatuses.Contains(status) ? status : "open";
    }

    /// Un albarán. Los albaranes de devolución llegan por el mismo endpoint
    /// (contrato 05 §3) y SÍ se listan: para el cliente son entregas suyas, solo que
    /// en negativo. Lo que los distingue en pantalla es el importe.
    public static DeliveryNoteRow DeliveryNote(string id, JsonObject payload) => new(
        Id: id,
        Number: Text(payload["number"]),
        Date: Date(payload["deliveryDate"]),
        IsInvoiced: Bool(payload["isInvoiced"]),
        Total: Money(payload["totals"]?["total"]),
        Currency: Currency(payload["totals"]?["total"]),
        Address: Address(payload["shippingAddress"]),
        TransportId: Text(payload["transportId"]),
        // Solo el albarán de venta con transportista y nº de seguimiento lo trae
        // (contrato 05 §2): sin él la celda ENLACE TRASNPORTE va vacía.
        TransportUrlTrack: Text(payload["transportUrlTrack"]));

    /// Una factura (o un abono, que llega por el mismo endpoint en negativo).
    /// `today` entra por parámetro para que "Vencida" sea comprobable.
    public static InvoiceRow Invoice(string id, JsonObject payload, string locale, DateOnly today)
    {
        var total = Money(payload["totals"]?["total"]);
        // El contrato solo distingue Paid/Unpaid: la deuda es todo o nada
        // (contrato 05 §4). "Parcial" y "A Crédito" del rail existen en la
        // referencia pero BC todavía no los puede producir.
        var paid = string.Equals(Text(payload["status"]), "Paid", StringComparison.OrdinalIgnoreCase);
        var due = Date(First(payload["payments"])?["dueDate"]);

        var status = paid ? "paid"
            : due is { } limit && DateOnly.FromDateTime(limit.UtcDateTime) < today ? "overdue"
            : "pending";

        return new InvoiceRow(
            Id: id,
            Number: Text(payload["number"]),
            Date: Date(payload["issueDate"]),
            PayMethod: Localized(payload["payMethodName"], locale),
            Total: total,
            Debt: paid ? 0m : total,
            Currency: Currency(payload["totals"]?["total"]),
            Status: status,
            DueDate: due);
    }

    // ══════════════ Líneas de detalle ══════════════

    /// Líneas de un pedido: `items[]` (contrato 04 §5.3)
    public static IReadOnlyList<DocumentLine> OrderLines(JsonObject payload, string locale) =>
        [.. (payload["items"] as JsonArray ?? []).OfType<JsonObject>().Select(item =>
        {
            var info = item["transactionInfo"]?["info"];
            var product = item["productInfo"];
            var reference = Text(item["productExternalReference"]);
            if (reference.Length == 0) reference = Text(product?["externalReference"]);

            return new DocumentLine(
                Id: Text(item["id"]),
                Name: Name(item["productName"], product?["name"], locale),
                Reference: reference,
                Size: Size(product),
                Sku: Text(product?["sku"]),
                Quantity: Decimal(info?["quantity"]),
                QuantityDelivered: Decimal(item["quantityDelivered"]),
                Price: Money(info?["price"]),
                Discount: Decimal(info?["discount"]),
                TaxPercent: Decimal(First(item["transactionInfo"]?["taxes"])?["percent"]),
                Amount: Money(info?["amount"]),
                Status: Text(item["status"]));
        })];

    /// Líneas de un albarán o de una factura: `lines[]` (contrato 05 §2 y §4)
    public static IReadOnlyList<DocumentLine> DocumentLines(JsonObject payload, string locale) =>
        [.. (payload["lines"] as JsonArray ?? []).OfType<JsonObject>().Select(line =>
        {
            var info = line["transactionInfo"]?["info"];
            var product = line["productInfo"];
            var reference = Text(line["externalReference"]);
            if (reference.Length == 0) reference = Text(product?["externalReference"]);
            var quantity = Decimal(info?["quantity"]);

            return new DocumentLine(
                Id: Text(line["id"]),
                Name: Name(line["productName"], product?["name"], locale),
                Reference: reference,
                Size: Size(product),
                Sku: Text(product?["sku"]),
                Quantity: quantity,
                QuantityDelivered: quantity,   // ya está entregado: es un documento registrado
                Price: Money(info?["price"]),
                Discount: Decimal(info?["discount"]),
                TaxPercent: Decimal(First(line["transactionInfo"]?["taxes"])?["percent"]),
                Amount: Money(info?["amount"]),
                Status: "");
        })];

    // El nombre traducido de la línea (4 idiomas) manda; el de productInfo (6, sin
    // traducir de verdad) es el respaldo.
    private static string Name(JsonNode? productName, JsonNode? productInfoName, string locale)
    {
        var name = Localized(productName, locale);
        return name.Length > 0 ? name : Localized(productInfoName, locale);
    }

    // ══════════════ Filtrado, recuento y paginación ══════════════

    /// El pipeline compartido por las tres vistas:
    /// 1) las temporadas salen del universo del cliente (si no, elegir una las borraría);
    /// 2) buscar + temporada + fechas acotan el conjunto;
    /// 3) el rail cuenta sobre ESE conjunto, no sobre el estado elegido — así los
    ///    contadores siguen visibles cuando ya has pinchado un estado;
    /// 4) el estado filtra, se ordena y se pagina.
    public static DocumentPage<T> Paginate<T>(
        IEnumerable<T> rows, DocumentQuery query, IReadOnlyList<string> statuses,
        Func<IEnumerable<T>, string, IEnumerable<T>> sort) where T : IDocumentRow
    {
        var universe = rows.ToList();

        var seasons = universe
            .Select(row => row.Season)
            .Where(season => season.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(season => season, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matched = universe.Where(row => Matches(row, query)).ToList();

        var counts = new Dictionary<string, int>(statuses.Count + 1) { ["all"] = matched.Count };
        foreach (var status in statuses)
            counts[status] = matched.Count(row => row.Status == status);

        var filtered = statuses.Contains(query.Status)
            ? matched.Where(row => row.Status == query.Status).ToList()
            : matched;

        var page = sort(filtered, query.Sort).Skip(query.Skip).Take(query.Take).ToList();
        return new DocumentPage<T>(page, filtered.Count, counts, seasons);
    }

    /// Igual que Paginate pero sin recortar: lo que necesita el CSV de EXPORTAR
    public static IReadOnlyList<T> All<T>(
        IEnumerable<T> rows, DocumentQuery query, IReadOnlyList<string> statuses,
        Func<IEnumerable<T>, string, IEnumerable<T>> sort) where T : IDocumentRow =>
        Paginate(rows, query with { Skip = 0, Take = DocumentQuery.MaxTake }, statuses, sort).Items;

    private static bool Matches(IDocumentRow row, DocumentQuery query)
    {
        if (query.Search.Length > 0 &&
            row.SearchBlob.Contains(query.Search, StringComparison.OrdinalIgnoreCase) is false)
            return false;

        if (query.Season.Length > 0 && !string.Equals(row.Season, query.Season, StringComparison.OrdinalIgnoreCase))
            return false;

        if (query.From is { } from && (row.Date is null || DateOnly.FromDateTime(row.Date.Value.UtcDateTime) < from))
            return false;

        if (query.To is { } to && (row.Date is null || DateOnly.FromDateTime(row.Date.Value.UtcDateTime) > to))
            return false;

        return true;
    }

    /// Orden por defecto de pedidos y albaranes: lo último que pasó, primero
    public static IEnumerable<T> ByDateDesc<T>(IEnumerable<T> rows, string _) where T : IDocumentRow =>
        rows.OrderByDescending(row => row.Date ?? DateTimeOffset.MinValue)
            .ThenByDescending(row => row.Number, StringComparer.OrdinalIgnoreCase);

    /// "Ordenar por" de la toolbar de facturas (05-invoices.png)
    public static readonly string[] InvoiceSorts =
        ["date-desc", "date-asc", "amount-desc", "amount-asc", "debt-desc", "number-asc"];

    public static IEnumerable<InvoiceRow> SortInvoices(IEnumerable<InvoiceRow> rows, string sort) => sort switch
    {
        "date-asc" => rows.OrderBy(row => row.Date ?? DateTimeOffset.MaxValue).ThenBy(row => row.Number),
        "amount-desc" => rows.OrderByDescending(row => row.Total).ThenBy(row => row.Number),
        "amount-asc" => rows.OrderBy(row => row.Total).ThenBy(row => row.Number),
        "debt-desc" => rows.OrderByDescending(row => row.Debt).ThenBy(row => row.Number),
        "number-asc" => rows.OrderBy(row => row.Number, StringComparer.OrdinalIgnoreCase),
        _ => ByDateDesc(rows, sort)
    };

    // ══════════════ Lectura del JSON del conector ══════════════

    public static string Text(JsonNode? node) => ClientIdentity.Text(node);

    public static decimal Decimal(JsonNode? node)
    {
        if (node is null)
            return 0m;
        try { return node.GetValue<decimal>(); }
        catch (Exception)
        {
            return decimal.TryParse(node.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : 0m;
        }
    }

    /// Objeto Money del contrato: { "code": "EUR", "value": 12.34 }
    public static decimal Money(JsonNode? node) => Decimal(node?["value"]);

    public static string Currency(JsonNode? node) =>
        Text(node?["code"]) is { Length: > 0 } code ? code : "EUR";

    public static bool Bool(JsonNode? node)
    {
        if (node is null)
            return false;
        try { return node.GetValue<bool>(); }
        catch (Exception) { return bool.TryParse(node.ToString(), out var parsed) && parsed; }
    }

    /// Fechas del conector: con `.000Z` (documentos registrados) o sin él (pedidos)
    public static DateTimeOffset? Date(JsonNode? node)
    {
        var text = Text(node);
        if (text.Length == 0)
            return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment) ? moment : null;
    }

    private static JsonNode? First(JsonNode? node) =>
        node is JsonArray { Count: > 0 } array ? array[0] : null;

    // El portal habla en códigos de 2 letras; el conector en culturas de 6
    private static readonly Dictionary<string, string> Cultures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = "es_ES", ["en"] = "en_EN", ["fr"] = "fr_FR", ["it"] = "it_IT"
    };

    public static string Locale(string? value)
    {
        var code = (value ?? "").Trim().ToLowerInvariant();
        if (code.Length > 2) code = code[..2];
        return Cultures.ContainsKey(code) ? code : "es";
    }

    /// Texto multiidioma del conector (TranslatedText4/6) en el idioma de la ruta.
    /// Antes de dejar una celda vacía se cae al español, al inglés y a lo que haya.
    public static string Localized(JsonNode? node, string locale)
    {
        if (node is null)
            return "";
        if (node is not JsonObject obj)
            return Text(node);

        var wanted = Cultures.GetValueOrDefault(Locale(locale), "es_ES");
        foreach (var key in new[] { wanted, "es_ES", "en_EN" })
            if (obj[key] is { } found && Text(found) is { Length: > 0 } value)
                return value;

        return obj.Select(pair => Text(pair.Value)).FirstOrDefault(value => value.Length > 0) ?? "";
    }

    /// La talla vive dentro del SKU: nº de artículo + código de variante
    /// concatenados (contrato 05 §2). Se quita el prefijo del modelo y queda "42".
    public static string Size(JsonNode? productInfo)
    {
        var sku = Text(productInfo?["sku"]);
        if (sku.Length == 0)
            return "";

        foreach (var prefix in new[]
                 {
                     Text(productInfo?["modelExternalReference"]),
                     Text(productInfo?["externalReference"])
                 })
        {
            if (prefix.Length > 0 && sku.Length > prefix.Length &&
                sku.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return sku[prefix.Length..];
        }

        return "";
    }

    /// Dirección en una línea para la columna DIRECCIÓN del listado de albaranes
    public static string Address(JsonNode? address)
    {
        if (address is null)
            return "";

        var street = Join(" ", Text(address["streetAddress"]), Text(address["num"]));
        var town = Join(" ", Text(address["zipCode"]), Text(address["city"]));
        return Join(", ", street, town, Text(address["province"]) == Text(address["city"]) ? "" : Text(address["province"]));
    }

    private static string Join(string separator, params string[] parts) =>
        string.Join(separator, parts.Where(part => part.Length > 0));
}
