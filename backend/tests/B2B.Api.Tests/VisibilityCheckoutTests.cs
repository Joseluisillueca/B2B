using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Tarea 5: el VisibilityScope de VisibilityStore.ScopeForAsync (el mismo que la Tarea 4
// enchufó en CatalogService.QueryAsync) cierra las dos costuras que el catálogo filtrado
// NO tapaba: el checkout de POST /api/portal/orders (que arma el pedido a partir de las
// líneas que manda el CLIENTE, sin pasar por el catálogo) y el selector de modelos del
// agente en GET /api/agent/catalog-models (que listaba TODO el catálogo activo, cliente
// aparte). Ambos consultan el mismo scope; nada nuevo que mantener.
public class VisibilityCheckoutTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Pass = "cliente-checkout-vis-123";
    private const string AgentPass = "comercial-checkout-vis-123";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VisibilityCheckoutTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Utilidades de siembra (mismo patrón que VisibilityCatalogTests/PortalAutonomoTests) ──

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private Task PutModel(string id, string name, string reference, string familyId, string attributesJson) =>
        Put($"/api/catalog/models/{id}",
            $$"""
            {"name":{"es_ES":"{{name}}"},"active":true,"externalReference":"{{reference}}","familyId":"{{familyId}}","productSegments":["A"],"attributes":{{attributesJson}} }
            """);

    private Task PutOffer(string offerId, string modelId, decimal pvd)
    {
        var value = pvd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Put("/api/catalog/offers",
            $$$"""[{"id":"{{{offerId}}}","offerData":{"basePrice":{"code":"EUR","value":{{{value}}} },"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{modelId}}}"}}]""");
    }

    // El campo visibleAttributes es el que proyecta VisibilityStore.ProjectFromPayloadAsync
    // (Tarea 3) a la fila "bc" de CatalogVisibility, tanto para "client" como para "agent".
    private Task PutClientVisibility(string clientId, string rulesJsonArray) =>
        Put($"/api/clients/{clientId}",
            $$"""{"name":"Cliente checkout","canShop":true,"visibleAttributes":{{rulesJsonArray}} }""");

    // Token de un cliente real del portal (no el de integración), mismo patrón que
    // VisibilityCatalogTests.ClientTokenAsync: un AppUser "client-admin" con
    // ClientExternalId = clientId, del que PortalScope.ActorAsync saca el ClientId.
    private async Task<string> ClientTokenAsync(string clientId)
    {
        var email = $"{clientId}@cliente-checkout.test".ToLowerInvariant();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync(u => u.Email == email))
            {
                var user = new AppUser
                {
                    Id = Guid.NewGuid(), Email = email, PasswordHash = "",
                    Role = ClientIdentity.ClientAdminRole, ClientExternalId = clientId, Culture = "es_ES"
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Pass);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
        }
        return await _factory.LoginAsync(_client, email, Pass);
    }

    // Token de un comercial (rol "agent"), provisto por el sync de /api/agents/{id} (mismo
    // patrón que AgentModelTests): visibleAttributes va en el propio documento del agente,
    // por el mismo hook de ingesta que el del cliente (VisibilityStore.ProjectFromPayloadAsync).
    private async Task<string> AgentTokenAsync(string agentId, string email, string? rulesJsonArray = null)
    {
        var rules = rulesJsonArray is null ? "" : $$""", "visibleAttributes": {{rulesJsonArray}}""";
        await Put($"/api/agents/{agentId}",
            $$"""{"id":"{{agentId}}","parentId":null,"clientIds":[],"name":"Comercial","email":"{{email}}","culture":"es_ES"{{rules}} }""");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, AgentPass);
            await db.SaveChangesAsync();
        }

        return await _factory.LoginAsync(_client, email, AgentPass);
    }

    // Conmutador de Conexiones (rol admin, endpoint dedicado en IntegrationEndpoints):
    // fija el modo en la fila IntegrationSettings de BD, que manda sobre Portal:OrdersMode.
    private async Task SetOrdersModeAsync(string mode) =>
        (await Send(HttpMethod.Put, "/api/admin/integration/orders-mode",
            await _factory.GetAdminTokenAsync(_client), new { mode })).EnsureSuccessStatusCode();

    private static object Line(string modelId, string productId, string reference, decimal price, int qty = 1) => new
    {
        modelId, productId, size = "40", name = reference, reference, qty, price
    };

    // ── 1. Checkout (modo portal) bloquea una línea fuera de scope ─────────────

    [Fact]
    public async Task CheckoutBloqueaLineaFueraDeScope_ModoPortal()
    {
        const string clientId = "VISCK1CL-0000-4000-9000-000000000001";
        const string nike = "visck1n0-0000-4000-9000-000000000002";

        // Con oferta y precio válidos: si el checkout la bloquea, es SOLO por visibilidad,
        // no porque a la línea le faltara tarifa (RepriceAsync ya la rechazaría por eso).
        await PutModel(nike, "VISCK1 NIKE", "VCK1-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");
        await PutOffer("visck1of-0000-4000-9000-000000000003", nike, 40m);
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("portal");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(nike, "PRD-VCK1-NIKE", "VCK1-NIKE-REF", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("VCK1-NIKE-REF", body.GetProperty("error").GetString());

        // Nada de SaveChanges: el pedido no existe.
        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", token))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }

    // ── 2. Checkout (modo erp) también bloquea ──────────────────────────────────

    [Fact]
    public async Task CheckoutBloquea_ModoErp()
    {
        const string clientId = "VISCK2CL-0000-4000-9000-000000000003";
        const string nike = "visck2n0-0000-4000-9000-000000000004";

        await PutModel(nike, "VISCK2 NIKE", "VCK2-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("erp");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(nike, "PRD-VCK2-NIKE", "VCK2-NIKE-REF", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("VCK2-NIKE-REF", body.GetProperty("error").GetString());
    }

    // ── 3. Comprar solo lo visible funciona (modo portal, con re-tarificación real) ──

    [Fact]
    public async Task CheckoutPermite_SoloAdidas()
    {
        const string clientId = "VISCK3CL-0000-4000-9000-000000000005";
        const string adidas = "visck3a0-0000-4000-9000-000000000006";

        await PutModel(adidas, "VISCK3 ADIDAS", "VCK3-ADI-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutOffer("visck3of-0000-4000-9000-000000000007", adidas, 40m);
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("portal");

        // El precio de la línea (0,01) lo debe pisar la re-tarificación en servidor: la
        // validación de visibilidad, colocada ANTES, no debe romper ese pipeline.
        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(adidas, "PRD-VCK3-ADI", "VCK3-ADI-REF", 0.01m) }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40m, body.GetProperty("total").GetDecimal());
    }

    // ── 4. Selector de modelos del agente (GET /api/agent/catalog-models) filtrado ──

    [Fact]
    public async Task CatalogModelsDelAgenteFiltrado()
    {
        const string restrictedAgentId = "VISCK4AG-0000-4000-9000-000000000008";
        const string openAgentId = "VISCK4AG-0000-4000-9000-000000000009";
        const string adidas = "visck4a0-0000-4000-9000-000000000010";
        const string nike = "visck4n0-0000-4000-9000-000000000011";

        await PutModel(adidas, "VISCK4 ADIDAS", "VCK4-ADI-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutModel(nike, "VISCK4 NIKE", "VCK4-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");

        // Agente con reglas: solo ve ADIDAS
        var restrictedToken = await AgentTokenAsync(
            restrictedAgentId, "comercial-visck4-restringido@agente.test",
            """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var restrictedBody = await (await Send(HttpMethod.Get, "/api/agent/catalog-models?take=100", restrictedToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        var restrictedRefs = restrictedBody.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("reference").GetString()).ToList();
        Assert.Contains("VCK4-ADI-REF", restrictedRefs);
        Assert.DoesNotContain("VCK4-NIKE-REF", restrictedRefs);

        // Agente sin reglas: ve de todo (incluidos ambos modelos de esta prueba)
        var openToken = await AgentTokenAsync(openAgentId, "comercial-visck4-abierto@agente.test");
        var openBody = await (await Send(HttpMethod.Get, "/api/agent/catalog-models?take=100", openToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        var openRefs = openBody.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("reference").GetString()).ToList();
        Assert.Contains("VCK4-ADI-REF", openRefs);
        Assert.Contains("VCK4-NIKE-REF", openRefs);
    }
}
