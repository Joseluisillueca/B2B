using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Portal;
using B2B.Api.Shop;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// Tarea 7: administración de la visibilidad del catálogo + cinta (ribbon).
// - GET/PUT /api/admin/visibility/{type}/{id}: las dos filas de CatalogVisibility a la
//   vez (bc = la fija el sync, solo lectura; manual = editable en /manage). El PUT solo
//   toca la manual y normaliza a slug (la moneda canónica de VisibilityScope).
// - PUT /api/admin/integration/ribbon: config cruda en IntegrationSettings.CatalogRibbonJson
//   (el GET de settings de IntegrationEndpoints la devuelve como catalogRibbon).
// - GET /api/shop/ribbon: las entradas de la cinta COMPUTADAS EN SERVIDOR para el actor,
//   sobre las facetas ya filtradas por su VisibilityScope (cero fugas de valores prohibidos).
public static class VisibilityEndpoints
{
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

            var manual = await db.CatalogVisibilities.FirstOrDefaultAsync(v =>
                v.SubjectType == type && v.SubjectId == id && v.Source == "manual");

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

        // ── La cinta computada para el actor (la llama el portal) ──────────────

        app.MapGet("/api/shop/ribbon", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var locale = DocumentProjections.Locale(request.Query["locale"]);

            // Take = 1: las facetas (Families/AttributeFacets) se computan sobre TODO el
            // catálogo filtrado por visibilidad ("all" en CatalogService.QueryAsync); Take
            // solo recorta Rows, que aquí no se usan.
            var prices = actor is null
                ? PortalActorPrices.Anonymous
                : new PortalActorPrices(actor.ClientId, actor.GroupIds);
            var page = await CatalogService.QueryAsync(db, prices,
                new CatalogQuery { Take = 1, Locale = locale }, DateTimeOffset.UtcNow, visibility);

            var settings = await db.IntegrationSettings.FindAsync(1);
            var config = ParseNode(settings?.CatalogRibbonJson) as JsonObject;

            // Candidatas: SIEMPRE nacidas de las facetas filtradas — jamás una entrada
            // que el actor no pueda ver. Sin config → solo familias (cinta autogenerada).
            var candidates = new List<RibbonCandidate>();
            foreach (var family in page.Families)
                candidates.Add(new("family:" + family.Id, "family", null, null, family.Label, family.Count));

            if (config?["attributes"] is JsonArray attributes)
                foreach (var wanted in attributes)
                {
                    var slug = CatalogVocabulary.Slug(Text(wanted));
                    if (slug.Length == 0) continue;
                    var facet = page.AttributeFacets.FirstOrDefault(f =>
                        string.Equals(f.KeySlug, slug, StringComparison.OrdinalIgnoreCase));
                    if (facet is null) continue;
                    foreach (var value in facet.Values)
                        candidates.Add(new($"attr:{facet.KeySlug}:{value.Slug}", "attr",
                            facet.KeySlug, value.Slug, value.Label, value.Count));
                }

            // Overrides por entrada: hidden → fuera; order → delante (los sin order al
            // final, en el orden natural de las facetas); titles → etiqueta del locale.
            var overrides = Overrides(config);
            var entries = candidates
                .Select((candidate, index) => (candidate, index,
                    over: overrides.GetValueOrDefault(candidate.Key.ToLowerInvariant())))
                .Where(x => x.over?.Hidden != true)
                .OrderBy(x => x.over?.Order ?? int.MaxValue)
                .ThenBy(x => x.index)
                .Select(x => new
                {
                    key = x.candidate.Key,
                    kind = x.candidate.Kind,
                    attributeId = x.candidate.AttributeId,
                    value = x.candidate.Value,
                    label = Title(x.over, locale) ?? x.candidate.Label,
                    count = x.candidate.Count,
                });

            return Results.Ok(new { locale, entries });
        }).RequireAuthorization();
    }

    // ── Proyección GET/PUT de visibilidad ──────────────────────────────────────
    // rules = las EFECTIVAS (misma precedencia que VisibilityStore.RulesForAsync: bc si
    // existe, si no manual, si no []); bcRules/manualRules = cada fila si existe, como
    // JSON parseado (la UI enseña "lo fija BC" y a la vez edita lo manual).
    private static async Task<object> ProjectAsync(AppDbContext db, string type, string id)
    {
        var rows = await db.CatalogVisibilities
            .Where(v => v.SubjectType == type && v.SubjectId == id)
            .ToListAsync();
        var bc = rows.FirstOrDefault(r => r.Source == "bc");
        var manual = rows.FirstOrDefault(r => r.Source == "manual");
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

    // ── Utilidades de la cinta ─────────────────────────────────────────────────

    private sealed record RibbonCandidate(
        string Key, string Kind, string? AttributeId, string? Value, string Label, int Count);

    private sealed record RibbonOverride(bool Hidden, int? Order, JsonObject? Titles);

    private static Dictionary<string, RibbonOverride> Overrides(JsonObject? config)
    {
        var result = new Dictionary<string, RibbonOverride>();
        if (config?["entries"] is not JsonArray entries) return result;
        foreach (var entry in entries)
        {
            var key = Text(entry?["key"]).ToLowerInvariant();
            if (key.Length == 0) continue;
            int? order = null;
            try { order = entry?["order"]?.GetValue<int>(); } catch { /* no numérico → sin orden */ }
            result[key] = new RibbonOverride(
                Hidden: (entry?["hidden"] as JsonValue)?.TryGetValue<bool>(out var hidden) == true && hidden,
                Order: order,
                Titles: entry?["titles"] as JsonObject);
        }
        return result;
    }

    /// Título del locale pedido, si está configurado; si no, null (cae al de la faceta)
    private static string? Title(RibbonOverride? over, string locale)
    {
        if (over?.Titles is not { } titles) return null;
        var title = Text(titles[locale]);
        return title.Length > 0 ? title : null;
    }

    private static string Text(JsonNode? node) =>
        (node as JsonValue)?.TryGetValue<string>(out var text) == true ? text : "";

    private static JsonNode? ParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static JsonArray? ParseArray(string? json) => ParseNode(json) as JsonArray;
}

public sealed record VisibilityRulesBody(JsonElement? Rules);
public sealed record RibbonBody(JsonElement? Ribbon);
