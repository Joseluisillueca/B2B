using System.Text.Json.Nodes;
using B2B.Api.Data;

namespace B2B.Api.Sync;

// Proyecta los payloads crudos del conector a las tablas de dominio del catálogo.
// Tolerante a campos ausentes: las vías legacy del conector envían menos campos
// (contrato 02 §2 y §4) y no deben fallar.
public static class CatalogNormalizer
{
    // Sin escape \uXXXX: los segmentos como "A+" se guardan tal cual llegan
    private static readonly System.Text.Json.JsonSerializerOptions RawJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Normalize(AppDbContext db, string entityType, string externalId, JsonNode? payload)
    {
        if (payload is not JsonObject obj)
            return;

        switch (entityType)
        {
            case "model":
                UpsertModel(db, externalId, obj);
                break;
            case "product":
                UpsertProduct(db, externalId, obj);
                break;
        }
    }

    private static void UpsertModel(AppDbContext db, string externalId, JsonObject obj)
    {
        var model = db.CatalogModels.SingleOrDefault(m => m.ExternalId == externalId);
        if (model is null)
        {
            model = new CatalogModel { ExternalId = externalId };
            db.CatalogModels.Add(model);
        }

        model.Name = SpanishText(obj["name"]);
        model.Description = SpanishText(obj["description"]);
        model.Active = obj["active"]?.GetValue<bool>() ?? true;
        model.ExternalReference = Text(obj["externalReference"]);
        model.FamilyId = Text(obj["familyId"]);
        model.NameTranslationsJson = (obj["name"] as JsonObject)?.ToJsonString(RawJson) ?? "{}";
        model.AttributesJson = (obj["attributes"] as JsonObject)?.ToJsonString(RawJson) ?? "{}";
        model.ProductSegmentsJson = (obj["productSegments"] as JsonArray)?.ToJsonString(RawJson) ?? "[]";
        model.UpdatedAt = DateTime.UtcNow;
    }

    private static void UpsertProduct(AppDbContext db, string externalId, JsonObject obj)
    {
        var product = db.CatalogProducts.SingleOrDefault(p => p.ExternalId == externalId);
        if (product is null)
        {
            product = new CatalogProduct { ExternalId = externalId };
            db.CatalogProducts.Add(product);
        }

        var attributes = obj["attributes"] as JsonObject;
        var bundle = obj["bundle"] as JsonObject;

        product.ModelExternalId = Text(obj["modelId"]);
        product.Name = SpanishText(obj["name"]);
        product.Active = obj["active"]?.GetValue<bool>() ?? true;
        product.Sku = Text(obj["sku"]);
        product.Ean = Text(obj["ean"]);
        product.Size = SizeFrom(attributes);
        product.TaxId = Text(obj["taxId"]);
        product.AttributesJson = attributes?.ToJsonString(RawJson) ?? "{}";
        product.IsCasePack = bundle is not null;
        product.BundleJson = bundle?.ToJsonString(RawJson);
        product.UpdatedAt = DateTime.UtcNow;
    }

    // BC valida que toda variante lleve el atributo con B2B Code "tallas" (contrato 02 §4);
    // los case packs llegan sin él.
    private static string? SizeFrom(JsonObject? attributes) =>
        attributes?
            .FirstOrDefault(a => string.Equals(a.Key, "tallas", StringComparison.OrdinalIgnoreCase))
            .Value?.GetValue<string>();

    private static string Text(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : "";

    // Campos multiidioma: es_ES como texto principal; si no viene, el primer idioma presente
    private static string SpanishText(JsonNode? node)
    {
        if (node is not JsonObject translations)
            return "";
        if (translations["es_ES"] is JsonNode es)
            return Text(es);
        return Text(translations.FirstOrDefault().Value);
    }
}
