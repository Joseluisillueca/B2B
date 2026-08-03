using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace B2B.Api.Tests;

// Contrato: docs/contrato-api/01-autenticacion-y-convenciones.md §1
// BC envía {email, password, type:"global", longDuration:true} y espera
// {token, tokenExpiresIn} donde tokenExpiresIn es una fecha absoluta "dd/MM/yyyy HH:mm:ss".
public class LoginEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static object ValidRequest => new
    {
        email = TestWebApplicationFactory.SeededEmail,
        password = TestWebApplicationFactory.SeededPassword,
        type = "global",
        longDuration = true
    };

    [Fact]
    public async Task Login_CredencialesValidas_Devuelve200ConTokenYExpiracionAbsoluta()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", ValidRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(token!.Length <= 2048, "BC solo almacena tokens de hasta 2048 caracteres");

        // tokenExpiresIn: fecha absoluta parseable por Evaluate de BC-es, no segundos
        var expiresText = body.GetProperty("tokenExpiresIn").GetString();
        var expiresAt = DateTime.ParseExact(expiresText!, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        // Con longDuration el token debe sobrevivir con holgura al intervalo de refresh (60 min + margen de 10)
        Assert.True(expiresAt > DateTime.Now.AddMinutes(90),
            $"El token expira demasiado pronto: {expiresAt:O}");
    }

    [Fact]
    public async Task Login_PasswordIncorrecta_Devuelve401ConBodyJson()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestWebApplicationFactory.SeededEmail,
            password = "incorrecta",
            type = "global",
            longDuration = true
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // El conector trata cualquier no-2xx como fallo y loguea el body; debe ser JSON
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Login_EmailDesconocido_Devuelve401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "noexiste@test.com",
            password = "loquesea",
            type = "global",
            longDuration = true
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
