using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace B2B.Api.Tests;

// Fase 1: las imágenes de la portada se suben desde el CMS. Se guardan en el disco
// del portal (wwwroot/media/portal) y se sirven como estáticos, sin CDN.
public class AdminMediaTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminMediaTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // PNG mínimo real (1x1) para que la subida válida no dependa de bytes inventados
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/media") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Subida_SinToken_Devuelve401()
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Png), "file", "banner.png");
        var response = await _client.PostAsync("/api/admin/media", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/admin/media")).StatusCode);
    }

    [Fact]
    public async Task Subida_DeUnaImagen_GuardaElFicheroYLoListaConSuUrl()
    {
        var response = await UploadAsync(Png, "Banner Portada SS26.png", "image/png");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("url").GetString()!;
        var name = body.GetProperty("name").GetString()!;

        // La URL es la que se pega en el bloque de contenido y sirve el propio portal
        Assert.StartsWith("/media/portal/", url);
        Assert.EndsWith(".png", url);
        Assert.DoesNotContain(' ', url);
        Assert.Equal(Png.Length, body.GetProperty("size").GetInt64());
        Assert.True(File.Exists(Path.Combine(_factory.MediaRoot, name)));

        var list = await (await SendAsync(HttpMethod.Get, "/api/admin/media")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.GetProperty("items").EnumerateArray(), i => i.GetProperty("url").GetString() == url);
    }

    [Fact]
    public async Task Subida_DeDosFicherosConElMismoNombre_NoSePisan()
    {
        var primera = await (await UploadAsync(Png, "repetido.png", "image/png")).Content.ReadFromJsonAsync<JsonElement>();
        var segunda = await (await UploadAsync(Png, "repetido.png", "image/png")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(primera.GetProperty("url").GetString(), segunda.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("script.txt", "text/plain")]              // no es una imagen
    [InlineData("marca.svg", "image/svg+xml")]            // SVG: ejecuta script en el mismo origen
    [InlineData("truco.png", "text/html")]                // extensión de imagen, contenido no
    [InlineData("truco.php", "image/png")]                // tipo de imagen, extensión ejecutable
    [InlineData("sin-extension", "image/png")]
    public async Task Subida_ConTipoNoPermitido_Devuelve400YNoEscribeNada(string fileName, string contentType)
    {
        var antes = Directory.Exists(_factory.MediaRoot) ? Directory.GetFiles(_factory.MediaRoot).Length : 0;

        var response = await UploadAsync(Png, fileName, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
        var despues = Directory.Exists(_factory.MediaRoot) ? Directory.GetFiles(_factory.MediaRoot).Length : 0;
        Assert.Equal(antes, despues);
    }

    [Fact]
    public async Task Subida_DeFicheroVacio_Devuelve400()
    {
        var response = await UploadAsync([], "vacio.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subida_DemasiadoGrande_Devuelve400()
    {
        var response = await UploadAsync(new byte[6 * 1024 * 1024], "enorme.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Borrado_EliminaElFicheroYDevuelve404SiNoExiste()
    {
        var subida = await (await UploadAsync(Png, "borrame.png", "image/png")).Content.ReadFromJsonAsync<JsonElement>();
        var name = subida.GetProperty("name").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/admin/media/{name}")).StatusCode);
        Assert.False(File.Exists(Path.Combine(_factory.MediaRoot, name)));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, $"/api/admin/media/{name}")).StatusCode);
    }

    [Fact]
    public async Task Borrado_ConNombreQueSaleDeLaCarpeta_NoTocaNada()
    {
        var testigo = Path.Combine(Path.GetDirectoryName(_factory.MediaRoot.TrimEnd(Path.DirectorySeparatorChar))!, "testigo.png");
        await File.WriteAllBytesAsync(testigo, Png);
        try
        {
            var response = await SendAsync(HttpMethod.Delete, "/api/admin/media/..%2Ftestigo.png");

            Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.True(File.Exists(testigo));
        }
        finally
        {
            File.Delete(testigo);
        }
    }
}
