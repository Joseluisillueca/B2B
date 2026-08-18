using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Fase 2 del portal: el catálogo del portal necesita facetas dinámicas, orden,
// paginación, precio por talla y precio del cliente. Todo sale de una sola llamada,
// sin romper el contrato que ya consume /shop.
public class ShopCatalogFacetsTests : IClassFixture<ShopCatalogFacetsTests.Factory>, IAsyncLifetime
{
    // El catálogo se filtra con el cliente del token, así que la fábrica vincula
    // el usuario de pruebas a un cliente antes de sembrar ofertas.
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "CA000000-1111-4111-8111-000000000001";
    private const string ClientB = "CB000000-2222-4222-8222-000000000002";

    private const string Aeterna = "MODL0001-0000-4000-9000-000000000001";
    private const string Kids = "MODL0002-0000-4000-9000-000000000002";
    private const string CleanKit = "MODL0003-0000-4000-9000-000000000003";

    private const string Aeterna36 = "PROD0001-0000-4000-9000-000000000036";
    private const string Aeterna37 = "PROD0001-0000-4000-9000-000000000037";
    private const string Kids21 = "PROD0002-0000-4000-9000-000000000021";
    private const string Kids35 = "PROD0002-0000-4000-9000-000000000035";
    private const string CleanKitU = "PROD0003-0000-4000-9000-0000000000ff";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ShopCatalogFacetsTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await SeedAsync();

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

            await PutAsync($"/api/clients/{ClientA}",
                """{"name":"TEST 5","externalReference":"C100057","canShop":true,"groupIds":["mayorista"],"productSegments":["A+"],"payMethods":["transf30"]}""");
            await PutAsync($"/api/clients/{ClientA}/users/admin",
                $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test","culture":"es_ES"}""");

            await PutAsync("/api/core/service-windows/reposic",
                """{"id":"reposic","name":{"es_ES":"Reposición"},"orderType":"REPLENISHMENT","from":"2026-01-01","to":"2026-12-31","limit":"2026-12-31"}""");
            await PutAsync("/api/core/service-windows/ss27",
                """{"id":"ss27","name":{"es_ES":"Programación SS27"},"orderType":"SCHEDULED","from":"2027-02-01","to":"2027-04-30","limit":"2026-11-30"}""");

            // Adulto, silueta One, colección General — dos tallas con stock
            await PutAsync($"/api/catalog/models/{Aeterna}",
                """{"name":{"es_ES":"LEJAN ONE - AETERNA"},"active":true,"externalReference":"1974","familyId":"calzado","attributes":{"Grupo de edad":"Adulto","Silueta":"One","Colección":"General"},"productSegments":["A"]}""");
            await PutAsync($"/api/catalog/products/{Aeterna36}",
                $$"""{"modelId":"{{Aeterna}}","name":{"es_ES":"Aeterna 36"},"active":true,"sku":"1974-36","ean":"8400036","attributes":{"tallas":"36"},"taxId":"iva-normal"}""");
            await PutAsync($"/api/catalog/products/{Aeterna37}",
                $$"""{"modelId":"{{Aeterna}}","name":{"es_ES":"Aeterna 37"},"active":true,"sku":"1974-37","ean":"8400037","attributes":{"tallas":"37"},"taxId":"iva-normal"}""");
            await PutAsync($"/api/stock/inventory/{Aeterna36}",
                """{"stock":51,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");
            await PutAsync($"/api/stock/inventory/{Aeterna37}",
                """{"stock":7,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");
            // Ventana programada abierta: el conector manda 10000 como "ilimitado"
            await PutAsync($"/api/stock/inventory/{Aeterna36}",
                """{"stock":10000,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"SS27","orderType":"SCHEDULED"}""");

            // Kids, colección Mesh — precio POR TALLA (oferta por productId) y oferta de cliente
            await PutAsync($"/api/catalog/models/{Kids}",
                """{"name":{"es_ES":"LEJAN ONE KIDS - MUSTARD"},"active":true,"externalReference":"1005","familyId":"calzado","attributes":{"Grupo de edad":"Kids","Silueta":"One","Colección":"Mesh"},"productSegments":["A"]}""");
            await PutAsync($"/api/catalog/products/{Kids21}",
                $$"""{"modelId":"{{Kids}}","name":{"es_ES":"Kids 21"},"active":true,"sku":"1005-21","ean":"8400021","attributes":{"tallas":"21"},"taxId":"iva-normal"}""");
            await PutAsync($"/api/catalog/products/{Kids35}",
                $$"""{"modelId":"{{Kids}}","name":{"es_ES":"Kids 35"},"active":true,"sku":"1005-35","ean":"8400035","attributes":{"tallas":"35"},"taxId":"iva-normal"}""");
            await PutAsync($"/api/stock/inventory/{Kids21}",
                """{"stock":99,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");
            await PutAsync($"/api/stock/inventory/{Kids35}",
                """{"stock":4,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");

            // Limpieza, talla única y sin stock → "Consultar"
            await PutAsync($"/api/catalog/models/{CleanKit}",
                """{"name":{"es_ES":"LEJAN CLEAN KIT"},"active":true,"externalReference":"9001","familyId":"limpieza","attributes":{"Grupo de edad":"Adulto","Colección":"General"},"productSegments":["A"]}""");
            await PutAsync($"/api/catalog/products/{CleanKitU}",
                $$"""{"modelId":"{{CleanKit}}","name":{"es_ES":"Clean kit"},"active":true,"sku":"9001-U","ean":"8409001","attributes":{"tallas":"U"},"taxId":"iva-normal"}""");
            await PutAsync($"/api/stock/inventory/{CleanKitU}",
                """{"stock":0,"type":"Inventory","entryDate":"2026-08-17","stockServiceId":"REPOSIC","orderType":"REPLENISHMENT"}""");

            await PutAsync("/api/catalog/offers", $$$"""
            [
              {"id":"OFR00001-0000-4000-9000-000000000001","offerData":{"basePrice":{"code":"EUR","value":48.0},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{Aeterna}}}"}},
              {"id":"OFR00001-0000-4000-9000-000000000002","offerData":{"basePrice":{"code":"EUR","value":79.95},"priceType":"PVP","stock":0,"priority":1,"modelId":"{{{Aeterna}}}"}},
              {"id":"OFR00001-0000-4000-9000-000000000003","offerData":{"basePrice":{"code":"EUR","value":40.0},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{Aeterna}}}","clientId":"{{{ClientA}}}","clientGroupId":""}},
              {"id":"OFR00001-0000-4000-9000-000000000004","offerData":{"basePrice":{"code":"EUR","value":9.99},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{Aeterna}}}","clientId":"{{{ClientB}}}","clientGroupId":""}},
              {"id":"OFR00002-0000-4000-9000-000000000001","offerData":{"basePrice":{"code":"EUR","value":30.0},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{Kids}}}"}},
              {"id":"OFR00002-0000-4000-9000-000000000021","offerData":{"basePrice":{"code":"EUR","value":28.0},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{Kids}}}","productId":"{{{Kids21}}}"}},
              {"id":"OFR00002-0000-4000-9000-000000000035","offerData":{"basePrice":{"code":"EUR","value":28.5},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{Kids}}}","productId":"{{{Kids35}}}"}},
              {"id":"OFR00003-0000-4000-9000-000000000001","offerData":{"basePrice":{"code":"EUR","value":7.9},"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{CleanKit}}}"}}
            ]
            """);

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private static JsonElement Item(JsonElement body, string modelId) =>
        body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("modelId").GetString() == modelId);

    private static string[] ModelIds(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("modelId").GetString()!)];

    [Fact]
    public async Task Catalog_MantieneElContratoQueYaConsumeLaTiendaActual()
    {
        var body = await GetAsync("/api/shop/catalog");

        Assert.Contains(body.GetProperty("windows").EnumerateArray(),
            w => w.GetProperty("id").GetString() == "reposic");

        var item = Item(body, Aeterna);
        Assert.Equal("LEJAN ONE - AETERNA", item.GetProperty("name").GetString());
        Assert.Equal("1974", item.GetProperty("reference").GetString());
        Assert.Equal("calzado", item.GetProperty("familyId").GetString());
        Assert.Equal(79.95m, item.GetProperty("pvp").GetDecimal());

        var talla36 = item.GetProperty("products").EnumerateArray()
            .Single(p => p.GetProperty("size").GetString() == "36");
        Assert.Equal(51m, talla36.GetProperty("stock").GetProperty("reposic").GetDecimal());
        // El sentinela de "ilimitado" llega tal cual: lo interpreta el front (∞)
        Assert.Equal(10000m, talla36.GetProperty("stock").GetProperty("ss27").GetDecimal());
    }

    [Fact]
    public async Task Catalog_DevuelveFacetasDeFamiliaAtributoYDisponibilidad()
    {
        var body = await GetAsync("/api/shop/catalog");
        var facets = body.GetProperty("facets");

        var calzado = facets.GetProperty("families").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == "calzado");
        Assert.Equal(2, calzado.GetProperty("count").GetInt32());
        Assert.Equal("Calzado", calzado.GetProperty("label").GetString());

        var attributes = facets.GetProperty("attributes").EnumerateArray().ToList();
        var edad = attributes.Single(a => a.GetProperty("key").GetString() == "Grupo de edad");
        Assert.Equal(2, edad.GetProperty("values").EnumerateArray()
            .Single(v => v.GetProperty("value").GetString() == "Adulto").GetProperty("count").GetInt32());
        Assert.Equal(1, edad.GetProperty("values").EnumerateArray()
            .Single(v => v.GetProperty("value").GetString() == "Kids").GetProperty("count").GetInt32());

        var silueta = attributes.Single(a => a.GetProperty("key").GetString() == "Silueta");
        Assert.Equal(2, silueta.GetProperty("values").EnumerateArray()
            .Single(v => v.GetProperty("value").GetString() == "One").GetProperty("count").GetInt32());

        // Las tres etiquetas de la captura: Disponible, Consultar y < 10u
        var availability = facets.GetProperty("availability").EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetString()!, a => a.GetProperty("count").GetInt32());
        Assert.Equal(2, availability["available"]);   // Aeterna (51) y Kids (99)
        Assert.Equal(2, availability["low"]);         // Aeterna (7) y Kids (4)
        Assert.Equal(1, availability["consult"]);     // Clean kit, todo a 0
    }

    [Fact]
    public async Task Catalog_FiltraPorAtributo()
    {
        var body = await GetAsync("/api/shop/catalog?a.Grupo%20de%20edad=Kids");

        Assert.Equal([Kids], ModelIds(body));
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Catalog_FiltraPorFamiliaYBusquedaDeModelo()
    {
        Assert.Equal([Aeterna, Kids], ModelIds(await GetAsync("/api/shop/catalog?family=calzado")).Order().ToArray());
        Assert.Equal([Kids], ModelIds(await GetAsync("/api/shop/catalog?q=kids")));
        // La búsqueda también entra por referencia, como el buscador del portal actual
        Assert.Equal([Aeterna], ModelIds(await GetAsync("/api/shop/catalog?q=1974")));
    }

    [Fact]
    public async Task Catalog_FiltraPorDisponibilidad()
    {
        Assert.Equal([CleanKit], ModelIds(await GetAsync("/api/shop/catalog?availability=consult")));
        Assert.DoesNotContain(CleanKit, ModelIds(await GetAsync("/api/shop/catalog?availability=available")));
    }

    [Fact]
    public async Task Catalog_Ordena()
    {
        var barato = ModelIds(await GetAsync("/api/shop/catalog?sort=price-asc"));
        Assert.Equal(CleanKit, barato[0]);           // 7,90 €
        var caro = ModelIds(await GetAsync("/api/shop/catalog?sort=price-desc"));
        Assert.Equal(CleanKit, caro[^1]);
        // Destacados = alfabético, como el listado actual
        Assert.Equal(ModelIds(await GetAsync("/api/shop/catalog?sort=name")),
                     ModelIds(await GetAsync("/api/shop/catalog?sort=featured")));
    }

    [Fact]
    public async Task Catalog_Pagina()
    {
        var page = await GetAsync("/api/shop/catalog?skip=1&take=1");

        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(1, page.GetProperty("skip").GetInt32());
    }

    // m-8: la página por defecto era de 500 modelos (~1 MB). El front pide 24; el
    // servidor ahora sirve esa misma cifra por defecto y no deja pedir más de 100.
    [Fact]
    public async Task Catalog_PaginaPorDefecto24YNoDejaPasarDe100()
    {
        Assert.Equal(24, (await GetAsync("/api/shop/catalog")).GetProperty("take").GetInt32());
        Assert.Equal(100, (await GetAsync("/api/shop/catalog?take=500")).GetProperty("take").GetInt32());
        Assert.Equal(100, (await GetAsync("/api/shop/catalog?take=101")).GetProperty("take").GetInt32());
        Assert.Equal(50, (await GetAsync("/api/shop/catalog?take=50")).GetProperty("take").GetInt32());
    }

    // El techo de la página no puede recortar la descarga de stock: el CSV es el
    // listado completo con los mismos filtros, no la página que se está viendo.
    [Fact]
    public async Task StockExport_NoSeQuedaEnElTamanoDePagina()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/stock-export.csv?take=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);

        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        foreach (var referencia in new[] { "1974", "1005", "9001" })
            Assert.Contains(referencia, csv);
    }

    [Fact]
    public async Task Catalog_LaOfertaPorProductIdGanaALaDeModelId()
    {
        var item = Item(await GetAsync("/api/shop/catalog"), Kids);

        var products = item.GetProperty("products").EnumerateArray()
            .ToDictionary(p => p.GetProperty("size").GetString()!, p => p.GetProperty("pvd").GetDecimal());
        Assert.Equal(28.0m, products["21"]);
        Assert.Equal(28.5m, products["35"]);

        // La cabecera del artículo muestra el precio desde el que arranca la curva
        Assert.Equal(28.0m, item.GetProperty("pvd").GetDecimal());
        // Con precios distintos por talla el front pinta el precio en cada celda
        Assert.True(item.GetProperty("pricePerSize").GetBoolean());
    }

    [Fact]
    public async Task Catalog_SinPrecioPorTalla_NoMarcaPricePerSize()
    {
        var item = Item(await GetAsync("/api/shop/catalog"), Aeterna);
        Assert.False(item.GetProperty("pricePerSize").GetBoolean());
    }

    [Fact]
    public async Task Catalog_AplicaLaTarifaDelClienteDelTokenYNuncaLaDeOtro()
    {
        var body = await GetAsync("/api/shop/catalog");
        var item = Item(body, Aeterna);

        // El usuario de pruebas es del cliente A: 40,00 € gana a la tarifa general de 48,00 €
        Assert.Equal(40.0m, item.GetProperty("pvd").GetDecimal());
        Assert.Equal(40.0m, item.GetProperty("products").EnumerateArray()
            .First().GetProperty("pvd").GetDecimal());
        // La tarifa del cliente B (9,99 €) no asoma por ningún lado
        Assert.DoesNotContain("9.99", body.GetRawText());
    }

    [Fact]
    public async Task StockExport_DevuelveCsvConBomYRespetaLosFiltros()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/stock-export.csv?a.Grupo%20de%20edad=Kids");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);   // BOM: Excel abre el CSV en UTF-8

        var csv = Encoding.UTF8.GetString(bytes);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Referencia", lines[0]);
        Assert.Contains("Stock", lines[0]);
        Assert.Contains(lines, l => l.Contains("1005-21") && l.Contains("99"));
        Assert.DoesNotContain(lines, l => l.Contains("1974-36"));
    }

    [Fact]
    public async Task StockExport_SinToken_Devuelve401()
    {
        var response = await _client.GetAsync("/api/shop/stock-export.csv");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
