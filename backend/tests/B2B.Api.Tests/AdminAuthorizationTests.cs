using System.Net;
using System.Net.Http.Headers;
using System.Text;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Auditoría B-1: /api/admin/* era alcanzable con el token de cualquier usuario del
// portal. El sync de BC provisiona a TODOS los clientes como "client-admin", así que
// ese token abría el CMS entero —incluidos los payloads crudos de otros clientes—.
// El CMS es ahora exclusivo del rol "admin", que el sync nunca asigna.
public class AdminAuthorizationTests : IClassFixture<AdminAuthorizationTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    public const string ClientAdminEmail = "cliente@test.com";
    public const string ClientAdminPassword = "cliente-secreto123";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminAuthorizationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedClientAdmin(factory);
    }

    // El usuario que el sync crea para el cliente: rol client-admin y vínculo a cliente.
    // Se siembra directo en la BD porque el sync no le pone contraseña.
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

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? token)
    {
        var request = new HttpRequestMessage(method, route);
        if (method == HttpMethod.Put)
            request.Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json");
        if (method == HttpMethod.Post)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent([1, 2, 3]);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "banner.png");
            request.Content = form;
        }
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    // Lectura y escritura de los tres grupos del CMS (AdminEndpoints, ContentEndpoints,
    // MediaEndpoints), que es donde el informe encontró la puerta abierta.
    public static TheoryData<string, string> AdminRoutes() => new()
    {
        { "GET", "/api/admin/summary" },
        { "GET", "/api/admin/sync-documents?includePayload=true" },
        { "GET", "/api/admin/sync-documents/client/7A31C5D2-9E44-4C18-B0F3-0011AA22BB33" },
        { "GET", "/api/admin/content" },
        { "GET", "/api/admin/content/dashboard.hero?locale=es" },
        { "PUT", "/api/admin/content/dashboard.hero?locale=es" },
        { "DELETE", "/api/admin/content/dashboard.hero?locale=es" },
        { "GET", "/api/admin/media" },
        { "POST", "/api/admin/media" },
        { "DELETE", "/api/admin/media/inexistente.png" },
        { "GET", "/api/admin/model-images" },
        { "PUT", "/api/admin/model-images/DEMO0001" },
        { "DELETE", "/api/admin/model-images/DEMO0001" }
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Cms_ConTokenDeClienteDevuelve403(string method, string route)
    {
        var token = await _factory.LoginAsync(_client, ClientAdminEmail, ClientAdminPassword);
        var response = await SendAsync(new HttpMethod(method), route, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Cms_SinTokenDevuelve401(string method, string route)
    {
        var response = await SendAsync(new HttpMethod(method), route, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // El usuario técnico del conector tampoco es administrador del CMS: solo sincroniza
    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Cms_ConTokenDeIntegracionDevuelve403(string method, string route)
    {
        var token = await _factory.GetTokenAsync(_client);
        var response = await SendAsync(new HttpMethod(method), route, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Con el administrador del CMS la autorización se supera: lo que llegue después es
    // negocio (404 del bloque que no existe, 400 de la subida sin imagen real), nunca 401/403.
    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Cms_ConTokenDeAdministradorPasaLaAutorizacion(string method, string route)
    {
        var token = await _factory.GetAdminTokenAsync(_client);
        var response = await SendAsync(new HttpMethod(method), route, token);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cms_ConTokenDeAdministrador_LeeElInventarioDelSync()
    {
        var token = await _factory.GetAdminTokenAsync(_client);
        var response = await SendAsync(HttpMethod.Get, "/api/admin/summary", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // El sync sigue siendo del usuario de integración: la política del CMS no lo toca
    [Fact]
    public async Task Sync_SigueAbiertoAlUsuarioDeIntegracion()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/core/warehouses/AUTHZ-1")
        {
            Content = new StringContent("""{"name":{"es_ES":"Almacén"}}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));

        var response = await _client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode);
    }

    // El portal del cliente no se ve afectado: su token sigue entrando en /api/portal/*
    [Fact]
    public async Task Portal_SigueAbiertoAlClienteAdmin()
    {
        var token = await _factory.LoginAsync(_client, ClientAdminEmail, ClientAdminPassword);
        var response = await SendAsync(HttpMethod.Get, "/api/portal/me", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
