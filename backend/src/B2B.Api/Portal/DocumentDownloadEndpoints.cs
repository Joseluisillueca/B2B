using System.Security.Claims;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Descarga del PDF de un documento (pedido/albarán/factura) resolviéndolo contra
// Business Central (Origen de documentos): GET salesDocuments?$filter=systemId eq {id}
// → transformer {url} → URL pública del PDF en Azure Blob. Acotado al cliente del token.
public static class DocumentDownloadEndpoints
{
    private static readonly Dictionary<string, string> TypeToEntity = new()
    {
        ["order"] = DocumentProjections.OrderEntity,
        ["delivery-note"] = DocumentProjections.DeliveryNoteEntity,
        ["invoice"] = DocumentProjections.InvoiceEntity,
    };

    public static void MapDocumentDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portal/documents/{type}/{id}/pdf", async (
            string type, string id, ClaimsPrincipal principal, AppDbContext db, BcClient bc) =>
        {
            if (!TypeToEntity.TryGetValue(type, out var entityType))
                return Results.NotFound(new { error = "Tipo de documento no válido." });

            // El documento debe ser del cliente del token (si no, 404, nunca 403)
            var docs = await PortalScope.DocumentsAsync(db, principal, entityType);
            var doc = docs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (doc.Payload is null) return Results.NotFound(new { error = "El documento no existe." });

            var source = await db.DocumentSources.FindAsync(type);
            if (source is null || !source.Active)
                return Results.NotFound(new { error = "Origen de documento no configurado." });

            var settings = await db.IntegrationSettings.FindAsync(1) ?? new IntegrationSettings();
            if (!settings.BcConfigured)
                return Results.Json(new { error = "La conexión con Business Central no está configurada." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var externalRef = DocumentProjections.Text(doc.Payload["externalReference"]);
            var endpoint = Encode(source.Endpoint
                .Replace("{id}", Uri.EscapeDataString(id))
                .Replace("{externalReference}", Uri.EscapeDataString(externalRef)));

            var res = await bc.GetAsync(settings, endpoint);
            if (!res.Ok)
                return Results.Json(new { error = $"BC devolvió HTTP {res.Status}." },
                    statusCode: StatusCodes.Status502BadGateway);

            string url;
            try
            {
                var transformed = JsonTransformService.Transform(source.Transformer, res.Body);
                using var t = JsonDocument.Parse(transformed);
                url = t.RootElement.TryGetProperty("url", out var u) ? (u.GetString() ?? "") : "";
            }
            catch (Exception ex) { return Results.Json(new { error = "Transformer inválido: " + ex.Message }, statusCode: 500); }

            if (string.IsNullOrWhiteSpace(url))
                return Results.NotFound(new { error = "BC no devolvió una URL de documento." });

            return Results.Ok(new { url });
        }).RequireAuthorization();
    }

    // El $filter de OData lleva espacios y '$'; se codifican para la URL.
    private static string Encode(string endpoint) => endpoint.Replace("$", "%24").Replace(" ", "%20");
}
