using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace B2B.Api.Tests;

// API del CMS para las REGLAS DE TRANSPORTE (/api/admin/transport-rules) y su /preview.
// Cubre el ciclo alta→listado→edición→borrado, la validación de entrada, la autorización
// (solo el rol admin) y la previsualización (qué regla casa y qué coste sale). Sigue el
// patrón del repo: WebApplicationFactory + InMemory + login admin. Cada regla creada aquí
// lleva un ClientExternalId propio, de modo que las previews por cliente son deterministas
// aunque la BD en memoria se comparta entre pruebas de la clase.
public class TransportRulesEndpointTests : IClassFixture<TransportRulesEndpointTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public TransportRulesEndpointTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private const string Base = "/api/admin/transport-rules";

    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string route, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    // Cuerpo de regla (TransportRuleBody); los null se omiten para probar defaults del servidor.
    private static object RuleBody(
        string? name = "Regla", bool? active = null, int? priority = null,
        string? clientExternalId = null, string? countryIsoId = null, string? orderType = null,
        int? minUnits = null, decimal? minAmount = null,
        decimal cost = 0, bool? perUnit = null, string? incotermId = null) => new
    {
        name, active, priority, clientExternalId, countryIsoId, orderType,
        minUnits, minAmount, cost, perUnit, incotermId,
    };

    // ── Ciclo de vida: alta → listado → edición → borrado ─────────────────────────
    [Fact]
    public async Task Crud_AltaListadoEdicionBorrado()
    {
        // Alta
        var created = await AdminAsync(HttpMethod.Post, Base,
            RuleBody(name: "CRUD Rule", clientExternalId: "CRUD-CLI", priority: 5, cost: 10m, incotermId: "fob"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        // Listado: la regla aparece con sus datos
        var listed = FindById(await ListAsync(), id!);
        Assert.Equal("CRUD Rule", listed.GetProperty("name").GetString());
        Assert.Equal(10m, listed.GetProperty("cost").GetDecimal());

        // Edición
        var edited = await AdminAsync(HttpMethod.Put, $"{Base}/{id}",
            RuleBody(name: "CRUD Rule v2", clientExternalId: "CRUD-CLI", priority: 5, cost: 22.5m));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(22.5m, (await JsonAsync(edited)).GetProperty("cost").GetDecimal());

        // El cambio se ve en el listado
        var reread = FindById(await ListAsync(), id!);
        Assert.Equal("CRUD Rule v2", reread.GetProperty("name").GetString());
        Assert.Equal(22.5m, reread.GetProperty("cost").GetDecimal());

        // Borrado → 204 y desaparece del listado
        var deleted = await AdminAsync(HttpMethod.Delete, $"{Base}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(await ListAsync(), item => item.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Editar_ReglaInexistente_Devuelve404()
    {
        var res = await AdminAsync(HttpMethod.Put, $"{Base}/{Guid.NewGuid()}", RuleBody(name: "X", cost: 1m));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Borrar_ReglaInexistente_Devuelve404()
    {
        var res = await AdminAsync(HttpMethod.Delete, $"{Base}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ── El listado se ordena por prioridad ascendente ─────────────────────────────
    [Fact]
    public async Task Listado_OrdenadoPorPrioridad()
    {
        // Se crean fuera de orden; el GET debe devolverlas por Priority ascendente.
        await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "ORD-C", clientExternalId: "ORD-C-CLI", priority: 30, cost: 1m));
        await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "ORD-A", clientExternalId: "ORD-A-CLI", priority: 10, cost: 1m));
        await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "ORD-B", clientExternalId: "ORD-B-CLI", priority: 20, cost: 1m));

        var priorities = (await ListAsync())
            .Where(i => (i.GetProperty("name").GetString() ?? "").StartsWith("ORD-"))
            .Select(i => i.GetProperty("priority").GetInt32())
            .ToList();

        Assert.Equal(new[] { 10, 20, 30 }, priorities);
    }

    // ── Validación (400) ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Alta_NombreVacio_Devuelve400()
    {
        var res = await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "   ", cost: 5m));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Alta_CosteNegativo_Devuelve400()
    {
        var res = await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "Neg", cost: -1m));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Alta_TipoDePedidoInvalido_Devuelve400()
    {
        var res = await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "BadType", orderType: "URGENTE", cost: 1m));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Alta_TipoDePedidoValido_SeAcepta()
    {
        foreach (var type in new[] { "REPLENISHMENT", "SCHEDULED" })
        {
            var res = await AdminAsync(HttpMethod.Post, Base,
                RuleBody(name: $"OK-{type}", clientExternalId: $"OKT-{type}", orderType: type, cost: 1m));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }
    }

    [Fact]
    public async Task Alta_MinimosNegativos_Devuelve400()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "MU", minUnits: -1, cost: 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AdminAsync(HttpMethod.Post, Base, RuleBody(name: "MA", minAmount: -1m, cost: 1m))).StatusCode);
    }

    // ── Autorización ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task SinToken_Devuelve401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(Base)).StatusCode);

        var post = new HttpRequestMessage(HttpMethod.Post, Base) { Content = JsonContent.Create(RuleBody(name: "X", cost: 1m)) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(post)).StatusCode);
    }

    [Fact]
    public async Task ConTokenNoAdmin_Devuelve403()
    {
        // El usuario de integración está autenticado pero NO es admin del CMS → 403.
        var token = await _factory.GetTokenAsync(_client);

        var get = new HttpRequestMessage(HttpMethod.Get, Base);
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(get)).StatusCode);

        var post = new HttpRequestMessage(HttpMethod.Post, Base) { Content = JsonContent.Create(RuleBody(name: "X", cost: 1m)) };
        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(post)).StatusCode);
    }

    // ── Previsualización ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Preview_DevuelveLaReglaQueCasa()
    {
        await AdminAsync(HttpMethod.Post, Base,
            RuleBody(name: "Preview Match", clientExternalId: "PREVM-CLI", cost: 25m, incotermId: "usa"));

        var res = await AdminAsync(HttpMethod.Post, $"{Base}/preview",
            new { clientExternalId = "PREVM-CLI", units = 1, amount = 0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var j = await JsonAsync(res);
        Assert.True(j.GetProperty("matched").GetBoolean());
        Assert.Equal(25m, j.GetProperty("cost").GetDecimal());
        Assert.Equal("Preview Match", j.GetProperty("ruleName").GetString());
        Assert.Equal("usa", j.GetProperty("incotermId").GetString());
    }

    [Fact]
    public async Task Preview_PerUnit_MultiplicaPorUnidades()
    {
        await AdminAsync(HttpMethod.Post, Base,
            RuleBody(name: "Preview PerUnit", clientExternalId: "PREVU-CLI", cost: 3m, perUnit: true));

        var j = await JsonAsync(await AdminAsync(HttpMethod.Post, $"{Base}/preview",
            new { clientExternalId = "PREVU-CLI", units = 4, amount = 0 }));
        Assert.True(j.GetProperty("matched").GetBoolean());
        Assert.Equal(12m, j.GetProperty("cost").GetDecimal());   // 3 × 4
    }

    [Fact]
    public async Task Preview_SinCoincidencia_MatchedFalseYCoste0()
    {
        // Ningún regla lleva este cliente y ninguna casa con todo → sin coincidencia.
        var j = await JsonAsync(await AdminAsync(HttpMethod.Post, $"{Base}/preview",
            new { clientExternalId = "NOBODY-PREV-XYZ", units = 1, amount = 0 }));
        Assert.False(j.GetProperty("matched").GetBoolean());
        Assert.Equal(0m, j.GetProperty("cost").GetDecimal());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────
    private async Task<List<JsonElement>> ListAsync()
    {
        var res = await AdminAsync(HttpMethod.Get, Base);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await JsonAsync(res)).GetProperty("items").EnumerateArray().ToList();
    }

    private static JsonElement FindById(List<JsonElement> items, string id) =>
        items.Single(i => i.GetProperty("id").GetString() == id);
}
