using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// Gestión de pedidos desde el CMS (portal autónomo). El pedido nativo se guarda como
// documento "order"; aquí el admin avanza su estado (confirmar/servir/facturar/cancelar)
// sin ERP. Lo lee el cliente en /orders con el estado que fije el admin.
public static class OrderAdminEndpoints
{
    public static void MapOrderAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/admin/orders/{id}/status", async (string id, OrderStatusRequest? body, AppDbContext db) =>
        {
            var status = (body?.Status ?? "").Trim().ToLowerInvariant();
            if (!DocumentProjections.OrderStatuses.Contains(status))
                return Results.BadRequest(new { error = "Estado no válido.", allowed = DocumentProjections.OrderStatuses });

            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == "order" && d.ExternalId == id);
            if (doc is null)
                return Results.NotFound(new { error = "El pedido no existe." });

            JsonObject payload;
            try { payload = JsonNode.Parse(doc.Payload) as JsonObject ?? new JsonObject(); }
            catch (System.Text.Json.JsonException) { payload = new JsonObject(); }

            payload["status"] = status;
            // El estado de las líneas sigue al de la cabecera para que el detalle sea coherente
            var lineStatus = status switch
            {
                "shipped" or "invoiced" => "Delivered",
                "partially-shipped" => "Partial",
                "canceled" => "Canceled",
                _ => "Open"
            };
            foreach (var item in (payload["items"] as JsonArray ?? []).OfType<JsonObject>())
                item["status"] = lineStatus;

            doc.Payload = payload.ToJsonString();
            doc.LastReceivedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { id, status });
        }).RequireAdmin();

        // Retirar un pedido (erróneo, duplicado o de prueba). Cancelar = cambiar estado;
        // esto lo elimina de verdad del portal.
        app.MapDelete("/api/admin/orders/{id}", async (string id, AppDbContext db) =>
        {
            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == "order" && d.ExternalId == id);
            if (doc is null)
                return Results.NotFound(new { error = "El pedido no existe." });
            db.SyncDocuments.Remove(doc);

            // El pedido nativo del portal nace con el Cart (su id = ExternalId del doc):
            // al retirar el pedido se retira también ese carrito para no dejar huérfanos.
            if (Guid.TryParse(id, out var cartId))
            {
                var cart = await db.Carts.SingleOrDefaultAsync(c => c.Id == cartId);
                if (cart is not null) db.Carts.Remove(cart);
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();
    }
}

public sealed record OrderStatusRequest(string? Status);
