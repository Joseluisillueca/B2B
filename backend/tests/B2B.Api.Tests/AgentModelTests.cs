using System.IdentityModel.Tokens.Jwt;
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

// Modelo de agente, Fase 1 (contrato 04 §4): login del comercial, cartera de clientes
// y suplantación. La regla que atraviesa todo el bloque es de AISLAMIENTO: un agente
// SOLO ve y suplanta a los clientes de SU cartera, nunca a los de otro agente.
public class AgentModelTests : IClassFixture<AgentModelTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    // Cartera del agente A
    private const string ClientA1 = "A1A1A1A1-1111-4111-8111-000000000001";
    private const string ClientA2 = "A2A2A2A2-2222-4222-8222-000000000002";
    // Cartera del agente B (jamás debe cruzarse con la de A)
    private const string ClientB1 = "B1B1B1B1-3333-4333-8333-000000000003";

    private const string AgentAId = "A9A9A9A9-9999-4999-8999-00000000000A";
    private const string AgentBId = "B9B9B9B9-8888-4888-8888-00000000000B";

    private const string AgentAEmail = "comercial-a@agente.test";
    private const string AgentBEmail = "comercial-b@agente.test";
    private const string AgentPassword = "agente-secreto-123";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AgentModelTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await SeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Semilla ────────────────────────────────────────────────────────────────

    private static bool _seeded;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task SeedAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            await PutAsync($"/api/clients/{ClientA1}", Client("CLIENTE A1", "C0001", "Madrid", "Madrid", "ES", canShop: true, segment: "A+"));
            await PutAsync($"/api/clients/{ClientA2}", Client("CLIENTE A2", "C0002", "Vigo", "Pontevedra", "ES", canShop: false, segment: "B"));
            await PutAsync($"/api/clients/{ClientB1}", Client("CLIENTE B1", "C0003", "Sevilla", "Sevilla", "ES", canShop: true, segment: "A+"));

            // Pedidos del cliente A1: fijan lastOrderDate (máximo) y total (suma)
            await PutAsync("/api/orders/ORD-A1-1", Order("ORD-A1-1", ClientA1, "PV0001", "2026-07-10", 1000.0m));
            await PutAsync("/api/orders/ORD-A1-2", Order("ORD-A1-2", ClientA1, "PV0002", "2026-08-05", 500.0m));
            // Pedido del cliente B1: nunca debe contarse en la cartera de A
            await PutAsync("/api/orders/ORD-B1-1", Order("ORD-B1-1", ClientB1, "PV9999", "2026-08-01", 999.0m));

            // Documentos de agente (contrato 04 §4)
            await PutAsync($"/api/agents/{AgentAId}", Agent(AgentAId, AgentAEmail, "Comercial A", ClientA1, ClientA2));
            await PutAsync($"/api/agents/{AgentBId}", Agent(AgentBId, AgentBEmail, "Comercial B", ClientB1));

            // El sync no trae contraseña: se le asigna una para poder iniciar sesión
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var email in new[] { AgentAEmail, AgentBEmail })
                {
                    var user = await db.Users.SingleAsync(u => u.Email == email);
                    user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, AgentPassword);
                }
                await db.SaveChangesAsync();
            }

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private static string Client(string name, string number, string city, string province, string country, bool canShop, string segment) => $$"""
    {
      "name": "{{name}}",
      "externalReference": "{{number}}",
      "canShop": {{(canShop ? "true" : "false")}},
      "groupIds": [],
      "productSegments": ["{{segment}}"],
      "payMethods": [],
      "fiscalInfo": { "address": { "city": "{{city}}", "province": "{{province}}", "countryIsoId": "{{country}}" } }
    }
    """;

    private static string Order(string id, string clientId, string number, string date, decimal total) => $$"""
    {
      "id": "{{id}}",
      "clientId": "{{clientId}}",
      "totals": { "total": { "code": "EUR", "value": {{total.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } },
      "type": "SCHEDULED",
      "items": [ { "transactionInfo": { "info": { "quantity": 2 } } } ],
      "externalReference": "{{number}}",
      "orderedDate": "{{date}}T00:00:00",
      "status": "open",
      "seasonId": ""
    }
    """;

    private static string Agent(string id, string email, string name, params string[] clientIds)
    {
        var ids = string.Join(",", clientIds.Select(c => $"\"{c}\""));
        return $$"""
        { "id": "{{id}}", "parentId": null, "clientIds": [{{ids}}],
          "name": "{{name}}", "email": "{{email}}", "culture": "es_ES" }
        """;
    }

    // ── Utilidades HTTP ──────────────────────────────────────────────────────────

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private Task<string> AgentAToken() => _factory.LoginAsync(_client, AgentAEmail, AgentPassword);
    private Task<string> AgentBToken() => _factory.LoginAsync(_client, AgentBEmail, AgentPassword);

    private async Task<HttpResponseMessage> GetAsync(string route, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> JsonAsync(string route, string token)
    {
        var response = await GetAsync(route, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> PostAsync(string route, object body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static string[] ClientIds(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("clientId").GetString()!)];

    // ══════════════ Provisión del agente ══════════════

    [Fact]
    public async Task Sync_Agente_ProvisionaUsuarioConRolAgenteYSinCliente()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == AgentAEmail);

        Assert.NotNull(user);
        Assert.Equal("agent", user!.Role);
        Assert.Equal(AgentAId, user.AgentExternalId);
        Assert.Null(user.ClientExternalId);
        Assert.Null(user.ClientNumber);
        Assert.Equal("Comercial A", user.Name);
        Assert.Equal("es_ES", user.Culture);
    }

    [Fact]
    public async Task Sync_Agente_EmailQueEraClienteAdmin_SeConvierteEnAgente()
    {
        const string email = "convertible@agente.test";
        const string clientId = "CCCCCCCC-0000-4000-8000-00000000000C";
        const string agentId = "CAFECAFE-0000-4000-8000-00000000000C";

        // Primero llega como usuario admin de un cliente
        await PutAsync($"/api/clients/{clientId}", Client("CONVERTIBLE", "C0009", "Bilbao", "Bizkaia", "ES", true, "A+"));
        await PutAsync($"/api/clients/{clientId}/users/admin",
            $$"""{ "email": "{{email}}", "name": "Antiguo", "culture": "es_ES" }""");

        // Luego el mismo email llega como comercial: gana el agente
        await PutAsync($"/api/agents/{agentId}", Agent(agentId, email, "Ahora Comercial", clientId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal("agent", user.Role);
        Assert.Equal(agentId, user.AgentExternalId);
        Assert.Null(user.ClientExternalId);   // el vínculo de cliente se retira
        Assert.Null(user.ClientNumber);
    }

    [Fact]
    public async Task Login_Agente_EmiteTokenConRolAgenteYSinCliente()
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(await AgentAToken());
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "clientId");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "actingAgent");
    }

    // ══════════════ /api/portal/me para el agente ══════════════

    [Fact]
    public async Task Me_Agente_DevuelveCredencialDeAgente()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AgentAToken());
        var body = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("isAgent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("client").ValueKind);

        var credential = Assert.Single(body.GetProperty("credentials").EnumerateArray());
        Assert.Equal("agent", credential.GetProperty("type").GetString());
        Assert.Equal("agent", credential.GetProperty("roleKey").GetString());
        Assert.True(credential.GetProperty("agent").GetBoolean());
        Assert.Equal("Comercial A", credential.GetProperty("name").GetString());
    }

    // UX-A1 (14a-5): bajo suplantación /me devuelve la ficha del cliente SUPLANTADO
    // (fiscalInfo, direcciones, formas de pago) igual que para un cliente normal, y la
    // credencial de cliente va delante con su nombre: el checkout ya no enseña al
    // agente como cliente/facturación ni pierde direcciones y pagos.
    [Fact]
    public async Task Me_Suplantando_DevuelveFichaDelClienteSuplantado()
    {
        // Mismos datos de A2 que la semilla + forma de pago y una dirección de envío.
        await PutAsync($"/api/clients/{ClientA2}", """
            { "name": "CLIENTE A2", "externalReference": "C0002", "canShop": false, "groupIds": [],
              "productSegments": ["B"], "payMethods": ["transf30"],
              "fiscalInfo": { "fiscalName": "Cliente A2 SL", "address": { "city": "Vigo", "province": "Pontevedra", "countryIsoId": "ES" } } }
            """);
        await PutAsync($"/api/clients/{ClientA2}/shipping-addresses/SA-A2-0001",
            """{ "alias": "Almacén Vigo", "address": { "city": "Vigo", "countryIsoId": "ES" } }""");

        var impersonate = await PostAsync("/api/agent/impersonate", new { clientId = ClientA2 }, await AgentAToken());
        var token = (await impersonate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var body = await JsonAsync("/api/portal/me", token);

        Assert.True(body.GetProperty("isAgent").GetBoolean());
        Assert.True(body.GetProperty("impersonating").GetBoolean());
        var client = body.GetProperty("client");
        Assert.Equal(ClientA2, client.GetProperty("id").GetString());
        Assert.Equal("CLIENTE A2", client.GetProperty("name").GetString());
        Assert.Equal("Cliente A2 SL", client.GetProperty("fiscalInfo").GetProperty("fiscalName").GetString());
        Assert.Equal("transf30", client.GetProperty("payMethods")[0].GetProperty("id").GetString());
        Assert.Equal("SA-A2-0001", client.GetProperty("shippingAddresses")[0].GetProperty("id").GetString());

        // La credencial del cliente (la que usa el checkout) va primero y lleva SU nombre.
        var first = body.GetProperty("credentials")[0];
        Assert.Equal("CLIENTE A2", first.GetProperty("name").GetString());
        Assert.Equal(ClientA2, first.GetProperty("clientId").GetString());

        // Sin suplantar, todo sigue igual: sin cliente.
        var plain = await JsonAsync("/api/portal/me", await AgentAToken());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("client").ValueKind);
        Assert.False(plain.GetProperty("impersonating").GetBoolean());
    }

    // ══════════════ /api/agent/clients ══════════════

    [Fact]
    public async Task Clients_Agente_SoloVeSuCartera()
    {
        var a = await JsonAsync("/api/agent/clients", await AgentAToken());
        Assert.Equal(2, a.GetProperty("total").GetInt32());
        Assert.Equal([ClientA1, ClientA2], ClientIds(a).Order().ToArray());

        var b = await JsonAsync("/api/agent/clients", await AgentBToken());
        Assert.Equal([ClientB1], ClientIds(b));

        // Ni rastro de la cartera ajena en la respuesta del otro agente
        Assert.DoesNotContain("CLIENTE B1", a.GetRawText());
        Assert.DoesNotContain("CLIENTE A1", b.GetRawText());
    }

    [Fact]
    public async Task Clients_ProyectaLasColumnasDeLaCartera()
    {
        var body = await JsonAsync("/api/agent/clients", await AgentAToken());
        var row = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("clientId").GetString() == ClientA1);

        Assert.Equal("C0001", row.GetProperty("number").GetString());
        Assert.Equal("CLIENTE A1", row.GetProperty("name").GetString());
        Assert.Equal("A+", row.GetProperty("segment").GetString());
        Assert.Equal("ES", row.GetProperty("country").GetString());
        Assert.Equal("Madrid", row.GetProperty("province").GetString());
        Assert.Equal("Madrid", row.GetProperty("city").GetString());
        Assert.True(row.GetProperty("canShop").GetBoolean());
        Assert.True(row.GetProperty("active").GetBoolean());
        // lastOrderDate = pedido más reciente; total = suma de los pedidos del cliente
        Assert.StartsWith("2026-08-05", row.GetProperty("lastOrderDate").GetString());
        Assert.Equal(1500.0m, row.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Clients_Filtra_PorBusquedaCiudadSegmentoYCanShop()
    {
        var token = await AgentAToken();
        Assert.Equal([ClientA2], ClientIds(await JsonAsync("/api/agent/clients?search=A2", token)));
        Assert.Equal([ClientA1], ClientIds(await JsonAsync("/api/agent/clients?search=C0001", token)));
        Assert.Equal([ClientA1], ClientIds(await JsonAsync("/api/agent/clients?city=madrid", token)));
        Assert.Equal([ClientA1], ClientIds(await JsonAsync("/api/agent/clients?segment=A%2B", token)));
        Assert.Equal([ClientA1], ClientIds(await JsonAsync("/api/agent/clients?canShop=true", token)));
        Assert.Equal([ClientA2], ClientIds(await JsonAsync("/api/agent/clients?canShop=false", token)));
    }

    [Fact]
    public async Task Clients_SinToken_401_ClientAdminYIntegracion_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/agent/clients")).StatusCode);

        // Token de integración (rol integration): no es agente
        var integ = await _factory.GetTokenAsync(_client);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/agent/clients", integ)).StatusCode);
    }

    // ══════════════ /api/agent/impersonate ══════════════

    [Fact]
    public async Task Impersonate_ClientePropio_DevuelveTokenYFicha_YElTokenVeSoloEseCliente()
    {
        var response = await PostAsync("/api/agent/impersonate", new { clientId = ClientA1 }, await AgentAToken());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var token = body.GetProperty("token").GetString()!;
        Assert.Equal(ClientA1, body.GetProperty("client").GetProperty("id").GetString());
        Assert.Equal("C0001", body.GetProperty("client").GetProperty("number").GetString());

        // El token de suplantación marca actingAgent y fija el cliente elegido
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(AgentAId, jwt.Claims.Single(c => c.Type == "actingAgent").Value);
        Assert.Equal(ClientA1, jwt.Claims.Single(c => c.Type == "clientId").Value);

        // Con ese token, /api/portal/orders opera como ClientA1 y de nadie más
        var orders = await JsonAsync("/api/portal/orders", token);
        var numbers = orders.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("number").GetString()!).Order().ToArray();
        Assert.Equal(["PV0001", "PV0002"], numbers);
        Assert.DoesNotContain("PV9999", orders.GetRawText());   // nada del cliente B
    }

    [Fact]
    public async Task Impersonate_ClienteDeOtroAgente_403_YNoEmiteToken()
    {
        var response = await PostAsync("/api/agent/impersonate", new { clientId = ClientB1 }, await AgentAToken());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("token", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Impersonate_OtroClientePropio_NoFiltraLosPedidosDelPrimero()
    {
        // Suplantar A2 (sin pedidos) no debe dejar ver los de A1
        var response = await PostAsync("/api/agent/impersonate", new { clientId = ClientA2 }, await AgentAToken());
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var orders = await JsonAsync("/api/portal/orders", token);
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
        Assert.DoesNotContain("PV0001", orders.GetRawText());
    }

    [Fact]
    public async Task Impersonate_SinToken_401_Integracion_403()
    {
        var anon = new HttpRequestMessage(HttpMethod.Post, "/api/agent/impersonate")
        {
            Content = JsonContent.Create(new { clientId = ClientA1 })
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(anon)).StatusCode);

        var integ = await _factory.GetTokenAsync(_client);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync("/api/agent/impersonate", new { clientId = ClientA1 }, integ)).StatusCode);
    }

    // ══════════════ /api/agent/token (deseleccionar) ══════════════

    [Fact]
    public async Task Token_ReemiteTokenDeAgenteSinCliente()
    {
        var response = await PostAsync("/api/agent/token", new { }, await AgentAToken());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "clientId");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "actingAgent");

        // Vuelto al modo agente: /api/portal/orders no opera como ningún cliente
        var orders = await JsonAsync("/api/portal/orders", token);
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }
}
