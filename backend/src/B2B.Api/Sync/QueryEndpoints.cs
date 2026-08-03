using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Sync;

// Lecturas que hace el conector BC: GET de ofertas por modelo para el ciclo
// GET-comparar-DELETE (contrato 01 §3.4) y búsqueda de pedidos (contrato 04 §6).
// Ambas llegan como GET con body JSON — peculiaridad del conector que hay que aceptar.
public static class QueryEndpoints
{
    public static void MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/catalog/offers", ["GET"], async (HttpRequest request, AppDbContext db) =>
        {
            string? modelId;
            try
            {
                var body = await ReadBodyAsync(request);
                modelId = body is null ? null : (JsonNode.Parse(body) as JsonObject)?["modelId"]?.GetValue<string>();
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "Body must be valid JSON" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var offers = await db.SyncDocuments.Where(d => d.EntityType == "offer").ToListAsync();
            var items = offers
                .Where(o => modelId is null
                    || string.Equals(ExtractModelId(o.Payload), modelId, StringComparison.OrdinalIgnoreCase))
                .Select(o => new { id = o.ExternalId })
                .ToList();

            return Results.Ok(new { items });
        }).RequireAuthorization();

        app.MapDelete("/api/catalog/offers/{id}", async (string id, AppDbContext db) =>
        {
            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == "offer" && d.ExternalId == id);
            if (doc is not null)
            {
                db.SyncDocuments.Remove(doc);
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { id, deleted = doc is not null });
        }).RequireAuthorization();

        app.MapMethods("/api/orders/search", ["GET", "POST"], async (HttpRequest request, AppDbContext db) =>
        {
            // El conector envía {"search":[{"all":true}]} y espera todos los pedidos;
            // la variante con status está comentada en su código, así que no se filtra aún.
            _ = await ReadBodyAsync(request);

            var orders = await db.SyncDocuments.Where(d => d.EntityType == "order").ToListAsync();
            var items = new JsonArray(orders.Select(o => JsonNode.Parse(o.Payload)).ToArray());
            return Results.Ok(new JsonObject { ["items"] = items });
        }).RequireAuthorization();
    }

    private static async Task<string?> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static string? ExtractModelId(string payload)
    {
        try
        {
            return (JsonNode.Parse(payload) as JsonObject)?["modelId"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
