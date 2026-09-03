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
        foreach (var (attr, allowed) in _allowed)
        {
            var value = attr == "familyid" ? model.FamilyId : AttributeValue(model, attr);
            if (value is null || !allowed.Contains(Slug(value))) return false;
        }
        return true;
    }

    private static string? AttributeValue(CatalogModel model, string attrSlug)
    {
        try
        {
            if (JsonNode.Parse(model.AttributesJson ?? "{}") is not JsonObject obj) return null;
            foreach (var (key, node) in obj)
                if (Slug(key) == attrSlug) return node?.GetValue<string>();
        }
        catch { /* atributos rotos → como si no existieran */ }
        return null;
    }

    private static Dictionary<string, HashSet<string>>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return null;
            var result = new Dictionary<string, HashSet<string>>();
            foreach (var item in arr)
            {
                var attr = Slug(item?["attributeId"]?.GetValue<string>() ?? "");
                if (attr.Length == 0 || item?["valueIds"] is not JsonArray values) continue;
                var set = result.TryGetValue(attr, out var existing)
                    ? existing : result[attr] = new HashSet<string>(StringComparer.Ordinal);
                foreach (var v in values)
                    if (v?.GetValue<string>() is { Length: > 0 } s) set.Add(Slug(s));
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    // Paridad con SanitizeId del conector (Cod80114): minúsculas; espacio / \ _ . → '-'.
    public static string Slug(string text)
    {
        var chars = text.Trim().ToLowerInvariant().Select(c =>
            c is ' ' or '/' or '\\' or '_' or '.' ? '-' : c);
        return new string(chars.ToArray());
    }
}
