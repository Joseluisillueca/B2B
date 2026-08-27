using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Sync;

// Ingesta genérica de los PUT-upsert del conector BC (contrato 01 §2.3-2.4):
// idempotentes por id de la URL, body JSON (objeto o array), respuesta 2xx JSON.
public static class SyncEndpoints
{
    // Una ruta por cada campo "... URL" del Setup del conector (contrato 01 §4.2)
    private static readonly (string Route, string EntityType)[] UpsertRoutes =
    [
        ("/api/catalog/models/{id}", "model"),
        ("/api/catalog/products/{id}", "product"),
        ("/api/catalog/attributes/{id}", "attribute"),
        ("/api/catalog/categories/{id}", "category"),
        ("/api/catalog/families/{id}", "family"),
        ("/api/catalog/case-packs/{id}", "case-pack"),
        ("/api/catalog/offers/{id}", "offer"),
        ("/api/stock/inventory/{id}", "inventory"),
        ("/api/core/service-windows/{id}", "service-window"),
        ("/api/core/warehouses/{id}", "warehouse"),
        ("/api/core/payment-methods/{id}", "payment-method"),
        ("/api/clients/groups/{id}", "client-group"),
        ("/api/clients/{id}", "client"),
        ("/api/agents/{id}", "agent"),
        ("/api/orders/{id}", "order"),
        ("/api/documents/delivery-notes/{id}", "delivery-note"),
        ("/api/documents/invoices/{id}", "invoice"),
    ];

    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var (route, entityType) in UpsertRoutes)
        {
            var type = entityType;
            app.MapPut(route, (HttpRequest request, string id, AppDbContext db) =>
                UpsertAsync(db, request, type, id, parentId: null)).RequireConnector();
        }

        // Singleton de empresa: PUT sin id en la ruta (contrato 01 §4.2, Sync Company URL)
        app.MapPut("/api/core/b2binfo", (HttpRequest request, AppDbContext db) =>
            UpsertAsync(db, request, "company", "company", parentId: null)).RequireConnector();

        // Ofertas del conector: PUT a URL fija SIN id, body = array [{id, offerData}]
        // (contrato 03 §4.2-4.3). Cada elemento se guarda por su id raíz.
        app.MapPut("/api/catalog/offers", async (HttpRequest request, AppDbContext db) =>
        {
            string body;
            using (var reader = new StreamReader(request.Body))
                body = await reader.ReadToEndAsync();

            JsonNode? payload;
            try
            {
                payload = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "Body must be valid JSON" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var elements = payload switch
            {
                JsonArray array => array.OfType<JsonObject>().ToList(),
                JsonObject single => [single],
                _ => []
            };

            var now = DateTime.UtcNow;
            var received = 0;
            foreach (var element in elements)
            {
                var id = CatalogNormalizer.Text(element["id"]);
                if (id.Length == 0)
                    continue;
                await UpsertDocumentAsync(db, "offer", id, parentId: null,
                    element.ToJsonString(), element, now);
                received++;
            }
            await db.SaveChangesAsync();

            return Results.Ok(new { received });
        }).RequireConnector();

        // Imágenes de modelo. Además de guardar el documento (con la `uri` que usa el
        // catálogo), si el conector manda la foto en base64 (modo imagen activo en su
        // Setup) se almacena el binario en MediaAsset y se sirve en /media/models/{id}.jpg.
        // El base64 se quita del documento para no engordar la tabla de sync.
        app.MapPut("/api/catalog/model-images/{id}", async (HttpRequest request, string id, AppDbContext db) =>
        {
            string body;
            using (var reader = new StreamReader(request.Body))
                body = await reader.ReadToEndAsync();

            JsonNode? payload;
            try { payload = JsonNode.Parse(body); }
            catch (JsonException)
            { return Results.Json(new { error = "Body must be valid JSON" }, statusCode: StatusCodes.Status400BadRequest); }

            var now = DateTime.UtcNow;

            if (payload is JsonObject root && root["images"] is JsonArray images)
            {
                foreach (var entry in images.OfType<JsonObject>())
                {
                    if (entry["image"] is not JsonObject img) continue;
                    var b64 = CatalogNormalizer.Text(img["base64"]);
                    if (b64.Length == 0) b64 = CatalogNormalizer.Text(img["data"]);
                    if (b64.Length == 0) continue;

                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(b64); }
                    catch (FormatException) { continue; }

                    var asset = await db.MediaAssets.SingleOrDefaultAsync(a => a.ExternalId == id);
                    if (asset is null) { asset = new MediaAsset { ExternalId = id }; db.MediaAssets.Add(asset); }
                    asset.Bytes = bytes;
                    asset.ContentType = CatalogNormalizer.Text(img["contentType"]) is { Length: > 0 } ct ? ct : "image/jpeg";
                    asset.UpdatedAt = now;

                    img.Remove("base64");
                    img.Remove("data");
                    break;   // una imagen por modelo
                }
            }

            await UpsertDocumentAsync(db, "model-image", id, parentId: null,
                payload?.ToJsonString() ?? body, payload, now);
            await db.SaveChangesAsync();
            return Results.Ok(new { id });
        }).RequireConnector();

        // Servir la foto alojada. Público (las imágenes de catálogo no llevan token) y
        // cacheable. La URL que manda el conector es /media/models/{SystemId}.jpg.
        app.MapGet("/media/models/{id}", async (string id, HttpResponse response, AppDbContext db) =>
        {
            var key = id.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
            var asset = await db.MediaAssets.SingleOrDefaultAsync(a => a.ExternalId == key);
            if (asset is null || asset.Bytes.Length == 0)
                return Results.NotFound();
            response.Headers.CacheControl = "public, max-age=3600";
            return Results.File(asset.Bytes, asset.ContentType);
        });

        // Rutas que el conector deriva de la URL de clientes con sufijos hardcodeados (contrato 04)
        // El usuario admin, además de guardarse crudo, provisiona el AppUser del portal.
        app.MapPut("/api/clients/{clientId}/users/admin", (HttpRequest request, string clientId, AppDbContext db) =>
            UpsertAsync(db, request, "client-user", clientId, parentId: clientId)).RequireConnector();

        app.MapPut("/api/clients/{clientId}/shipping-addresses/{id}", (HttpRequest request, string clientId, string id, AppDbContext db) =>
            UpsertAsync(db, request, "shipping-address", id, parentId: clientId)).RequireConnector();
    }

    private static async Task<IResult> UpsertAsync(
        AppDbContext db, HttpRequest request, string entityType, string externalId, string? parentId)
    {
        string body;
        using (var reader = new StreamReader(request.Body))
            body = await reader.ReadToEndAsync();

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "Body must be valid JSON" }, statusCode: StatusCodes.Status400BadRequest);
        }

        await UpsertDocumentAsync(db, entityType, externalId, parentId, body, payload, DateTime.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(new { id = externalId });
    }

    // Documentos que pertenecen a un cliente: el conector no los cuelga de nadie en
    // la URL (el id del recurso es el SystemId del documento), pero el dueño viene
    // dentro, en `clientId`. Guardarlo en ParentId — el mismo sitio donde ya cuelgan
    // las direcciones de envío — es lo que permite que el portal filtre por cliente
    // en SQL en vez de leerse todos los pedidos del mundo (Fase 3).
    private static readonly string[] ClientOwnedDocuments = ["order", "delivery-note", "invoice"];

    private static async Task UpsertDocumentAsync(
        AppDbContext db, string entityType, string externalId, string? parentId,
        string body, JsonNode? payload, DateTime now)
    {
        if (parentId is null && ClientOwnedDocuments.Contains(entityType))
            parentId = CatalogNormalizer.Text(payload?["clientId"]) is { Length: > 0 } owner ? owner : null;

        var doc = await db.SyncDocuments
            .SingleOrDefaultAsync(d => d.EntityType == entityType && d.ExternalId == externalId);
        if (doc is null)
        {
            db.SyncDocuments.Add(new SyncDocument
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                ExternalId = externalId,
                ParentId = parentId,
                Payload = body,
                FirstReceivedAt = now,
                LastReceivedAt = now
            });
        }
        else
        {
            doc.Payload = body;
            doc.ParentId = parentId;
            doc.LastReceivedAt = now;
        }

        // Crudo y normalizado se guardan en el mismo SaveChanges: nunca divergen
        CatalogNormalizer.Normalize(db, entityType, externalId, payload);
        await ClientIdentity.ApplyAsync(db, entityType, externalId, payload);
    }
}
