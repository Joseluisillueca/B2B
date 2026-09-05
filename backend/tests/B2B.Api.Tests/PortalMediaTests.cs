using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace B2B.Api.Tests;

// Los medios del CMS viven en la base de datos, no en el disco del contenedor: un
// despliegue estrena el disco vacío y con la primera instancia perdimos toda la portada.
// Los de demostración viajan en la imagen y se sirven detrás de las subidas.
public class PortalMediaTests : IClassFixture<TestWebApplicationFactory>
{
    // PNG de 1×1 válido (cabecera real): la subida comprueba la firma de algunos formatos
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PortalMediaTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpRequestMessage> AdminAsync(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        return request;
    }

    private async Task<JsonElement> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        var request = await AdminAsync(HttpMethod.Post, "/api/admin/media");
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);
        request.Content = form;
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── 1. Lo subido se sirve en su URL con su tipo, sin tocar el disco ────────

    [Fact]
    public async Task LoSubido_SeSirveDesdeLaBaseDeDatos()
    {
        var subido = await UploadAsync("portada verano.png", Png1x1, "image/png");
        var url = subido.GetProperty("url").GetString()!;
        Assert.StartsWith("/media/portal/portada-verano-", url);

        // No hay fichero en disco: el medio vive en la base de datos
        Assert.False(Directory.Exists(_factory.MediaRoot)
                     && Directory.GetFiles(_factory.MediaRoot).Any(f => f.EndsWith(".png")),
            "la subida no debe escribir en disco");

        var servido = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, servido.StatusCode);
        Assert.Equal("image/png", servido.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png1x1, await servido.Content.ReadAsByteArrayAsync());
        // Nombre único → cacheable a largo plazo
        Assert.Contains("immutable", servido.Headers.CacheControl?.ToString() ?? "");
    }

    // ── 2. La tipografía subida como octet-stream se sirve como font/woff2 ─────

    [Fact]
    public async Task LaTipografia_SeSirveConSuTipoCanonico()
    {
        var woff2 = new byte[] { 0x77, 0x4F, 0x46, 0x32, 0, 0, 0, 0 }; // "wOF2" + relleno
        var subido = await UploadAsync("marca.woff2", woff2, "application/octet-stream");
        var servido = await _client.GetAsync(subido.GetProperty("url").GetString()!);
        Assert.Equal("font/woff2", servido.Content.Headers.ContentType?.MediaType);
    }

    // ── 3. El listado une subidas y demostración; borrar solo borra lo subido ──

    [Fact]
    public async Task ElListado_UneSubidasYDemostracion_YBorrarSoloBorraLoSubido()
    {
        var subido = await UploadAsync("logo tienda.png", Png1x1, "image/png");
        var name = subido.GetProperty("name").GetString()!;

        var listado = await (await _client.SendAsync(await AdminAsync(HttpMethod.Get, "/api/admin/media")))
            .Content.ReadFromJsonAsync<JsonElement>();
        var nombres = listado.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Contains(name, nombres);
        Assert.Contains("demo-hero-carretera.svg", nombres);

        var borrado = await _client.SendAsync(await AdminAsync(HttpMethod.Delete, "/api/admin/media/" + name));
        Assert.Equal(HttpStatusCode.NoContent, borrado.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/media/portal/" + name)).StatusCode);

        // La demostración va con el producto: no se borra, y se explica por qué
        var demo = await _client.SendAsync(await AdminAsync(HttpMethod.Delete, "/api/admin/media/demo-hero-carretera.svg"));
        Assert.Equal(HttpStatusCode.BadRequest, demo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/media/portal/demo-hero-carretera.svg")).StatusCode);
    }

    // ── 4. Nadie sale de la carpeta por la URL de servicio ─────────────────────

    [Fact]
    public async Task LaUrlDeServicio_NoPermiteSalirDeLaCarpeta()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/media/portal/../appsettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/media/portal/..%2Fappsettings.json")).StatusCode);
    }
}
