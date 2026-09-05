using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// "B2B Visible Web" de Business Central (contrato 02 §6, `visibleWeb`): un atributo
// desmarcado se sigue sincronizando y sirve para buscar, relacionar y restringir, pero
// el comprador no lo ve ni como faceta ni como chip. Un código de color interno no es
// un argumento de venta.
public class AttributeVisibleWebTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AttributeVisibleWebTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task Connector(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
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

    [Fact]
    public async Task UnAtributoNoVisibleEnLaWeb_NoEsFacetaNiChip_PeroSigueEnLosDatos()
    {
        await Connector("/api/catalog/attributes/VW%20CODE",
            """{"code":"VW CODE","name":{"es_ES":"Código interno"},"type":"String","visibleWeb":false,"isModelAttributte":true}""");
        await Connector("/api/catalog/attributes/VW%20COLOR",
            """{"code":"VW COLOR","name":{"es_ES":"Color"},"type":"String","visibleWeb":true,"isModelAttributte":true}""");
        await Connector("/api/catalog/models/VISWEB01-0000-4000-9000-000000000001",
            """{"name":{"es_ES":"VISWEB Zapatilla"},"active":true,"externalReference":"VISWEB-1","familyId":"calzado","productSegments":["A"],"attributes":{"VW CODE":"C08","VW COLOR":"Aegean"}}""");

        var catalog = await CatalogAsync("&q=VISWEB");
        var item = catalog.GetProperty("items").EnumerateArray().Single();

        // El dato sigue ahí (búsqueda, relacionados, color)…
        Assert.Equal("C08", item.GetProperty("attributes").GetProperty("VW CODE").GetString());
        // …pero el comprador no lo ve: ni chip…
        var chips = item.GetProperty("attributeList").EnumerateArray()
            .Select(a => a.GetProperty("key").GetString()).ToList();
        Assert.DoesNotContain("VW CODE", chips);
        Assert.Contains("VW COLOR", chips);
        // …ni faceta
        var facets = catalog.GetProperty("facets").GetProperty("attributes").EnumerateArray()
            .Select(f => f.GetProperty("key").GetString()).ToList();
        Assert.DoesNotContain("VW CODE", facets);
        Assert.Contains("VW COLOR", facets);
    }

    [Fact]
    public async Task SinElCampo_SeEnsenaComoSiempre()
    {
        await Connector("/api/catalog/attributes/VW%20LEGACY",
            """{"code":"VW LEGACY","name":{"es_ES":"Heredado"},"type":"String"}""");
        await Connector("/api/catalog/models/VISWEB02-0000-4000-9000-000000000002",
            """{"name":{"es_ES":"VISWEBB Bota"},"active":true,"externalReference":"VISWEBB-1","familyId":"calzado","productSegments":["A"],"attributes":{"VW LEGACY":"Sí"}}""");

        var catalog = await CatalogAsync("&q=VISWEBB");
        var item = catalog.GetProperty("items").EnumerateArray().Single();
        Assert.Contains("VW LEGACY", item.GetProperty("attributeList").EnumerateArray()
            .Select(a => a.GetProperty("key").GetString()));
    }
}
