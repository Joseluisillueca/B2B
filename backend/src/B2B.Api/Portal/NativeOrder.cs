using System.Text.Json.Nodes;

namespace B2B.Api.Portal;

// Portal autónomo (sin ERP): cuando un cliente termina un pedido, el portal lo guarda
// como documento "order" con LA MISMA forma que produciría Business Central. Así el
// pedido lo leen sin cambios DocumentProjections/DocumentEndpoints (lo ve el cliente
// en /orders) y el CMS (lista y gestiona su estado) — sin depender de que BC lo procese.
public static class NativeOrder
{
    // IVA general del portal (checkout.js usa el mismo 21%); el desglose fino por línea
    // llega solo con el pedido real de BC. En modo autónomo aplicamos el tipo general.
    private const decimal Iva = 0.21m;

    public static JsonObject Build(
        string orderId, string number, string? clientId, string? orderType,
        string? reference, string? payMethodId, string? notes,
        JsonObject? shippingAddress, IReadOnlyList<CartLine> lines, DateTime now,
        decimal transportCost = 0)
    {
        decimal subtotal = lines.Sum(l => l.Qty * l.Price);
        decimal tax = Math.Round(subtotal * Iva, 2);

        var items = new JsonArray();
        var i = 0;
        foreach (var line in lines)
        {
            var amount = line.Qty * line.Price;
            var modelRef = line.Reference ?? "";
            var sku = modelRef + (line.Size ?? "");   // DocumentProjections.Size separa la talla del prefijo del modelo
            items.Add(new JsonObject
            {
                ["id"] = orderId + "-" + (++i),
                ["productName"] = Multilang(line.Name ?? ""),
                ["productExternalReference"] = modelRef,
                ["productInfo"] = new JsonObject
                {
                    ["sku"] = sku,
                    ["externalReference"] = modelRef,
                    ["modelExternalReference"] = modelRef,
                    ["name"] = Multilang(line.Name ?? ""),
                },
                ["quantityDelivered"] = 0,
                ["status"] = "Open",
                ["transactionInfo"] = new JsonObject
                {
                    ["info"] = new JsonObject
                    {
                        ["quantity"] = line.Qty,
                        ["price"] = Money(line.Price),
                        ["discount"] = 0,
                        ["amount"] = Money(amount),
                    },
                    ["taxes"] = new JsonArray { new JsonObject { ["percent"] = Iva * 100 } },
                },
            });
        }

        var order = new JsonObject
        {
            ["clientId"] = clientId,                                  // acota el pedido a su cliente (ParentId)
            ["externalReference"] = number,                          // Nº de pedido visible
            ["orderedDate"] = now.ToString("yyyy-MM-ddTHH:mm:ss") + ".000Z",
            // Tipo derivado de la ventana de servicio real (service-window.orderType),
            // no de un literal. Sin ventana o desconocido → REPLENISHMENT (reposición).
            ["type"] = string.IsNullOrWhiteSpace(orderType) ? "REPLENISHMENT" : orderType.ToUpperInvariant(),
            ["status"] = "open",
            ["seasonId"] = "",
            ["purchaseOrderId"] = reference ?? "",
            ["reference"] = reference ?? "",
            ["payMethodId"] = payMethodId ?? "",
            ["observations"] = notes ?? "",
            ["source"] = "portal",                                   // marca de origen (no viene de BC)
            ["paid"] = false,
            ["totals"] = new JsonObject
            {
                ["totalAmount"] = Money(subtotal),
                ["totalDiscount"] = Money(0),
                ["totalTax"] = Money(tax),
                ["total"] = Money(subtotal + tax),
            },
            // Transporte (portes) calculado por las reglas de transporte; misma forma que un
            // pedido de BC para que el cliente lo vea igual en su ficha de pedido.
            ["transportTotals"] = new JsonObject
            {
                ["totalAmount"] = Money(transportCost),
                ["totalDiscount"] = Money(0),
                ["totalTax"] = Money(0),
                ["total"] = Money(transportCost),
            },
            ["totalWithTransport"] = Money(subtotal + tax + transportCost),
            ["items"] = items,
        };

        if (shippingAddress is not null)
            order["shippingAddress"] = shippingAddress.DeepClone();

        return order;
    }

    private static JsonObject Money(decimal value) => new() { ["code"] = "EUR", ["value"] = value };

    private static JsonObject Multilang(string text) => new()
    {
        ["es_ES"] = text, ["en_EN"] = text, ["fr_FR"] = text, ["it_IT"] = text,
    };
}
