using System.Text.Json.Nodes;

namespace B2B.Api.Shop;

// Normalización ÚNICA de las reglas de visibilidad [{attributeId, valueIds[]}] a la
// moneda canónica (slug), la misma con la que compara VisibilityScope y la que emite el
// conector de BC. La usan el admin (PUT /api/admin/visibility, que rechaza el body
// entero si hay CUALQUIER error) y la ingesta del sync (VisibilityStore, que descarta
// los ítems inválidos con aviso y conserva los válidos: fail-closed, nunca "sin reglas").
public static class VisibilityRules
{
    public const int MaxRules = 200;
    public const int MaxValueIds = 500;

    /// Reglas válidas ya normalizadas + un error legible por cada ítem (o el conjunto)
    /// descartado. `Valid` vacío con `Errors` vacío = el array venía vacío.
    public sealed record Result(JsonArray Valid, IReadOnlyList<string> Errors);

    public static Result Normalize(JsonNode? rules)
    {
        var valid = new JsonArray();
        var errors = new List<string>();

        if (rules is not JsonArray array)
        {
            errors.Add("rules debe ser un array de reglas [{attributeId, valueIds[]}].");
            return new Result(valid, errors);
        }
        if (array.Count > MaxRules)
        {
            errors.Add($"Demasiadas reglas (máx. {MaxRules}).");
            return new Result(valid, errors);
        }

        foreach (var item in array)
        {
            // Cada ítem se valida por separado: uno roto NUNCA arrastra a los demás.
            try
            {
                if (NormalizeItem(item, out var error) is { } rule) valid.Add(rule);
                else errors.Add(error!);
            }
            catch (Exception ex)
            {
                errors.Add($"Regla ilegible: {ex.Message}");
            }
        }
        return new Result(valid, errors);
    }

    private static JsonObject? NormalizeItem(JsonNode? item, out string? error)
    {
        error = null;
        if (item is not JsonObject rule)
        {
            error = "Cada regla debe ser un objeto {attributeId, valueIds[]}.";
            return null;
        }

        var attributeText = Text(rule["attributeId"]);
        var attributeSlug = CatalogVocabulary.Slug(attributeText);
        if (attributeSlug.Length == 0)
        {
            error = "Cada regla necesita un attributeId (texto no vacío).";
            return null;
        }
        if (rule["valueIds"] is not JsonArray values)
        {
            error = $"La regla de \"{attributeText}\" necesita valueIds como array de textos.";
            return null;
        }
        if (values.Count > MaxValueIds)
        {
            error = $"Demasiados valueIds en \"{attributeText}\" (máx. {MaxValueIds}).";
            return null;
        }

        var slugs = new JsonArray();
        foreach (var value in values)
        {
            var slug = CatalogVocabulary.Slug(Text(value));
            if (slug.Length == 0)
            {
                error = $"valueIds de \"{attributeText}\" solo admite textos no vacíos.";
                return null;
            }
            slugs.Add(slug);
        }

        return new JsonObject { ["attributeId"] = attributeSlug, ["valueIds"] = slugs };
    }

    // Solo textos: un número, objeto o null cuenta como vacío (→ ítem inválido).
    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
