using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// Imágenes de producto para marketing. La imagen de un modelo del catálogo sale de
// un documento de sync "model-image" cuyo ExternalId es el del modelo; el catálogo
// lee payload["images"][0]["image"]["uri"] en cada petición (Shop/CatalogQuery.cs).
// Aquí el CMS ASIGNA esa imagen sin depender de que BC la sincronice: escribe el
// mismo documento y el catálogo la refleja de inmediato (no hay caché).
public static class ModelImageEndpoints
{
    private const string EntityType = "model-image";

    public static void MapModelImageEndpoints(this IEndpointRouteBuilder app)
    {
        // Todos los modelos del catálogo con su imagen actual (si la tienen). Une
        // CatalogModels con los documentos model-image por ExternalId.
        app.MapGet("/api/admin/model-images", async (AppDbContext db) =>
        {
            var models = await db.CatalogModels
                .OrderBy(m => m.ExternalReference)
                .ThenBy(m => m.Name)
                .ToListAsync();
            var imageDocs = await db.SyncDocuments
                .Where(d => d.EntityType == EntityType)
                .ToListAsync();
            var imageByModel = imageDocs.ToDictionary(
                d => d.ExternalId, ExtractUri, StringComparer.OrdinalIgnoreCase);

            var items = models.Select(m => new
            {
                externalId = m.ExternalId,
                reference = m.ExternalReference,
                name = m.Name,
                imageUri = imageByModel.GetValueOrDefault(m.ExternalId)
            });

            return Results.Ok(new { items });
        }).RequireAdmin();

        // Fija/actualiza la imagen del modelo (upsert del documento model-image).
        // La uri puede ser absoluta (https://…) o una ruta /media/portal/… subida
        // por el propio CMS con POST /api/admin/media.
        app.MapPut("/api/admin/model-images/{modelExternalId}",
            async (string modelExternalId, ModelImageRequest? body, AppDbContext db) =>
        {
            var uri = body?.Uri?.Trim();
            if (string.IsNullOrWhiteSpace(uri))
                return Results.BadRequest(new { error = "Indica la URL o la ruta de la imagen." });

            var payload = new JsonObject
            {
                ["images"] = new JsonArray
                {
                    new JsonObject { ["image"] = new JsonObject { ["uri"] = uri } }
                }
            }.ToJsonString();

            var now = DateTime.UtcNow;
            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == EntityType && d.ExternalId == modelExternalId);
            if (doc is null)
            {
                db.SyncDocuments.Add(new SyncDocument
                {
                    Id = Guid.NewGuid(),
                    EntityType = EntityType,
                    ExternalId = modelExternalId,
                    Payload = payload,
                    FirstReceivedAt = now,
                    LastReceivedAt = now
                });
            }
            else
            {
                doc.Payload = payload;
                doc.LastReceivedAt = now;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { externalId = modelExternalId, imageUri = uri });
        }).RequireAdmin();

        // Quita la imagen del modelo: borra el documento y el catálogo vuelve a placeholder.
        app.MapDelete("/api/admin/model-images/{modelExternalId}",
            async (string modelExternalId, AppDbContext db) =>
        {
            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == EntityType && d.ExternalId == modelExternalId);
            if (doc is null)
                return Results.NotFound(new { error = "Este modelo no tiene imagen asignada." });

            db.SyncDocuments.Remove(doc);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();
    }

    // Mismo formato que lee el catálogo: payload["images"][0]["image"]["uri"]
    private static string? ExtractUri(SyncDocument doc)
    {
        try
        {
            return (JsonNode.Parse(doc.Payload) as JsonObject)?
                ["images"]?[0]?["image"]?["uri"]?.GetValue<string>();
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}

public sealed record ModelImageRequest(string? Uri);
