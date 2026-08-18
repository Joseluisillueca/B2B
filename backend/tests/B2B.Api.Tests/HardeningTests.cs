using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace B2B.Api.Tests;

// Auditoría m-4, m-5 y m-6: superficie de la API fuera de desarrollo, fuerza bruta
// contra el login y arranque en producción con la clave de firma de desarrollo.
public class HardeningTests
{
    /// Misma app que el resto de pruebas pero con el entorno de producción
    private sealed class ProductionFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            // La guarda de m-6 impide arrancar con la clave por defecto: aquí se prueba
            // el resto del endurecimiento, así que la clave es propia.
            builder.UseSetting("Jwt:SigningKey", "clave-de-produccion-solo-para-pruebas-0123456789");
            base.ConfigureWebHost(builder);
        }
    }

    /// Producción conservando la clave de desarrollo de appsettings.json: no debe arrancar
    private sealed class ProductionWithDefaultKeyFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            base.ConfigureWebHost(builder);
        }
    }

    // ── m-4 · /docs y /openapi solo en desarrollo ──────────────────────────────

    [Fact]
    public async Task Docs_EnDesarrollo_SePublican()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await client.GetAsync("/docs")).StatusCode);
    }

    [Fact]
    public async Task Docs_FueraDeDesarrollo_NoSePublican()
    {
        using var factory = new ProductionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/docs")).StatusCode);
    }

    // La API sigue en pie: lo que se retira es la documentación, no los endpoints
    [Fact]
    public async Task Api_FueraDeDesarrollo_SigueRespondiendo()
    {
        using var factory = new ProductionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/summary")).StatusCode);
    }

    // ── m-6 · guarda de la clave de firma ──────────────────────────────────────

    [Fact]
    public void Arranque_FueraDeDesarrolloConLaClavePorDefecto_Falla()
    {
        using var factory = new ProductionWithDefaultKeyFactory();

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:SigningKey", error.Message);
    }

    [Fact]
    public void Arranque_EnDesarrolloConLaClavePorDefecto_Funciona()
    {
        using var factory = new TestWebApplicationFactory();

        Assert.NotNull(factory.CreateClient());
    }

    // ── m-5 · límite de intentos en el login ───────────────────────────────────

    /// Tres intentos por ventana: suficiente para ver el 429 sin esperar a que expire
    private sealed class LoginLimitFactory : TestWebApplicationFactory
    {
        protected override int LoginPermitLimit => 3;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestWebApplicationFactory.SeededEmail,
            password,
            type = "global",
            longDuration = false
        });

    [Fact]
    public async Task Login_TrasAgotarLosIntentos_Devuelve429()
    {
        using var factory = new LoginLimitFactory();
        var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 3; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "clave-mala")).StatusCode);

        // El cuarto intento ya no llega a comprobar la contraseña
        var blocked = await LoginAsync(client, TestWebApplicationFactory.SeededPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Login_ElLimiteNoAfectaAlRestoDeLaApi()
    {
        using var factory = new LoginLimitFactory();
        var client = factory.CreateClient();

        var token = await factory.GetTokenAsync(client);
        for (var attempt = 1; attempt <= 5; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        Assert.NotEmpty(token);
    }
}
