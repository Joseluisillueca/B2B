using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Shop;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// Tarea 7: administración de la visibilidad del catálogo + config de la cinta (ribbon).
// - GET/PUT /api/admin/visibility/{type}/{id}: las dos filas de CatalogVisibility a la
//   vez (bc = la fija el sync, solo lectura; manual = editable en /manage). El PUT solo
//   toca la manual y normaliza a slug (la moneda canónica de VisibilityScope).
// - PUT /api/admin/integration/ribbon: config cruda en IntegrationSettings.CatalogRibbonJson
//   (el GET de settings de IntegrationEndpoints la devuelve como catalogRibbon).
// La cinta computada por actor vive en Shop: GET /api/shop/ribbon (ShopEndpoints).
public static class VisibilityEndpoints
{
    private const int MaxRules = 200;
    private const int MaxValueIds = 500;

    public static void MapVisibilityEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Visibilidad por sujeto (cliente o agente) ──────────────────────────

        app.MapGet("/api/admin/visibility/{type}/{id}", async (string type, string id, AppDbContext db) =>
        {
            if (!ValidSubject(type)) return BadType();
            return Results.Ok(await ProjectAsync(db, type, id));
        }).RequireAdmin();

        app.MapPut("/api/admin/visibility/{type}/{id}",
            async (string type, string id, VisibilityRulesBody body, AppDbContext db) =>
        {
            if (!ValidSubject(type)) return BadType();
            var (normalized, error) = Normalize(body.Rules);
            if (error is not null) return Results.BadRequest(new { error });

            var (_, manual) = await VisibilityStore.RowsForAsync(db, type, id);

            if (normalized!.Count == 0)
            {
                // rules: [] → se retira la restricción manual. La fila bc NUNCA se toca.
                if (manual is not null) db.CatalogVisibilities.Remove(manual);
            }
            else if (manual is null)
            {
                db.CatalogVisibilities.Add(new CatalogVisibility
                {
                    SubjectType = type, SubjectId = id, Source = "manual",
                    RulesJson = normalized.ToJsonString()
                });
            }
            else
            {
                manual.RulesJson = normalized.ToJsonString();
                manual.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            return Results.Ok(await ProjectAsync(db, type, id));
        }).RequireAdmin();

        // ── Config de la cinta (JSON crudo en IntegrationSettings) ─────────────

        app.MapPut("/api/admin/integration/ribbon", async (RibbonBody body, AppDbContext db) =>
        {
            string? json = null;
            if (body.Ribbon is { } ribbon && ribbon.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                if (ribbon.ValueKind != JsonValueKind.Object)
                    return Results.BadRequest(new { error = "ribbon debe ser un objeto (o null para limpiar)." });
                // {} también limpia: una config vacía no configura nada.
                if (ribbon.EnumerateObject().Any()) json = ribbon.GetRawText();
            }

            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.CatalogRibbonJson = json;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, catalogRibbon = ParseNode(json) });
        }).RequireAdmin();
    }

    // ── Proyección GET/PUT de visibilidad ──────────────────────────────────────
    // rules = las EFECTIVAS (misma precedencia que el runtime, resuelta en
    // VisibilityStore.RowsForAsync: bc si existe, si no manual, si no []);
    // bcRules/manualRules = cada fila si existe, como JSON parseado (la UI enseña
    // "lo fija BC" y a la vez edita lo manual).
    private static async Task<object> ProjectAsync(AppDbContext db, string type, string id)
    {
        var (bc, manual) = await VisibilityStore.RowsForAsync(db, type, id);
        var effective = bc ?? manual;
        return new
        {
            source = effective?.Source,
            rules = ParseArray(effective?.RulesJson) ?? [],
            bcRules = bc is null ? null : ParseArray(bc.RulesJson) ?? [],
            manualRules = manual is null ? null : ParseArray(manual.RulesJson) ?? [],
        };
    }

    private static bool ValidSubject(string type) => type is "client" or "agent";

    private static IResult BadType() =>
        Results.BadRequest(new { error = "Tipo de sujeto no válido (client | agent)." });

    // Valida y normaliza el body del PUT a la moneda canónica (slug), la misma con la
    // que compara VisibilityScope y la que emite el conector de BC.
    private static (JsonArray? Normalized, string? Error) Normalize(JsonElement? rules)
    {
        if (rules is not { ValueKind: JsonValueKind.Array } array)
            return (null, "rules debe ser un array de reglas [{attributeId, valueIds[]}].");
        if (array.GetArrayLength() > MaxRules)
            return (null, $"Demasiadas reglas (máx. {MaxRules}).");

        var normalized = new JsonArray();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return (null, "Cada regla debe ser un objeto {attributeId, valueIds[]}.");
            if (!item.TryGetProperty("attributeId", out var attribute)
                || attribute.ValueKind != JsonValueKind.String
                || CatalogVocabulary.Slug(attribute.GetString() ?? "") is not { Length: > 0 } attributeSlug)
                return (null, "Cada regla necesita un attributeId (texto no vacío).");
            if (!item.TryGetProperty("valueIds", out var values) || values.ValueKind != JsonValueKind.Array)
                return (null, $"La regla de \"{attribute.GetString()}\" necesita valueIds como array de textos.");
            if (values.GetArrayLength() > MaxValueIds)
                return (null, $"Demasiados valueIds en \"{attribute.GetString()}\" (máx. {MaxValueIds}).");

            var slugs = new JsonArray();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String
                    || CatalogVocabulary.Slug(value.GetString() ?? "") is not { Length: > 0 } valueSlug)
                    return (null, $"valueIds de \"{attribute.GetString()}\" solo admite textos no vacíos.");
                slugs.Add(valueSlug);
            }

            normalized.Add(new JsonObject { ["attributeId"] = attributeSlug, ["valueIds"] = slugs });
        }
        return (normalized, null);
    }

    /// Parse defensivo compartido (settings/reglas guardados): JSON roto o vacío → null.
    /// Lo usan también IntegrationEndpoints (catalogRibbon del GET settings) y
    /// ShopEndpoints (la config de la cinta en /api/shop/ribbon).
    internal static JsonNode? ParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static JsonArray? ParseArray(string? json) => ParseNode(json) as JsonArray;
}

public sealed record VisibilityRulesBody(JsonElement? Rules);
public sealed record RibbonBody(JsonElement? Ribbon);
