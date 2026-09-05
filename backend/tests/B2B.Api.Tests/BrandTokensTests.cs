using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Marca configurable EXTENDIDA (theming multi-cliente, fase 2): los tokens de diseño de la
// instancia viajan en `tokens` por PUT /api/admin/integration/branding y los publican
// GET /api/portal/branding (público) y GET /api/admin/integration/settings (brandTokens).
//
// REGLA DE ORO de la fase: SIN tokens el portal debe quedar EXACTAMENTE como el de MITO
// PROJECTS, así que la primera prueba clava la forma del branding público sin tokens.
public class BrandTokensTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BrandTokensTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Juego completo de tokens: el de ALMA EN PENA (blanco/negro, Gill Sans, píldoras).
    private const string AlmaTokens = """
        {
          "logoUrlDark": "/media/alma-logo-dark.svg",
          "faviconUrl": "/media/alma-favicon.png",
          "fontUrl": "/media/GillSansMTLight.woff2",
          "fontFamily": "GillSansMTLight",
          "caps": true,
          "tracking": ".06em",
          "radius": "12px",
          "radiusButton": "50px",
          "paper": "#FFFFFF",
          "surface": "#F5F5F5",
          "ink": "#111111",
          "headerBg": "#000000",
          "headerInk": "#ffffff",
          "heroFilter": "none",
          "tagline": "Bienvenido a ALMA EN PENA",
          "supportEmail": "soporte@almaenpena.com"
        }
        """;

    // Los cuatro tokens de la EXTENSIÓN (BLOCCO 5): filete de capítulo (color y grosor),
    // fondo de paneles y segundo acento. Van APARTE de AlmaTokens a propósito: el juego de
    // ALMA tiene que seguir siendo el de una instancia que NO los usa (ver prueba 13).
    private const string Blocco5Tokens = """
        {"card": "#F0EFED", "rule": "#E70917", "ruleWidth": "1px", "accent": "#e70917"}
        """;

    // Los tres de la RONDA 1 de crítica de BLOCCO 5: composición del hero (lista cerrada),
    // peso de los titulares (centena) y texto legal del login. También aparte: ni ALMA ni
    // el juego anterior de BLOCCO los traen, y ambos tienen que normalizarse igual que antes.
    private const string Blocco5Round1Tokens = """
        {"heroStyle": "PAPER", "displayWeight": "900",
         "legal": "BLOCCO 5 vende exclusivamente a distribuidores y profesionales del sector. Cuéntanos quién eres y en 24 h laborables tendrás tu acceso."}
        """;

    // ── Utilidades ─────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PutBranding(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/integration/branding")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    /// PUT de marca con nombre/color/logo NEUTROS (los de MITO) y los tokens dados.
    private Task<HttpResponseMessage> PutTokens(string tokensJson) =>
        PutBranding($$"""{"name":null,"color":null,"logoUrl":null,"tokens":{{tokensJson}} }""");

    private async Task<JsonElement> PublicBrandingAsync() =>
        await (await _client.GetAsync("/api/portal/branding")).Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> PublicTokensAsync() =>
        (await PublicBrandingAsync()).GetProperty("tokens");

    private async Task<JsonElement> AdminSettingsAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/integration/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// Cada prueba parte de "sin marca y sin tokens" (la fila de settings es un singleton
    /// que comparten todas las pruebas de la clase).
    private async Task ResetAsync() => (await PutTokens("null")).EnsureSuccessStatusCode();

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("error").GetString() ?? "";
    }

    // ── 1. Regresión de MITO: sin tokens el branding público no cambia ─────────

    [Fact]
    public async Task BrandingPublico_SinTokens_ConservaLaFormaDeMito()
    {
        await ResetAsync();

        var body = await PublicBrandingAsync();

        Assert.Equal("MITO PROJECTS", body.GetProperty("name").GetString());
        Assert.Equal("#ec3013", body.GetProperty("color").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("logoUrl").ValueKind);
        // `tokens` va SIEMPRE presente y vale null mientras no haya theming: el front lee
        // null y deja intactos los valores por defecto de app.css.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("tokens").ValueKind);
        // Y no aparece ninguna propiedad más que las cuatro del contrato.
        Assert.Equal(
            new[] { "color", "logoUrl", "name", "tokens" },
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // El GET de administración tampoco se inventa tokens.
        Assert.Equal(JsonValueKind.Null, (await AdminSettingsAsync()).GetProperty("brandTokens").ValueKind);
    }

    // ── 2. Round-trip completo: se guardan y se publican tal cual ──────────────

    [Fact]
    public async Task Tokens_RoundTrip_GuardadoYLectura()
    {
        await ResetAsync();

        var saved = await PutTokens(AlmaTokens);
        saved.EnsureSuccessStatusCode();
        var echo = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens");
        Assert.Equal("GillSansMTLight", echo.GetProperty("fontFamily").GetString());

        var tokens = await PublicTokensAsync();
        Assert.Equal(JsonValueKind.Object, tokens.ValueKind);
        Assert.Equal("/media/alma-logo-dark.svg", tokens.GetProperty("logoUrlDark").GetString());
        Assert.Equal("/media/alma-favicon.png", tokens.GetProperty("faviconUrl").GetString());
        Assert.Equal("/media/GillSansMTLight.woff2", tokens.GetProperty("fontUrl").GetString());
        Assert.Equal("GillSansMTLight", tokens.GetProperty("fontFamily").GetString());
        Assert.True(tokens.GetProperty("caps").GetBoolean());
        Assert.Equal(".06em", tokens.GetProperty("tracking").GetString());
        Assert.Equal("12px", tokens.GetProperty("radius").GetString());
        Assert.Equal("50px", tokens.GetProperty("radiusButton").GetString());
        Assert.Equal("#ffffff", tokens.GetProperty("paper").GetString());      // colores en minúsculas
        Assert.Equal("#f5f5f5", tokens.GetProperty("surface").GetString());
        Assert.Equal("#111111", tokens.GetProperty("ink").GetString());
        Assert.Equal("#000000", tokens.GetProperty("headerBg").GetString());
        Assert.Equal("#ffffff", tokens.GetProperty("headerInk").GetString());
        Assert.Equal("none", tokens.GetProperty("heroFilter").GetString());
        Assert.Equal("Bienvenido a ALMA EN PENA", tokens.GetProperty("tagline").GetString());
        Assert.Equal("soporte@almaenpena.com", tokens.GetProperty("supportEmail").GetString());

        // El back-office los lee por su GET de settings (para el editor de Marca).
        var admin = (await AdminSettingsAsync()).GetProperty("brandTokens");
        Assert.Equal("50px", admin.GetProperty("radiusButton").GetString());

        // caps admite también el false explícito (theming que apaga las mayúsculas).
        (await PutTokens("""{"caps":false}""")).EnsureSuccessStatusCode();
        Assert.False((await PublicTokensAsync()).GetProperty("caps").GetBoolean());

        // Los cuatro de la extensión: los colores salen en minúsculas y la medida tal cual.
        (await PutTokens(Blocco5Tokens)).EnsureSuccessStatusCode();
        var blocco = await PublicTokensAsync();
        Assert.Equal("#f0efed", blocco.GetProperty("card").GetString());
        Assert.Equal("#e70917", blocco.GetProperty("rule").GetString());
        Assert.Equal("1px", blocco.GetProperty("ruleWidth").GetString());
        Assert.Equal("#e70917", blocco.GetProperty("accent").GetString());

        // Los tres de la ronda 1: heroStyle se publica en minúsculas (es el valor literal del
        // selector html[data-hero-style="paper"]); el peso y el legal, tal cual.
        (await PutTokens(Blocco5Round1Tokens)).EnsureSuccessStatusCode();
        var round1 = await PublicTokensAsync();
        Assert.Equal("paper", round1.GetProperty("heroStyle").GetString());
        Assert.Equal("900", round1.GetProperty("displayWeight").GetString());
        Assert.StartsWith("BLOCCO 5 vende exclusivamente", round1.GetProperty("legal").GetString());
        Assert.Equal(new[] { "heroStyle", "displayWeight", "legal" },
            round1.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("900", (await AdminSettingsAsync()).GetProperty("brandTokens").GetProperty("displayWeight").GetString());
    }

    // ── 3. Tokens desconocidos: se ignoran EN SILENCIO ─────────────────────────

    [Fact]
    public async Task Tokens_Desconocidos_SeIgnoranSinError()
    {
        await ResetAsync();

        (await PutTokens("""
            {"radius":"12px","futuro":"lo que sea","brandColor":"#123456","caps2":true,"__proto__":"x"}
            """)).EnsureSuccessStatusCode();

        var tokens = await PublicTokensAsync();
        Assert.Equal(new[] { "radius" }, tokens.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("12px", tokens.GetProperty("radius").GetString());

        // Un objeto ENTERO de tokens desconocidos no configura nada → equivale a limpiar.
        (await PutTokens("""{"loQueSea":1,"otro":"x"}""")).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // ── 4. null, {}, valores vacíos y la ausencia del campo: todo limpia ───────

    [Fact]
    public async Task Tokens_NullVacioYAusente_Limpian()
    {
        foreach (var payload in new[] { "null", "{}", """{"radius":"","tagline":"   ","paper":null}""" })
        {
            (await PutTokens(AlmaTokens)).EnsureSuccessStatusCode();
            Assert.Equal(JsonValueKind.Object, (await PublicTokensAsync()).ValueKind);

            (await PutTokens(payload)).EnsureSuccessStatusCode();
            Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
        }

        // Sin la propiedad `tokens`, el PUT de marca sigue siendo un reemplazo completo
        // (igual que name/color/logoUrl): también limpia.
        (await PutTokens(AlmaTokens)).EnsureSuccessStatusCode();
        (await PutBranding("""{"name":null,"color":null,"logoUrl":null}""")).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // ── 5. Colores: solo #rrggbb ───────────────────────────────────────────────

    [Theory]
    [InlineData("paper", "rojo")]
    [InlineData("surface", "#fff")]
    [InlineData("ink", "#GGGGGG")]
    [InlineData("headerBg", "#12345")]
    [InlineData("headerInk", "rgb(0,0,0)")]
    [InlineData("card", "rojo")]
    [InlineData("rule", "rgb(0,0,0)")]
    [InlineData("accent", "#GGGGGG")]
    public async Task Tokens_ColorInvalido_400(string token, string value)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"{{token}}":"{{value}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ErrorAsync(response);
        Assert.Contains(token, error);
        Assert.Contains("#rrggbb", error);
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // ── 6. Tipos: cada token tiene el suyo ─────────────────────────────────────

    [Theory]
    [InlineData("""{"caps":"true"}""")]        // booleano, no cadena
    [InlineData("""{"caps":1}""")]
    [InlineData("""{"tagline":123}""")]        // cadena, no número
    [InlineData("""{"radius":12}""")]
    [InlineData("""{"paper":{"hex":"#fff"}}""")]
    [InlineData("""{"fontFamily":["a","b"]}""")]
    [InlineData("""{"displayWeight":900}""")]  // cadena "900", no número
    [InlineData("""{"heroStyle":true}""")]
    [InlineData("""{"legal":["x"]}""")]
    public async Task Tokens_TipoInvalido_400(string tokensJson)
    {
        await ResetAsync();

        var response = await PutTokens(tokensJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await ErrorAsync(response));
    }

    [Fact]
    public async Task Tokens_NoObjeto_400()
    {
        await ResetAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PutTokens("[1,2]")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutTokens("\"gris\"")).StatusCode);
    }

    // ── 7. URLs: nada de javascript: ni data: (inyección) ──────────────────────

    [Theory]
    [InlineData("logoUrlDark", "javascript:alert(1)")]
    [InlineData("faviconUrl", "JavaScript:alert(1)")]
    [InlineData("fontUrl", "data:text/css;base64,Ym9keXtkaXNwbGF5Om5vbmV9")]
    [InlineData("logoUrlDark", "  javascript:alert(1)")]
    [InlineData("faviconUrl", "java\tscript:alert(1)")]
    public async Task Tokens_UrlPeligrosa_400(string token, string value)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"{{token}}":{{JsonSerializer.Serialize(value)}} }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(token, await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // El esquema no basta: estas URLs se interpolan dentro del url("…") de un @font-face
    // y de atributos src/href, así que los metacaracteres que cierran esos contextos son
    // inyección. Los cuatro casos devolvían 200 y quedaban publicados.
    [Theory]
    [InlineData("fontUrl", "/a.woff2\") format(\"woff2\")} body{display:none} @x{a:url(\"b")]
    [InlineData("logoUrlDark", "x\" onerror=\"alert(1)")]
    [InlineData("faviconUrl", "/a.png><script>alert(1)</script>")]
    [InlineData("logoUrlDark", "/media/mi logo (1).svg")]   // el portal lo descartaba en silencio
    public async Task Tokens_UrlConMetacaracteres_400(string token, string value)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"{{token}}":{{JsonSerializer.Serialize(value)}} }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(token, await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    [Fact]
    public async Task Tokens_UrlDemasiadoLarga_400()
    {
        await ResetAsync();

        var url = "/media/" + new string('a', 600) + ".woff2";
        var response = await PutTokens($$"""{"fontUrl":"{{url}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("fontUrl", await ErrorAsync(response));
    }

    // ── 8. Medidas: número + unidad (px, rem, em, %) ───────────────────────────

    [Theory]
    [InlineData("tracking", ".06")]            // sin unidad
    [InlineData("tracking", "6pt")]            // unidad no admitida
    [InlineData("radius", "12 px")]
    [InlineData("radius", "12px;color:red")]
    [InlineData("radiusButton", "calc(50px)")]
    [InlineData("radius", "..px")]             // pasaban la regex vieja y dejaban el portal
    [InlineData("radius", ".px")]              // SIN radios (var() inválida → esquinas a 0)
    [InlineData("radiusButton", "1.2.3px")]
    [InlineData("tracking", "-.em")]
    [InlineData("ruleWidth", "1")]             // sin unidad
    [InlineData("ruleWidth", "calc(1px)")]
    public async Task Tokens_MedidaInvalida_400(string token, string value)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"{{token}}":"{{value}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(token, await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    [Theory]
    [InlineData("tracking", ".06em")]
    [InlineData("tracking", "-0.5px")]
    [InlineData("radius", "0.75rem")]
    [InlineData("radiusButton", "50%")]
    public async Task Tokens_MedidaValida_SeGuarda(string token, string value)
    {
        await ResetAsync();

        (await PutTokens($$"""{"{{token}}":"{{value}}"}""")).EnsureSuccessStatusCode();

        Assert.Equal(value, (await PublicTokensAsync()).GetProperty(token).GetString());
    }

    // ── 9. heroFilter y fontFamily: no pueden inyectar CSS ─────────────────────

    [Theory]
    [InlineData("""{"heroFilter":"grayscale(1);background:url(http://x)"}""")]
    [InlineData("""{"heroFilter":"none} body { display:none"}""")]
    [InlineData("""{"heroFilter":"url(http://malo/x.png)"}""")]
    [InlineData("""{"heroFilter":"URL( http://malo/x.png )"}""")]
    [InlineData("""{"fontFamily":"Gill; background:red"}""")]
    [InlineData("""{"fontFamily":"Gill} body {"}""")]
    // En CSS «\75» es una «u»: «\75rl(…)» era un url() válido que se colaba entero, con su
    // petición a un dominio ajeno desde el portal público. La barra suelta escapa el
    // carácter siguiente y /*…*/ parte el valor.
    [InlineData("""{"heroFilter":"\\75rl(http://malo/x.svg#f)"}""")]
    [InlineData("""{"heroFilter":"grayscale(1)\\"}""")]
    [InlineData("""{"heroFilter":"none/*x*/"}""")]
    // fontFamily se emite entre comillas: comillas, barras y <> las rompen (y el portal las
    // borraba, así que lo guardado y lo aplicado no coincidían).
    [InlineData("""{"fontFamily":"Gill</style><img src=x>"}""")]
    [InlineData("""{"fontFamily":"Gill\" , x:url(http://malo)"}""")]
    public async Task Tokens_InyeccionCss_400(string tokensJson)
    {
        await ResetAsync();

        var response = await PutTokens(tokensJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    [Theory]
    [InlineData("heroFilter", 121)]
    [InlineData("fontFamily", 61)]
    [InlineData("tagline", 121)]
    [InlineData("supportEmail", 121)]
    [InlineData("legal", 401)]
    public async Task Tokens_DemasiadoLargos_400(string token, int length)
    {
        await ResetAsync();

        var value = token == "supportEmail" ? "a@" + new string('b', length - 2) : new string('a', length);
        var response = await PutTokens($$"""{"{{token}}":"{{value}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(token, await ErrorAsync(response));
    }

    // El tagline es el único token de texto libre: se publica y lo hereda cualquier
    // consumidor futuro (un email, un meta), así que nada de HTML crudo.
    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Bienvenido a <b>ALMA EN PENA</b>")]
    public async Task Tokens_TaglineConHtml_400(string tagline)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"tagline":{{JsonSerializer.Serialize(tagline)}} }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("tagline", await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // El legal se publica y el login lo pinta: mismo criterio que el tagline.
    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Solo vendemos a <b>profesionales</b>")]
    public async Task Tokens_LegalConHtml_400(string legal)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"legal":{{JsonSerializer.Serialize(legal)}} }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("legal", await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // heroStyle es una lista CERRADA (acaba en un atributo del <html> que selecciona CSS) y
    // displayWeight una centena de 100 a 900: cualquier otra cosa es 400 con el nombre del
    // token, para que el editor de /manage señale el campo.
    [Theory]
    [InlineData("heroStyle", "dark")]
    [InlineData("heroStyle", "paper; background:red")]
    [InlineData("heroStyle", "paper\"")]
    [InlineData("displayWeight", "950")]
    [InlineData("displayWeight", "9")]
    [InlineData("displayWeight", "1000")]
    [InlineData("displayWeight", "bold")]
    [InlineData("displayWeight", "900px")]
    [InlineData("displayWeight", "000")]
    public async Task Tokens_HeroStyleODisplayWeightInvalidos_400(string token, string value)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"{{token}}":{{JsonSerializer.Serialize(value)}} }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(token, await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    [Theory]
    [InlineData("heroStyle", "paper", "paper")]
    [InlineData("heroStyle", "  Paper ", "paper")]
    [InlineData("displayWeight", "100", "100")]
    [InlineData("displayWeight", " 900 ", "900")]
    public async Task Tokens_HeroStyleODisplayWeightValidos_SeGuardanNormalizados(string token, string value, string expected)
    {
        await ResetAsync();

        (await PutTokens($$"""{"{{token}}":{{JsonSerializer.Serialize(value)}} }""")).EnsureSuccessStatusCode();

        Assert.Equal(expected, (await PublicTokensAsync()).GetProperty(token).GetString());
    }

    // Un «@» no basta: el portal (asEmail) exige dominio con punto y, si no lo tiene,
    // descartaba el token en silencio tras haber dicho «Marca guardada y aplicada».
    [Theory]
    [InlineData("soporte@almaenpena")]
    [InlineData("soporte@")]
    [InlineData("@almaenpena.com")]
    [InlineData("sopo rte@almaenpena.com")]
    [InlineData("<b>@almaenpena.com")]
    public async Task Tokens_SupportEmailInvalido_400(string email)
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"supportEmail":{{JsonSerializer.Serialize(email)}} }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("supportEmail", await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    [Fact]
    public async Task Tokens_SupportEmailSinArroba_400()
    {
        await ResetAsync();

        var response = await PutTokens("""{"supportEmail":"soporte"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("supportEmail", await ErrorAsync(response));
    }

    // ── 10. Tamaño máximo del objeto: 4 KB ─────────────────────────────────────

    [Fact]
    public async Task Tokens_DemasiadoGrandes_400()
    {
        await ResetAsync();

        var big = new StringBuilder("{");
        for (var i = 0; i < 200; i++) big.Append($"\"relleno{i}\":\"{new string('a', 30)}\",");
        big.Append("\"radius\":\"12px\"}");

        var response = await PutTokens(big.ToString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("4 KB", await ErrorAsync(response));
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // El tope se mide sobre el JSON COMPACTO: la indentación con la que venga la petición
    // no puede gastarse el presupuesto de 4 KB.
    [Fact]
    public async Task Tokens_ConMuchaIndentacion_SeGuardan()
    {
        await ResetAsync();

        (await PutTokens("{" + new string(' ', 4100) + "\"radius\":\"12px\"}")).EnsureSuccessStatusCode();

        Assert.Equal("12px", (await PublicTokensAsync()).GetProperty("radius").GetString());
    }

    // Y el error CONCRETO del token gana al genérico de tamaño: el editor de /manage
    // necesita saber qué campo señalar.
    [Fact]
    public async Task Tokens_TaglineEnorme_DaElErrorDelTagline()
    {
        await ResetAsync();

        var response = await PutTokens($$"""{"tagline":"{{new string('a', 5000)}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ErrorAsync(response);
        Assert.Contains("tagline", error);
        Assert.DoesNotContain("4 KB", error);
    }

    // ── 11. Un token inválido no guarda NADA (tampoco la marca clásica) ────────

    [Fact]
    public async Task Tokens_Invalidos_NoTocanLaMarcaGuardada()
    {
        await ResetAsync();
        (await PutBranding($$"""
            {"name":"ALMA EN PENA","color":"#111111","logoUrl":"/media/alma.svg","tokens":{{AlmaTokens}} }
            """)).EnsureSuccessStatusCode();

        var response = await PutBranding("""
            {"name":"OTRA MARCA","color":"#00ff00","logoUrl":"/media/otra.svg","tokens":{"paper":"blanco"}}
            """);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await PublicBrandingAsync();
        Assert.Equal("ALMA EN PENA", body.GetProperty("name").GetString());
        Assert.Equal("#111111", body.GetProperty("color").GetString());
        Assert.Equal("/media/alma.svg", body.GetProperty("logoUrl").GetString());
        Assert.Equal("#ffffff", body.GetProperty("tokens").GetProperty("paper").GetString());

        await ResetAsync();
    }

    // ── 12. El editor de tokens es cosa de administradores ─────────────────────

    [Fact]
    public async Task Tokens_SinAdmin_NoSeGuardan()
    {
        await ResetAsync();

        var response = await _client.PutAsync("/api/admin/integration/branding",
            new StringContent($$"""{"tokens":{{AlmaTokens}} }""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await PublicTokensAsync()).ValueKind);
    }

    // ── 13. Extensión (card/rule/ruleWidth/accent): sin ellos NADA se mueve ────

    // La garantía de que la otra instancia (ALMA EN PENA) no cambia al crecer la lista
    // cerrada: su JSON de siempre, que no trae ninguno de los cuatro, se normaliza y se
    // publica EXACTAMENTE igual que antes —mismas claves, mismo orden, ningún token
    // inventado con valor por defecto—. El literal es la salida del servidor ANTERIOR a
    // la extensión, capturada tal cual; si alguna vez deja de cuadrar, el portal de ALMA
    // ha cambiado sin que nadie tocara su marca.
    [Fact]
    public async Task Tokens_SinLosCuatroDeLaExtension_ElJsonNormalizadoEsElDeAntes()
    {
        await ResetAsync();

        (await PutTokens(AlmaTokens)).EnsureSuccessStatusCode();

        const string antes = """{"logoUrlDark":"/media/alma-logo-dark.svg","faviconUrl":"/media/alma-favicon.png","fontUrl":"/media/GillSansMTLight.woff2","fontFamily":"GillSansMTLight","caps":true,"tracking":".06em","radius":"12px","radiusButton":"50px","paper":"#ffffff","surface":"#f5f5f5","ink":"#111111","headerBg":"#000000","headerInk":"#ffffff","heroFilter":"none","tagline":"Bienvenido a ALMA EN PENA","supportEmail":"soporte@almaenpena.com"}""";
        Assert.Equal(antes, (await PublicTokensAsync()).GetRawText());
        Assert.Equal(antes, (await AdminSettingsAsync()).GetProperty("brandTokens").GetRawText());
    }

    // ── 14. Ronda 1 (heroStyle/displayWeight/legal): sin ellos TAMPOCO se mueve nada ──

    // Misma garantía que la prueba 13, ahora para la lista tal como quedó tras la extensión:
    // el juego de BLOCCO 5 ANTERIOR a la ronda 1 (los cuatro del panel) se publica con las
    // mismas claves y el mismo orden, sin que el servidor invente heroStyle, displayWeight o
    // legal con un valor por defecto. Un `heroStyle` inventado pondría el atributo en <html>
    // y cambiaría la portada de una instancia que nunca lo pidió.
    [Fact]
    public async Task Tokens_SinLosTresDeLaRonda1_ElJsonNormalizadoEsElDeAntes()
    {
        await ResetAsync();

        (await PutTokens(Blocco5Tokens)).EnsureSuccessStatusCode();

        const string antes = """{"card":"#f0efed","rule":"#e70917","ruleWidth":"1px","accent":"#e70917"}""";
        Assert.Equal(antes, (await PublicTokensAsync()).GetRawText());
        Assert.Equal(antes, (await AdminSettingsAsync()).GetProperty("brandTokens").GetRawText());

        // Y a la inversa: los tres solos no arrastran ningún otro token.
        (await PutTokens(Blocco5Round1Tokens)).EnsureSuccessStatusCode();
        foreach (var name in new[] { "card", "rule", "ruleWidth", "accent", "paper", "tagline" })
            Assert.False((await PublicTokensAsync()).TryGetProperty(name, out _), name);
    }
}
