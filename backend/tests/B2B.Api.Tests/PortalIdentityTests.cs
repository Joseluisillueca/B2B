using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Fase 0 del portal: el usuario de la aplicación queda vinculado a su cliente a partir
// del sync del conector (contrato 04 §2), el token lleva ese vínculo en sus claims y
// /api/portal/me proyecta la ficha del cliente desde los sync_documents.
public class PortalIdentityTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ClientA = "AAAAAAAA-1111-4111-8111-000000000001";
    private const string ClientB = "BBBBBBBB-2222-4222-8222-000000000002";
    private const string AddressA = "AAAAAAAA-3333-4333-8333-000000000003";
    private const string AddressB = "BBBBBBBB-4444-4444-8444-000000000004";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PortalIdentityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string ClientPayload(string number, string name) => $$"""
    {
      "name": "{{name}}",
      "fiscalInfo": {
        "alias": "{{name}}",
        "address": { "streetAddress": "Calle Mayor", "num": "12", "description": "",
          "city": "Madrid", "province": "Madrid", "zipCode": "28001", "countryIsoId": "ES",
          "geo": { "latitude": 0, "longitude": 0 },
          "contact": { "name": "Ana", "lastName": "", "company": "{{name}}", "phones": [] } },
        "fiscalName": "{{name}} S.L.",
        "fiscalId": { "type": "nif", "document": "B12345678" }
      },
      "creditInfo": { "code": "EUR", "value": 15000.0 },
      "markets": [ "es" ],
      "payMethods": [ "transf30" ],
      "externalReference": "{{number}}",
      "email": "info@{{number}}.test",
      "phone": { "code": "+34", "number": "912345678" },
      "web": "",
      "canShop": true,
      "groupIds": [ "mayorista" ],
      "productSegments": [ "A+" ]
    }
    """;

    private static string AddressPayload(string alias, string city, string id) => $$"""
    {
      "address": { "streetAddress": "Poligono Norte", "num": "4B", "description": "Nave 7",
        "city": "{{city}}", "province": "Madrid", "zipCode": "28905", "countryIsoId": "ES",
        "geo": { "latitude": 0, "longitude": 0 },
        "contact": { "name": "Luis", "lastName": "", "company": "Almacen", "phones": [] } },
      "alias": "{{alias}}",
      "externalReference": "{{id}}"
    }
    """;

    private static string UserPayload(string email) =>
        $$"""{ "email": "{{email}}", "name": "{{email}}", "culture": "es_ES" }""";

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    // Estado común: dos clientes con dirección propia; el usuario de pruebas es admin del A.
    private async Task SeedAsync()
    {
        await PutAsync($"/api/clients/{ClientA}", ClientPayload("C100057", "TEST 5"));
        await PutAsync($"/api/clients/{ClientB}", ClientPayload("C100099", "OTRA TIENDA"));
        await PutAsync($"/api/clients/{ClientA}/shipping-addresses/{AddressA}", AddressPayload("GETAFE01", "Getafe", AddressA));
        await PutAsync($"/api/clients/{ClientB}/shipping-addresses/{AddressB}", AddressPayload("VIGO01", "Vigo", AddressB));
        await PutAsync($"/api/clients/{ClientA}/users/admin", UserPayload(TestWebApplicationFactory.SeededEmail));
    }

    private async Task<string> TokenAsync() => await _factory.GetTokenAsync(_client);

    private async Task<HttpResponseMessage> GetMeAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task SyncUsuarioAdmin_CreaElUsuarioDelPortalVinculadoAlCliente()
    {
        await SeedAsync();
        const string email = "tienda-nueva@cliente-a.test";
        await PutAsync($"/api/clients/{ClientA}/users/admin", UserPayload(email));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        Assert.NotNull(user);
        Assert.Equal(ClientA, user!.ClientExternalId);
        Assert.Equal("C100057", user.ClientNumber);
        Assert.Equal("client-admin", user.Role);
        Assert.Equal("es_ES", user.Culture);
        // Sin password del conector el usuario no puede entrar hasta que se le asigne una
        Assert.Equal(string.Empty, user.PasswordHash);
    }

    [Fact]
    public async Task SyncUsuarioAdmin_ConEmailYaExistente_VinculaSinRomperElLogin()
    {
        await SeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == TestWebApplicationFactory.SeededEmail);
            Assert.Equal(ClientA, user.ClientExternalId);
            Assert.NotEqual(string.Empty, user.PasswordHash);
        }

        // El usuario de integración del conector sigue autenticándose igual
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestWebApplicationFactory.SeededEmail,
            password = TestWebApplicationFactory.SeededPassword,
            type = "global",
            longDuration = true
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SyncUsuarioAdmin_SinEmail_NoCreaUsuarioPeroGuardaElDocumento()
    {
        await PutAsync($"/api/clients/{ClientB}/users/admin", """{ "email": "", "name": "", "culture": "es_ES" }""");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.SyncDocuments.AnyAsync(d => d.EntityType == "client-user" && d.ExternalId == ClientB));
        Assert.False(await db.Users.AnyAsync(u => u.Email == string.Empty));
    }

    [Fact]
    public async Task Login_UsuarioVinculado_EmiteClaimsDeClienteEnElToken()
    {
        await SeedAsync();
        var token = await TokenAsync();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(ClientA, jwt.Claims.Single(c => c.Type == "clientId").Value);
        Assert.Equal("C100057", jwt.Claims.Single(c => c.Type == "clientNumber").Value);
        Assert.Equal("client-admin", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("es_ES", jwt.Claims.Single(c => c.Type == "culture").Value);
    }

    [Fact]
    public async Task Me_SinToken_Devuelve401()
    {
        var response = await _client.GetAsync("/api/portal/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_UsuarioVinculado_DevuelveCredencialesYFichaDelCliente()
    {
        await SeedAsync();

        var response = await GetMeAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(TestWebApplicationFactory.SeededEmail, body.GetProperty("email").GetString());
        Assert.Equal("Administrador", body.GetProperty("rol").GetString());
        Assert.Equal("es_ES", body.GetProperty("culture").GetString());

        // Pantalla "SELECCIONA AHORA TUS CREDENCIALES": nombre de cliente, tipo y rol
        var credentials = body.GetProperty("credentials").EnumerateArray().ToList();
        var credential = Assert.Single(credentials);
        Assert.Equal(ClientA, credential.GetProperty("clientId").GetString());
        Assert.Equal("C100057", credential.GetProperty("clientNumber").GetString());
        Assert.Equal("TEST 5", credential.GetProperty("name").GetString());
        Assert.Equal("CLIENTE", credential.GetProperty("type").GetString());
        Assert.Equal("Administrador", credential.GetProperty("role").GetString());

        var client = body.GetProperty("client");
        Assert.Equal(ClientA, client.GetProperty("id").GetString());
        Assert.Equal("C100057", client.GetProperty("number").GetString());
        Assert.Equal("TEST 5", client.GetProperty("name").GetString());
        Assert.True(client.GetProperty("canShop").GetBoolean());
        Assert.Equal("A+", client.GetProperty("productSegments")[0].GetString());
        Assert.Equal("transf30", client.GetProperty("payMethods")[0].GetString());
        Assert.Equal(15000, client.GetProperty("creditInfo").GetProperty("value").GetDecimal());
        Assert.Equal("B12345678", client.GetProperty("fiscalInfo").GetProperty("fiscalId").GetProperty("document").GetString());
    }

    [Fact]
    public async Task Me_SoloDevuelveLasDireccionesDeSuPropioCliente()
    {
        await SeedAsync();

        var response = await GetMeAsync();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var addresses = body.GetProperty("client").GetProperty("shippingAddresses").EnumerateArray().ToList();

        var address = Assert.Single(addresses);
        Assert.Equal(AddressA, address.GetProperty("id").GetString());
        Assert.Equal("GETAFE01", address.GetProperty("alias").GetString());
        Assert.DoesNotContain(addresses, a => a.GetProperty("alias").GetString() == "VIGO01");
        // Ningún rastro del cliente ajeno en toda la respuesta
        Assert.DoesNotContain("OTRA TIENDA", body.GetRawText());
        Assert.DoesNotContain(ClientB, body.GetRawText());
    }

    [Fact]
    public async Task Me_UsuarioSinClienteVinculado_DevuelveClientNull()
    {
        // Fábrica aparte: el usuario de integración de esta base no se ha vinculado a nadie
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await factory.GetTokenAsync(client));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("client").ValueKind);
        Assert.Empty(body.GetProperty("credentials").EnumerateArray());
    }
}
