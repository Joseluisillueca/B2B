using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Los PUT de catálogo (contrato 02) además de guardar el payload crudo deben
// proyectarlo a tablas de dominio consultables por el front y el CMS.
public class CatalogNormalizationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogNormalizationTests(TestWebApplicationFactory factory)
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private T InDb<T>(Func<AppDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Payload real del contrato 02 §2, typos incluidos
    private const string ModelPayload = """
        {
          "name": { "es_ES": "Camiseta básica", "en_EN": "Basic T-Shirt", "fr_FR": "T-shirt basique", "it_IT": "T-shirt basic" },
          "description": { "es_ES": "Camiseta de algodón", "en_EN": "", "fr_FR": "", "it_IT": "" },
          "active": true,
          "externalReference": "ART-00123",
          "attributes": { "Color": "Azul", "temporada": "Verano 2026" },
          "familyId": "camisetas",
          "brandId": "",
          "crossSellingIds": [],
          "upSellingIds": [],
          "configuragleComponennts": [],
          "productSegments": ["A+", "A"]
        }
        """;

    [Fact]
    public async Task PutModel_NormalizaACatalogModels()
    {
        const string id = "MODEL111-4F3B-4E2A-9D77-001122334455";

        var response = await Put($"/api/catalog/models/{id}", ModelPayload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = InDb(db => db.CatalogModels.Single(m => m.ExternalId == id));
        Assert.Equal("Camiseta básica", model.Name);
        Assert.Equal("ART-00123", model.ExternalReference);
        Assert.Equal("camisetas", model.FamilyId);
        Assert.True(model.Active);
        Assert.Contains("A+", model.ProductSegmentsJson);
    }

    [Fact]
    public async Task PutModel_DosVeces_ActualizaSinDuplicar()
    {
        const string id = "MODEL222-4F3B-4E2A-9D77-001122334455";
        await Put($"/api/catalog/models/{id}", ModelPayload);

        var updated = ModelPayload.Replace("Camiseta básica", "Camiseta premium").Replace("\"active\": true", "\"active\": false");
        await Put($"/api/catalog/models/{id}", updated);

        var models = InDb(db => db.CatalogModels.Where(m => m.ExternalId == id).ToList());
        var model = Assert.Single(models);
        Assert.Equal("Camiseta premium", model.Name);
        Assert.False(model.Active);
    }

    [Fact]
    public async Task PutProduct_NormalizaConTallaDeAttributes()
    {
        // Payload real del contrato 02 §4: variante con solo es_ES y atributo tallas
        const string id = "VARIANT1-4F3B-4E2A-9D77-001122334455";
        var payload = """
            {
              "modelId": "MODEL111-4F3B-4E2A-9D77-001122334455",
              "name": { "es_ES": "Camiseta básica Azul T-M" },
              "description": { "es_ES": "Camiseta básica Azul T-M" },
              "active": true,
              "sku": "8412345678905",
              "externalReference": "8412345678905",
              "attributes": { "tallas": "M", "color": "Azul" },
              "ean": "8412345678905",
              "stockAlerts": [], "spareParts": [], "brandId": "",
              "crossSellingIds": [], "upSellingIds": [],
              "taxId": "iva-normal"
            }
            """;

        var response = await Put($"/api/catalog/products/{id}", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = InDb(db => db.CatalogProducts.Single(p => p.ExternalId == id));
        Assert.Equal("MODEL111-4F3B-4E2A-9D77-001122334455", product.ModelExternalId);
        Assert.Equal("M", product.Size);
        Assert.Equal("8412345678905", product.Sku);
        Assert.Equal("8412345678905", product.Ean);
        Assert.False(product.IsCasePack);
    }

    [Fact]
    public async Task PutCasePack_SeMarcaComoBundle()
    {
        // Contrato 02 §5: los case packs llegan al MISMO endpoint de productos, con bundle
        const string id = "CASEPCK1-4F3B-4E2A-9D77-001122334455";
        var payload = """
            {
              "modelId": "MODEL111-4F3B-4E2A-9D77-001122334455",
              "name": { "es_ES": "Caja 12 uds" },
              "active": true,
              "sku": "18412345678902",
              "attributes": {},
              "ean": "18412345678902",
              "taxId": "iva-normal",
              "bundle": {
                "products": { "VARIANT1-4F3B-4E2A-9D77-001122334455": 12 },
                "isVirtual": false
              }
            }
            """;

        var response = await Put($"/api/catalog/products/{id}", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = InDb(db => db.CatalogProducts.Single(p => p.ExternalId == id));
        Assert.True(product.IsCasePack);
        Assert.Null(product.Size);
        Assert.Contains("VARIANT1-4F3B-4E2A-9D77-001122334455", product.BundleJson);
    }

    [Fact]
    public async Task PutModel_ViaLegacySinSegmentos_NoFalla()
    {
        // Vía legacy Cod80103 (contrato 02 §2): sin traducciones reales ni segmentos
        const string id = "LEGACY11-4F3B-4E2A-9D77-001122334455";
        var payload = """
            {
              "name": { "es_ES": "Modelo legacy", "en_EN": "-", "fr_FR": "-", "it_IT": "-" },
              "active": true,
              "externalReference": "ART-LEG",
              "familyId": "",
              "productSegments": []
            }
            """;

        var response = await Put($"/api/catalog/models/{id}", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = InDb(db => db.CatalogModels.Single(m => m.ExternalId == id));
        Assert.Equal("Modelo legacy", model.Name);
        Assert.Equal("", model.FamilyId);
    }
}

// CMS: listado de la comunicación recibida (equivalente al "ver comunicación" del CMS actual)
public class AdminSyncDocumentsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminSyncDocumentsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListSyncDocuments_SinToken_Devuelve401()
    {
        var response = await _client.GetAsync("/api/admin/sync-documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListSyncDocuments_FiltraPorTipoYPagina()
    {
        var token = await _factory.GetTokenAsync(_client);
        for (var i = 1; i <= 3; i++)
        {
            var put = new HttpRequestMessage(HttpMethod.Put, $"/api/core/warehouses/ALM{i}")
            {
                Content = new StringContent($$"""{"code":"ALM{{i}}"}""", Encoding.UTF8, "application/json")
            };
            put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await _client.SendAsync(put)).EnsureSuccessStatusCode();
        }

        var get = new HttpRequestMessage(HttpMethod.Get, "/api/admin/sync-documents?entityType=warehouse&take=2");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("warehouse", i.GetProperty("entityType").GetString()));
        Assert.True(items[0].TryGetProperty("lastReceivedAt", out _));
    }
}
