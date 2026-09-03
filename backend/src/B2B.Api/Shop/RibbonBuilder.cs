using System.Text.Json.Nodes;
using B2B.Api.Admin;
using B2B.Api.Sync;

namespace B2B.Api.Shop;

/// Una entrada de la cinta del catálogo (banda bajo CATÁLOGO|LOOKBOOK). `Raw` es el valor
/// CRUDO de BC (lo que el front manda tal cual en a.{clave}=), `Label` la etiqueta ya con
/// los overrides de títulos de /manage aplicados.
public sealed record RibbonEntry(
    string Key, string Kind, string? AttributeId, string? Value, string Raw, string Label, int Count);

// Las entradas de la cinta, COMPUTADAS EN SERVIDOR para el actor: nacen de las facetas
// del SURTIDO COMPLETO del actor (CatalogPage.Ribbon: post-visibilidad, sin filtros de
// query — la cinta es navegación y es ESTABLE, 14a-8; cero fugas de valores prohibidos)
// y se les aplica la config de /manage (IntegrationSettings.CatalogRibbonJson: atributos
// que la alimentan + overrides hidden/order/titles por entrada). Sin config → solo
// familias. La usan GET /api/shop/ribbon (/manage, vista previa) y GET /api/shop/catalog
// (14a-4: la cinta viaja con el catálogo, sin segunda petición ni salto de layout).
public static class RibbonBuilder
{
    public static IReadOnlyList<RibbonEntry> Build(CatalogPage page, string? ribbonConfigJson, string locale)
    {
        var config = VisibilityEndpoints.ParseNode(ribbonConfigJson) as JsonObject;
        var facets = page.Ribbon;

        // Candidatas: SIEMPRE nacidas de las facetas filtradas por visibilidad — jamás una
        // entrada que el actor no pueda ver. Sin config → solo familias (autogenerada).
        var candidates = new List<RibbonEntry>();
        foreach (var family in facets.Families)
            candidates.Add(new("family:" + family.Id, "family", null, null, Raw: family.Id, family.Label, family.Count));

        if (config?["attributes"] is JsonArray attributes)
            foreach (var wanted in attributes)
            {
                var slug = CatalogVocabulary.Slug(CatalogNormalizer.Text(wanted));
                if (slug.Length == 0) continue;
                var facet = facets.AttributeFacets.FirstOrDefault(f =>
                    string.Equals(f.KeySlug, slug, StringComparison.OrdinalIgnoreCase));
                if (facet is null) continue;
                foreach (var value in facet.Values)
                    candidates.Add(new($"attr:{facet.KeySlug}:{value.Slug}", "attr", facet.KeySlug, value.Slug,
                        // Raw = el valor CRUDO de BC (Value/Label de la faceta, ANTES de los
                        // overrides de títulos): es lo que el filtro a.{clave}= compara tal cual.
                        Raw: value.Value, value.Label, value.Count));
            }

        // Overrides por entrada: hidden → fuera; order → delante (los sin order al
        // final, en el orden natural de las facetas); titles → etiqueta del locale.
        var overrides = Overrides(config);
        return [.. candidates
            .Select((candidate, index) => (candidate, index,
                over: overrides.GetValueOrDefault(candidate.Key.ToLowerInvariant())))
            .Where(x => x.over?.Hidden != true)
            .OrderBy(x => x.over?.Order ?? int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.candidate with { Label = Title(x.over, locale) ?? x.candidate.Label })];
    }

    private sealed record RibbonOverride(bool Hidden, int? Order, JsonObject? Titles);

    private static Dictionary<string, RibbonOverride> Overrides(JsonObject? config)
    {
        var result = new Dictionary<string, RibbonOverride>();
        if (config?["entries"] is not JsonArray entries) return result;
        foreach (var entry in entries)
        {
            // Config guardada con basura ("oops", 5 en entries): un elemento que no sea
            // objeto se IGNORA — indexarlo lanzaría InvalidOperationException y tumbaría
            // la cinta de TODOS los actores (fix de revisión).
            if (entry is not JsonObject over) continue;
            var key = CatalogNormalizer.Text(over["key"]).ToLowerInvariant();
            if (key.Length == 0) continue;
            int? order = null;
            try { order = over["order"]?.GetValue<int>(); } catch { /* no numérico → sin orden */ }
            result[key] = new RibbonOverride(
                Hidden: (over["hidden"] as JsonValue)?.TryGetValue<bool>(out var hidden) == true && hidden,
                Order: order,
                Titles: over["titles"] as JsonObject);
        }
        return result;
    }

    /// Título del locale pedido, si está configurado; si no, null (cae al de la faceta)
    private static string? Title(RibbonOverride? over, string locale)
    {
        if (over?.Titles is not { } titles) return null;
        var title = CatalogNormalizer.Text(titles[locale]);
        return title.Length > 0 ? title : null;
    }
}
