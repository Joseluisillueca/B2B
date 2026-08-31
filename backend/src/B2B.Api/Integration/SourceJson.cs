using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using B2B.Api.Portal;

namespace B2B.Api.Integration;

// Construye el JSON de ORIGEN (la "entrada" del transformer) para cada evento, en la
// forma que esperan los transformers de la referencia. Los GUID (clientId, shippingAddressId,
// productId=Item Variant, modelId=Item) ya son los SystemId de BC.
public static class SourceJson
{
    private const decimal Iva = 0.21m;
    private static JsonObject Money(decimal v) => new() { ["value"] = v };

    // El "Line ID" de BC es un Guid (Tab80118 field 2; la página lo bindea directo y lo
    // valida como "B2B Id" de la línea de venta), así que el id de línea debe ser un GUID
    // VÁLIDO. Lo derivamos de (pedido, índice) para que sea estable al recomunicar.
    private static string LineGuid(string orderId, int index) =>
        new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"{orderId}:line:{index}"))).ToString();

    public static JsonObject Order(
        string orderId, string? clientId, string? shippingAddressId, string? reference,
        string? payMethodId, string? incotermId, string? saleId,
        IReadOnlyList<CartLine> lines, Data.ServiceWindow? window)
    {
        decimal subtotal = lines.Sum(l => l.Qty * l.Price);
        decimal tax = Math.Round(subtotal * Iva, 2);

        var items = new JsonArray();
        var i = 0;
        foreach (var l in lines)
        {
            var amount = l.Qty * l.Price;
            items.Add(new JsonObject
            {
                ["id"] = LineGuid(orderId, ++i),
                ["productId"] = l.ProductId,     // = SystemId de Item Variant en BC
                ["modelId"] = l.ModelId,         // = SystemId de Item en BC
                ["sku"] = (l.Reference ?? "") + (l.Size ?? ""),
                ["quantity"] = l.Qty,
                ["productName"] = new JsonObject { ["es_ES"] = l.Name ?? "" },
                ["price"] = Money(l.Price),
                ["priceOriginal"] = Money(l.Price),
                ["amount"] = Money(amount),
                ["totalDiscounts"] = Money(0),
                ["stockServiceId"] = window?.ExternalId ?? "",
            });
        }

        var stockServices = new JsonArray();
        if (window is not null)
            stockServices.Add(new JsonObject
            {
                ["stockServiceId"] = window.ExternalId,
                ["from"] = window.FromDate ?? "",
                ["to"] = window.ToDate ?? "",
                ["baseFrom"] = window.FromDate ?? "",
                ["baseTo"] = window.ToDate ?? "",
            });

        return new JsonObject
        {
            ["id"] = orderId,
            ["clientId"] = clientId,
            ["shippingAddressId"] = shippingAddressId ?? "",
            ["referenceOrder"] = reference ?? "",
            ["payMethodId"] = payMethodId ?? "",
            ["incotermId"] = incotermId ?? "",
            ["saleId"] = saleId ?? "",
            ["total"] = Money(subtotal + tax),
            ["totalTax"] = Money(tax),
            ["totalDiscount"] = Money(0),
            ["totalCart"] = Money(subtotal),
            ["totalTransport"] = Money(0),
            ["totalCartDiscount"] = Money(0),
            ["items"] = items,
            ["stockServices"] = stockServices,
        };
    }

    // Cliente: payload del documento `client` + id + direcciones embebidas (cada una con
    // su shippingAddressId = GUID, para que BC lo fije como SystemId de la Ship-to Address).
    public static JsonObject Client(string clientId, JsonObject payload, IEnumerable<(string Id, JsonObject Payload)> addresses)
    {
        var src = payload.DeepClone().AsObject();
        src["id"] = clientId;
        // phone puede venir como string → normalizar a {number}
        if (src["phone"] is JsonValue) src["phone"] = new JsonObject { ["number"] = src["phone"]!.GetValue<string>() };
        var addrs = new JsonArray();
        foreach (var (id, p) in addresses)
        {
            var addr = (p["address"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
            addr["shippingAddressId"] = id;
            addr["alias"] = p["alias"]?.GetValue<string>() ?? "";
            addrs.Add(addr);
        }
        src["shippingAddresses"] = addrs;
        return src;
    }

    // Dirección: forma que espera el transformer de "Registro de direcciones".
    public static JsonObject Address(string addressId, string? clientId, JsonObject payload)
    {
        var addr = (payload["address"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
        return new JsonObject
        {
            ["clientID"] = clientId ?? "",
            ["shippingAddressId"] = addressId,
            ["shippingAddressAlias"] = payload["alias"]?.GetValue<string>() ?? "",
            ["shippingAddress"] = addr,
        };
    }
}
