using System.Text.Json.Nodes;
using B2B.Api.Data;

namespace B2B.Api.Shop;

// Predicado ÚNICO de visibilidad del catálogo. Lista blanca POR ATRIBUTO:
// si hay regla para un atributo, el modelo debe TENERLO con un valor permitido
// (whitelist estricta: sin el atributo → oculto). "familyId" es pseudo-atributo
// contra CatalogModel.FamilyId. Varias fuentes de reglas (agente + cliente en
// suplantación) = INTERSECCIÓN. Claves y valores comparados en slug (paridad
// con el SanitizeId del conector BC).
public sealed class VisibilityScope
{
    public static readonly VisibilityScope Unrestricted = new(null);

    // attributeId(slug) -> valores permitidos (slug). null = sin restricción.
    private readonly Dictionary<string, HashSet<string>>? _allowed;
    private VisibilityScope(Dictionary<string, HashSet<string>>? allowed) => _allowed = allowed;

    public bool IsRestricted => _allowed is { Count: > 0 };

    public static VisibilityScope FromRules(IEnumerable<string?> rulesJsonPerSubject)
    {
        Dictionary<string, HashSet<string>>? merged = null;
        foreach (var json in rulesJsonPerSubject)
        {
            var parsed = Parse(json);
            if (parsed is null) continue;                    // sin reglas / roto → no restringe
            if (merged is null) { merged = parsed; continue; }
            foreach (var (attr, values) in parsed)           // intersección por atributo
            {
                if (merged.TryGetValue(attr, out var mine)) mine.IntersectWith(values);
                else merged[attr] = values;
            }
        }
        return merged is { Count: > 0 } ? new VisibilityScope(merged) : Unrestricted;
    }

    public bool Visible(CatalogModel model)
    {
        if (_allowed is null) return true;
        Dictionary<string, string>? attrs = null;
        foreach (var (attr, allowed) in _allowed)
        {
            var value = attr == "familyid" ? model.FamilyId : (attrs ??= ParseAttributes(model)).GetValueOrDefault(attr);
            if (value is null || !allowed.Contains(CatalogVocabulary.Slug(value))) return false;
        }
        return true;
    }

    // Parseo único del payload de atributos por modelo (evitamos re-parsear por cada
    // atributo restringido). Claves ya en slug; valores no-string se ignoran.
    private static Dictionary<string, string> ParseAttributes(CatalogModel model)
    {
        var result = new Dictionary<string, string>();
        try
        {
            if (JsonNode.Parse(model.AttributesJson ?? "{}") is JsonObject obj)
                foreach (var (key, node) in obj)
                {
                    try { if (node?.GetValue<string>() is { } s) result[CatalogVocabulary.Slug(key)] = s; }
                    catch { /* valor no-string → se ignora */ }
                }
        }
        catch { /* JSON roto → como si no hubiera atributos */ }
        return result;
    }

    // Cada ítem se captura POR SEPARADO (14a-2): una regla rota no puede dejar al sujeto
    // sin restricción (fail-open) arrastrando a las válidas. Solo el JSON ilegible
    // entero devuelve null (sin reglas).
    private static Dictionary<string, HashSet<string>>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JsonArray arr;
        try
        {
            if (JsonNode.Parse(json) is not JsonArray parsed) return null;
            arr = parsed;
        }
        catch { return null; }

        var result = new Dictionary<string, HashSet<string>>();
        foreach (var item in arr)
        {
            try
            {
                var attr = CatalogVocabulary.Slug(Text(item?["attributeId"]));
                if (attr.Length == 0 || item?["valueIds"] is not JsonArray values) continue;
                var slugs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var v in values)
                    if (Text(v) is { Length: > 0 } s) slugs.Add(CatalogVocabulary.Slug(s));
                if (slugs.Count == 0) continue;   // regla configurada con valueIds vacío → se ignora
                if (result.TryGetValue(attr, out var existing)) existing.UnionWith(slugs);
                else result[attr] = slugs;
            }
            catch { /* ítem ilegible → se descarta solo él */ }
        }
        return result.Count > 0 ? result : null;
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
