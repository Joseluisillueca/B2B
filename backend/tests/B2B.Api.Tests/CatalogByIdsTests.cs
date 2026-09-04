using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// El raíl "Compra el look" del lookbook lo monta el CMS eligiendo artículos uno a uno, y
// el portal los buscaba dentro de una PÁGINA del catálogo (tope de 100 filas). Con un
// catálogo real de cientos de artículos, los elegidos que caían fuera de esa página
// desaparecían del raíl sin ningún aviso: la historia salía publicada y sin productos.
// Ahora el catálogo acepta una lista explícita de ids, que sigue pasando por el mismo
// pipeline (surtido del cliente, tarifa y regla de la foto).
public class CatalogByIdsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogByIdsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task PutModel(string id, string nombre, string referencia)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/catalog/models/{id}")
        {
            Content = new StringContent(
                $$$"""{"name":{"es_ES":"{{{nombre}}}"},"active":true,"externalReference":"{{{referencia}}}","familyId":"calzado","productSegments":["A"]}""",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> CatalogAsync(string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/catalog?locale=es" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> Ids(JsonElement catalog) =>
        [.. catalog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("modelId").GetString())];

    // ── 1. Se piden tres artículos concretos y llegan los tres ────────────────
    // Con MÁS artículos en el catálogo que el tope de una página, para que la prueba
    // falle si alguien vuelve a resolver el raíl paginando.

    [Fact]
    public async Task PideArticulosConcretos_LleganTodosAunqueElCatalogoSeaGrande()
    {
        var todos = new List<string>();
        for (var i = 0; i < 120; i++)
        {
            var id = $"IDSCAT{i:D2}-0000-4000-9000-000000000001";
            await PutModel(id, $"IDSCAT relleno {i}", $"IDSCAT-{i}");
            todos.Add(id);
        }
        // Tres del final: en una página de 100 no entrarían
        var elegidos = todos[^3..];

        var pagina = await CatalogAsync("&take=100&q=IDSCAT");
        Assert.Equal(100, Ids(pagina).Count);

        var porIds = await CatalogAsync("&ids=" + string.Join(',', elegidos));
        Assert.Equal(3, Ids(porIds).Count);
        foreach (var id in elegidos) Assert.Contains(id, Ids(porIds));
    }

    // ── 2. Pedir por id NO salta el surtido ni la regla de la foto ────────────
    // Es la parte importante: un id explícito no puede ser un atajo para ver algo que
    // el cliente no debería ver.

    [Fact]
    public async Task PedirPorId_NoSaltaLaReglaDeLaFoto()
    {
        const string id = "IDSFOTO1-0000-4000-9000-000000000002";
        await PutModel(id, "IDSFOTO sin foto", "IDSFOTO-1");

        var admin = new HttpRequestMessage(HttpMethod.Put, "/api/admin/integration/catalog")
        {
            Content = new StringContent("""{"requireModelImage":true}""", Encoding.UTF8, "application/json")
        };
        admin.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        (await _client.SendAsync(admin)).EnsureSuccessStatusCode();

        try
        {
            var porIds = await CatalogAsync("&ids=" + id);
            Assert.DoesNotContain(id, Ids(porIds));
        }
        finally
        {
            var apagar = new HttpRequestMessage(HttpMethod.Put, "/api/admin/integration/catalog")
            {
                Content = new StringContent("""{"requireModelImage":false}""", Encoding.UTF8, "application/json")
            };
            apagar.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await _factory.GetAdminTokenAsync(_client));
            (await _client.SendAsync(apagar)).EnsureSuccessStatusCode();
        }
    }

    // ── 3. Ids desconocidos: se ignoran, no rompen la petición ────────────────

    [Fact]
    public async Task IdsDesconocidos_SeIgnoran()
    {
        const string bueno = "IDSOK001-0000-4000-9000-000000000003";
        await PutModel(bueno, "IDSOK existe", "IDSOK-1");

        var porIds = await CatalogAsync($"&ids={bueno},NO-EXISTE-0000-4000-9000-000000000000");
        Assert.Equal([bueno], Ids(porIds));
    }
}
