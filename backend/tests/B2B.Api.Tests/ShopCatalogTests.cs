using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Catálogo comprable del front: modelos con variantes (tallas), precio PVD/PVP
// y stock por ventana, todo en una llamada.
public class ShopCatalogTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ShopCatalogTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Catalog_DevuelveModelosConTallasPrecioYStock()
    {
        const string modelId = "SHOPMODL-4F3B-4E2A-9D77-001122334455";
        const string v36 = "SHOPV36X-4F3B-4E2A-9D77-001122334455";
        const string v37 = "SHOPV37X-4F3B-4E2A-9D77-001122334455";

        await Put($"/api/catalog/models/{modelId}",
            """{"name":{"es_ES":"LEJAN MELROSE - BLACK"},"active":true,"externalReference":"1948","familyId":"calzado","productSegments":["A"]}""");
        await Put($"/api/catalog/products/{v36}",
            $$"""{"modelId":"{{modelId}}","name":{"es_ES":"Melrose Black 36"},"active":true,"sku":"8400001","ean":"8400001","attributes":{"tallas":"36"},"taxId":"iva-normal"}""");
        await Put($"/api/catalog/products/{v37}",
            $$"""{"modelId":"{{modelId}}","name":{"es_ES":"Melrose Black 37"},"active":true,"sku":"8400002","ean":"8400002","attributes":{"tallas":"37"},"taxId":"iva-normal"}""");
        await Put($"/api/stock/inventory/{v36}",
            """{"stock":9,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");
        await Put("/api/catalog/offers",
            $$$"""[{"id":"SHOPOFR1-4F3B-4E2A-9D77-001122334455","offerData":{"basePrice":{"code":"EUR","value":52.0},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{modelId}}}"}},{"id":"SHOPOFR2-4F3B-4E2A-9D77-001122334455","offerData":{"basePrice":{"code":"EUR","value":89.9},"priceType":"PVP","stock":0,"priority":1,"modelId":"{{{modelId}}}"}}]""");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var model = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("modelId").GetString() == modelId);

        Assert.Equal("LEJAN MELROSE - BLACK", model.GetProperty("name").GetString());
        Assert.Equal("1948", model.GetProperty("reference").GetString());
        Assert.Equal(52.0m, model.GetProperty("pvd").GetDecimal());
        Assert.Equal(89.9m, model.GetProperty("pvp").GetDecimal());

        var products = model.GetProperty("products").EnumerateArray().ToList();
        Assert.Equal(2, products.Count);
        var p36 = products.Single(p => p.GetProperty("size").GetString() == "36");
        Assert.Equal(9m, p36.GetProperty("stock").GetProperty("reposic").GetDecimal());
    }

    [Fact]
    public async Task Catalog_SinToken_Devuelve401()
    {
        var response = await _client.GetAsync("/api/shop/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
