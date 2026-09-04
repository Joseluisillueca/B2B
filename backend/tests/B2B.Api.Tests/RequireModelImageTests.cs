using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// "Enseñar solo los artículos con foto" (ajuste por instancia). En una tienda de moda un
// artículo sin imagen se ve pobre, y mientras el ERP va subiendo fotos hay artículos a
// medias. Con el ajuste activo el portal los oculta hasta que tengan imagen, y el recorte
// alcanza al listado, al buscador, a las facetas, a la cinta y a los relacionados, porque
// se filtra en el MISMO punto del catálogo que la visibilidad.
public class RequireModelImageTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Jpeg1x1 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a"
        + "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA"
        + "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

    private const string ConFoto = "REQIMG01-0000-4000-9000-000000000001";
    private const string SinFoto = "REQIMG02-0000-4000-9000-000000000002";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RequireModelImageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> Connector(HttpMethod method, string route, string json)
    {
        var request = new HttpRequestMessage(method, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> Admin(HttpMethod method, string route, string? json = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> CatalogAsync(string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/catalog?take=200&locale=es" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> Ids(JsonElement catalog) =>
        [.. catalog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("modelId").GetString())];

    private Task Regla(bool soloConFoto) =>
        Admin(HttpMethod.Put, "/api/admin/integration/catalog",
            $$"""{"requireModelImage":{{(soloConFoto ? "true" : "false")}} }""");

    private async Task SembrarAsync()
    {
        foreach (var (id, nombre, referencia) in new[]
                 { (ConFoto, "REQIMG CON FOTO", "REQIMG-1"), (SinFoto, "REQIMG SIN FOTO", "REQIMG-2") })
            (await Connector(HttpMethod.Put, $"/api/catalog/models/{id}",
                $$$"""{"name":{"es_ES":"{{{nombre}}}"},"active":true,"externalReference":"{{{referencia}}}","familyId":"reqimgfam","productSegments":["A"]}"""))
                .EnsureSuccessStatusCode();

        // Solo uno recibe foto (en base64, como el modo imagen del conector)
        (await Connector(HttpMethod.Put, $"/api/catalog/model-images/{ConFoto}",
            $$$"""{"images":[{"id":"{{{ConFoto}}}","image":{"uri":"","order":0,"base64":"{{{Jpeg1x1}}}","contentType":"image/jpeg"}}]}"""))
            .EnsureSuccessStatusCode();
    }

    // ── 1. Con la regla activa desaparece el que no tiene foto, y solo ese ─────

    [Fact]
    public async Task ConLaReglaActiva_SoloSeVenLosArticulosConFoto()
    {
        await SembrarAsync();
        try
        {
            await Regla(false);
            var todos = Ids(await CatalogAsync());
            Assert.Contains(ConFoto, todos);
            Assert.Contains(SinFoto, todos);

            await Regla(true);
            var conFoto = await CatalogAsync();
            Assert.Contains(ConFoto, Ids(conFoto));
            Assert.DoesNotContain(SinFoto, Ids(conFoto));

            // El recuento también baja: no se anuncia lo que no se enseña
            Assert.Equal(Ids(conFoto).Count, conFoto.GetProperty("total").GetInt32());
        }
        finally { await Regla(false); }
    }

    // ── 2. El buscador tampoco lo saca (mismo punto de filtrado) ───────────────

    [Fact]
    public async Task ConLaReglaActiva_ElBuscadorTampocoLoEncuentra()
    {
        await SembrarAsync();
        try
        {
            await Regla(true);
            var busqueda = await CatalogAsync("&q=REQIMG");
            Assert.Contains(ConFoto, Ids(busqueda));
            Assert.DoesNotContain(SinFoto, Ids(busqueda));
        }
        finally { await Regla(false); }
    }

    // ── 3. Por defecto NO se filtra: una instancia sin fotos no se queda vacía ─

    [Fact]
    public async Task PorDefecto_NoSeFiltraNada()
    {
        await SembrarAsync();
        var settings = await (await Admin(HttpMethod.Get, "/api/admin/integration/settings"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(settings.GetProperty("requireModelImage").GetBoolean());
        Assert.Contains(SinFoto, Ids(await CatalogAsync()));
    }

    // ── 4. El ajuste se guarda y se relee (y solo lo toca un administrador) ────

    [Fact]
    public async Task ElAjuste_SeGuardaYSeRelee()
    {
        try
        {
            await Regla(true);
            var guardado = await (await Admin(HttpMethod.Get, "/api/admin/integration/settings"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(guardado.GetProperty("requireModelImage").GetBoolean());

            // Sin credenciales de administrador no se cambia
            var response = await _client.PutAsync("/api/admin/integration/catalog",
                new StringContent("""{"requireModelImage":false}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally { await Regla(false); }
    }
}
