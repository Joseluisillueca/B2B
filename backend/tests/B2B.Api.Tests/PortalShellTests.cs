using System.Net;

namespace B2B.Api.Tests;

// Fase 0: el portal se enruta con History API sobre las rutas reales /{market}/{lang}/{vista},
// así que recargar cualquiera de ellas debe servir el cascarón, no un 404.
public class PortalShellTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PortalShellTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/es/es/dashboard")]
    [InlineData("/es/es/orders")]
    [InlineData("/es/en/dashboard")]
    [InlineData("/es/fr/catalog/catalog")]
    [InlineData("/es/it/delivery-notes")]
    [InlineData("/es/es")]
    [InlineData("/login")]
    public async Task RutaDelPortal_DevuelveElCascaron(string route)
    {
        var response = await _client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("portal/js/boot.js", html);
    }

    [Fact]
    public async Task Portal_RedirigeAlIdiomaPorDefecto()
    {
        using var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/portal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/es/es/dashboard", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/api/portal/no-existe")]
    [InlineData("/es/es/orders/no/tan/profundo/como/para/ser/vista")]
    public async Task RutaFueraDelPortal_NoDevuelveElCascaron(string route)
    {
        var response = await _client.GetAsync(route);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ElCatalogoDeShopHtmlSigueServido()
    {
        // El plan mantiene /shop intacto hasta que views/catalog.js supere sus criterios
        var response = await _client.GetAsync("/shop.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
