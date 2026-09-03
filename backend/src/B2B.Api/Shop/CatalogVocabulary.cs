using System.Text;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Shop;

// Auditoría M-1. El vocabulario del rail del catálogo (familias y atributos) no vive
// en las tablas de dominio: llega como documentos del sync —"family" (contrato 02 §7)
// y "attribute" (§6)—, cada uno con su nombre multiidioma. Esta clase los indexa una
// vez por petición y resuelve la etiqueta en el idioma pedido.
//
// Ojo con la clave de los atributos: el modelo los trae con el **Name** del atributo
// ("Grupo de edad") o con su **B2B Code** ("grupo-de-edad") según la fuente (contrato
// 02 §2), así que el índice acepta las dos formas y también el código sanitizado.
public sealed class CatalogVocabulary
{
    private readonly Dictionary<string, JsonNode?> _families;
    private readonly Dictionary<string, JsonNode?> _attributes;
    // C1 (revisión AL): alias (slug) → código canónico (slug del B2B Code) del atributo.
    private readonly Dictionary<string, string> _attributeCodes;

    private CatalogVocabulary(
        Dictionary<string, JsonNode?> families, Dictionary<string, JsonNode?> attributes,
        Dictionary<string, string> attributeCodes)
    {
        _families = families;
        _attributes = attributes;
        _attributeCodes = attributeCodes;
    }

    public static readonly CatalogVocabulary Empty = new([], [], []);

    public static async Task<CatalogVocabulary> LoadAsync(AppDbContext db)
    {
        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == "family" || d.EntityType == "attribute")
            .ToListAsync();

        var families = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var attributes = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var attributeCodes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            var payload = ClientIdentity.Parse(doc.Payload);
            var name = payload?["name"];
            var target = doc.EntityType == "family" ? families : attributes;

            target[doc.ExternalId] = name;
            if (name is JsonObject translations)
                foreach (var (_, value) in translations)
                    if (DocumentProjections.Text(value) is { Length: > 0 } label)
                        target.TryAdd(label, name);

            target.TryAdd(Slug(doc.ExternalId), name);

            if (doc.EntityType == "attribute")
            {
                // Código canónico = slug del B2B Code (`code`), o del id del doc si no viene.
                // Alias: el propio id y cada etiqueta de `name` (Item Attribute.Name en cada
                // idioma) — es la clave con la que BC emite los atributos MAPEADOS del modelo.
                var codeText = DocumentProjections.Text(payload?["code"]);
                var code = Slug(codeText.Length > 0 ? codeText : doc.ExternalId);
                if (code.Length == 0) continue;
                attributeCodes.TryAdd(code, code);
                attributeCodes.TryAdd(Slug(doc.ExternalId), code);
                if (name is JsonObject labels)
                    foreach (var (_, value) in labels)
                        if (Slug(DocumentProjections.Text(value)) is { Length: > 0 } alias)
                            attributeCodes.TryAdd(alias, code);
            }
        }

        return new CatalogVocabulary(families, attributes, attributeCodes);
    }

    /// Clave canónica de un atributo para comparar reglas de visibilidad con las claves
    /// de los modelos: el slug del B2B Code del doc `attribute` cuando la clave (o su slug)
    /// es un alias conocido (id del doc o etiqueta en cualquier idioma); si no, su slug.
    /// Así "Marca del producto" (Name) y "marca" (B2B Code) son la MISMA clave.
    public string CanonicalAttributeKey(string key)
    {
        var slug = Slug(key);
        return _attributeCodes.TryGetValue(slug, out var code) ? code : slug;
    }

    /// Etiqueta de la familia; sin documento publicado, el id capitalizado de siempre
    public string FamilyLabel(string familyId, string locale)
    {
        if (familyId.Length == 0) return familyId;
        if (Lookup(_families, familyId, locale) is { Length: > 0 } translated) return translated;
        return char.ToUpperInvariant(familyId[0]) + familyId[1..];
    }

    /// Etiqueta del atributo; sin documento publicado, la propia clave del modelo
    public string AttributeLabel(string key, string locale) =>
        Lookup(_attributes, key, locale) is { Length: > 0 } translated ? translated : key;

    private static string? Lookup(Dictionary<string, JsonNode?> index, string key, string locale)
    {
        if (!index.TryGetValue(key, out var name) && !index.TryGetValue(Slug(key), out name))
            return null;
        return DocumentProjections.Localized(name, locale);
    }

    // Sanitización del conector (contrato 02 §6, values[].id): minúsculas; espacios,
    // "/", "\", "_" y "." a "-"; sin guiones repetidos ni en los extremos. Es la clave
    // estable con la que el portal puede traducir por su cuenta el vocabulario que BC
    // manda igual en los cuatro idiomas.
    // Ojo: replicado en wwwroot/manage/js/views/visibility.js (visSlug) — si cambia esto, cambiar allí.
    public static string Slug(string value)
    {
        var slug = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (character is ' ' or '/' or '\\' or '_' or '.' or '-')
            {
                if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            }
            else slug.Append(character);
        }

        return slug.ToString().Trim('-');
    }
}
