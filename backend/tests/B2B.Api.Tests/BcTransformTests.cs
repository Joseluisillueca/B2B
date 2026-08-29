using System.Text.Json;
using B2B.Api.Integration;

namespace B2B.Api.Tests;

// Verifica que el motor JUST.net (mismo que el portal de referencia) aplica los
// transformers literales de la referencia y produce el JSON EXACTO que espera la
// API OData de Business Central (contrato 06).
public class BcTransformTests
{
    // Transformer LITERAL de "Orden de compra" del portal de referencia → endpoint salesOrders
    private const string OrderTransformer = """
    {
      "orderId": "#valueof($.id)",
      "customerId": "#valueof($.clientId)",
      "shippingAddressId": "#valueof($.shippingAddressId)",
      "reference": "#valueof($.referenceOrder)",
      "paymentMethodId": "#valueof($.payMethodId)",
      "incotermId": "#valueof($.incotermId)",
      "saleId": "#valueof($.saleId)",
      "total": "#valueof($.total.value)",
      "totalTax": "#valueof($.totalTax.value)",
      "totalDiscount": "#valueof($.totalDiscount.value)",
      "totalCart": "#valueof($.totalCart.value)",
      "totalTransport": "#valueof($.totalTransport.value)",
      "totalCartDiscount": "#valueof($.totalCartDiscount.value)",
      "items": {
        "#loop($.items)": {
          "lineId": "#currentvalueatpath($.id)",
          "productId": "#currentvalueatpath($.productId)",
          "modelId": "#currentvalueatpath($.modelId)",
          "sku": "#currentvalueatpath($.sku)",
          "qty": "#currentvalueatpath($.quantity)",
          "name": "#currentvalueatpath($.productName.es_ES)",
          "unitPrice": "#currentvalueatpath($.price.value)",
          "originalUnitPrice": "#currentvalueatpath($.priceOriginal.value)",
          "amount": "#currentvalueatpath($.amount.value)",
          "discountAmount": "#currentvalueatpath($.totalDiscounts.value)",
          "stockServiceId": "#currentvalueatpath($.stockServiceId)"
        }
      },
      "stockServices": {
        "#loop($.stockServices)": {
          "stockServiceId": "#currentvalueatpath($.stockServiceId)",
          "from": "#currentvalueatpath($.from)",
          "to": "#currentvalueatpath($.to)",
          "baseFrom": "#currentvalueatpath($.baseFrom)",
          "baseTo": "#currentvalueatpath($.baseTo)"
        }
      }
    }
    """;

    private const string SampleOrder = """
    {
      "id": "8f2f6f1e-0000-0000-0000-000000000001",
      "clientId": "c1a2b3c4-0000-0000-0000-000000000002",
      "shippingAddressId": "d5e6f7a8-0000-0000-0000-000000000003",
      "referenceOrder": "REF-WEB-1234",
      "payMethodId": "sepa30",
      "incotermId": "",
      "saleId": "",
      "total": { "value": 100.0 },
      "totalTax": { "value": 21.0 },
      "totalDiscount": { "value": 0.0 },
      "totalCart": { "value": 100.0 },
      "totalTransport": { "value": 0.0 },
      "totalCartDiscount": { "value": 0.0 },
      "items": [
        {
          "id": "11111111-0000-0000-0000-000000000010",
          "productId": "22222222-0000-0000-0000-000000000011",
          "modelId": "33333333-0000-0000-0000-000000000012",
          "sku": "SKU-001", "quantity": 5,
          "productName": { "es_ES": "Producto X" },
          "price": { "value": 18.0 },
          "priceOriginal": { "value": 20.0 },
          "amount": { "value": 90.0 },
          "totalDiscounts": { "value": 10.0 },
          "stockServiceId": "SS-001"
        }
      ],
      "stockServices": [
        { "stockServiceId": "SS-001", "from": "01/09/2026", "to": "15/09/2026", "baseFrom": "2026-09-01", "baseTo": "2026-09-15" }
      ]
    }
    """;

    [Fact]
    public void OrdenDeCompra_Transformer_ProduceElJsonDeBc()
    {
        var outJson = JsonTransformService.Transform(OrderTransformer, SampleOrder);
        using var doc = JsonDocument.Parse(outJson);
        var r = doc.RootElement;

        Assert.Equal("8f2f6f1e-0000-0000-0000-000000000001", r.GetProperty("orderId").GetString());
        Assert.Equal("c1a2b3c4-0000-0000-0000-000000000002", r.GetProperty("customerId").GetString());
        Assert.Equal("REF-WEB-1234", r.GetProperty("reference").GetString());
        Assert.Equal("sepa30", r.GetProperty("paymentMethodId").GetString());

        var lines = r.GetProperty("items");
        Assert.Equal(1, lines.GetArrayLength());
        var line = lines[0];
        Assert.Equal("11111111-0000-0000-0000-000000000010", line.GetProperty("lineId").GetString());
        Assert.Equal("22222222-0000-0000-0000-000000000011", line.GetProperty("productId").GetString());  // = Item Variant SystemId
        Assert.Equal("33333333-0000-0000-0000-000000000012", line.GetProperty("modelId").GetString());     // = Item SystemId
        Assert.Equal("SKU-001", line.GetProperty("sku").GetString());
        Assert.Equal(5, line.GetProperty("qty").GetInt32());
        Assert.Equal("Producto X", line.GetProperty("name").GetString());
        Assert.Equal(18.0, line.GetProperty("unitPrice").GetDouble());
        Assert.Equal(20.0, line.GetProperty("originalUnitPrice").GetDouble());
        Assert.Equal(10.0, line.GetProperty("discountAmount").GetDouble());
        Assert.Equal("SS-001", line.GetProperty("stockServiceId").GetString());

        var ss = r.GetProperty("stockServices");
        Assert.Equal(1, ss.GetArrayLength());
        Assert.Equal("SS-001", ss[0].GetProperty("stockServiceId").GetString());
        Assert.Equal("2026-09-01", ss[0].GetProperty("baseFrom").GetString());
    }

    [Fact]
    public void DocumentoDescarga_Transformer_ExtraeLaUrl()
    {
        // "Origen de documentos": BC devuelve {value:[{url}]}; el transformer extrae la url
        const string t = """{ "url": "#valueof($.value[0].url)" }""";
        const string bcResponse = """{ "value": [ { "systemId": "x", "url": "https://blob/doc.pdf" } ] }""";
        var outJson = JsonTransformService.Transform(t, bcResponse);
        using var doc = JsonDocument.Parse(outJson);
        Assert.Equal("https://blob/doc.pdf", doc.RootElement.GetProperty("url").GetString());
    }
}
