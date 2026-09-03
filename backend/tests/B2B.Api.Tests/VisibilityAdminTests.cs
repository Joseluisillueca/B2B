using System.Net;
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

// Tarea 7: endpoints admin de visibilidad (fila bc de solo lectura + fila manual editable)
// y la cinta del catálogo: config en IntegrationSettings.CatalogRibbonJson (admin) +
// GET /api/shop/ribbon computada SERVER-SIDE sobre las facetas ya filtradas por el
// VisibilityScope del actor (cero fugas de valores prohibidos).
public class VisibilityAdminTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Pass = "cliente-vis-admin-123";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VisibilityAdminTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Utilidades (mismo patrón que VisibilityCatalogTests) ───────────────────

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendJson(HttpMethod method, string route, string token, string json)
    {
        var request = new HttpRequestMessage(method, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

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

    private Task PutModel(string id, string name, string reference, string familyId, string attributesJson) =>
        Put($"/api/catalog/models/{id}",
            $$"""
            {"name":{"es_ES":"{{name}}"},"active":true,"externalReference":"{{reference}}","familyId":"{{familyId}}","productSegments":["A"],"attributes":{{attributesJson}} }
            """);

    // La fila "bc" se siembra por el mismo hook de ingesta real (visibleAttributes del
    // documento de cliente), como en VisibilityCatalogTests.
    private Task PutClientVisibility(string clientId, string rulesJsonArray) =>
        Put($"/api/clients/{clientId}",
            $$"""{"name":"Cliente de prueba","visibleAttributes":{{rulesJsonArray}} }""");

    private async Task<string> ClientTokenAsync(string clientId)
    {
        var email = $"{clientId}@cliente-vis-admin.test".ToLowerInvariant();
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

    // Siembra directa de una fila de CatalogVisibility (para montar bc+manual sin pasar
    // por los endpoints que se están probando).
    private async Task SeedVisibilityRowAsync(string subjectType, string subjectId, string source, string rulesJson)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CatalogVisibilities.Add(new CatalogVisibility
        {
            SubjectType = subjectType, SubjectId = subjectId, Source = source, RulesJson = rulesJson
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<CatalogVisibility>> RowsAsync(string subjectType, string subjectId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CatalogVisibilities
            .Where(v => v.SubjectType == subjectType && v.SubjectId == subjectId)
            .ToListAsync();
    }

    private async Task SetRibbonAsync(string ribbonJsonOrNull)
    {
        var admin = await _factory.GetAdminTokenAsync(_client);
        var response = await SendJson(HttpMethod.Put, "/api/admin/integration/ribbon", admin,
            $$"""{"ribbon":{{ribbonJsonOrNull}} }""");
        response.EnsureSuccessStatusCode();
    }

    // ── 1. GET: shape combinado bc + manual, con las efectivas delante ─────────

    [Fact]
    public async Task GetVisibility_DevuelveShapeCombinado()
    {
        const string clientId = "VISAD1CL-0000-4000-9000-000000000001";
        await SeedVisibilityRowAsync("client", clientId, "bc",
            """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        await SeedVisibilityRowAsync("client", clientId, "manual",
            """[{"attributeId":"marca","valueIds":["nike"]}]""");

        var admin = await _factory.GetAdminTokenAsync(_client);
        var response = await Send(HttpMethod.Get, $"/api/admin/visibility/client/{clientId}", admin);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("bc", body.GetProperty("source").GetString());

        // rules = las EFECTIVAS (manda la bc), parseadas como JSON, no como string.
        var rules = body.GetProperty("rules");
        Assert.Equal(JsonValueKind.Array, rules.ValueKind);
        Assert.Equal("marca", rules[0].GetProperty("attributeId").GetString());
        Assert.Equal("adidas", rules[0].GetProperty("valueIds")[0].GetString());

        // Las dos caras a la vez: la UI enseña "lo fija BC" y deja editar lo manual.
        Assert.Equal("adidas",
            body.GetProperty("bcRules")[0].GetProperty("valueIds")[0].GetString());
        Assert.Equal("nike",
            body.GetProperty("manualRules")[0].GetProperty("valueIds")[0].GetString());
    }

    // ── 2. PUT: upsert de la manual (normalizada a slug); [] la borra; bc intacta ──

    [Fact]
    public async Task PutVisibility_UpsertManual_YVacioBorra()
    {
        const string clientId = "VISAD2CL-0000-4000-9000-000000000002";
        const string bcRules = """[{"attributeId":"marca","valueIds":["adidas"]}]""";
        await SeedVisibilityRowAsync("client", clientId, "bc", bcRules);

        var admin = await _factory.GetAdminTokenAsync(_client);

        // Se manda SIN normalizar ("MARCA"/"ADIDAS"): debe guardarse en slug.
        var response = await Send(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin, new
        {
            rules = new[] { new { attributeId = "MARCA", valueIds = new[] { "ADIDAS", "Nike Air" } } }
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Respuesta con el mismo shape del GET, ya aplicado.
        Assert.Equal("bc", body.GetProperty("source").GetString());
        var manualRules = body.GetProperty("manualRules");
        Assert.Equal("marca", manualRules[0].GetProperty("attributeId").GetString());
        Assert.Equal("adidas", manualRules[0].GetProperty("valueIds")[0].GetString());
        Assert.Equal("nike-air", manualRules[0].GetProperty("valueIds")[1].GetString());

        // En BD: fila manual normalizada; la bc intacta.
        var rows = await RowsAsync("client", clientId);
        var manual = Assert.Single(rows, r => r.Source == "manual");
        Assert.Contains("\"marca\"", manual.RulesJson);
        Assert.Contains("\"adidas\"", manual.RulesJson);
        Assert.Contains("\"nike-air\"", manual.RulesJson);
        Assert.DoesNotContain("MARCA", manual.RulesJson);
        Assert.Equal(bcRules, Assert.Single(rows, r => r.Source == "bc").RulesJson);

        // rules: [] → la fila manual DESAPARECE; la bc sigue.
        var clear = await Send(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin,
            new { rules = Array.Empty<object>() });
        clear.EnsureSuccessStatusCode();
        var cleared = await clear.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("manualRules").ValueKind);

        rows = await RowsAsync("client", clientId);
        Assert.DoesNotContain(rows, r => r.Source == "manual");
        Assert.Equal(bcRules, Assert.Single(rows, r => r.Source == "bc").RulesJson);
    }

    // ── 3. PUT con basura → 400 con mensaje claro ──────────────────────────────

    [Fact]
    public async Task PutVisibility_Basura_400()
    {
        const string clientId = "VISAD3CL-0000-4000-9000-000000000003";
        var admin = await _factory.GetAdminTokenAsync(_client);

        // rules no es array
        var notArray = await SendJson(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin,
            """{"rules":{"attributeId":"marca"}}""");
        Assert.Equal(HttpStatusCode.BadRequest, notArray.StatusCode);

        // entrada sin attributeId
        var noAttribute = await SendJson(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin,
            """{"rules":[{"valueIds":["adidas"]}]}""");
        Assert.Equal(HttpStatusCode.BadRequest, noAttribute.StatusCode);

        // valueIds no es array
        var badValues = await SendJson(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin,
            """{"rules":[{"attributeId":"marca","valueIds":"adidas"}]}""");
        Assert.Equal(HttpStatusCode.BadRequest, badValues.StatusCode);

        // type desconocido → 400 (en GET y en PUT)
        var badTypePut = await SendJson(HttpMethod.Put, $"/api/admin/visibility/banana/{clientId}", admin,
            """{"rules":[]}""");
        Assert.Equal(HttpStatusCode.BadRequest, badTypePut.StatusCode);
        var badTypeGet = await Send(HttpMethod.Get, $"/api/admin/visibility/banana/{clientId}", admin);
        Assert.Equal(HttpStatusCode.BadRequest, badTypeGet.StatusCode);

        // Topes: más de 200 reglas o más de 500 valueIds en una regla → 400.
        var tooManyRules = await Send(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin, new
        {
            rules = Enumerable.Range(0, 201)
                .Select(i => new { attributeId = $"attr{i}", valueIds = new[] { "x" } }).ToArray()
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooManyRules.StatusCode);

        var tooManyValues = await Send(HttpMethod.Put, $"/api/admin/visibility/client/{clientId}", admin, new
        {
            rules = new[] { new { attributeId = "marca", valueIds = Enumerable.Range(0, 501).Select(i => $"v{i}").ToArray() } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooManyValues.StatusCode);

        // Nada se guardó por el camino.
        Assert.Empty(await RowsAsync("client", clientId));
    }

    // ── 3a. Resumen para los chips de las listas de /manage (UX-M6, 14a-5):
    // { client: {id: source}, agent: {id: source} } con la fuente EFECTIVA por sujeto ──

    [Fact]
    public async Task GetSummary_DevuelveSujetosConReglasYSuFuenteEfectiva()
    {
        const string onlyBc = "VISAD3AC-0000-4000-9000-000000000031";
        const string both = "VISAD3AB-0000-4000-9000-000000000032";
        const string onlyManualAgent = "VISAD3AA-0000-4000-9000-000000000033";
        await SeedVisibilityRowAsync("client", onlyBc, "bc", """[{"attributeId":"marca","valueIds":["a"]}]""");
        await SeedVisibilityRowAsync("client", both, "bc", """[{"attributeId":"marca","valueIds":["a"]}]""");
        await SeedVisibilityRowAsync("client", both, "manual", """[{"attributeId":"marca","valueIds":["b"]}]""");
        await SeedVisibilityRowAsync("agent", onlyManualAgent, "manual", """[{"attributeId":"marca","valueIds":["c"]}]""");

        var admin = await _factory.GetAdminTokenAsync(_client);
        var response = await Send(HttpMethod.Get, "/api/admin/visibility/summary", admin);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("bc", body.GetProperty("client").GetProperty(onlyBc).GetString());
        Assert.Equal("bc", body.GetProperty("client").GetProperty(both).GetString());
        Assert.Equal("manual", body.GetProperty("agent").GetProperty(onlyManualAgent).GetString());
        Assert.False(body.GetProperty("agent").TryGetProperty(onlyBc, out _));
    }

    // ── 3b. Borrar un cliente/agente desde /manage limpia sus filas de visibilidad
    // (14a-3): ni la bc ni la manual sobreviven al sujeto ──────────────────────

    [Theory]
    [InlineData("client", "/api/clients/")]
    [InlineData("agent", "/api/agents/")]
    public async Task DeleteSubject_LimpiaSusFilasDeVisibilidad(string type, string syncRoute)
    {
        var subjectId = $"VISAD3B{(type == "client" ? "C" : "A")}-0000-4000-9000-000000000099";
        var email = $"visad3b-{type}@sujeto.test";
        // Fila bc por el hook de ingesta real + manual sembrada aparte.
        await Put(syncRoute + subjectId,
            $$"""{"name":"Sujeto a borrar","email":"{{email}}","externalReference":"X1","visibleAttributes":[{"attributeId":"marca","valueIds":["adidas"]}] }""");
        await SeedVisibilityRowAsync(type, subjectId, "manual", """[{"attributeId":"marca","valueIds":["nike"]}]""");
        Assert.Equal(2, (await RowsAsync(type, subjectId)).Count);

        var admin = await _factory.GetAdminTokenAsync(_client);
        var response = await Send(HttpMethod.Delete, $"/api/admin/entities/{type}/{subjectId}", admin);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Empty(await RowsAsync(type, subjectId));
    }

    // ── 4. Config de la cinta: PUT + GET settings; null la limpia ──────────────

    [Fact]
    public async Task RibbonSettings_RoundTrip()
    {
        var admin = await _factory.GetAdminTokenAsync(_client);

        await SetRibbonAsync("""{"attributes":["marca"],"entries":[{"key":"family:calzado","order":1}]}""");

        var settings = await (await Send(HttpMethod.Get, "/api/admin/integration/settings", admin))
            .Content.ReadFromJsonAsync<JsonElement>();
        var ribbon = settings.GetProperty("catalogRibbon");
        Assert.Equal(JsonValueKind.Object, ribbon.ValueKind);
        Assert.Equal("marca", ribbon.GetProperty("attributes")[0].GetString());
        Assert.Equal("family:calzado", ribbon.GetProperty("entries")[0].GetProperty("key").GetString());

        // ribbon: null → desaparece.
        await SetRibbonAsync("null");
        settings = await (await Send(HttpMethod.Get, "/api/admin/integration/settings", admin))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("catalogRibbon").ValueKind);

        // Si viene y no es objeto → 400.
        var bad = await SendJson(HttpMethod.Put, "/api/admin/integration/ribbon", admin,
            """{"ribbon":[1,2]}""");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ── 5. /api/shop/ribbon sin config: solo las familias VISIBLES del actor ───

    [Fact]
    public async Task ShopRibbon_AutogeneradaSoloFamiliasVisibles()
    {
        const string clientId = "VISAD5CL-0000-4000-9000-000000000005";
        const string visible = "visad5a0-0000-4000-9000-000000000006";
        const string hidden = "visad5b0-0000-4000-9000-000000000007";

        // Sin config: la cinta se autogenera con las familias. La marca y las familias
        // son únicas de esta prueba (la BD de la fixture es compartida): la restricción
        // por marca deja visible solo el modelo de la familia visad5calz.
        await SetRibbonAsync("null");
        await PutModel(visible, "VISAD5 ADIDAS", "VA5-A-REF", "visad5calz", """{"Marca":"VISAD5ADIDAS"}""");
        await PutModel(hidden, "VISAD5 NIKE", "VA5-B-REF", "visad5ropa", """{"Marca":"VISAD5NIKE"}""");
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["visad5adidas"]}]""");
        var token = await ClientTokenAsync(clientId);

        var response = await Send(HttpMethod.Get, "/api/shop/ribbon", token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        var entry = Assert.Single(entries);
        Assert.Equal("family:visad5calz", entry.GetProperty("key").GetString());
        Assert.Equal("family", entry.GetProperty("kind").GetString());
        Assert.Equal(1, entry.GetProperty("count").GetInt32());
        Assert.DoesNotContain(entries, e => (e.GetProperty("key").GetString() ?? "").Contains("visad5ropa"));
    }

    // ── 6. /api/shop/ribbon con config: atributos + orden + títulos + hidden ───

    [Fact]
    public async Task ShopRibbon_ConConfig_AtributosOrdenTitulos()
    {
        const string clientId = "VISAD6CL-0000-4000-9000-000000000008";
        const string alfa = "visad6a0-0000-4000-9000-000000000009";
        const string beta = "visad6b0-0000-4000-9000-000000000010";
        const string gamma = "visad6c0-0000-4000-9000-000000000011";

        // Atributo y familia únicos de esta prueba. El cliente ve alfa y beta; gamma NO.
        await PutModel(alfa, "VISAD6 ALFA", "VA6-A-REF", "visad6fam", """{"MarcaR6":"ALFA"}""");
        await PutModel(beta, "VISAD6 BETA", "VA6-B-REF", "visad6fam", """{"MarcaR6":"BETA"}""");
        await PutModel(gamma, "VISAD6 GAMMA", "VA6-C-REF", "visad6fam", """{"MarcaR6":"GAMMA"}""");
        await PutClientVisibility(clientId, """[{"attributeId":"marcar6","valueIds":["alfa","beta"]}]""");
        var token = await ClientTokenAsync(clientId);

        // Limpieza garantizada: la config de la cinta es global (IntegrationSettings) y no
        // debe contaminar a las demás pruebas ni aunque los asserts fallen.
        try
        {
            await SetRibbonAsync("""
                {"attributes":["marcar6"],
                 "entries":[{"key":"attr:marcar6:beta","order":1,"titles":{"en":"Beta EN"}},
                            {"key":"attr:marcar6:alfa","order":2},
                            {"key":"family:visad6fam","hidden":true}]}
                """);

            var response = await Send(HttpMethod.Get, "/api/shop/ribbon?locale=en", token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            var entries = body.GetProperty("entries").EnumerateArray().ToList();
            Assert.Equal(2, entries.Count);

            // Orden respetado (order 1 y 2) y título del locale pedido.
            Assert.Equal("attr:marcar6:beta", entries[0].GetProperty("key").GetString());
            Assert.Equal("attr", entries[0].GetProperty("kind").GetString());
            Assert.Equal("marcar6", entries[0].GetProperty("attributeId").GetString());
            Assert.Equal("beta", entries[0].GetProperty("value").GetString());
            Assert.Equal("Beta EN", entries[0].GetProperty("label").GetString());
            Assert.Equal(1, entries[0].GetProperty("count").GetInt32());
            // `raw` = el valor CRUDO de BC con el que filtra a.{clave}=; el override
            // de título ("Beta EN") NO lo toca — es dato de filtro, no etiqueta.
            Assert.Equal("BETA", entries[0].GetProperty("raw").GetString());

            // Sin título configurado → label de la faceta (el valor tal cual llega de BC).
            Assert.Equal("attr:marcar6:alfa", entries[1].GetProperty("key").GetString());
            Assert.Equal("ALFA", entries[1].GetProperty("label").GetString());
            Assert.Equal("ALFA", entries[1].GetProperty("raw").GetString());

            // GAMMA (fuera del scope) JAMÁS aparece; la familia hidden tampoco.
            Assert.DoesNotContain(entries, e => (e.GetProperty("key").GetString() ?? "").Contains("gamma"));
            Assert.DoesNotContain(entries, e => (e.GetProperty("key").GetString() ?? "").StartsWith("family:"));
        }
        finally { await SetRibbonAsync("null"); }
    }

    // ── 6b. La cinta viaja DENTRO de /api/shop/catalog (14a-4): misma forma que
    // /api/shop/ribbon, computada con las facetas ya filtradas por el scope, más
    // `restricted` para que el front avise "Catálogo adaptado a tu cuenta" ──────

    [Fact]
    public async Task ShopCatalog_TraeRibbonFiltradaYRestricted()
    {
        const string restrictedClient = "VISAD6BC-0000-4000-9000-000000000021";
        const string openClient = "VISAD6BO-0000-4000-9000-000000000022";
        const string visible = "visad6ba-0000-4000-9000-000000000023";
        const string hidden = "visad6bb-0000-4000-9000-000000000024";

        await PutModel(visible, "VISAD6B ADIDAS", "VA6B-A-REF", "visad6bcalz", """{"MarcaR6B":"VISAD6BADIDAS"}""");
        await PutModel(hidden, "VISAD6B NIKE", "VA6B-B-REF", "visad6bropa", """{"MarcaR6B":"VISAD6BNIKE"}""");
        await PutClientVisibility(restrictedClient, """[{"attributeId":"marcar6b","valueIds":["visad6badidas"]}]""");
        await Put($"/api/clients/{openClient}", """{"name":"Cliente abierto"}""");
        var restrictedToken = await ClientTokenAsync(restrictedClient);
        var openToken = await ClientTokenAsync(openClient);

        try
        {
            await SetRibbonAsync("""{"attributes":["marcar6b"]}""");

            var response = await Send(HttpMethod.Get, "/api/shop/catalog?q=VISAD6B", restrictedToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.True(body.GetProperty("restricted").GetBoolean());
            var entries = body.GetProperty("ribbon").GetProperty("entries").EnumerateArray().ToList();
            var keys = entries.Select(e => e.GetProperty("key").GetString()).ToList();
            Assert.Contains("family:visad6bcalz", keys);
            Assert.Contains("attr:marcar6b:visad6badidas", keys);
            Assert.DoesNotContain(keys, k => k!.Contains("visad6bropa") || k.Contains("nike"));
            // Misma forma que /api/shop/ribbon.
            var attr = entries.Single(e => e.GetProperty("key").GetString() == "attr:marcar6b:visad6badidas");
            Assert.Equal("attr", attr.GetProperty("kind").GetString());
            Assert.Equal("marcar6b", attr.GetProperty("attributeId").GetString());
            Assert.Equal("VISAD6BADIDAS", attr.GetProperty("raw").GetString());
            Assert.Equal(1, attr.GetProperty("count").GetInt32());

            var open = await (await Send(HttpMethod.Get, "/api/shop/catalog?q=VISAD6B", openToken))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(open.GetProperty("restricted").GetBoolean());
            var openKeys = open.GetProperty("ribbon").GetProperty("entries").EnumerateArray()
                .Select(e => e.GetProperty("key").GetString()).ToList();
            Assert.Contains("family:visad6bropa", openKeys);
            Assert.Contains("attr:marcar6b:visad6bnike", openKeys);
        }
        finally { await SetRibbonAsync("null"); }
    }

    // ── 6c. La cinta es NAVEGACIÓN y es ESTABLE (14a-8): sus entradas y recuentos son
    // los del surtido del actor y NO cambian al filtrar por familia/atributo ni al
    // buscar — solo la visibilidad del actor la cambia ──────────────────────────

    [Fact]
    public async Task ShopCatalog_RibbonEstable_NoCambiaConLosFiltrosDeQuery()
    {
        const string clientId = "VISAD6CC-0000-4000-9000-000000000031";
        const string one = "visad6ca-0000-4000-9000-000000000032";
        const string two = "visad6cb-0000-4000-9000-000000000033";

        await PutModel(one, "VISAD6C UNO", "VA6C-A-REF", "visad6cfam1", """{"MarcaR6C":"UNO"}""");
        await PutModel(two, "VISAD6C DOS", "VA6C-B-REF", "visad6cfam2", """{"MarcaR6C":"DOS"}""");
        await Put($"/api/clients/{clientId}", """{"name":"Cliente cinta estable"}""");
        var token = await ClientTokenAsync(clientId);

        try
        {
            await SetRibbonAsync("""{"attributes":["marcar6c"]}""");

            async Task<string> RibbonAsync(string query)
            {
                var body = await (await Send(HttpMethod.Get, "/api/shop/catalog" + query, token))
                    .Content.ReadFromJsonAsync<JsonElement>();
                return body.GetProperty("ribbon").GetProperty("entries").GetRawText();
            }

            var baseline = await RibbonAsync("");
            Assert.Contains("attr:marcar6c:uno", baseline);
            Assert.Contains("attr:marcar6c:dos", baseline);
            Assert.Contains("family:visad6cfam1", baseline);

            // Con búsqueda, familia o atributo la cinta es IDÉNTICA (entradas y recuentos).
            Assert.Equal(baseline, await RibbonAsync("?q=VISAD6C%20UNO"));
            Assert.Equal(baseline, await RibbonAsync("?family=visad6cfam2"));
            Assert.Equal(baseline, await RibbonAsync("?a.MarcaR6C=DOS"));
            Assert.Equal(baseline, await RibbonAsync("?q=nada-que-coincida-xyz"));

            // Y coincide con la del endpoint autónomo (/manage).
            var standalone = await (await Send(HttpMethod.Get, "/api/shop/ribbon", token))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(baseline, standalone.GetProperty("entries").GetRawText());
        }
        finally { await SetRibbonAsync("null"); }
    }

    // ── 7. Config con entries basura: el endpoint no revienta y aplica lo válido ──
    // (fix de revisión: un elemento de entries que no sea objeto —"oops", 5— hacía
    // saltar InvalidOperationException al indexarlo → 500 para TODOS los actores).

    [Fact]
    public async Task ShopRibbon_ConfigConEntriesBasura_NoRevienta()
    {
        const string clientId = "VISAD7CL-0000-4000-9000-000000000012";
        const string one = "visad7a0-0000-4000-9000-000000000013";
        const string two = "visad7b0-0000-4000-9000-000000000014";

        await PutModel(one, "VISAD7 UNO", "VA7-A-REF", "visad7fam", """{"Marca":"VISAD7X"}""");
        await PutModel(two, "VISAD7 DOS", "VA7-B-REF", "visad7otra", """{"Marca":"VISAD7X"}""");
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["visad7x"]}]""");
        var token = await ClientTokenAsync(clientId);

        try
        {
            // Dos elementos basura y un override válido: 200 y solo el válido aplica.
            await SetRibbonAsync("""{"entries":["oops",5,{"key":"family:visad7fam","hidden":true}]}""");

            var response = await Send(HttpMethod.Get, "/api/shop/ribbon", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            var keys = body.GetProperty("entries").EnumerateArray()
                .Select(e => e.GetProperty("key").GetString()).ToList();
            Assert.Contains("family:visad7otra", keys);
            Assert.DoesNotContain("family:visad7fam", keys);
        }
        finally { await SetRibbonAsync("null"); }
    }
}
