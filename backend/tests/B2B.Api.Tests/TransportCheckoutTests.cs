using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Integración del CHECKOUT con las reglas de transporte (modo portal autónomo). Al terminar
// un pedido, si una regla casa, su coste + incoterm deben viajar (a) al JSON de origen a BC
// (Cart.SourceJson → totalTransport + incotermId) y (b) al pedido nativo que ve el cliente
// (documento "order" → transportTotals + totalWithTransport). Se verifica leyendo la BD.
public class TransportCheckoutTests : IClassFixture<TransportCheckoutTests.PortalFactory>
{
    // Despliegue autónomo: los pedidos se guardan y comunican desde el portal.
    public sealed class PortalFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Portal:OrdersMode", "portal");
        }
    }

    private readonly PortalFactory _factory;
    private readonly HttpClient _client;

    public TransportCheckoutTests(PortalFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── helpers (mismo patrón que PortalAutonomoTests) ────────────────────────────
    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string route, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutEntityAsync(string type, string id, object body) =>
        AdminAsync(HttpMethod.Put, $"/api/admin/entities/{type}/{id}", body);

    private async Task<HttpResponseMessage> ClientAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task SeedCatalogAsync(string tag, decimal price)
    {
        await PutEntityAsync("model", $"MOD-{tag}", new { name = new { es_ES = $"Modelo {tag}" }, externalReference = $"REF-{tag}", active = true, productSegments = Array.Empty<string>(), attributes = new { } });
        await PutEntityAsync("product", $"PRD-{tag}", new { modelId = $"MOD-{tag}", name = new { es_ES = $"Modelo {tag}" }, sku = $"REF-{tag}-38", attributes = new { tallas = "38" }, active = true });
        await PutEntityAsync("offer", $"OFF-{tag}", new { modelId = $"MOD-{tag}", priceType = "PVD", basePrice = new { code = "EUR", value = price }, stock = 0, discounts = new[] { new { percent = 0 } }, priority = 0, pricesPerUnit = Array.Empty<object>() });
    }

    private async Task<string> SeedClientUserAsync(string clientId, string email, string password)
    {
        await PutEntityAsync("client", clientId, new { name = $"Cliente {clientId}", externalReference = clientId, canShop = true, markets = new[] { "es" }, productSegments = Array.Empty<string>() });
        (await AdminAsync(HttpMethod.Post, "/api/admin/users", new { email, role = "client-admin", name = "Acceso", culture = "es_ES", clientExternalId = clientId, password })).EnsureSuccessStatusCode();
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password, type = "global", longDuration = true });
        login.EnsureSuccessStatusCode();
        return (await JsonAsync(login)).GetProperty("token").GetString()!;
    }

    private static object OrderBody(string tag, decimal price, int qty = 3) => new
    {
        windowId = "reposic",
        reference = "PED-TRANSPORTE",
        lines = new[] { new { modelId = $"MOD-{tag}", productId = $"PRD-{tag}", size = "38", name = $"Modelo {tag}", reference = $"REF-{tag}", qty, price } }
    };

    // Lee un pedido nativo (documento "order") y el Cart de origen directamente de la BD.
    private (JsonElement Native, JsonElement Source) ReadOrder(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var doc = db.SyncDocuments.Single(d => d.EntityType == "order" && d.ExternalId == orderId);
        var cart = db.Carts.Single(c => c.Id == Guid.Parse(orderId));
        Assert.False(string.IsNullOrEmpty(cart.SourceJson));

        var native = JsonDocument.Parse(doc.Payload).RootElement.Clone();
        var source = JsonDocument.Parse(cart.SourceJson!).RootElement.Clone();
        return (native, source);
    }

    private static decimal Value(JsonElement money) => money.GetProperty("value").GetDecimal();

    // ── El coste de la regla llega al pedido y al JSON de BC ──────────────────────
    [Fact]
    public async Task Checkout_ReglaQueCasa_ReflejaCosteEnPedidoYJsonBc()
    {
        await SeedCatalogAsync("TR1", 20m);
        var token = await SeedClientUserAsync("CLI-TR1", "cli-tr1@portal.test", "Clave-1234");

        // Regla de transporte fija de 15 € + incoterm fob (BC solo reconoce fob/usa), acotada a este cliente.
        (await AdminAsync(HttpMethod.Post, "/api/admin/transport-rules", new
        {
            name = "Portes CLI-TR1",
            clientExternalId = "CLI-TR1",
            cost = 15m,
            incotermId = "fob",
        })).EnsureSuccessStatusCode();

        // 3 uds × 20 € = 60 (subtotal) + 21% IVA (12,60) = 72,60; + transporte 15 = 87,60
        var created = await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("TR1", 20m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var orderId = (await JsonAsync(created)).GetProperty("id").GetString()!;

        var (native, source) = ReadOrder(orderId);

        // (a) JSON de origen a BC: totalTransport + incotermId
        Assert.Equal(15m, Value(source.GetProperty("totalTransport")));
        Assert.Equal("fob", source.GetProperty("incotermId").GetString());

        // (b) Pedido nativo: transportTotals + totalWithTransport
        Assert.Equal(15m, Value(native.GetProperty("transportTotals").GetProperty("total")));
        Assert.Equal(87.60m, Value(native.GetProperty("totalWithTransport")));
        Assert.Equal(72.60m, Value(native.GetProperty("totals").GetProperty("total")));   // sin transporte
    }

    [Fact]
    public async Task Checkout_ReglaPorUnidad_MultiplicaPorUnidades()
    {
        await SeedCatalogAsync("TR2", 10m);
        var token = await SeedClientUserAsync("CLI-TR2", "cli-tr2@portal.test", "Clave-1234");

        // 2 € por unidad → 3 uds = 6 € de transporte.
        (await AdminAsync(HttpMethod.Post, "/api/admin/transport-rules", new
        {
            name = "Portes por unidad CLI-TR2",
            clientExternalId = "CLI-TR2",
            cost = 2m,
            perUnit = true,
        })).EnsureSuccessStatusCode();

        var created = await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("TR2", 10m));
        var orderId = (await JsonAsync(created)).GetProperty("id").GetString()!;

        var (native, source) = ReadOrder(orderId);
        // 3 uds × 10 € = 30 + 21% IVA (6,30) = 36,30; + transporte 6 = 42,30
        Assert.Equal(6m, Value(source.GetProperty("totalTransport")));
        Assert.Equal(6m, Value(native.GetProperty("transportTotals").GetProperty("total")));
        Assert.Equal(42.30m, Value(native.GetProperty("totalWithTransport")));
    }

    // ── Sin regla que case, el transporte es 0 (comportamiento por defecto) ────────
    [Fact]
    public async Task Checkout_SinReglaQueCase_TransporteCero()
    {
        await SeedCatalogAsync("TR3", 20m);
        var token = await SeedClientUserAsync("CLI-TR3", "cli-tr3@portal.test", "Clave-1234");

        // Regla existente pero de OTRO cliente → no casa con CLI-TR3.
        (await AdminAsync(HttpMethod.Post, "/api/admin/transport-rules", new
        {
            name = "Solo otro cliente",
            clientExternalId = "CLI-OTRO",
            cost = 99m,
        })).EnsureSuccessStatusCode();

        var created = await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("TR3", 20m));
        var orderId = (await JsonAsync(created)).GetProperty("id").GetString()!;

        var (native, source) = ReadOrder(orderId);
        Assert.Equal(0m, Value(source.GetProperty("totalTransport")));
        Assert.Equal("", source.GetProperty("incotermId").GetString());   // sin incoterm
        Assert.Equal(0m, Value(native.GetProperty("transportTotals").GetProperty("total")));
        // 3 × 20 = 60 + 21% = 72,60; sin transporte, totalWithTransport = total
        Assert.Equal(72.60m, Value(native.GetProperty("totalWithTransport")));
    }
}
