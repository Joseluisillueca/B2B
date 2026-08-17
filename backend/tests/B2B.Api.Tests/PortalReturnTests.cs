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

// Fase 4 del portal: /sat (08-sat.png). Las devoluciones NO son documentos de
// Business Central: son un flujo propio del portal sobre la tabla return_requests
// (plan §1 y §4), con bultos, horario de recogida, foto y resolución.
//
// Acotado al clientId del token, como todo lo demás: la solicitud del cliente A no
// existe para el cliente B (404, no 403 que confirme que existe).
public class PortalReturnTests : IClassFixture<PortalReturnTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "SAT0000A-1111-4111-8111-000000000001";
    private const string ClientB = "SAT0000B-2222-4222-8222-000000000002";
    private const string OtherEmail = "otra-tienda@sat-b.test";
    private const string OtherPassword = "otra-clave-321";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PortalReturnTests(Factory factory)
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
                """{"name":"TEST 5","externalReference":"C100057","canShop":true,"groupIds":[]}""");
            await PutAsync($"/api/clients/{ClientB}",
                """{"name":"OTRA TIENDA","externalReference":"C100099","canShop":true,"groupIds":[]}""");
            await PutAsync($"/api/clients/{ClientA}/users/admin",
                $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test 5","culture":"es_ES"}""");
            await PutAsync($"/api/clients/{ClientB}/users/admin",
                $$"""{"email":"{{OtherEmail}}","name":"Otra","culture":"es_ES"}""");

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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<string> OtherTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = OtherEmail, password = OtherPassword, type = "global", longDuration = true });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string route, object? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", token ?? await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object NewReturn(string reference = "AV-2400123", int packages = 2, int items = 3) => new
    {
        type = "return",
        pickupSlot = "morning",
        packages,
        items,
        reference,
        notes = "Dos pares con la costura abierta"
    };

    // ══════════════ Alta y listado ══════════════

    [Fact]
    public async Task Devoluciones_SinNinguna_DevuelveListaVacia()
    {
        // Fábrica limpia: con esta respuesta la vista dice "No se han encontrado resultados"
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/returns");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await factory.GetTokenAsync(client));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("counts").GetProperty("all").GetInt32());
    }

    [Fact]
    public async Task Devoluciones_NuevaDevolucion_ApareceEnElListadoConSus10Columnas()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/portal/returns", NewReturn());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var detail = await JsonAsync(created);
        var id = detail.GetProperty("id").GetString();
        var code = detail.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Equal("pending", detail.GetProperty("status").GetString());
        Assert.Equal("", detail.GetProperty("resolution").GetString());

        var list = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/returns"));
        var row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

        // Las 10 columnas de 08-sat.png: IMG · CÓDIGO · FECHA · CLIENTE · TIPO ·
        // HORARIO · BULTOS · ITEMS · ESTADO · RESOLUCIÓN
        Assert.Equal(code, row.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("createdAt").GetString()));
        Assert.Equal("TEST 5", row.GetProperty("client").GetString());
        Assert.Equal("return", row.GetProperty("type").GetString());
        Assert.Equal("morning", row.GetProperty("pickupSlot").GetString());
        Assert.Equal(2, row.GetProperty("packages").GetInt32());
        Assert.Equal(3, row.GetProperty("items").GetInt32());
        Assert.Equal("pending", row.GetProperty("status").GetString());
        Assert.Equal("", row.GetProperty("resolution").GetString());
        Assert.True(row.TryGetProperty("photoUrl", out _));

        // El rail cuenta por estado, como en los otros listados
        Assert.True(list.GetProperty("counts").GetProperty("pending").GetInt32() >= 1);
    }

    [Fact]
    public async Task Devoluciones_ElCodigoEsUnicoYCorrelativo()
    {
        var first = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/returns", NewReturn())))
            .GetProperty("code").GetString();
        var second = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/returns", NewReturn())))
            .GetProperty("code").GetString();

        Assert.NotEqual(first, second);
        Assert.StartsWith($"DEV-{DateTime.UtcNow:yyyy}-", first);
    }

    [Fact]
    public async Task Devoluciones_FiltraPorEstadoYPorTextoDeBusqueda()
    {
        var created = await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/returns",
            NewReturn(reference: "AV-BUSCABLE-9")));
        var code = created.GetProperty("code").GetString();

        var found = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/returns?search=BUSCABLE-9"));
        Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(code, found.GetProperty("items")[0].GetProperty("code").GetString());

        var byCode = await JsonAsync(await SendAsync(HttpMethod.Get, $"/api/portal/returns?search={code}"));
        Assert.Single(byCode.GetProperty("items").EnumerateArray());

        // Ninguna está confirmada todavía: el filtro del rail devuelve vacío
        var confirmed = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/returns?status=confirmed"));
        Assert.Empty(confirmed.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Devoluciones_Pagina()
    {
        for (var i = 0; i < 3; i++)
            await SendAsync(HttpMethod.Post, "/api/portal/returns", NewReturn(reference: $"PAG-{i}"));

        var page = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/returns?skip=0&take=2"));
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("total").GetInt32() >= 3);
    }

    // ══════════════ Validación ══════════════

    [Theory]
    [InlineData("inventado", "morning", 1, 1)]     // tipo desconocido
    [InlineData("return", "madrugada", 1, 1)]      // horario desconocido
    [InlineData("return", "morning", 0, 1)]        // sin bultos
    [InlineData("return", "morning", 1, 0)]        // sin artículos
    public async Task Devoluciones_ConDatosInvalidos_Devuelve400(
        string type, string slot, int packages, int items)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/portal/returns",
            new { type, pickupSlot = slot, packages, items });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ══════════════ Aislamiento entre clientes ══════════════

    [Fact]
    public async Task Devoluciones_NoSeVenNiSeAbrenLasDeOtroCliente()
    {
        var mine = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/returns",
            NewReturn(reference: "SOLO-DEL-CLIENTE-A")))).GetProperty("id").GetString();

        var otherToken = await OtherTokenAsync();
        var theirs = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/returns", token: otherToken));
        Assert.DoesNotContain("SOLO-DEL-CLIENTE-A", theirs.GetRawText());

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Get, $"/api/portal/returns/{mine}", token: otherToken)).StatusCode);
    }

    [Fact]
    public async Task Devoluciones_ElDetalleDevuelveLaSolicitudCompleta()
    {
        var id = (await JsonAsync(await SendAsync(HttpMethod.Post, "/api/portal/returns",
            NewReturn(reference: "AV-DETALLE-1")))).GetProperty("id").GetString();

        var detail = await JsonAsync(await SendAsync(HttpMethod.Get, $"/api/portal/returns/{id}"));
        Assert.Equal("AV-DETALLE-1", detail.GetProperty("reference").GetString());
        Assert.Equal("Dos pares con la costura abierta", detail.GetProperty("notes").GetString());
        Assert.Equal(TestWebApplicationFactory.SeededEmail, detail.GetProperty("owner").GetString());
    }

    [Fact]
    public async Task Devoluciones_SinToken_Devuelve401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/returns")).StatusCode);
    }
}
