using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Shop;

namespace B2B.Api.Tests;

// Orden de tallas (POLO CLUB): el catálogo ordenaba las numéricas por su valor y dejaba las
// alfabéticas por texto, así que «S, M, L, XL, XXL» salían como «L, M, S, XL, XXL». Ahora hay
// un rango XXS < XS < S < M < L < XL < XXL < 3XL < 4XL < 5XL por delante del numérico; las
// numéricas (34–48) conservan su orden y la talla única sigue detrás.
public class SizeOrderTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SizeOrderTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── 1. El rango, en unidad ────────────────────────────────────────────────

    [Theory]
    [InlineData("XXS", 0)]
    [InlineData("xs", 1)]
    [InlineData(" S ", 2)]
    [InlineData("m", 3)]
    [InlineData("L", 4)]
    [InlineData("XL", 5)]
    [InlineData("XXL", 6)]
    [InlineData("2XL", 6)]
    [InlineData("3XL", 7)]
    [InlineData("xxxl", 7)]
    [InlineData("4XL", 8)]
    [InlineData("5XL", 9)]
    public void Rank_TallasAlfabeticas_EnOrden(string size, int expected) =>
        Assert.Equal(expected, SizeOrder.Rank(size));

    [Theory]
    [InlineData("42")]
    [InlineData("38.5")]
    [InlineData("U")]
    [InlineData("TU")]
    [InlineData("")]
    [InlineData(null)]
    public void Rank_FueraDelRango_VanDetras(string? size) =>
        Assert.Equal(int.MaxValue, SizeOrder.Rank(size));

    // ── 2. A través del catálogo del portal ───────────────────────────────────

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<List<string?>> SizesOfAsync(string modelId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/shop/catalog?locale=es&ids={modelId}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var catalog = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(catalog.GetProperty("items").EnumerateArray());
        return [.. item.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("size").GetString())];
    }

    private async Task SeedModelAsync(string modelId, string reference, string[] sizes)
    {
        await PutAsync($"/api/catalog/models/{modelId}",
            $$"""{"name":{"es_ES":"Talla {{reference}}"},"active":true,"externalReference":"{{reference}}","familyId":"ropa","productSegments":["A"]}""");
        // Se dan de alta DESORDENADAS a propósito (orden de llegada ≠ orden de talla).
        for (var i = 0; i < sizes.Length; i++)
        {
            var productId = $"{modelId[..8]}-0000-4000-9000-{i:D12}";
            await PutAsync($"/api/catalog/products/{productId}",
                $$"""{"modelId":"{{modelId}}","name":{"es_ES":"{{reference}} {{sizes[i]}}"},"active":true,"sku":"{{reference}}-{{sizes[i]}}","attributes":{"tallas":"{{sizes[i]}}"},"taxId":"iva-normal"}""");
        }
    }

    [Fact]
    public async Task Catalogo_TallasAlfabeticas_SalenEnElRango()
    {
        const string model = "SZALPHA1-0000-4000-9000-000000000001";
        await SeedModelAsync(model, "SZ-47217", ["XXL", "L", "S", "3XL", "M", "XL", "xs"]);

        Assert.Equal(["xs", "S", "M", "L", "XL", "XXL", "3XL"], await SizesOfAsync(model));
    }

    [Fact]
    public async Task Catalogo_TallasNumericas_ConservanElOrdenNumerico()
    {
        const string model = "SZNUMER1-0000-4000-9000-000000000001";
        await SeedModelAsync(model, "SZ-47283", ["40", "34", "48", "36", "44", "38", "42", "46"]);

        Assert.Equal(["34", "36", "38", "40", "42", "44", "46", "48"], await SizesOfAsync(model));
    }

    [Fact]
    public async Task Catalogo_TallaUnica_SigueDetrasDeLasNumericas()
    {
        const string model = "SZMIXED1-0000-4000-9000-000000000001";
        await SeedModelAsync(model, "SZ-MIX", ["U", "38", "36"]);

        Assert.Equal(["36", "38", "U"], await SizesOfAsync(model));
    }
}
