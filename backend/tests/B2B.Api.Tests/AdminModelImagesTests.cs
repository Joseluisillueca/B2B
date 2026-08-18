using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Imágenes de producto para marketing: el CMS fija la imagen de cada modelo del
// catálogo escribiendo el mismo documento de sync "model-image" que ya lee el
// catálogo comprable. La subida del fichero es /api/admin/media; aquí se ASIGNA.
public class AdminModelImagesTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminModelImagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Siembra un modelo del catálogo con el usuario del conector (como el sync real)
    private async Task SeedModel(string modelId, string reference, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/catalog/models/{modelId}")
        {
            Content = new StringContent(
                $$"""{"name":{"es_ES":"{{name}}"},"active":true,"externalReference":"{{reference}}","familyId":"calzado","productSegments":["A"]}""",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> AdminSend(HttpMethod method, string route, string? json = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        token ??= await _factory.GetAdminTokenAsync(_client);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> CatalogModel(string modelId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("modelId").GetString() == modelId);
    }

    [Fact]
    public async Task Lista_IncluyeTodosLosModelosConYSinImagen()
    {
        const string conImagen = "MIMG-CON1-4E2A-9D77-001122334401";
        const string sinImagen = "MIMG-SIN1-4E2A-9D77-001122334402";
        await SeedModel(conImagen, "9101", "MODELO CON IMAGEN");
        await SeedModel(sinImagen, "9102", "MODELO SIN IMAGEN");
        await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{conImagen}",
            """{"uri":"https://cdn.lejan.com/foto-9101.jpg"}""");

        var list = await (await AdminSend(HttpMethod.Get, "/api/admin/model-images")).Content.ReadFromJsonAsync<JsonElement>();
        var items = list.GetProperty("items").EnumerateArray().ToList();

        var con = items.Single(i => i.GetProperty("externalId").GetString() == conImagen);
        var sin = items.Single(i => i.GetProperty("externalId").GetString() == sinImagen);
        Assert.Equal("9101", con.GetProperty("reference").GetString());
        Assert.Equal("MODELO CON IMAGEN", con.GetProperty("name").GetString());
        Assert.Equal("https://cdn.lejan.com/foto-9101.jpg", con.GetProperty("imageUri").GetString());
        // El modelo sin imagen aparece igualmente, con imageUri nula
        Assert.Equal(JsonValueKind.Null, sin.GetProperty("imageUri").ValueKind);
    }

    [Fact]
    public async Task Put_FijaLaImagenYElCatalogoLaDevuelve()
    {
        const string modelId = "MIMG-PUT1-4E2A-9D77-001122334403";
        await SeedModel(modelId, "9103", "MODELO PUT");

        var response = await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{modelId}",
            """{"uri":"/media/portal/foto-9103.png"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = await CatalogModel(modelId);
        Assert.Equal("/media/portal/foto-9103.png", model.GetProperty("imageUri").GetString());
    }

    [Fact]
    public async Task Put_SobreEscribeLaImagenExistente()
    {
        const string modelId = "MIMG-PUT2-4E2A-9D77-001122334404";
        await SeedModel(modelId, "9104", "MODELO UPSERT");

        await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{modelId}", """{"uri":"/media/portal/vieja.png"}""");
        await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{modelId}", """{"uri":"/media/portal/nueva.png"}""");

        var model = await CatalogModel(modelId);
        Assert.Equal("/media/portal/nueva.png", model.GetProperty("imageUri").GetString());
    }

    [Theory]
    [InlineData("""{"uri":""}""")]
    [InlineData("""{"uri":"   "}""")]
    [InlineData("""{}""")]
    public async Task Put_ConUriVacia_Devuelve400(string json)
    {
        const string modelId = "MIMG-PUT3-4E2A-9D77-001122334405";
        await SeedModel(modelId, "9105", "MODELO VACIO");

        var response = await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{modelId}", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_QuitaLaImagenYElCatalogoVuelveAPlaceholder()
    {
        const string modelId = "MIMG-DEL1-4E2A-9D77-001122334406";
        await SeedModel(modelId, "9106", "MODELO DELETE");
        await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{modelId}", """{"uri":"/media/portal/borrame.png"}""");

        var delete = await AdminSend(HttpMethod.Delete, $"/api/admin/model-images/{modelId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var model = await CatalogModel(modelId);
        Assert.Equal(JsonValueKind.Null, model.GetProperty("imageUri").ValueKind);

        // Borrar lo que ya no existe: 404
        var again = await AdminSend(HttpMethod.Delete, $"/api/admin/model-images/{modelId}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task ConTokenDeCliente_Devuelve403()
    {
        const string modelId = "MIMG-403X-4E2A-9D77-001122334407";
        var clientToken = await _factory.GetTokenAsync(_client); // rol "integration", no admin

        var get = await AdminSend(HttpMethod.Get, "/api/admin/model-images", token: clientToken);
        var put = await AdminSend(HttpMethod.Put, $"/api/admin/model-images/{modelId}", """{"uri":"x"}""", token: clientToken);
        var del = await AdminSend(HttpMethod.Delete, $"/api/admin/model-images/{modelId}", token: clientToken);

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }
}
