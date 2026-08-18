using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Auditoría M-1: /api/shop/catalog era el único endpoint de datos del portal que no
// recibía locale, así que la vista principal de compra enseñaba las facetas y los
// nombres en español en inglés, francés e italiano. Ahora acepta locale y localiza
// todo el vocabulario que viene traducido en los payloads del sync; lo que BC manda
// igual en los cuatro idiomas viaja además con una clave estable para que el portal
// pueda traducirlo por su cuenta.
public class ShopCatalogLocaleTests : IClassFixture<ShopCatalogLocaleTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string Aeterna = "LOCL0001-0000-4000-9000-000000000001";
    private const string CleanKit = "LOCL0002-0000-4000-9000-000000000002";
    private const string Aeterna36 = "LOCL0001-0000-4000-9000-000000000036";
    private const string CleanKitU = "LOCL0002-0000-4000-9000-0000000000ff";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ShopCatalogLocaleTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> GetAsync(string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static bool _seeded;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task SeedAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            // Ventana de servicio con nombre traducido (contrato 03 §1)
            await PutAsync("/api/core/service-windows/reposic", """
            {"id":"reposic","name":{"es_ES":"Reposición","en_EN":"Replenishment",
              "fr_FR":"Réassort","it_IT":"Riassortimento"},
             "orderType":"REPLENISHMENT","from":"2026-01-01","to":"2026-12-31","limit":"2026-12-31"}
            """);

            // Familias y atributos: el conector los publica con su propio nombre
            // multiidioma (contrato 02 §6 y §7)
            await PutAsync("/api/catalog/families/calzado", """
            {"name":{"es_ES":"Calzado","en_EN":"Footwear","fr_FR":"Chaussures","it_IT":"Calzature"},
             "code":"calzado","atributes":[]}
            """);
            await PutAsync("/api/catalog/families/limpieza", """
            {"name":{"es_ES":"Limpieza","en_EN":"Care","fr_FR":"Entretien","it_IT":"Pulizia"},
             "code":"limpieza","atributes":[]}
            """);
            await PutAsync("/api/catalog/attributes/grupo-de-edad", """
            {"name":{"es_ES":"Grupo de edad","en_EN":"Age group","fr_FR":"Groupe d'âge","it_IT":"Fascia d'età"},
             "type":"ListString","isModelAttributte":true,"code":"grupo-de-edad","visibleWeb":true,
             "visibleFormat":"List","values":[{"order":1,"id":"adulto"},{"order":2,"id":"kids"}]}
            """);

            await PutAsync($"/api/catalog/models/{Aeterna}", """
            {"name":{"es_ES":"LEJAN ONE - AETERNA","en_EN":"LEJAN ONE - AETERNA (EN)",
              "fr_FR":"LEJAN ONE - AETERNA (FR)","it_IT":"LEJAN ONE - AETERNA (IT)"},
             "active":true,"externalReference":"1974","familyId":"calzado",
             "attributes":{"Grupo de edad":"Adulto","Silueta":"One"},"productSegments":["A"]}
            """);
            await PutAsync($"/api/catalog/products/{Aeterna36}", $$"""
            {"modelId":"{{Aeterna}}","name":{"es_ES":"Aeterna 36"},"active":true,
             "sku":"1974-36","ean":"8400036","attributes":{"tallas":"36"},"taxId":"iva-normal"}
            """);
            await PutAsync($"/api/stock/inventory/{Aeterna36}",
                """{"stock":51,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");

            // Modelo sin traducciones: el fallback al español tiene que sostenerlo
            await PutAsync($"/api/catalog/models/{CleanKit}", """
            {"name":{"es_ES":"LEJAN CLEAN KIT"},"active":true,"externalReference":"9001",
             "familyId":"limpieza","attributes":{"Grupo de edad":"Adulto"},"productSegments":["A"]}
            """);
            await PutAsync($"/api/catalog/products/{CleanKitU}", $$"""
            {"modelId":"{{CleanKit}}","name":{"es_ES":"Clean kit"},"active":true,
             "sku":"9001-U","ean":"8409001","attributes":{"tallas":"U"},"taxId":"iva-normal"}
            """);

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private static JsonElement Item(JsonElement body, string modelId) =>
        body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("modelId").GetString() == modelId);

    private static JsonElement Attribute(JsonElement body, string key) =>
        body.GetProperty("facets").GetProperty("attributes").EnumerateArray()
            .Single(a => a.GetProperty("key").GetString() == key);

    private static JsonElement Family(JsonElement body, string id) =>
        body.GetProperty("facets").GetProperty("families").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == id);

    // ── Sin locale: el comportamiento de siempre ───────────────────────────────

    [Fact]
    public async Task Catalog_SinLocale_SigueEnEspanol()
    {
        var body = await GetAsync("/api/shop/catalog");

        Assert.Equal("es", body.GetProperty("locale").GetString());
        Assert.Equal("LEJAN ONE - AETERNA", Item(body, Aeterna).GetProperty("name").GetString());
        Assert.Equal("Calzado", Item(body, Aeterna).GetProperty("familyLabel").GetString());
        Assert.Equal("Calzado", Family(body, "calzado").GetProperty("label").GetString());
        Assert.Equal("Grupo de edad", Attribute(body, "Grupo de edad").GetProperty("label").GetString());
        Assert.Equal("Reposición", body.GetProperty("windows").EnumerateArray()
            .Single(w => w.GetProperty("id").GetString() == "reposic").GetProperty("name").GetString());
    }

    // ── Con locale: nombre, familia, atributo y ventana ────────────────────────

    [Theory]
    [InlineData("en", "LEJAN ONE - AETERNA (EN)", "Footwear", "Age group", "Replenishment")]
    [InlineData("fr", "LEJAN ONE - AETERNA (FR)", "Chaussures", "Groupe d'âge", "Réassort")]
    [InlineData("it", "LEJAN ONE - AETERNA (IT)", "Calzature", "Fascia d'età", "Riassortimento")]
    public async Task Catalog_ConLocale_TraduceElVocabularioQueVieneTraducido(
        string locale, string name, string family, string attribute, string window)
    {
        var body = await GetAsync($"/api/shop/catalog?locale={locale}");

        Assert.Equal(locale, body.GetProperty("locale").GetString());
        Assert.Equal(name, Item(body, Aeterna).GetProperty("name").GetString());
        Assert.Equal(family, Item(body, Aeterna).GetProperty("familyLabel").GetString());
        Assert.Equal(family, Family(body, "calzado").GetProperty("label").GetString());
        Assert.Equal(attribute, Attribute(body, "Grupo de edad").GetProperty("label").GetString());
        Assert.Equal(window, body.GetProperty("windows").EnumerateArray()
            .Single(w => w.GetProperty("id").GetString() == "reposic").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Catalog_SinTraduccionEnLosDatos_CaeAlEspanol()
    {
        var body = await GetAsync("/api/shop/catalog?locale=fr");

        // El modelo solo trae es_ES; nada de dejar la ficha sin nombre
        Assert.Equal("LEJAN CLEAN KIT", Item(body, CleanKit).GetProperty("name").GetString());
        // El atributo "Silueta" no está publicado como entidad: la clave hace de etiqueta
        Assert.Equal("Silueta", Attribute(body, "Silueta").GetProperty("label").GetString());
    }

    [Fact]
    public async Task Catalog_LocaleDesconocido_CaeAlEspanol()
    {
        var body = await GetAsync("/api/shop/catalog?locale=de");

        Assert.Equal("es", body.GetProperty("locale").GetString());
        Assert.Equal("LEJAN ONE - AETERNA", Item(body, Aeterna).GetProperty("name").GetString());
    }

    // ── Claves estables: lo que el front traduce y con lo que filtra ───────────

    [Fact]
    public async Task Catalog_LasClavesDeFiltroNoCambianConElIdioma()
    {
        var español = await GetAsync("/api/shop/catalog");
        var ingles = await GetAsync("/api/shop/catalog?locale=en");

        foreach (var body in new[] { español, ingles })
        {
            var edad = Attribute(body, "Grupo de edad");
            Assert.Equal("grupo-de-edad", edad.GetProperty("keySlug").GetString());

            var adulto = edad.GetProperty("values").EnumerateArray()
                .Single(v => v.GetProperty("value").GetString() == "Adulto");
            Assert.Equal("adulto", adulto.GetProperty("slug").GetString());
            Assert.Equal("Adulto", adulto.GetProperty("label").GetString());
        }

        // El id de familia tampoco se traduce: es con lo que se filtra
        Assert.Equal("calzado", Family(ingles, "calzado").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Catalog_ConLocale_ElFiltroPorAtributoSigueFuncionando()
    {
        var body = await GetAsync("/api/shop/catalog?locale=en&a.Grupo%20de%20edad=Adulto");

        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal("LEJAN ONE - AETERNA (EN)", Item(body, Aeterna).GetProperty("name").GetString());
    }

    // La ficha del artículo pinta sus atributos: necesita etiqueta y clave a la vez
    [Fact]
    public async Task Catalog_ElArticuloTraeSusAtributosConEtiquetaYClave()
    {
        var body = await GetAsync("/api/shop/catalog?locale=en");
        var item = Item(body, Aeterna);

        // El diccionario crudo sigue publicado: contrato antiguo intacto
        Assert.Equal("Adulto", item.GetProperty("attributes").GetProperty("Grupo de edad").GetString());

        var edad = item.GetProperty("attributeList").EnumerateArray()
            .Single(a => a.GetProperty("key").GetString() == "Grupo de edad");
        Assert.Equal("Age group", edad.GetProperty("label").GetString());
        Assert.Equal("grupo-de-edad", edad.GetProperty("keySlug").GetString());
        Assert.Equal("Adulto", edad.GetProperty("value").GetString());
        Assert.Equal("adulto", edad.GetProperty("valueSlug").GetString());
        Assert.Equal("Adulto", edad.GetProperty("valueLabel").GetString());
    }

    [Fact]
    public async Task Catalog_ConLocale_OrdenaPorElNombreQueSeVe()
    {
        var body = await GetAsync("/api/shop/catalog?locale=en&sort=name");

        var names = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
    }
}
