using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Asistente del portal: /api/portal/purchases agrega el historial de compras del
// cliente (por artículo y por talla) a partir de sus pedidos, acotado por el clientId
// del token. Es la base de "¿qué artículo he comprado más?", "¿cuánto he comprado de
// la talla 40?".
public class PortalPurchasesTests : IClassFixture<PortalPurchasesTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "7A31C5D2-9E44-4C18-B0F3-0011AA22BB33";
    private const string ClientB = "0000AAAA-0000-4000-9000-0000000000BB";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PortalPurchasesTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await Put($"/api/clients/{ClientA}", $$"""{"externalReference":"C100057","name":"Test 5"}""");
        await Put($"/api/clients/{ClientA}/users/admin",
            $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test 5","culture":"es_ES"}""");

        // Pedido 1 de A: dos tallas del mismo artículo
        await Put("/api/orders/ORD-A1", Order(ClientA, "PV-A1", "2026-06-01", new[]
        {
            Line("Camiseta Roja", "CAM", "40", 5, 50),
            Line("Camiseta Roja", "CAM", "42", 3, 30),
        }));
        // Pedido 2 de A: otro artículo, talla 40
        await Put("/api/orders/ORD-A2", Order(ClientA, "PV-A2", "2026-07-01", new[]
        {
            Line("Pantalón Azul", "PAN", "40", 10, 200),
        }));
        // Devolución de A (importe negativo): NO cuenta como compra
        await Put("/api/orders/ORD-ARET", Order(ClientA, "DEV-A", "2026-07-10", new[]
        {
            Line("Camiseta Roja", "CAM", "40", -2, -20),
        }, type: "NOT_DEFINED", total: -20));
        // Pedido de OTRO cliente: no debe verse
        await Put("/api/orders/ORD-B1", Order(ClientB, "PV-B1", "2026-06-15", new[]
        {
            Line("Zapato Ajeno", "ZAP", "41", 99, 999),
        }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Purchases_AgregaPorArticuloYPorTalla_SoloDelClienteDelToken()
    {
        var token = await _factory.LoginAsync(_client, TestWebApplicationFactory.SeededEmail, TestWebApplicationFactory.SeededPassword);
        var res = await Get("/api/portal/purchases", token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();

        // 2 pedidos de compra (la devolución no cuenta)
        Assert.Equal(2, body.GetProperty("orderCount").GetInt32());
        Assert.Equal(18m, body.GetProperty("totalUnits").GetDecimal());   // 5+3+10

        // Artículo más comprado: Pantalón Azul (10) por delante de Camiseta Roja (8)
        var top = body.GetProperty("topProducts").EnumerateArray().ToList();
        Assert.Equal("Pantalón Azul", top[0].GetProperty("name").GetString());
        Assert.Equal(10m, top[0].GetProperty("units").GetDecimal());
        Assert.Equal("Camiseta Roja", top[1].GetProperty("name").GetString());
        Assert.Equal(8m, top[1].GetProperty("units").GetDecimal());

        // Por talla: la 40 suma 15 (5+10), la 42 suma 3. Nada del cliente ajeno (talla 41).
        var sizes = body.GetProperty("bySize").EnumerateArray()
            .ToDictionary(s => s.GetProperty("size").GetString()!, s => s.GetProperty("units").GetDecimal());
        Assert.Equal(15m, sizes["40"]);
        Assert.Equal(3m, sizes["42"]);
        Assert.False(sizes.ContainsKey("41"));
    }

    [Fact]
    public async Task Purchases_SinToken_Devuelve401()
    {
        var res = await Get("/api/portal/purchases", token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Asistente_RespondeArticuloMasCompradoSinModelo()
    {
        var token = await _factory.LoginAsync(_client, TestWebApplicationFactory.SeededEmail, TestWebApplicationFactory.SeededPassword);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/portal/assistant")
        {
            Content = JsonContent.Create(new { question = "¿Qué artículo he comprado más?" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var answer = body.GetProperty("answer").GetString()!;
        Assert.Contains("Pantalón Azul", answer);
        Assert.Equal("rules", body.GetProperty("source").GetString());   // sin clave: determinista
    }

    // ── helpers ──
    private static string Line(string name, string modelRef, string size, decimal qty, decimal amount) => $$"""
        {
          "id": "{{modelRef}}{{size}}-L",
          "productName": { "es_ES": "{{name}}", "en_EN": "{{name}}" },
          "productExternalReference": "{{modelRef}}",
          "quantityDelivered": 0,
          "productInfo": { "modelExternalReference": "{{modelRef}}", "sku": "{{modelRef}}{{size}}" },
          "transactionInfo": {
            "info": { "quantity": {{Num(qty)}}, "discount": 0, "price": {"amount": 10, "currency": "EUR"},
                      "amount": {"amount": {{Num(amount)}}, "currency": "EUR"} },
            "taxes": [ { "percent": 21 } ]
          }
        }
        """;

    private static string Order(string clientId, string number, string date, string[] lines,
        string type = "SCHEDULED", decimal total = 100) => $$"""
        {
          "clientId": "{{clientId}}",
          "externalReference": "{{number}}",
          "orderedDate": "{{date}}T00:00:00Z",
          "type": "{{type}}",
          "status": "open",
          "totals": { "total": {"amount": {{Num(total)}}, "currency": "EUR"} },
          "items": [ {{string.Join(",", lines)}} ]
        }
        """;

    private static string Num(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> Get(string route, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }
}
