using B2B.Api.Auth;
using System.Security.Claims;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// CMS de contenido del portal (plan §3): "Web → Portada". El administrador publica
// aquí los banners del carrusel y las tarjetas de acceso; el portal los lee filtrados
// por su ventana de publicación en /api/portal/content/{key}.
public static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        // Índice para la navegación del CMS: claves editables y bloques ya guardados
        app.MapGet("/api/admin/content", async (AppDbContext db) =>
        {
            var blocks = await db.PortalContents
                .OrderBy(c => c.Key).ThenBy(c => c.Locale)
                .ToListAsync();

            var items = blocks.Select(block => new
            {
                key = block.Key,
                locale = block.Locale,
                count = (PortalContentModel.Parse(block.Json) as JsonArray)?.Count ?? 0,
                updatedAt = block.UpdatedAt,
                updatedBy = block.UpdatedBy
            });

            return Results.Ok(new
            {
                keys = PortalContentModel.Keys,
                locales = PortalContentModel.Locales,
                items
            });
        }).RequireAdmin();

        // El CMS ve el bloque entero, incluidos los elementos apagados o caducados
        app.MapGet("/api/admin/content/{key}", async (string key, string? locale, AppDbContext db) =>
        {
            if (Invalid(key, locale, out var normalizedLocale, out var problem)) return problem;

            var block = await db.PortalContents
                .SingleOrDefaultAsync(c => c.Key == key && c.Locale == normalizedLocale);
            if (block is null)
                return Results.NotFound(new { error = "El bloque todavía no tiene contenido." });

            return Results.Ok(new
            {
                key = block.Key,
                locale = block.Locale,
                items = PortalContentModel.Parse(block.Json) ?? new JsonArray(),
                updatedAt = block.UpdatedAt,
                updatedBy = block.UpdatedBy
            });
        }).RequireAdmin();

        app.MapPut("/api/admin/content/{key}", async (
            string key, string? locale, JsonNode? body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            if (Invalid(key, locale, out var normalizedLocale, out var problem)) return problem;

            if (!PortalContentModel.TryNormalize(key, body, out var items, out var error))
                return Results.BadRequest(new { error });

            var block = await db.PortalContents
                .SingleOrDefaultAsync(c => c.Key == key && c.Locale == normalizedLocale);
            if (block is null)
            {
                block = new PortalContent { Key = key, Locale = normalizedLocale! };
                db.PortalContents.Add(block);
            }

            block.Json = items.ToJsonString();
            block.UpdatedAt = DateTime.UtcNow;
            block.UpdatedBy = principal.FindFirstValue(ClaimTypes.Email)
                              ?? principal.FindFirstValue("email")
                              ?? principal.Identity?.Name;
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                key = block.Key,
                locale = block.Locale,
                items = (JsonNode)items,
                updatedAt = block.UpdatedAt,
                updatedBy = block.UpdatedBy
            });
        }).RequireAdmin();

        app.MapDelete("/api/admin/content/{key}", async (string key, string? locale, AppDbContext db) =>
        {
            if (Invalid(key, locale, out var normalizedLocale, out var problem)) return problem;

            var block = await db.PortalContents
                .SingleOrDefaultAsync(c => c.Key == key && c.Locale == normalizedLocale);
            if (block is null)
                return Results.NotFound(new { error = "El bloque todavía no tiene contenido." });

            db.PortalContents.Remove(block);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();
    }

    // Clave e idioma son parte del contrato: fuera de la lista, 400 (no 404), porque
    // el error está en la petición, no en el contenido.
    private static bool Invalid(string key, string? locale, out string? normalized, out IResult problem)
    {
        normalized = null;
        problem = Results.Empty;

        if (!PortalContentModel.IsKnownKey(key))
        {
            problem = Results.BadRequest(new
            {
                error = $"Clave de contenido desconocida. Válidas: {string.Join(", ", PortalContentModel.Keys)}."
            });
            return true;
        }

        normalized = PortalContentModel.NormalizeLocale(locale);
        if (normalized is null)
        {
            problem = Results.BadRequest(new
            {
                error = $"Idioma no soportado. Válidos: {string.Join(", ", PortalContentModel.Locales)}."
            });
            return true;
        }

        return false;
    }
}
