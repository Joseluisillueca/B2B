using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Modo imagen del conector (contrato 02): la foto del modelo viaja en base64 y la `uri`
// del documento llega VACÍA, porque quien aloja la imagen es el portal. Guardábamos el
// binario en MediaAsset y lo servíamos en /media/models/{id}.jpg, pero el catálogo lee
// la `uri` del documento para pintar la foto: nadie apuntaba al binario y el artículo
// salía sin imagen aunque la tuviéramos guardada.
public class ModelImageBase64Tests : IClassFixture<TestWebApplicationFactory>
{
    // 1x1 JPEG real, suficiente para comprobar el almacenamiento y el servido
    private const string Jpeg1x1 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a"
        + "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA"
        + "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ModelImageBase64Tests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> CatalogAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/catalog?take=200&locale=es");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement? Item(JsonElement catalog, string modelId) =>
        catalog.GetProperty("items").EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(i => i!.Value.GetProperty("modelId").GetString() == modelId);

    // ── 1. Foto en base64 con la uri vacía: el catálogo la enseña igual ────────

    [Fact]
    public async Task ImagenEnBase64ConUriVacia_ElCatalogoApuntaAlBinarioAlojado()
    {
        const string modelId = "IMGB6401-0000-4000-9000-000000000001";
        (await Put($"/api/catalog/models/{modelId}",
            $$$"""{"name":{"es_ES":"IMGB64 CON FOTO"},"active":true,"externalReference":"IMGB64-1","familyId":"calzado","productSegments":["A"]}"""))
            .EnsureSuccessStatusCode();

        // Tal cual lo manda el conector en modo imagen: uri vacía y la foto en base64
        (await Put($"/api/catalog/model-images/{modelId}",
            $$$"""{"images":[{"id":"{{{modelId}}}","image":{"uri":"","order":0,"base64":"{{{Jpeg1x1}}}","contentType":"image/jpeg"}}]}"""))
            .EnsureSuccessStatusCode();

        var item = Item(await CatalogAsync(), modelId);
        Assert.NotNull(item);
        Assert.Equal($"/media/models/{modelId}.jpg", item!.Value.GetProperty("imageUri").GetString());
        Assert.Equal($"/media/models/{modelId}.jpg", item!.Value.GetProperty("images")[0].GetString());

        // Y la ruta sirve el binario de verdad, sin token (las fotos son públicas)
        var photo = await _client.GetAsync($"/media/models/{modelId}.jpg");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.True((await photo.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    // ── 2. Si el conector SÍ manda una uri, manda la suya ──────────────────────

    [Fact]
    public async Task ImagenConUriPropia_LaUriDelConectorMandaSobreLaAlojada()
    {
        const string modelId = "IMGB6402-0000-4000-9000-000000000002";
        (await Put($"/api/catalog/models/{modelId}",
            $$$"""{"name":{"es_ES":"IMGB64 CON URI"},"active":true,"externalReference":"IMGB64-2","familyId":"calzado","productSegments":["A"]}"""))
            .EnsureSuccessStatusCode();

        (await Put($"/api/catalog/model-images/{modelId}",
            $$$"""{"images":[{"id":"{{{modelId}}}","image":{"uri":"https://cdn.cliente.com/foto.jpg","order":0,"base64":"{{{Jpeg1x1}}}","contentType":"image/jpeg"}}]}"""))
            .EnsureSuccessStatusCode();

        var item = Item(await CatalogAsync(), modelId);
        Assert.NotNull(item);
        Assert.Equal("https://cdn.cliente.com/foto.jpg", item!.Value.GetProperty("imageUri").GetString());
    }

    // ── 3. Sin foto y sin uri: el artículo sale sin imagen, no con una rota ────

    [Fact]
    public async Task ImagenSinBinarioYSinUri_NoInventaRuta()
    {
        const string modelId = "IMGB6403-0000-4000-9000-000000000003";
        (await Put($"/api/catalog/models/{modelId}",
            $$$"""{"name":{"es_ES":"IMGB64 SIN FOTO"},"active":true,"externalReference":"IMGB64-3","familyId":"calzado","productSegments":["A"]}"""))
            .EnsureSuccessStatusCode();

        (await Put($"/api/catalog/model-images/{modelId}",
            $$$"""{"images":[{"id":"{{{modelId}}}","image":{"uri":"","order":0}}]}"""))
            .EnsureSuccessStatusCode();

        var item = Item(await CatalogAsync(), modelId);
        Assert.NotNull(item);
        var uri = item!.Value.GetProperty("imageUri");
        Assert.True(uri.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(uri.GetString()));
        Assert.Empty(item!.Value.GetProperty("images").EnumerateArray());
    }

    // ── 4. El documento queda autodescriptivo: la uri se rellena al ingerir ────

    [Fact]
    public async Task AlGuardarLaFoto_ElDocumentoSeQuedaConLaRutaEnLaUri()
    {
        const string modelId = "IMGB6404-0000-4000-9000-000000000004";
        (await Put($"/api/catalog/model-images/{modelId}",
            $$$"""{"images":[{"id":"{{{modelId}}}","image":{"uri":"","order":0,"base64":"{{{Jpeg1x1}}}","contentType":"image/jpeg"}}]}"""))
            .EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/admin/sync-documents?entityType=model-image&take=200&includePayload=true");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        var body = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

        var doc = body.GetProperty("items").EnumerateArray()
            .Single(d => d.GetProperty("externalId").GetString() == modelId);
        var payload = JsonDocument.Parse(doc.GetProperty("payload").GetString()!).RootElement;
        var image = payload.GetProperty("images")[0].GetProperty("image");
        Assert.Equal($"/media/models/{modelId}.jpg", image.GetProperty("uri").GetString());
        // Y el base64 no se queda en el documento: engordaría la tabla de sincronización
        Assert.False(image.TryGetProperty("base64", out _));
    }
}
