using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Fase 2 del portal: carritos favoritos (06-shopping-carts.png), favoritos de modelo
// y el "TERMINAR PEDIDO" del checkout, que deja el pedido pendiente de envío a BC.
// Todo va acotado al cliente del token: un cliente jamás ve el carrito de otro.
public class PortalCartTests : IClassFixture<PortalCartTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "CART000A-1111-4111-8111-000000000001";
    private const string ClientB = "CART000B-2222-4222-8222-000000000002";
    private const string OtherEmail = "otra-tienda@cliente-b.test";
    private const string OtherPassword = "otra-clave-123";

    private const string ModelId = "CARTMODL-0000-4000-9000-000000000001";
    private const string Product36 = "CARTPROD-0000-4000-9000-000000000036";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PortalCartTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static bool _seeded;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task SeedAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            await PutAsync($"/api/clients/{ClientA}",
                """{"name":"TEST 5","externalReference":"C100057","canShop":true,"groupIds":["mayorista"]}""");
            await PutAsync($"/api/clients/{ClientB}",
                """{"name":"OTRA TIENDA","externalReference":"C100099","canShop":true,"groupIds":[]}""");
            // Tarea 5: el checkout (POST /api/portal/orders) valida las líneas contra el
            // catálogo real (modelo conocido y activo), así que la línea de prueba necesita
            // un CatalogModel de verdad detrás de ModelId.
            await PutAsync($"/api/catalog/models/{ModelId}",
                """{"name":{"es_ES":"LEJAN ONE - AETERNA"},"active":true,"externalReference":"1974","productSegments":[],"attributes":{}}""");
            await PutAsync($"/api/clients/{ClientA}/users/admin",
                $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test 5","culture":"es_ES"}""");
            await PutAsync($"/api/clients/{ClientB}/users/admin",
                $$"""{"email":"{{OtherEmail}}","name":"Otra","culture":"es_ES"}""");

            // El sync no trae contraseña: se la ponemos para poder autenticar al otro cliente
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var other = await db.Users.SingleAsync(u => u.Email == OtherEmail);
            other.PasswordHash = new PasswordHasher<AppUser>().HashPassword(other, OtherPassword);
            await db.SaveChangesAsync();

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<string> OtherTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = OtherEmail, password = OtherPassword, type = "global", longDuration = true });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, object? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", token ?? await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private static object Cart(string name, params object[] lines) => new
    {
        name,
        windowId = "reposic",
        lines = lines.Length > 0 ? lines : [Line(2)]
    };

    private static object Line(int qty) => new
    {
        modelId = ModelId,
        productId = Product36,
        size = "36",
        name = "LEJAN ONE - AETERNA",
        reference = "1974",
        qty,
        price = 48.0
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Carts_SinNingunCarrito_DevuelveListaVacia()
    {
        // Fábrica limpia: la vista dice "No hay carritos guardados" con esta respuesta
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/carts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await factory.GetTokenAsync(client));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await JsonAsync(response)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Carts_GuardaElCarritoActualYLoRecupera()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/portal/carts", Cart("Reposición semana 34"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("id").GetString();
        Assert.NotNull(id);

        // Listado: NOMBRE · FECHA · PROPIETARIO/A · UNIDADES
        var list = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/carts"));
        var row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);
        Assert.Equal("Reposición semana 34", row.GetProperty("name").GetString());
        Assert.Equal(2, row.GetProperty("units").GetInt32());
        Assert.Equal(TestWebApplicationFactory.SeededEmail, row.GetProperty("owner").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("createdAt").GetString()));

        // Recuperación: las líneas vuelven íntegras para volcarlas en el carrito activo
        var detail = await JsonAsync(await SendAsync(HttpMethod.Get, $"/api/portal/carts/{id}"));
        var line = Assert.Single(detail.GetProperty("lines").EnumerateArray());
        Assert.Equal(Product36, line.GetProperty("productId").GetString());
        Assert.Equal("36", line.GetProperty("size").GetString());
        Assert.Equal(2, line.GetProperty("qty").GetInt32());
        Assert.Equal(48.0m, line.GetProperty("price").GetDecimal());
        Assert.Equal("reposic", detail.GetProperty("windowId").GetString());
    }

    [Fact]
    public async Task Carts_RenombraYBorra()
    {
        var id = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/carts", Cart("Borrador"))))
            .GetProperty("id").GetString();

        var updated = await SendAsync(HttpMethod.Put, $"/api/portal/carts/{id}",
            new { name = "Pedido de agosto", windowId = "reposic", lines = new[] { Line(5) } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await JsonAsync(updated);
        Assert.Equal("Pedido de agosto", body.GetProperty("name").GetString());
        Assert.Equal(5, body.GetProperty("units").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/portal/carts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/portal/carts/{id}")).StatusCode);
    }

    [Fact]
    public async Task Carts_NoDevuelveElCarritoDeOtroCliente()
    {
        var mine = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/carts", Cart("Solo del cliente A"))))
            .GetProperty("id").GetString();

        var otherToken = await OtherTokenAsync();
        var theirList = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/carts", token: otherToken));
        Assert.DoesNotContain("Solo del cliente A", theirList.GetRawText());

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Get, $"/api/portal/carts/{mine}", token: otherToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Delete, $"/api/portal/carts/{mine}", token: otherToken)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Carts_SinNombre_Devuelve400(string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/portal/carts", Cart(name));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Carts_SinLineasOConCantidadInvalida_Devuelve400()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Post, "/api/portal/carts",
                new { name = "Vacío", windowId = "reposic", lines = Array.Empty<object>() })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Post, "/api/portal/carts",
                new { name = "Cantidad cero", windowId = "reposic", lines = new[] { Line(0) } })).StatusCode);
    }

    [Fact]
    public async Task Carts_ExportaCsvConBom()
    {
        var id = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/carts", Cart("Para Excel"))))
            .GetProperty("id").GetString();

        var response = await SendAsync(HttpMethod.Get, $"/api/portal/carts/{id}/export.csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Referencia", csv);
        Assert.Contains("1974", csv);
        Assert.Contains("36", csv);
    }

    [Fact]
    public async Task Favoritos_SeMarcanYSeQuitanPorModelo()
    {
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(HttpMethod.Put, $"/api/portal/favorites/{ModelId}")).StatusCode);

        var list = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/favorites"));
        Assert.Contains(ModelId, list.GetProperty("items").EnumerateArray().Select(i => i.GetString()));

        // Idempotente: marcar dos veces no duplica ni falla
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(HttpMethod.Put, $"/api/portal/favorites/{ModelId}")).StatusCode);
        Assert.Single((await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/favorites")))
            .GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(HttpMethod.Delete, $"/api/portal/favorites/{ModelId}")).StatusCode);
        Assert.Empty((await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/favorites")))
            .GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Favoritos_NoSeCompartenEntreClientes()
    {
        await SendAsync(HttpMethod.Put, $"/api/portal/favorites/{ModelId}");
        var theirs = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/favorites", token: await OtherTokenAsync()));
        Assert.Empty(theirs.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task TerminarPedido_DejaElPedidoPendienteDeEnvioABc()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/portal/orders", new
        {
            windowId = "reposic",
            reference = "PED-2026-01",
            lines = new[] { Line(3) }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("pending-bc", body.GetProperty("status").GetString());
        Assert.Equal(3, body.GetProperty("units").GetInt32());
        Assert.Equal(144.0m, body.GetProperty("total").GetDecimal());

        // El pedido no es un carrito favorito: no ensucia /shopping-carts
        var carts = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/carts"));
        Assert.DoesNotContain(carts.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetString() == body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task TerminarPedido_ConCarritoVacio_Devuelve400()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/portal/orders",
            new { windowId = "reposic", lines = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Carts_SinToken_Devuelve401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/carts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/favorites")).StatusCode);
    }
}
