using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Contrato 03: stock por (producto, ventana), ofertas en array a URL fija,
// ventanas de servicio con payload rico y legacy.
public class StockOffersWindowsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public StockOffersWindowsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string? json = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private T InDb<T>(Func<AppDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ---------- Stock ----------

    [Fact]
    public async Task PutInventory_UpsertPorProductoYVentana_ConVentanaCaseInsensitive()
    {
        // Contrato 03 §2: la URL identifica el producto; la ventana viaja en el body.
        // El id de ventana llega con mayúsculas inconsistentes (hallazgo §7.2).
        const string productId = "STOCKPR1-4F3B-4E2A-9D77-001122334455";

        (await Send(HttpMethod.Put, $"/api/stock/inventory/{productId}",
            """{"stock":142,"type":"Inventory","entryDate":"2026-08-02","stockServiceId":"SS26","orderType":"SCHEDULED"}"""))
            .EnsureSuccessStatusCode();
        (await Send(HttpMethod.Put, $"/api/stock/inventory/{productId}",
            """{"stock":57,"type":"Inventory","entryDate":"2026-08-02","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}"""))
            .EnsureSuccessStatusCode();
        // Misma ventana SS26 en minúsculas: debe actualizar, no duplicar
        (await Send(HttpMethod.Put, $"/api/stock/inventory/{productId}",
            """{"stock":10000,"type":"Inventory","entryDate":"2026-08-03","stockServiceId":"ss26","orderType":"SCHEDULED"}"""))
            .EnsureSuccessStatusCode();

        var levels = InDb(db => db.StockLevels.Where(s => s.ProductExternalId == productId).ToList());
        Assert.Equal(2, levels.Count);
        var ss26 = levels.Single(l => l.ServiceWindowKey == "ss26");
        Assert.Equal(10000m, ss26.Stock);
        Assert.Equal("SCHEDULED", ss26.OrderType);
        Assert.Equal(57m, levels.Single(l => l.ServiceWindowKey == "reposic").Stock);
    }

    // ---------- Ofertas ----------

    // Forma real del contrato 03 §4.3: array raíz de {id, offerData}
    private const string OffersArrayPayload = """
        [
          {
            "id": "0f8fad5b-d9cb-469f-a165-708677289501",
            "offerData": {
              "stock": 12,
              "basePrice": { "code": "EUR", "value": 21.5 },
              "pricesPerUnit": [],
              "fromDate": "2026-02-01T00:00:00.000Z",
              "toDate": "2026-02-28T00:00:00.000Z",
              "productId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
              "clientGroupId": "mayorista",
              "priority": 3,
              "marketId": "es",
              "priceType": "PVD",
              "tag": "",
              "discounts": [ { "percent": 10, "description": { "es_ES": "Discount" } } ],
              "priceOriginal": { "code": "EUR", "value": 21.5 },
              "modelId": "OFERMODL-1111-4A5B-8C3D-2E4F5A6B7C8D",
              "orderType": "SCHEDULED"
            }
          },
          {
            "id": "0f8fad5b-d9cb-469f-a165-708677289502",
            "offerData": {
              "stock": 0,
              "basePrice": { "code": "EUR", "value": 49.9 },
              "pricesPerUnit": [],
              "priority": 1,
              "marketId": "es",
              "priceType": "PVP",
              "tag": "",
              "discounts": [],
              "priceOriginal": { "code": "EUR", "value": 49.9 },
              "modelId": "OFERMODL-1111-4A5B-8C3D-2E4F5A6B7C8D"
            }
          }
        ]
        """;

    [Fact]
    public async Task PutOffersArray_AUrlFijaSinId_NormalizaCadaOferta()
    {
        var response = await Send(HttpMethod.Put, "/api/catalog/offers", OffersArrayPayload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pvd = InDb(db => db.Offers.Single(o => o.ExternalId == "0f8fad5b-d9cb-469f-a165-708677289501"));
        Assert.Equal("OFERMODL-1111-4A5B-8C3D-2E4F5A6B7C8D", pvd.ModelId);
        Assert.Equal("7c9e6679-7425-40de-944b-e07fc1f90ae7", pvd.ProductId);
        Assert.Equal("mayorista", pvd.ClientGroupId);
        Assert.Equal(21.5m, pvd.PriceValue);
        Assert.Equal("EUR", pvd.PriceCode);
        Assert.Equal("PVD", pvd.PriceType);
        Assert.Equal(12m, pvd.MinQuantity);
        Assert.Equal(10m, pvd.DiscountPercent);
        Assert.Equal("SCHEDULED", pvd.OrderType);

        var pvp = InDb(db => db.Offers.Single(o => o.ExternalId == "0f8fad5b-d9cb-469f-a165-708677289502"));
        Assert.Equal("PVP", pvp.PriceType);
        Assert.Null(pvp.ProductId);
        Assert.Null(pvp.DiscountPercent);
    }

    [Fact]
    public async Task GetOffers_DevuelveLasOfertasDelArrayPorModelo()
    {
        (await Send(HttpMethod.Put, "/api/catalog/offers", OffersArrayPayload)).EnsureSuccessStatusCode();

        var response = await Send(HttpMethod.Get, "/api/catalog/offers",
            """{"modelId":"ofermodl-1111-4a5b-8c3d-2e4f5a6b7c8d"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Contains("0f8fad5b-d9cb-469f-a165-708677289501", ids);
        Assert.Contains("0f8fad5b-d9cb-469f-a165-708677289502", ids);
    }

    [Fact]
    public async Task DeleteOffer_EliminaTambienLaFilaNormalizada()
    {
        (await Send(HttpMethod.Put, "/api/catalog/offers", OffersArrayPayload)).EnsureSuccessStatusCode();

        (await Send(HttpMethod.Delete, "/api/catalog/offers/0f8fad5b-d9cb-469f-a165-708677289501"))
            .EnsureSuccessStatusCode();

        Assert.False(InDb(db => db.Offers.Any(o => o.ExternalId == "0f8fad5b-d9cb-469f-a165-708677289501")));
    }

    // ---------- Ventanas de servicio ----------

    [Fact]
    public async Task PutServiceWindow_PayloadRico_NormalizaConIdEnMinusculas()
    {
        // La URL lleva el id sin lowercase; el body sí lo lleva en minúsculas (hallazgo §7.2)
        var payload = """
            {
              "id": "ss26",
              "name": { "es_ES": "Spring/Summer 2026", "en_EN": "Spring/Summer 2026", "fr_FR": "Spring/Summer 2026", "it_IT": "Spring/Summer 2026" },
              "from": "2026-01-15",
              "to": "2026-03-15",
              "limit": "2026-03-31",
              "limitDays": 75,
              "orderType": "SCHEDULED",
              "incoterms": []
            }
            """;

        var response = await Send(HttpMethod.Put, "/api/core/service-windows/SS26", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var window = InDb(db => db.ServiceWindows.Single(w => w.ExternalId == "ss26"));
        Assert.Equal("Spring/Summer 2026", window.Name);
        Assert.Equal("SCHEDULED", window.OrderType);
        Assert.Equal("2026-01-15", window.FromDate);
        Assert.Equal("2026-03-31", window.LimitDate);
    }

    [Fact]
    public async Task PutServiceWindow_PayloadLegacyMinimo_NoFalla()
    {
        // Flujo legacy Cod80106 (contrato 03 §1.2): solo from/to/limit/orderType, sin id ni name
        var response = await Send(HttpMethod.Put, "/api/core/service-windows/LEGACYWIN",
            """{"from":"2026-05-01","to":"2026-06-30","limit":"2026-07-15","orderType":"scheduled"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var window = InDb(db => db.ServiceWindows.Single(w => w.ExternalId == "legacywin"));
        Assert.Equal("2026-05-01", window.FromDate);
    }
}
