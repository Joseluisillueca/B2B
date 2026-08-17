using System.Security.Claims;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Fase 3 del plan: pedidos, albaranes y facturas del cliente, leídos de los
// payloads que el conector ya deja en sync_documents. Solo lectura.
//
// INVARIANTE: todo se acota al clientId del token. No hay ningún parámetro de
// cliente en ninguna ruta de este fichero, y el documento de otro cliente no
// existe (404), no es un 403 que confirme que existe.
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Pedidos (03-orders.png) ───────────────────────────────────────────
        // Nota de enrutado: /export.csv es un segmento literal y gana siempre a
        // {id}, así que el orden de registro no lo decide.
        app.MapGet("/api/portal/orders", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var (rows, query) = await OrdersAsync(request, principal, db);
            var page = DocumentProjections.Paginate(rows, query,
                DocumentProjections.OrderStatuses, DocumentProjections.ByDateDesc);
            return Page(page, query);
        }).RequireAuthorization();

        app.MapGet("/api/portal/orders/export.csv", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var (rows, query) = await OrdersAsync(request, principal, db);
            var all = DocumentProjections.All(rows, query,
                DocumentProjections.OrderStatuses, DocumentProjections.ByDateDesc);

            var csv = Csv.Build(
                ["N. DE PEDIDO", "FECHA", "REF. DE CLIENTE", "TIPO", "TOTAL UNIDADES", "IMPORTE", "ESTADO"],
                all.Select(row => new object?[]
                {
                    row.Number, row.Date?.UtcDateTime, row.Reference, OrderTypeLabel(row.Type),
                    Units(row.Units), row.Total, OrderStatusLabel(row.Status)
                }));

            return Results.File(csv, "text/csv; charset=utf-8", $"pedidos-{DateTime.Now:yyyyMMdd}.csv");
        }).RequireAuthorization();

        app.MapGet("/api/portal/orders/{id}", async (
            string id, HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var payload = await DocumentAsync(db, principal, DocumentProjections.OrderEntity, id);
            if (payload is null) return NotFound();

            var row = DocumentProjections.Order(id, payload);
            if (row is null) return NotFound();   // una devolución no es un pedido

            return Results.Ok(new
            {
                row.Id, row.Number, row.Date, row.Reference, row.Type,
                row.Units, row.Total, row.Currency, row.Status, row.Season,
                payMethodId = DocumentProjections.Text(payload["payMethodId"]),
                observations = DocumentProjections.Text(payload["observations"]),
                shippingAddress = DocumentProjections.Address(payload["shippingAddress"]),
                totals = Totals(payload),
                lines = DocumentProjections.OrderLines(payload, locale)
            });
        }).RequireAuthorization();

        // ── Albaranes (04-delivery-notes.png) ─────────────────────────────────
        app.MapGet("/api/portal/delivery-notes", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var (rows, query) = await DeliveryNotesAsync(request, principal, db);
            var page = DocumentProjections.Paginate(rows, query,
                DocumentProjections.DeliveryNoteStatuses, DocumentProjections.ByDateDesc);
            return Page(page, query);
        }).RequireAuthorization();

        app.MapGet("/api/portal/delivery-notes/export.csv", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var (rows, query) = await DeliveryNotesAsync(request, principal, db);
            var all = DocumentProjections.All(rows, query,
                DocumentProjections.DeliveryNoteStatuses, DocumentProjections.ByDateDesc);

            // "ENLACE TRASNPORTE" con la errata del portal actual: la columna se
            // llama así en la referencia y el cliente la reconoce por ese nombre.
            var csv = Csv.Build(
                ["REFERENCIA", "FECHA", "FACTURADO", "IMPORTE", "DIRECCIÓN", "ENLACE TRASNPORTE"],
                all.Select(row => new object?[]
                {
                    row.Number, row.Date?.UtcDateTime, row.IsInvoiced ? "Sí" : "No",
                    row.Total, row.Address, row.TransportUrlTrack
                }));

            return Results.File(csv, "text/csv; charset=utf-8", $"albaranes-{DateTime.Now:yyyyMMdd}.csv");
        }).RequireAuthorization();

        app.MapGet("/api/portal/delivery-notes/{id}", async (
            string id, HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var payload = await DocumentAsync(db, principal, DocumentProjections.DeliveryNoteEntity, id);
            if (payload is null) return NotFound();

            var row = DocumentProjections.DeliveryNote(id, payload);
            return Results.Ok(new
            {
                row.Id, row.Number, row.Date, row.IsInvoiced, row.Total, row.Currency,
                row.Address, row.TransportId, row.TransportUrlTrack,
                status = row.Status,
                payMethodId = DocumentProjections.Text(payload["paymethodId"]),
                observations = DocumentProjections.Text(payload["observations"]),
                totals = Totals(payload),
                lines = DocumentProjections.DocumentLines(payload, locale)
            });
        }).RequireAuthorization();

        // ── Facturas (05-invoices.png) ────────────────────────────────────────
        app.MapGet("/api/portal/invoices", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var (rows, query) = await InvoicesAsync(request, principal, db);
            var page = DocumentProjections.Paginate(rows, query,
                DocumentProjections.InvoiceStatuses, DocumentProjections.SortInvoices);
            return Page(page, query);
        }).RequireAuthorization();

        app.MapGet("/api/portal/invoices/export.csv", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var (rows, query) = await InvoicesAsync(request, principal, db);
            var all = DocumentProjections.All(rows, query,
                DocumentProjections.InvoiceStatuses, DocumentProjections.SortInvoices);

            var csv = Csv.Build(
                ["Nº DE FACTURA", "FECHA", "FORMA DE PAGO", "IMPORTE", "DEUDA PENDIENTE", "ESTADO"],
                all.Select(row => new object?[]
                {
                    row.Number, row.Date?.UtcDateTime, row.PayMethod,
                    row.Total, row.Debt, InvoiceStatusLabel(row.Status)
                }));

            return Results.File(csv, "text/csv; charset=utf-8", $"facturas-{DateTime.Now:yyyyMMdd}.csv");
        }).RequireAuthorization();

        app.MapGet("/api/portal/invoices/{id}", async (
            string id, HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var payload = await DocumentAsync(db, principal, DocumentProjections.InvoiceEntity, id);
            if (payload is null) return NotFound();

            var row = DocumentProjections.Invoice(id, payload, locale, Today());
            return Results.Ok(new
            {
                row.Id, row.Number, row.Date, row.PayMethod, row.Total, row.Debt,
                row.Currency, row.Status, row.DueDate,
                fiscalName = DocumentProjections.Text(payload["fiscalInfo"]?["fiscalName"]),
                observations = DocumentProjections.Text(payload["observations"]),
                totals = Totals(payload),
                lines = DocumentProjections.DocumentLines(payload, locale)
            });
        }).RequireAuthorization();
    }

    // ══════════════ Ámbito ══════════════

    /// Los documentos del cliente del token y nada más (PortalScope.DocumentsAsync,
    /// compartido con las estadísticas de la Fase 4).
    private static Task<List<(string Id, JsonObject Payload)>> ScopedAsync(
        AppDbContext db, ClaimsPrincipal principal, string entityType) =>
        PortalScope.DocumentsAsync(db, principal, entityType);

    private static async Task<JsonObject?> DocumentAsync(
        AppDbContext db, ClaimsPrincipal principal, string entityType, string id) =>
        (await ScopedAsync(db, principal, entityType))
        .FirstOrDefault(doc => string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase)).Payload;

    private static async Task<(IEnumerable<OrderRow> Rows, DocumentQuery Query)> OrdersAsync(
        HttpRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var query = DocumentQuery.Read(request.Query);
        var docs = await ScopedAsync(db, principal, DocumentProjections.OrderEntity);
        // Los pedidos de devolución comparten endpoint con los pedidos: Order() los
        // descarta devolviendo null y aquí se caen del listado.
        var rows = docs.Select(doc => DocumentProjections.Order(doc.Id, doc.Payload)).OfType<OrderRow>();
        return (rows, query);
    }

    private static async Task<(IEnumerable<DeliveryNoteRow> Rows, DocumentQuery Query)> DeliveryNotesAsync(
        HttpRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var query = DocumentQuery.Read(request.Query);
        var docs = await ScopedAsync(db, principal, DocumentProjections.DeliveryNoteEntity);
        return (docs.Select(doc => DocumentProjections.DeliveryNote(doc.Id, doc.Payload)), query);
    }

    private static async Task<(IEnumerable<InvoiceRow> Rows, DocumentQuery Query)> InvoicesAsync(
        HttpRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var query = DocumentQuery.Read(request.Query);
        var docs = await ScopedAsync(db, principal, DocumentProjections.InvoiceEntity);
        var today = Today();
        return (docs.Select(doc => DocumentProjections.Invoice(doc.Id, doc.Payload, query.Locale, today)), query);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    // ══════════════ Respuestas ══════════════

    private static IResult Page<T>(DocumentPage<T> page, DocumentQuery query) => Results.Ok(new
    {
        page.Total,
        query.Skip,
        query.Take,
        status = query.Status,
        sort = query.Sort,
        page.Counts,
        page.Seasons,
        items = page.Items
    });

    private static object Totals(JsonObject payload) => new
    {
        amount = DocumentProjections.Money(payload["totals"]?["totalAmount"]),
        discount = DocumentProjections.Money(payload["totals"]?["totalDiscount"]),
        tax = DocumentProjections.Money(payload["totals"]?["totalTax"]),
        total = DocumentProjections.Money(payload["totals"]?["total"])
    };

    private static IResult NotFound() =>
        Results.NotFound(new { error = "El documento no existe." });

    // Las unidades son piezas: en la hoja de cálculo van sin decimales salvo que
    // BC mande de verdad una cantidad fraccionada.
    // El (object) del primer brazo no es decorativo: sin él el ternario unifica
    // long y decimal en decimal y el CSV vuelve a escribir "24,00".
    private static object Units(decimal units) =>
        units == Math.Truncate(units) ? (object)(long)units : units;

    // Etiquetas del CSV: el export reproduce lo que se ve en pantalla, y la
    // referencia (y el Csv del proyecto, en es-ES) es española.
    private static string OrderTypeLabel(string type) => type switch
    {
        "SCHEDULED" => "Programación",
        "REPLENISHMENT" => "Reposición",
        _ => type
    };

    private static string OrderStatusLabel(string status) => status switch
    {
        "invoiced" => "Facturado",
        "partially-shipped" => "Envío Parcial",
        "shipped" => "Enviado",
        "canceled" => "Cancelado",
        _ => "Abierto"
    };

    private static string InvoiceStatusLabel(string status) => status switch
    {
        "overdue" => "Vencida",
        "paid" => "Cobrada",
        "partial" => "Parcial",
        "credit" => "A Crédito",
        _ => "Pendiente de cobro"
    };
}
