using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Fase 1 del portal: la portada se configura desde el CMS. El bloque de contenido
// vive en portal_content (Key + Locale + jsonb) y se edita con /api/admin/content;
// el portal solo ve los elementos activos y dentro de su ventana de publicación.
public class PortalContentTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PortalContentTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? json = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private static string Banner(string image, string title = "", string extra = "") => $$"""
        { "imageUrl": "{{image}}", "title": "{{title}}" {{(extra.Length > 0 ? "," + extra : "")}} }
        """;

    private static string Block(params string[] items) => $$"""{ "items": [ {{string.Join(",", items)}} ] }""";

    // ───────────────────────── CRUD del bloque ─────────────────────────

    [Theory]
    [InlineData("GET", "/api/admin/content")]
    [InlineData("GET", "/api/admin/content/dashboard.hero")]
    [InlineData("PUT", "/api/admin/content/dashboard.hero")]
    [InlineData("DELETE", "/api/admin/content/dashboard.hero")]
    public async Task Admin_SinToken_Devuelve401(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "PUT")
            request.Content = new StringContent(Block(), Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_PutYGet_GuardaElBloqueNormalizado()
    {
        var put = await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.hero?locale=es", Block(
            Banner("/media/portal/uno.png", "Nueva temporada", """ "subtitle":"SS26", "ctaText":"Ver", "ctaHref":"/es/es/catalog/catalog" """),
            Banner("https://cdn.lejan.test/dos.jpg")));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var response = await SendAsync(HttpMethod.Get, "/api/admin/content/dashboard.hero?locale=es");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("dashboard.hero", body.GetProperty("key").GetString());
        Assert.Equal("es", body.GetProperty("locale").GetString());
        Assert.Equal(TestWebApplicationFactory.AdminEmail, body.GetProperty("updatedBy").GetString());
        Assert.True(body.GetProperty("updatedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        // Normalización: id generado, orden por posición y activo por defecto
        Assert.False(string.IsNullOrWhiteSpace(items[0].GetProperty("id").GetString()));
        Assert.NotEqual(items[0].GetProperty("id").GetString(), items[1].GetProperty("id").GetString());
        Assert.Equal(0, items[0].GetProperty("order").GetInt32());
        Assert.Equal(1, items[1].GetProperty("order").GetInt32());
        Assert.True(items[0].GetProperty("active").GetBoolean());
        Assert.Equal("Nueva temporada", items[0].GetProperty("title").GetString());
        Assert.Equal("SS26", items[0].GetProperty("subtitle").GetString());
        Assert.Equal("/es/es/catalog/catalog", items[0].GetProperty("ctaHref").GetString());
        Assert.Equal("https://cdn.lejan.test/dos.jpg", items[1].GetProperty("imageUrl").GetString());
    }

    [Fact]
    public async Task Admin_Put_SobreescribeElBloqueSinDuplicarlo()
    {
        await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.hero?locale=en", Block(Banner("/media/portal/old.png")));
        await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.hero?locale=en", Block(Banner("/media/portal/new.png")));

        var body = await (await SendAsync(HttpMethod.Get, "/api/admin/content/dashboard.hero?locale=en"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("/media/portal/new.png", item.GetProperty("imageUrl").GetString());

        var list = await (await SendAsync(HttpMethod.Get, "/api/admin/content")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(list.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("key").GetString() == "dashboard.hero"
              && i.GetProperty("locale").GetString() == "en");
    }

    [Fact]
    public async Task Admin_Listado_DeclaraLasClavesYLosBloquesGuardados()
    {
        await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.tiles?locale=fr", Block(
            Banner("/media/portal/repo.png", "Réapprovisionnement", """ "window":"replenishment" """)));

        var body = await (await SendAsync(HttpMethod.Get, "/api/admin/content")).Content.ReadFromJsonAsync<JsonElement>();

        var keys = body.GetProperty("keys").EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Contains("dashboard.hero", keys);
        Assert.Contains("dashboard.tiles", keys);

        var block = Assert.Single(body.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("key").GetString() == "dashboard.tiles"
              && i.GetProperty("locale").GetString() == "fr");
        Assert.Equal(1, block.GetProperty("count").GetInt32());
        Assert.Equal(TestWebApplicationFactory.AdminEmail, block.GetProperty("updatedBy").GetString());
    }

    [Fact]
    public async Task Admin_Delete_EliminaElBloque()
    {
        await SendAsync(HttpMethod.Put, "/api/admin/content/login.background?locale=it", Block(Banner("/media/portal/login.png")));

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(HttpMethod.Delete, "/api/admin/content/login.background?locale=it")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Get, "/api/admin/content/login.background?locale=it")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Delete, "/api/admin/content/login.background?locale=it")).StatusCode);
    }

    // ───────────────────────── Validación del payload ─────────────────────────

    [Fact]
    public async Task Admin_ClaveDesconocida_Devuelve400()
    {
        var response = await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.inventado?locale=es",
            Block(Banner("/media/portal/uno.png")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_LocaleNoSoportado_Devuelve400()
    {
        var response = await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.hero?locale=de",
            Block(Banner("/media/portal/uno.png")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    // items ausente o de tipo equivocado
    [InlineData("""{ "titulo": "sin items" }""")]
    [InlineData("""{ "items": "no soy una lista" }""")]
    [InlineData("""{ "items": [ "no soy un objeto" ] }""")]
    // la imagen es obligatoria en un banner
    [InlineData("""{ "items": [ { "title": "sin imagen" } ] }""")]
    [InlineData("""{ "items": [ { "imageUrl": "   " } ] }""")]
    // rutas que no son una URL de imagen servible
    [InlineData("""{ "items": [ { "imageUrl": "javascript:alert(1)" } ] }""")]
    [InlineData("""{ "items": [ { "imageUrl": "media/portal/relativa.png" } ] }""")]
    // enlaces con esquema peligroso
    [InlineData("""{ "items": [ { "imageUrl": "/media/portal/a.png", "ctaHref": "javascript:alert(1)" } ] }""")]
    // fechas de campaña ilegibles
    [InlineData("""{ "items": [ { "imageUrl": "/media/portal/a.png", "publishFrom": "el martes" } ] }""")]
    [InlineData("""{ "items": [ { "imageUrl": "/media/portal/a.png", "publishTo": "32/13/2026" } ] }""")]
    // tipos equivocados en los campos normalizados
    [InlineData("""{ "items": [ { "imageUrl": "/media/portal/a.png", "active": "sí" } ] }""")]
    [InlineData("""{ "items": [ { "imageUrl": "/media/portal/a.png", "title": 7 } ] }""")]
    public async Task Admin_PayloadInvalido_Devuelve400(string json)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.hero?locale=it", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
        // Un payload inválido no deja rastro
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Get, "/api/admin/content/dashboard.hero?locale=it")).StatusCode);
    }

    [Fact]
    public async Task Admin_TarjetaConVentanaDeServicioInvalida_Devuelve400()
    {
        var response = await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.tiles?locale=it",
            Block(Banner("/media/portal/a.png", "Tarjeta", """ "window":"outlet" """)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ───────────────────────── Lectura desde el portal ─────────────────────────

    [Fact]
    public async Task Portal_SinToken_Devuelve401()
    {
        var response = await _client.GetAsync("/api/portal/content/dashboard.hero?locale=es");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Portal_SoloDevuelveElementosActivosYEnVentanaDePublicacion()
    {
        var ayer = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var manana = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var haceUnMes = DateTimeOffset.UtcNow.AddDays(-30).ToString("O");

        await SendAsync(HttpMethod.Put, "/api/admin/content/dashboard.hero?locale=fr", Block(
            Banner("/media/portal/vigente.png", "Vigente", $""" "order": 2, "publishFrom":"{haceUnMes}", "publishTo":"{manana}" """),
            Banner("/media/portal/caducado.png", "Caducado", $""" "publishTo":"{ayer}" """),
            Banner("/media/portal/futuro.png", "Futuro", $""" "publishFrom":"{manana}" """),
            Banner("/media/portal/apagado.png", "Apagado", """ "active": false """),
            Banner("/media/portal/primero.png", "Primero", """ "order": 1 """)));

        var body = await (await SendAsync(HttpMethod.Get, "/api/portal/content/dashboard.hero?locale=fr"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var titulos = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("title").GetString()).ToList();

        Assert.Equal(["Primero", "Vigente"], titulos);

        // El CMS sigue viendo los cinco, incluido el caducado
        var admin = await (await SendAsync(HttpMethod.Get, "/api/admin/content/dashboard.hero?locale=fr"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, admin.GetProperty("items").GetArrayLength());
        Assert.Contains("Caducado", admin.GetRawText());
    }

    [Fact]
    public async Task Portal_SinTraduccion_CaeAlContenidoComun()
    {
        await SendAsync(HttpMethod.Put, "/api/admin/content/footer.social?locale=*", Block(Banner("/media/portal/comun.png", "Común")));

        var body = await (await SendAsync(HttpMethod.Get, "/api/portal/content/footer.social?locale=it"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("*", body.GetProperty("locale").GetString());
        Assert.Equal("Común", Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Portal_ClaveSinContenido_DevuelveListaVacia()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/portal/content/login.background?locale=en");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Portal_ClaveDesconocida_Devuelve400()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/portal/content/lo.que.sea?locale=es");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

// La portada de demostración se siembra al arrancar cuando la tabla está vacía, para
// que /es/es/dashboard se vea con imágenes reales sin pasar por el CMS.
public class PortalContentSeedTests : IClassFixture<PortalContentSeedTests.SeedingFactory>
{
    public sealed class SeedingFactory : TestWebApplicationFactory
    {
        protected override bool SeedPortalContent => true;
    }

    private readonly SeedingFactory _factory;
    private readonly HttpClient _client;

    public PortalContentSeedTests(SeedingFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<JsonElement> GetAsync(string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task PortadaDeDemo_SeSiembraConCarruselYDosTarjetas()
    {
        var hero = await GetAsync("/api/portal/content/dashboard.hero?locale=es");
        var tiles = await GetAsync("/api/portal/content/dashboard.tiles?locale=es");

        Assert.NotEmpty(hero.GetProperty("items").EnumerateArray());
        var windows = tiles.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("window").GetString()).ToList();
        Assert.Equal(["replenishment", "scheduled"], windows);
    }

    [Fact]
    public async Task PortadaDeDemo_ApuntaAImagenesServidasPorElPropioPortal()
    {
        var hero = await GetAsync("/api/portal/content/dashboard.hero?locale=es");
        var tiles = await GetAsync("/api/portal/content/dashboard.tiles?locale=es");

        var urls = hero.GetProperty("items").EnumerateArray()
            .Concat(tiles.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("imageUrl").GetString()!)
            .ToList();

        Assert.NotEmpty(urls);
        foreach (var url in urls)
        {
            Assert.StartsWith("/media/portal/", url);
            var image = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        }
    }
}
