using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// PRODUCTOS RELACIONADOS (/api/shop/related): resuelve crossSellingIds/upSellingIds del
// payload crudo de los modelos de origen con el MISMO pipeline del catálogo (tarifa del
// cliente, stock por ventana, visibilidad) y devuelve cards en el orden comercial de BC.
public class RelatedProductsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RelatedProductsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Utilidades de siembra (mismo patrón que ShopCatalogTests) ──────────────

    private async Task Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    /// Modelo con relaciones tal y como llegan de BC: los arrays van en el payload crudo
    /// (jsonb) del documento; el normalizador no los materializa y el endpoint los lee de ahí.
    private Task PutModel(string id, string name, string reference,
        string crossJson = "[]", string upJson = "[]", bool active = true) =>
        Put($"/api/catalog/models/{id}",
            $$"""
            {"name":{"es_ES":"{{name}}"},"active":{{(active ? "true" : "false")}},"externalReference":"{{reference}}","familyId":"calzado","productSegments":["A"],"crossSellingIds":{{crossJson}},"upSellingIds":{{upJson}} }
            """);

    private Task PutOffer(string offerId, string modelId, decimal pvd)
    {
        // JSON con punto decimal aunque la cultura de la máquina sea es-ES
        var value = pvd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Put("/api/catalog/offers",
            $$$"""[{"id":"{{{offerId}}}","offerData":{"basePrice":{"code":"EUR","value":{{{value}}} },"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{modelId}}}"}}]""");
    }

    private async Task<HttpResponseMessage> GetRelated(string queryString)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/shop/related{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<List<JsonElement>> GetItems(string queryString)
    {
        var response = await GetRelated(queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray().ToList();
    }

    private static string ModelIdOf(JsonElement item) =>
        item.GetProperty("card").GetProperty("modelId").GetString()!;

    // ── 1. Camino feliz ────────────────────────────────────────────────────────

    [Fact]
    public async Task Related_CrossYUp_EnElOrdenDeBcYConTarifa()
    {
        const string a = "relhap0a-0000-4000-9000-000000000001";
        const string b = "relhap0b-0000-4000-9000-000000000002";
        const string c = "relhap0c-0000-4000-9000-000000000003";
        const string d = "relhap0d-0000-4000-9000-000000000004";

        await PutModel(b, "HAPPY CROSS UNO", "H-100");
        await PutModel(c, "HAPPY CROSS DOS", "H-200");
        await PutModel(d, "HAPPY UPSELL", "H-300");
        await PutOffer("relhof0b-0000-4000-9000-000000000002", b, 41.5m);
        await PutOffer("relhof0c-0000-4000-9000-000000000003", c, 52.0m);
        await PutOffer("relhof0d-0000-4000-9000-000000000004", d, 63.9m);
        await PutModel(a, "HAPPY ORIGEN", "H-000",
            crossJson: $"""["{b}","{c}"]""", upJson: $"""["{d}"]""");

        var items = await GetItems($"?models={a}");

        // Tres relacionados, en el orden comercial de BC: primero cross, luego up
        Assert.Equal(3, items.Count);
        Assert.Equal([b, c, d], items.Select(ModelIdOf).ToList());
        Assert.Equal(["cross", "cross", "up"],
            items.Select(i => i.GetProperty("relation").GetString()).ToList());

        // Las cards llevan la proyección del catálogo: tarifa y referencia reales
        var cardB = items[0].GetProperty("card");
        Assert.Equal("H-100", cardB.GetProperty("reference").GetString());
        Assert.Equal(41.5m, cardB.GetProperty("pvd").GetDecimal());
        Assert.Equal("HAPPY CROSS UNO", cardB.GetProperty("name").GetString());
        Assert.Equal(63.9m, items[2].GetProperty("card").GetProperty("pvd").GetDecimal());
    }

    // ── 1b. El parámetro models es case-insensitive (regresión del auditor: en
    // Postgres la traducción SQL de un HashSet ignora el comparer; la resolución debe
    // comparar SIEMPRE en memoria) ────────────────────────────────────────────────
    [Fact]
    public async Task Related_ParametroModelsEnMinusculas_ResuelveIgual()
    {
        const string a = "RELCASEA-0000-4000-9000-000000000041";
        const string b = "RELCASEB-0000-4000-9000-000000000042";
        await PutModel(b, "CASE CROSS", "K-100");
        await PutOffer("RELCASOF-0000-4000-9000-000000000042", b, 30m);
        await PutModel(a, "CASE ORIGEN", "K-000", crossJson: $"""["{b}"]""");

        var items = await GetItems($"?models={a.ToLowerInvariant()}");

        Assert.Single(items);
        Assert.Equal(b, ModelIdOf(items[0]));
    }

    // ── 1c. Resolución SIMÉTRICA: si B lista a A, la ficha de A enseña a B (y a los
    // hermanos que B declare) aunque el array de A esté vacío (BC aún no re-envió A) ──
    [Fact]
    public async Task Related_RelacionInversa_LaFichaDelHermanoSinArrayTambienSugiere()
    {
        const string a = "relsym0a-0000-4000-9000-000000000051";   // sin array (no re-enviado)
        const string b = "relsym0b-0000-4000-9000-000000000052";   // lista a A y a C
        const string c = "relsym0c-0000-4000-9000-000000000053";   // hermano declarado por B
        await PutModel(a, "SYM ORIGEN SIN ARRAY", "S-000");
        await PutModel(c, "SYM HERMANO DOS", "S-200");
        await PutOffer("relsymof-0000-4000-9000-000000000052", b, 20m);
        await PutOffer("relsymo2-0000-4000-9000-000000000053", c, 25m);
        await PutModel(b, "SYM HERMANO UNO", "S-100", crossJson: $"""["{a}","{c}"]""");

        var items = await GetItems($"?models={a}");

        // A no declara nada, pero B lo lista → B y su hermano C aparecen (A nunca).
        var ids = items.Select(ModelIdOf).ToList();
        Assert.Contains(b, ids);
        Assert.Contains(c, ids);
        Assert.DoesNotContain(a, ids);
    }

    // ── 1d. La simetría también aplica a la VENTA CRUZADA (misma colección): si B lista
    // a A en upSellingIds, la ficha de A sugiere a B como "up" aunque A no declare nada ──
    [Fact]
    public async Task Related_RelacionInversaDeVentaCruzada_TambienSugiereComoUp()
    {
        const string a = "relsyu0a-0000-4000-9000-000000000061";   // zapatilla sin arrays
        const string b = "relsyu0b-0000-4000-9000-000000000062";   // camiseta que lista a A (colección)
        await PutModel(a, "SYM UP ORIGEN", "U-000");
        await PutOffer("relsyuof-0000-4000-9000-000000000062", b, 15m);
        await PutModel(b, "SYM UP CAMISETA", "U-100", upJson: $"""["{a}"]""");

        var items = await GetItems($"?models={a}");

        Assert.Single(items);
        Assert.Equal(b, ModelIdOf(items[0]));
        Assert.Equal("up", items[0].GetProperty("relation").GetString());
    }

    // ── 2. El origen nunca se devuelve a sí mismo ──────────────────────────────

    [Fact]
    public async Task Related_ModeloQueSeListaASiMismo_NoSaleEnLaRespuesta()
    {
        const string a = "relself0-0000-4000-9000-000000000011";
        const string b = "relselfb-0000-4000-9000-000000000012";

        await PutModel(b, "SELF HERMANO", "S-100");
        await PutOffer("relsofb0-0000-4000-9000-000000000012", b, 30m);
        // BC a veces incluye al propio modelo en su lista de hermanos
        await PutModel(a, "SELF ORIGEN", "S-000", crossJson: $"""["{a}","{b}"]""");
        await PutOffer("relsofa0-0000-4000-9000-000000000011", a, 25m);

        var items = await GetItems($"?models={a}");

        Assert.Single(items);
        Assert.Equal(b, ModelIdOf(items[0]));
    }

    // ── 3. Solo lo visible/comprable ───────────────────────────────────────────

    [Fact]
    public async Task Related_InexistentesEInactivos_NoAparecen()
    {
        const string a = "relvis0a-0000-4000-9000-000000000021";
        const string ghost = "relghost-0000-4000-9000-000000000022"; // nunca sembrado
        const string off = "reloff00-0000-4000-9000-000000000023";   // active:false
        const string ok = "relvisok-0000-4000-9000-000000000024";

        await PutModel(off, "VISIBLE NO (INACTIVO)", "V-100", active: false);
        await PutOffer("relvofof-0000-4000-9000-000000000023", off, 10m);
        await PutModel(ok, "VISIBLE SI", "V-200");
        await PutOffer("relvofok-0000-4000-9000-000000000024", ok, 20m);
        await PutModel(a, "VISIBLE ORIGEN", "V-000",
            crossJson: $"""["{ghost}","{off}","{ok}"]""");

        var items = await GetItems($"?models={a}");

        // Ni el id inexistente ni el modelo desactivado pasan el filtro del catálogo
        Assert.Single(items);
        Assert.Equal(ok, ModelIdOf(items[0]));
    }

    // DECISIÓN DE NEGOCIO (2026-09-02): un relacionado ACTIVO pero sin oferta/tarifa para
    // el cliente NO se sugiere (en el catálogo aparecería como "consultar"; como sugerencia
    // sería ruido sin precio). El endpoint lo filtra por Pvd != null.
    [Fact]
    public async Task Related_ActivoSinTarifa_NoSeSugiere()
    {
        const string a = "relprb0a-0000-4000-9000-000000000091";
        const string noPrice = "relprbnp-0000-4000-9000-000000000092";
        await PutModel(noPrice, "PROBE SIN OFERTA", "PR-100");
        await PutModel(a, "PROBE ORIGEN", "PR-000", crossJson: $"""["{noPrice}"]""");

        var items = await GetItems($"?models={a}");

        Assert.Empty(items);   // sin tarifa → fuera de las sugerencias
    }

    // ── 4. Ids con llaves y mayúsculas (formato SystemId de BC) ────────────────

    [Fact]
    public async Task Related_IdsConLlavesYMayusculas_CasanIgualmente()
    {
        const string a = "relbrace-0000-4000-9000-000000000031";
        const string b = "relbraceb-000-4000-9000-000000000032";

        await PutModel(b, "BRACE HERMANO", "B-100");
        await PutOffer("relbofb0-0000-4000-9000-000000000032", b, 15m);
        // BC publica SystemIds como {GUID} en mayúsculas; el endpoint los normaliza
        var systemIdDeBc = "{" + b.ToUpperInvariant() + "}";
        await PutModel(a, "BRACE ORIGEN", "B-000",
            crossJson: $"""["{systemIdDeBc}"]""");

        var items = await GetItems($"?models={a}");

        Assert.Single(items);
        Assert.Equal(b, ModelIdOf(items[0]));
        Assert.Equal("cross", items[0].GetProperty("relation").GetString());
    }

    // ── 5. Multi-origen (carrito): unión deduplicada, sin los orígenes ─────────

    [Fact]
    public async Task Related_VariosOrigenes_UnionDeduplicadaSinLosOrigenes()
    {
        const string a = "relcart0-0000-4000-9000-000000000041";
        const string b = "relcartb-0000-4000-9000-000000000042";
        const string c = "relcartc-0000-4000-9000-000000000043";
        const string d = "relcartd-0000-4000-9000-000000000044";

        await PutModel(c, "CART COMUN", "C-100");
        await PutOffer("relcofc0-0000-4000-9000-000000000043", c, 12m);
        await PutModel(d, "CART SOLO DE B", "C-200");
        await PutOffer("relcofd0-0000-4000-9000-000000000044", d, 14m);
        // A y B se referencian entre sí y comparten a C: en el carrito con ambos,
        // ni A ni B deben salir y C solo una vez.
        await PutModel(a, "CART ORIGEN A", "C-000", crossJson: $"""["{c}","{b}"]""");
        await PutOffer("relcofa0-0000-4000-9000-000000000041", a, 10m);
        await PutModel(b, "CART ORIGEN B", "C-001", crossJson: $"""["{c}","{d}","{a}"]""");
        await PutOffer("relcofb0-0000-4000-9000-000000000042", b, 11m);

        var items = await GetItems($"?models={a},{b}");

        Assert.Equal(2, items.Count);
        Assert.Equal([c, d], items.Select(ModelIdOf).ToList());
    }

    // ── 6. Sin relaciones: 200 con lista vacía ─────────────────────────────────

    [Fact]
    public async Task Related_ModeloSinRelaciones_DevuelveVacio()
    {
        const string plain = "relplain-0000-4000-9000-000000000051";
        await PutModel(plain, "PLAIN SIN HERMANOS", "P-000"); // arrays vacíos

        Assert.Empty(await GetItems($"?models={plain}"));

        // Modelo sembrado por otra vía sin los campos crossSellingIds/upSellingIds
        const string legacy = "rellegcy-0000-4000-9000-000000000052";
        await Put($"/api/catalog/models/{legacy}",
            """{"name":{"es_ES":"LEGACY SIN ARRAYS"},"active":true,"externalReference":"P-100","familyId":"calzado","productSegments":["A"]}""");
        Assert.Empty(await GetItems($"?models={legacy}"));

        // Y sin parámetro models tampoco es un error
        Assert.Empty(await GetItems(""));
    }

    // ── 7. Payload roto: nunca un 500 ──────────────────────────────────────────

    [Fact]
    public async Task Related_PayloadConTiposRotos_DevuelveVacioSinReventar()
    {
        const string broken = "relbrokn-0000-4000-9000-000000000061";
        // crossSellingIds llega como string y upSellingIds mezcla tipos no-string
        await Put($"/api/catalog/models/{broken}",
            """{"name":{"es_ES":"BROKEN PAYLOAD"},"active":true,"externalReference":"X-000","familyId":"calzado","productSegments":["A"],"crossSellingIds":"esto-no-es-un-array","upSellingIds":[42,null,{"id":"objeto"}]}""");

        Assert.Empty(await GetItems($"?models={broken}"));
    }

    // ── 8. Autenticación ───────────────────────────────────────────────────────

    [Fact]
    public async Task Related_SinToken_Devuelve401()
    {
        var response = await _client.GetAsync("/api/shop/related?models=algo");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
