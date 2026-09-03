using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Enforcement: el VisibilityScope resuelto por VisibilityStore.ScopeForAsync se
// enchufa en CatalogService.QueryAsync, así que TODO lo que pasa por ese pipeline
// (catálogo, facetas, búsqueda, relacionados, PDFs y CSV) queda filtrado por las
// reglas del cliente/agente que pregunta. Aquí se prueba desde los endpoints HTTP
// con un token de cliente real (no el de integración): PortalScope.ActorAsync saca
// el ClientId del propio AppUser.ClientExternalId, así que basta con sembrar un
// usuario "client-admin" con ese vínculo y loguearlo (mismo patrón que PaymentTests).
public class VisibilityCatalogTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Pass = "cliente-visibilidad-123";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VisibilityCatalogTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Utilidades de siembra (mismo patrón que RelatedProductsTests/ShopCatalogTests) ──

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

    private Task PutModel(string id, string name, string reference, string familyId,
        string attributesJson = "{}", string crossJson = "[]", bool active = true) =>
        Put($"/api/catalog/models/{id}",
            $$"""
            {"name":{"es_ES":"{{name}}"},"active":{{(active ? "true" : "false")}},"externalReference":"{{reference}}","familyId":"{{familyId}}","productSegments":["A"],"attributes":{{attributesJson}},"crossSellingIds":{{crossJson}} }
            """);

    private Task PutOffer(string offerId, string modelId, decimal pvd)
    {
        var value = pvd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Put("/api/catalog/offers",
            $$$"""[{"id":"{{{offerId}}}","offerData":{"basePrice":{"code":"EUR","value":{{{value}}} },"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{modelId}}}"}}]""");
    }

    private Task PutProduct(string id, string modelId, string size, string sku) =>
        Put($"/api/catalog/products/{id}",
            $$"""{"modelId":"{{modelId}}","name":{"es_ES":"Talla {{size}}"},"active":true,"sku":"{{sku}}","ean":"{{sku}}","attributes":{"tallas":"{{size}}"},"taxId":"iva-normal"}""");

    // El campo visibleAttributes es el que proyecta VisibilityStore.ProjectFromPayloadAsync
    // a la fila "bc" de CatalogVisibility (Tarea 3). rulesJsonArray va con sus corchetes.
    private Task PutClientVisibility(string clientId, string rulesJsonArray) =>
        Put($"/api/clients/{clientId}",
            $$"""{"name":"Cliente de prueba","visibleAttributes":{{rulesJsonArray}} }""");

    // Token de un cliente REAL del portal (no el de integración): un AppUser
    // "client-admin" con ClientExternalId = clientId, como lo deja el conector con
    // PUT /api/clients/{id}/users/admin. PortalScope.ActorAsync saca el ClientId de ahí.
    private async Task<string> ClientTokenAsync(string clientId)
    {
        var email = $"{clientId}@cliente.test".ToLowerInvariant();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync(u => u.Email == email))
            {
                var user = new AppUser
                {
                    Id = Guid.NewGuid(), Email = email, PasswordHash = "",
                    Role = ClientIdentity.ClientAdminRole, ClientExternalId = clientId, Culture = "es_ES"
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Pass);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
        }
        return await _factory.LoginAsync(_client, email, Pass);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    // ── 1. El catálogo (listado + facetas) solo enseña lo permitido ────────────

    [Fact]
    public async Task CatalogoFiltrado_SoloLoPermitido()
    {
        const string clientId = "VISCA1CL-0000-4000-9000-000000000001";
        const string a = "visca1a0-0000-4000-9000-000000000002";
        const string b = "visca1b0-0000-4000-9000-000000000003";
        const string tag = "VISCAT1TAG";

        await PutModel(a, $"{tag} ORIGEN ADIDAS", "VC1-A-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutOffer("visca1of-0000-4000-9000-000000000002", a, 40m);
        await PutModel(b, $"{tag} HERMANO NIKE", "VC1-B-REF", "ropa", """{"Marca":"NIKE"}""");
        await PutOffer("visca1og-0000-4000-9000-000000000003", b, 50m);

        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);

        // El tag en el nombre acota las facetas a este par de modelos (la BD de la
        // fixture es compartida entre los tests de esta clase): así "total" y las
        // facetas no dependen del resto de siembras.
        var response = await GetAsync($"/api/shop/catalog?q={tag}", token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("modelId").GetString()).ToList();
        Assert.Equal([a], ids);

        var families = body.GetProperty("facets").GetProperty("families").EnumerateArray()
            .Select(f => f.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain("ropa", families);

        var attributeValues = body.GetProperty("facets").GetProperty("attributes").EnumerateArray()
            .SelectMany(f => f.GetProperty("values").EnumerateArray())
            .Select(v => v.GetProperty("value").GetString())
            .ToList();
        Assert.DoesNotContain("NIKE", attributeValues);
    }

    // ── 2. La búsqueda no "destapa" un modelo oculto por su referencia ─────────

    [Fact]
    public async Task BusquedaNoDestapa()
    {
        const string clientId = "VISCA2CL-0000-4000-9000-000000000004";
        const string a = "visca2a0-0000-4000-9000-000000000005";
        const string b = "visca2b0-0000-4000-9000-000000000006";

        await PutModel(a, "VISCAT2 ORIGEN ADIDAS", "VC2-A-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutOffer("visca2of-0000-4000-9000-000000000005", a, 40m);
        await PutModel(b, "VISCAT2 HERMANO NIKE", "VC2-B-REF", "ropa", """{"Marca":"NIKE"}""");
        await PutOffer("visca2og-0000-4000-9000-000000000006", b, 50m);

        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);

        var response = await GetAsync("/api/shop/catalog?q=VC2-B-REF", token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    // ── 3. Relacionados no sugiere lo oculto, aunque venga en crossSellingIds ──

    [Fact]
    public async Task RelatedNoSugiereOculto()
    {
        const string clientId = "VISCA3CL-0000-4000-9000-000000000007";
        const string a = "visca3a0-0000-4000-9000-000000000008";
        const string b = "visca3b0-0000-4000-9000-000000000009";

        await PutModel(b, "VISCAT3 HERMANO NIKE", "VC3-B-REF", "ropa", """{"Marca":"NIKE"}""");
        await PutOffer("visca3of-0000-4000-9000-000000000009", b, 50m);
        await PutModel(a, "VISCAT3 ORIGEN ADIDAS", "VC3-A-REF", "calzado", """{"Marca":"ADIDAS"}""",
            crossJson: $"""["{b}"]""");
        await PutOffer("visca3og-0000-4000-9000-000000000008", a, 40m);

        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);

        var response = await GetAsync($"/api/shop/related?models={a}", token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    // ── 3b. Favoritos (14a-6, BAJO): el corazón valida que el modelo exista y esté en el
    // scope del actor (400 si no) — no se guardan favoritos fantasma ni de lo oculto.

    [Fact]
    public async Task Favoritos_ModeloInexistenteOFueraDeScope_400()
    {
        const string clientId = "VISCA3BC-0000-4000-9000-000000000031";
        const string a = "visca3ba-0000-4000-9000-000000000032";
        const string b = "visca3bb-0000-4000-9000-000000000033";

        await PutModel(a, "VISCAT3B ADIDAS", "VC3B-A-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutModel(b, "VISCAT3B NIKE", "VC3B-B-REF", "ropa", """{"Marca":"NIKE"}""");
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);

        async Task<HttpResponseMessage> Fav(string modelId)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/portal/favorites/{modelId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _client.SendAsync(request);
        }

        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Fav(a)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await Fav(b)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await Fav("no-existe-0000")).StatusCode);

        var list = await (await GetAsync("/api/portal/favorites", token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([a], list.GetProperty("items").EnumerateArray().Select(i => i.GetString()).ToArray());
    }

    // ── 4. Sin reglas de visibilidad, el cliente ve todo ───────────────────────

    [Fact]
    public async Task SinReglas_VeTodo()
    {
        const string clientId = "VISCA4CL-0000-4000-9000-000000000010";
        const string a = "visca4a0-0000-4000-9000-000000000011";
        const string b = "visca4b0-0000-4000-9000-000000000012";
        const string tag = "VISCAT4TAG";

        await PutModel(a, $"{tag} ORIGEN ADIDAS", "VC4-A-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutOffer("visca4of-0000-4000-9000-000000000011", a, 40m);
        await PutModel(b, $"{tag} HERMANO NIKE", "VC4-B-REF", "ropa", """{"Marca":"NIKE"}""");
        await PutOffer("visca4og-0000-4000-9000-000000000012", b, 50m);
        // Sin PutClientVisibility: este cliente no tiene reglas (ni bc ni manual).

        var token = await ClientTokenAsync(clientId);

        var response = await GetAsync($"/api/shop/catalog?q={tag}", token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, body.GetProperty("total").GetInt32());
    }

    // ── 4b. C1 (revisión AL, 14a-6): paridad de claves de atributo. El doc `attribute`
    // {code:"marca", name:{es_ES:"Marca del producto"}} hace que el modelo con la clave
    // "Marca del producto" (Item Attribute.Name) case con la regla marca=[...] (B2B Code).

    [Fact]
    public async Task ParidadDeClaves_NombreDelAtributoCasaConElCodigoDeLaRegla()
    {
        const string visibleClient = "VISCA4BC-0000-4000-9000-000000000041";
        const string hiddenClient = "VISCA4BD-0000-4000-9000-000000000042";
        const string model = "visca4bm-0000-4000-9000-000000000043";
        const string tag = "VISCAT4BTAG";

        await Put("/api/catalog/attributes/ATTR-VISCA4B-MARCA",
            """{"code":"marca","name":{"es_ES":"Marca del producto","en_EN":"Product brand"},"values":[]}""");
        await PutModel(model, $"{tag} ADIDAS", "VC4B-REF", "calzado", """{"Marca del producto":"ADIDAS"}""");
        await PutOffer("visca4bo-0000-4000-9000-000000000044", model, 40m);
        await PutClientVisibility(visibleClient, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        await PutClientVisibility(hiddenClient, """[{"attributeId":"marca","valueIds":["nike"]}]""");

        var visible = await (await GetAsync($"/api/shop/catalog?q={tag}", await ClientTokenAsync(visibleClient)))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, visible.GetProperty("total").GetInt32());

        var hidden = await (await GetAsync($"/api/shop/catalog?q={tag}", await ClientTokenAsync(hiddenClient)))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, hidden.GetProperty("total").GetInt32());
    }

    // ── 5. La descarga de stock (CSV) también queda filtrada ───────────────────

    [Fact]
    public async Task StockExportFiltrado()
    {
        const string clientId = "VISCA5CL-0000-4000-9000-000000000013";
        const string a = "visca5a0-0000-4000-9000-000000000014";
        const string b = "visca5b0-0000-4000-9000-000000000015";
        const string aProd = "visca5p0-0000-4000-9000-000000000016";
        const string bProd = "visca5p1-0000-4000-9000-000000000017";

        await PutModel(a, "VISCAT5 ORIGEN ADIDAS", "VC5-A-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutOffer("visca5of-0000-4000-9000-000000000014", a, 40m);
        await PutProduct(aProd, a, "40", "VC5-A-SKU");
        await PutModel(b, "VISCAT5 HERMANO NIKE", "VC5-B-REF", "ropa", """{"Marca":"NIKE"}""");
        await PutOffer("visca5og-0000-4000-9000-000000000015", b, 50m);
        await PutProduct(bProd, b, "40", "VC5-B-SKU");

        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);

        var response = await GetAsync("/api/shop/stock-export.csv", token);
        response.EnsureSuccessStatusCode();
        var csv = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("VC5-B-REF", csv);
        Assert.Contains("VC5-A-REF", csv);
    }
}
