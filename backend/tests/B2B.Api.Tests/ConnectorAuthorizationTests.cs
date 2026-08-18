using System.Net;
using System.Net.Http.Headers;
using System.Text;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Auditoría B-2: /api/sync/* y /api/query/* estaban con RequireAuthorization() pelado,
// alcanzables con el token "client-admin" de cualquier cliente del portal (el rol que el
// sync asigna a todos). Eran la puerta de al lado de B-1: pedidos crudos sin filtrar
// (GET /api/orders/search), toma de control de otro cliente (PUT .../users/admin) y
// escritura sobre datos maestros. Estas rutas son del conector de BC: solo "integration"
// (y "admin", superusuario de la plataforma) entran.
public class ConnectorAuthorizationTests : IClassFixture<ConnectorAuthorizationTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    public const string ClientAdminEmail = "cliente-conector@test.com";
    public const string ClientAdminPassword = "cliente-secreto123";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ConnectorAuthorizationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedClientAdmin(factory);
    }

    private static void SeedClientAdmin(Factory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Users.Any(u => u.Email == ClientAdminEmail)) return;

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = ClientAdminEmail,
            PasswordHash = "",
            Role = "client-admin",
            ClientExternalId = "7A31C5D2-9E44-4C18-B0F3-0011AA22BB33",
            ClientNumber = "C100057"
        };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, ClientAdminPassword);
        db.Users.Add(user);
        db.SaveChanges();
    }

    // Ingesta (SyncEndpoints) y lecturas del conector (QueryEndpoints): una ruta
    // representativa de cada familia, incluidas las de mayor impacto del informe.
    public static TheoryData<string, string> ConnectorRoutes() => new()
    {
        { "PUT", "/api/core/warehouses/BC-AUTHZ-1" },        // upsert genérico
        { "PUT", "/api/catalog/offers" },                    // ofertas en array
        { "PUT", "/api/core/b2binfo" },                      // singleton de empresa
        { "PUT", "/api/clients/OTRO-CLIENTE/users/admin" },  // toma de control de cuenta
        { "PUT", "/api/clients/OTRO-CLIENTE/shipping-addresses/A1" },
        { "GET", "/api/catalog/offers" },                    // reconciliación de ofertas
        { "DELETE", "/api/catalog/offers/BC-AUTHZ-INEXISTENTE" },
        { "POST", "/api/orders/search" }                     // pedidos crudos
    };

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? token)
    {
        var request = new HttpRequestMessage(method, route);
        if (method == HttpMethod.Put || method == HttpMethod.Post)
            request.Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Theory]
    [MemberData(nameof(ConnectorRoutes))]
    public async Task Conector_ConTokenDeClienteDevuelve403(string method, string route)
    {
        var token = await _factory.LoginAsync(_client, ClientAdminEmail, ClientAdminPassword);
        var response = await SendAsync(new HttpMethod(method), route, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ConnectorRoutes))]
    public async Task Conector_SinTokenDevuelve401(string method, string route)
    {
        var response = await SendAsync(new HttpMethod(method), route, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Con el token del conector (integración) la autorización se supera: lo que llegue
    // después es negocio (400 de validación), nunca 401/403.
    [Theory]
    [MemberData(nameof(ConnectorRoutes))]
    public async Task Conector_ConTokenDeIntegracionPasaLaAutorizacion(string method, string route)
    {
        var token = await _factory.GetTokenAsync(_client);
        var response = await SendAsync(new HttpMethod(method), route, token);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // El admin del CMS es superusuario de la plataforma: también pasa
    [Theory]
    [MemberData(nameof(ConnectorRoutes))]
    public async Task Conector_ConTokenDeAdministradorPasaLaAutorizacion(string method, string route)
    {
        var token = await _factory.GetAdminTokenAsync(_client);
        var response = await SendAsync(new HttpMethod(method), route, token);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
