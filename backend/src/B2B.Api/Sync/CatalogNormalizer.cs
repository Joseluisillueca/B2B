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
            case "inventory":
                UpsertStock(db, externalId, obj);
                break;
            case "service-window":
                UpsertServiceWindow(db, externalId, obj);
                break;
            case "offer":
                UpsertOffer(db, externalId, obj);
                break;
        }
    }

    // Contrato 03 §2: la URL identifica el producto; la ventana viaja en el body
    // (stockServiceId) con mayúsculas inconsistentes → clave normalizada a minúsculas.
    private static void UpsertStock(AppDbContext db, string productExternalId, JsonObject obj)
    {
        var windowId = Text(obj["stockServiceId"]);
        var key = windowId.ToLowerInvariant();

        var level = db.StockLevels.SingleOrDefault(s =>
            s.ProductExternalId == productExternalId && s.ServiceWindowKey == key);
        if (level is null)
        {
            level = new StockLevel
            {
                Id = Guid.NewGuid(),
                ProductExternalId = productExternalId,
                ServiceWindowId = windowId,
                ServiceWindowKey = key
            };
            db.StockLevels.Add(level);
        }

        level.ServiceWindowId = windowId;
        level.Stock = Number(obj["stock"]);
        level.OrderType = Text(obj["orderType"]);
        level.EntryDate = Text(obj["entryDate"]);
        level.UpdatedAt = DateTime.UtcNow;
    }

    // Contrato 03 §1: payload rico del adapter (con id en minúsculas en el body)
    // y payload legacy mínimo (solo fechas y orderType). Campos ausentes no machacan.
    private static void UpsertServiceWindow(AppDbContext db, string urlId, JsonObject obj)
    {
        var id = (Text(obj["id"]) is { Length: > 0 } bodyId ? bodyId : urlId).ToLowerInvariant();

        var window = db.ServiceWindows.SingleOrDefault(w => w.ExternalId == id);
        if (window is null)
        {
            window = new ServiceWindow { ExternalId = id };
            db.ServiceWindows.Add(window);
        }

        if (obj.ContainsKey("name"))
            window.Name = SpanishText(obj["name"]);
        window.OrderType = Text(obj["orderType"]);
        window.FromDate = Text(obj["from"]);
        window.ToDate = Text(obj["to"]);
        window.LimitDate = Text(obj["limit"]);
        window.PayloadJson = obj.ToJsonString(RawJson);
        window.UpdatedAt = DateTime.UtcNow;
    }

    // Contrato 03 §4.3: forma real {id, offerData:{...}}; también se acepta el
    // objeto de datos directamente en la raíz (rutas con id individual).
    private static void UpsertOffer(AppDbContext db, string externalId, JsonObject obj)
    {
        var data = obj["offerData"] as JsonObject ?? obj;

        var offer = db.Offers.SingleOrDefault(o => o.ExternalId == externalId);
        if (offer is null)
        {
            offer = new Offer { ExternalId = externalId };
            db.Offers.Add(offer);
        }

        var basePrice = data["basePrice"] as JsonObject;
        var firstDiscount = (data["discounts"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();

        offer.ModelId = Text(data["modelId"]);
        offer.ProductId = NullableText(data["productId"]);
        offer.ClientId = NullableText(data["clientId"]);
        offer.ClientGroupId = NullableText(data["clientGroupId"]);
        offer.PriceType = Text(data["priceType"]) is { Length: > 0 } pt ? pt : "PVD";
        offer.PriceCode = Text(basePrice?["code"]);
        offer.PriceValue = Number(basePrice?["value"]);
        offer.MinQuantity = Number(data["stock"]);
        offer.DiscountPercent = firstDiscount is null ? null : Number(firstDiscount["percent"]);
        offer.FromDate = NullableText(data["fromDate"]);
        offer.ToDate = NullableText(data["toDate"]);
        offer.OrderType = NullableText(data["orderType"]);
        offer.Priority = (int)Number(data["priority"]);
        offer.PayloadJson = obj.ToJsonString(RawJson);
        offer.UpdatedAt = DateTime.UtcNow;
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

    public static string Text(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : "";

    private static string? NullableText(JsonNode? node) => node is null ? null : Text(node);

    private static decimal Number(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.Number ? node.GetValue<decimal>() : 0m;

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
