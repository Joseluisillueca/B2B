using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace B2B.Api.Tests;

// Portal autónomo (clientes SIN ERP): el CMS crea las entidades a mano (mismo documento
// que el conector) y los pedidos se GUARDAN y gestionan en el portal. Estas pruebas
// cubren el CRUD del CMS, el alta de accesos y el ciclo de vida del pedido nativo,
// incluida la re-tarificación en servidor (el cliente no fija el precio).
public class PortalAutonomoTests : IClassFixture<PortalAutonomoTests.PortalFactory>
{
    // Despliegue autónomo: los pedidos se guardan en el portal (no se esperan de BC).
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

    public PortalAutonomoTests(PortalFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string route, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutEntityAsync(string type, string id, object body, string? parentId = null) =>
        AdminAsync(HttpMethod.Put, $"/api/admin/entities/{type}/{id}" + (parentId is null ? "" : $"?parentId={parentId}"), body);

    private async Task<HttpResponseMessage> ClientAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    // Da de alta catálogo comprable (modelo + variante + oferta a `price`) bajo un sufijo único.
    private async Task SeedCatalogAsync(string tag, decimal price)
    {
        await PutEntityAsync("model", $"MOD-{tag}", new { name = new { es_ES = $"Modelo {tag}" }, externalReference = $"REF-{tag}", active = true, productSegments = Array.Empty<string>(), attributes = new { } });
        await PutEntityAsync("product", $"PRD-{tag}", new { modelId = $"MOD-{tag}", name = new { es_ES = $"Modelo {tag}" }, sku = $"REF-{tag}-38", attributes = new { tallas = "38" }, active = true });
        await PutEntityAsync("offer", $"OFF-{tag}", new { modelId = $"MOD-{tag}", priceType = "PVD", basePrice = new { code = "EUR", value = price }, stock = 0, discounts = new[] { new { percent = 0 } }, priority = 0, pricesPerUnit = Array.Empty<object>() });
    }

    // Crea un cliente con su acceso (client-admin con contraseña) y devuelve su token.
    private async Task<string> SeedClientUserAsync(string clientId, string email, string password)
    {
        await PutEntityAsync("client", clientId, new { name = $"Cliente {clientId}", externalReference = clientId, canShop = true, markets = new[] { "es" }, productSegments = Array.Empty<string>() });
        (await AdminAsync(HttpMethod.Post, "/api/admin/users", new { email, role = "client-admin", name = "Acceso", culture = "es_ES", clientExternalId = clientId, password })).EnsureSuccessStatusCode();
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password, type = "global", longDuration = true });
        login.EnsureSuccessStatusCode();
        return (await JsonAsync(login)).GetProperty("token").GetString()!;
    }

    private static object OrderBody(string tag, decimal clientPrice, int qty = 3, string window = "reposic") => new
    {
        windowId = window,
        reference = "PED-TEST",
        lines = new[] { new { modelId = $"MOD-{tag}", productId = $"PRD-{tag}", size = "38", name = $"Modelo {tag}", reference = $"REF-{tag}", qty, price = clientPrice } }
    };

    // ── CRUD del CMS ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Entidad_CreaModeloYApareceEnCatalogo()
    {
        await SeedCatalogAsync("CAT1", 30m);
        var catalog = await (await AdminAsync(HttpMethod.Get, "/api/shop/catalog")).Content.ReadAsStringAsync();
        Assert.Contains("REF-CAT1", catalog);
    }

    [Fact]
    public async Task Entidad_SinObligatorios_Devuelve400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntityAsync("model", "MOD-EMPTY", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntityAsync("product", "PRD-EMPTY", new { name = new { es_ES = "x" }, sku = "y" })).StatusCode); // falta modelId
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntityAsync("offer", "OFF-EMPTY", new { modelId = "m" })).StatusCode); // falta basePrice.value
    }

    [Fact]
    public async Task Entidad_TipoNoEditable_Devuelve400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntityAsync("order", "X", new { a = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntityAsync("inventado", "X", new { a = 1 })).StatusCode);
    }

    [Fact]
    public async Task Entidad_BorrarModelo_CascadaNoResucita()
    {
        await SeedCatalogAsync("CASC", 45m);
        await PutEntityAsync("inventory", "PRD-CASC", new { stock = 10, stockServiceId = "reposic", type = "Inventory" });

        Assert.Equal(HttpStatusCode.NoContent, (await AdminAsync(HttpMethod.Delete, "/api/admin/entities/model/MOD-CASC")).StatusCode);

        // Recrear el MISMO id de modelo, sin hijos: no deben reaparecer producto/precio
        await PutEntityAsync("model", "MOD-CASC", new { name = new { es_ES = "Modelo CASC" }, externalReference = "REF-CASC", active = true });
        var catalog = await (await AdminAsync(HttpMethod.Get, "/api/shop/catalog")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("REF-CASC-38", catalog);   // el SKU de la variante ya no existe
    }

    // ── Accesos ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Acceso_ConContraseña_PermiteLogin()
    {
        var email = "acceso-ok@portal.test";
        var created = await AdminAsync(HttpMethod.Post, "/api/admin/users", new { email, role = "admin", password = "Secreto-123" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Secreto-123", type = "global", longDuration = true });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Acceso_EmailDuplicado_409_y_RolInvalido_400()
    {
        var email = "dup@portal.test";
        (await AdminAsync(HttpMethod.Post, "/api/admin/users", new { email, role = "admin", password = "Secreto-123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await AdminAsync(HttpMethod.Post, "/api/admin/users", new { email, role = "admin", password = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AdminAsync(HttpMethod.Post, "/api/admin/users", new { email = "rol@portal.test", role = "jefe", password = "x" })).StatusCode);
    }

    // ── Pedidos nativos ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Pedido_SeGuardaVisibleYRepreciaEnServidor()
    {
        await SeedCatalogAsync("ORD1", 20m);
        var token = await SeedClientUserAsync("CLI-ORD1", "cli-ord1@portal.test", "Clave-1234");

        // El cliente MANIPULA el precio a 0,01; el servidor debe re-tarificar a 20
        var created = await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("ORD1", 0.01m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var orderId = (await JsonAsync(created)).GetProperty("id").GetString();

        var list = await JsonAsync(await ClientAsync(HttpMethod.Get, "/api/portal/orders", token));
        Assert.Contains(list.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetString() == orderId);

        var detail = await JsonAsync(await ClientAsync(HttpMethod.Get, $"/api/portal/orders/{orderId}", token));
        Assert.Equal("open", detail.GetProperty("status").GetString());
        Assert.Equal(72.6m, detail.GetProperty("totals").GetProperty("total").GetDecimal()); // 3×20 +21% IVA, NO 3×0,01
    }

    [Fact]
    public async Task Pedido_SinOfertaEnCatalogo_Devuelve400()
    {
        var token = await SeedClientUserAsync("CLI-NOOF", "cli-noof@portal.test", "Clave-1234");
        var response = await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { new { modelId = "MOD-INEXISTENTE", productId = "PRD-INEXISTENTE", qty = 1, price = 5.0 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pedido_AdminCambiaEstado_ClienteLoVe_yEstadoInvalido400()
    {
        await SeedCatalogAsync("ORD2", 10m);
        var token = await SeedClientUserAsync("CLI-ORD2", "cli-ord2@portal.test", "Clave-1234");
        var orderId = (await JsonAsync(await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("ORD2", 10m)))).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Put, $"/api/admin/orders/{orderId}/status", new { status = "shipped" })).StatusCode);
        var detail = await JsonAsync(await ClientAsync(HttpMethod.Get, $"/api/portal/orders/{orderId}", token));
        Assert.Equal("shipped", detail.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await AdminAsync(HttpMethod.Put, $"/api/admin/orders/{orderId}/status", new { status = "inventado" })).StatusCode);
    }

    [Fact]
    public async Task Pedido_BorradoDesdeCms_DesapareceDelPortal()
    {
        await SeedCatalogAsync("ORD3", 15m);
        var token = await SeedClientUserAsync("CLI-ORD3", "cli-ord3@portal.test", "Clave-1234");
        var orderId = (await JsonAsync(await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("ORD3", 15m)))).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await AdminAsync(HttpMethod.Delete, $"/api/admin/orders/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientAsync(HttpMethod.Get, $"/api/portal/orders/{orderId}", token)).StatusCode);
    }

    [Fact]
    public async Task Pedido_NumeroUnicoTrasBorrado()
    {
        await SeedCatalogAsync("SEQ", 12m);
        var token = await SeedClientUserAsync("CLI-SEQ", "cli-seq@portal.test", "Clave-1234");

        async Task<(string id, string number)> Place()
        {
            var id = (await JsonAsync(await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("SEQ", 12m)))).GetProperty("id").GetString()!;
            var number = (await JsonAsync(await ClientAsync(HttpMethod.Get, $"/api/portal/orders/{id}", token))).GetProperty("number").GetString()!;
            return (id, number);
        }

        var o1 = await Place();
        var o2 = await Place();
        var o3 = await Place();
        // Borrar el del medio no debe hacer que el siguiente recicle su número
        await AdminAsync(HttpMethod.Delete, $"/api/admin/orders/{o2.id}");
        var o4 = await Place();

        var numbers = new[] { o1.number, o3.number, o4.number };
        Assert.Equal(numbers.Length, numbers.Distinct().Count());   // todos distintos
        Assert.DoesNotContain(o4.number, new[] { o1.number, o3.number });
    }

    [Fact]
    public async Task Pedido_AisladoEntreClientes()
    {
        await SeedCatalogAsync("ISO", 25m);
        var tokenA = await SeedClientUserAsync("CLI-ISOA", "cli-isoa@portal.test", "Clave-1234");
        var tokenB = await SeedClientUserAsync("CLI-ISOB", "cli-isob@portal.test", "Clave-1234");

        var orderA = (await JsonAsync(await ClientAsync(HttpMethod.Post, "/api/portal/orders", tokenA, OrderBody("ISO", 25m)))).GetProperty("id").GetString();

        // El cliente B no lo ve en su lista ni puede abrirlo (404, no 403)
        var listB = await JsonAsync(await ClientAsync(HttpMethod.Get, "/api/portal/orders", tokenB));
        Assert.DoesNotContain(listB.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetString() == orderA);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientAsync(HttpMethod.Get, $"/api/portal/orders/{orderA}", tokenB)).StatusCode);
    }

    [Fact]
    public async Task Pedido_DespachaCanalBc_SimuladoYRegistrado()
    {
        await SeedCatalogAsync("DISP", 20m);
        var token = await SeedClientUserAsync("CLI-DISP", "cli-disp@portal.test", "Clave-1234");
        await ClientAsync(HttpMethod.Post, "/api/portal/orders", token, OrderBody("DISP", 20m));

        // El evento "Orden de compra" debe haber despachado el canal Business Central en
        // modo SIMULADO (BC no configurado) con el JSON transformado (salesOrders).
        var logs = await JsonAsync(await AdminAsync(HttpMethod.Get, "/api/admin/integration/logs?eventKey=shoes.purchase_order.updated"));
        var items = logs.GetProperty("items").EnumerateArray().ToList();
        var bc = items.FirstOrDefault(l => l.GetProperty("channelType").GetString() == "business-central");
        Assert.Equal("simulated", bc.GetProperty("status").GetString());
        var payload = bc.GetProperty("payloadJson").GetString() ?? "";
        Assert.Contains("orderId", payload);
        Assert.Contains("\"items\"", payload);
    }

    [Fact]
    public async Task TestTransform_EndpointAplicaJustNet()
    {
        var res = await AdminAsync(HttpMethod.Post, "/api/admin/integration/test-transform",
            new { transformer = """{"x":"#valueof($.a)"}""", input = """{"a":"hola"}""" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var j = await JsonAsync(res);
        Assert.Contains("hola", j.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Admin_RequierePermisos()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/admin/users")).StatusCode);
        var clientToken = await SeedClientUserAsync("CLI-SEC", "cli-sec@portal.test", "Clave-1234");
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientAsync(HttpMethod.Get, "/api/admin/users", clientToken)).StatusCode);
    }
}
