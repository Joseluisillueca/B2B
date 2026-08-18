using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Fase 4 del portal: cuenta (09-profile.png), empresa (10-business.png),
// estadísticas (11-statistics.png) y contacto (07-contact.png).
//
// Regla que atraviesa todo el bloque, igual que en la Fase 3: el clientId sale
// SIEMPRE del token. Un usuario del cliente A no ve —ni toca— nada del cliente B.
public class PortalAccountTests : IClassFixture<PortalAccountTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "ACC0000A-1111-4111-8111-000000000001";
    private const string ClientB = "ACC0000B-2222-4222-8222-000000000002";
    private const string AddressA = "ACCADR0A-1111-4111-8111-00000000000A";
    private const string OtherEmail = "otra-tienda@cuenta-b.test";
    private const string OtherPassword = "otra-clave-789";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PortalAccountTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Semilla ───────────────────────────────────────────────────────────────

    private static bool _seeded;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task SeedAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            await PutAsync($"/api/clients/{ClientA}", ClientPayload("TEST 5", "C100057"));
            await PutAsync($"/api/clients/{ClientB}", ClientPayload("OTRA TIENDA", "C100099"));
            await PutAsync($"/api/clients/{ClientA}/users/admin",
                $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test 5","culture":"es_ES"}""");
            await PutAsync($"/api/clients/{ClientB}/users/admin",
                $$"""{"email":"{{OtherEmail}}","name":"Otra","culture":"es_ES"}""");
            await PutAsync($"/api/clients/{ClientA}/shipping-addresses/{AddressA}", """
                {
                  "alias": "GETAFE01",
                  "address": { "streetAddress": "Poligono Norte", "num": "4B", "description": "Nave 7",
                    "city": "Getafe", "province": "Madrid", "zipCode": "28905", "countryIsoId": "ES",
                    "geo": { "latitude": 0, "longitude": 0 },
                    "contact": { "name": "Ana", "lastName": "", "company": "TEST 5", "phones": [] } }
                }
                """);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var other = await db.Users.SingleAsync(u => u.Email == OtherEmail);
                other.PasswordHash = new PasswordHasher<AppUser>().HashPassword(other, OtherPassword);
                await db.SaveChangesAsync();
            }

            // Facturas del cliente A repartidas por meses: son la materia prima del
            // gráfico "Ventas totales por meses" (11-statistics.png).
            await PutAsync("/api/documents/invoices/STAT-1", Invoice("STAT-1", ClientA, "FV-3000001", "2026-01-15", 1000m));
            await PutAsync("/api/documents/invoices/STAT-2", Invoice("STAT-2", ClientA, "FV-3000002", "2026-01-31", 500m));
            await PutAsync("/api/documents/invoices/STAT-3", Invoice("STAT-3", ClientA, "FV-3000003", "2026-03-02", 250m));
            // Abono: llega por el mismo endpoint en negativo y resta del mes
            await PutAsync("/api/documents/invoices/STAT-4", Invoice("STAT-4", ClientA, "FA-3000004", "2026-03-20", -50m));
            // Fuera de la ventana por defecto de la prueba
            await PutAsync("/api/documents/invoices/STAT-5", Invoice("STAT-5", ClientA, "FV-3000005", "2024-05-05", 777m));
            // Del otro cliente: no puede aparecer jamás en las estadísticas de A
            await PutAsync("/api/documents/invoices/STAT-B", Invoice("STAT-B", ClientB, "FV-9000001", "2026-01-20", 4242m));

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private static string ClientPayload(string name, string number) => $$"""
    {
      "name": "{{name}}",
      "fiscalInfo": {
        "alias": "{{name}} COMERCIAL",
        "address": { "streetAddress": "Calle de prueba 2", "num": "", "description": "",
          "city": "Alicante", "province": "Alicante/Alacant", "zipCode": "03005", "countryIsoId": "ES",
          "geo": { "latitude": 0, "longitude": 0 },
          "contact": { "name": "Ana", "lastName": "", "company": "{{name}}", "phones": [] } },
        "fiscalName": "{{name}}",
        "fiscalId": { "type": "nif", "document": "48718888T" }
      },
      "creditInfo": { "code": "EUR", "value": 15000.0 },
      "markets": [ "es" ],
      "payMethods": [ "transf30" ],
      "externalReference": "{{number}}",
      "email": "jilluecasaus@gmail.com",
      "secondaryEmails": [ { "email": "facturas@{{number}}.test", "type": "Invoices", "emailName": "Invoices" } ],
      "phone": { "code": "+34", "number": "62 999 99 99" },
      "secondaryPhones": [ { "code": "+34", "number": "965 00 00 00" } ],
      "web": "https://b2b.lejanbrand.com/es/es/agent/clients/new",
      "canShop": true,
      "taxId": "iva-general",
      "groupIds": [ "mayorista" ],
      "productSegments": [ "A+" ]
    }
    """;

    private static string Invoice(string id, string clientId, string number, string date, decimal total) => $$"""
    {
      "clientId": "{{clientId}}",
      "fiscalInfo": { "alias": "TEST 5", "fiscalName": "TEST 5",
                      "fiscalId": { "type": "nif", "document": "48718888T" } },
      "number": "{{number}}",
      "payMethodName": { "es_ES": "Transferencia bancaria" },
      "issueDate": "{{date}}T00:00:00.000Z",
      "status": "Unpaid",
      "observations": "", "documentUrl": "",
      "totals": {
        "totalAmount":   { "code": "EUR", "value": {{Num(total / 1.21m)}} },
        "totalDiscount": { "code": "EUR", "value": 0 },
        "totalTax":      { "code": "EUR", "value": {{Num(total - total / 1.21m)}} },
        "total":         { "code": "EUR", "value": {{Num(total)}} }
      },
      "lines": [],
      "payments": [ { "paymentInfo": "", "dueDate": "2099-12-31T00:00:00.000Z",
                      "emittedAt": "{{date}}T00:00:00.000Z",
                      "amount": { "code": "EUR", "value": {{Num(total)}} } } ]
    }
    """;

    private static string Num(decimal value) =>
        Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ── Utilidades HTTP ───────────────────────────────────────────────────────

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<string> OtherTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = OtherEmail, password = OtherPassword, type = "global", longDuration = true });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string route, object? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", token ?? await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // ══════════════ Perfil y preferencias (09-profile.png) ══════════════

    [Fact]
    public async Task Perfil_DevuelveMisDatosYLasPreferenciasPorDefecto()
    {
        var body = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/profile"));

        Assert.Equal(TestWebApplicationFactory.SeededEmail, body.GetProperty("email").GetString());
        Assert.Equal("Test 5", body.GetProperty("name").GetString());
        Assert.Equal("Administrador", body.GetProperty("rol").GetString());
        Assert.Equal("es_ES", body.GetProperty("culture").GetString());
        Assert.Equal("es", body.GetProperty("lang").GetString());

        // Sin preferencias guardadas la tarjeta enseña los valores de la referencia
        var prefs = body.GetProperty("prefs");
        Assert.Equal("pvd", prefs.GetProperty("showPrices").GetString());
        Assert.Equal("list", prefs.GetProperty("listDesktop").GetString());
        Assert.Equal("list", prefs.GetProperty("listMobile").GetString());

        // "DIRECCIÓN DE ENVÍO POR DEFECTO" se elige entre las del propio cliente
        var address = Assert.Single(body.GetProperty("shippingAddresses").EnumerateArray());
        Assert.Equal(AddressA, address.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Perfil_GuardaNombreIdiomaYPreferencias()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client);

        var saved = await PutProfileAsync(client, token, new
        {
            name = "Jose Luis",
            culture = "fr_FR",
            prefs = new { showPrices = "pvp", listDesktop = "grid", listMobile = "list" }
        });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await JsonAsync(saved);
        Assert.Equal("Jose Luis", body.GetProperty("name").GetString());
        Assert.Equal("fr_FR", body.GetProperty("culture").GetString());
        Assert.Equal("fr", body.GetProperty("lang").GetString());
        Assert.Equal("pvp", body.GetProperty("prefs").GetProperty("showPrices").GetString());
        Assert.Equal("grid", body.GetProperty("prefs").GetProperty("listDesktop").GetString());

        // Persisten: la siguiente lectura (y el /me con el que arranca el portal) las trae
        var again = await GetAsync(client, token, "/api/portal/profile");
        Assert.Equal("pvp", (await JsonAsync(again)).GetProperty("prefs").GetProperty("showPrices").GetString());

        var me = await JsonAsync(await GetAsync(client, token, "/api/portal/me"));
        Assert.Equal("pvp", me.GetProperty("prefs").GetProperty("showPrices").GetString());
    }

    [Fact]
    public async Task Perfil_ConIdiomaODireccionNoValidos_Devuelve400()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await PutProfileAsync(client, token, new { culture = "de_DE" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PutProfileAsync(client, token, new { name = "   " })).StatusCode);
        // Una dirección que no es del cliente del token no puede quedar por defecto
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PutProfileAsync(client, token,
                new { prefs = new { shippingAddressId = "AJENA-0000-0000-0000-000000000000" } })).StatusCode);
    }

    private static Task<HttpResponseMessage> PutProfileAsync(HttpClient client, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/portal/profile") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    // ══════════════ Cambiar contraseña ══════════════

    [Fact]
    public async Task Contrasena_SeCambiaYLaAnteriorDejaDeValer()
    {
        // Fábrica propia: cambiar la contraseña del usuario sembrado invalidaría
        // el resto de pruebas que comparten la fixture.
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client);

        var response = await PostAsync(client, token, "/api/portal/password", new
        {
            current = TestWebApplicationFactory.SeededPassword,
            next = "nueva-clave-2026",
            repeat = "nueva-clave-2026"
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestWebApplicationFactory.SeededEmail,
            password = "nueva-clave-2026",
            type = "global",
            longDuration = true
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var stale = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestWebApplicationFactory.SeededEmail,
            password = TestWebApplicationFactory.SeededPassword,
            type = "global",
            longDuration = true
        });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    [Fact]
    public async Task Contrasena_ConLaActualEquivocada_Devuelve400YNoCambiaNada()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client);

        var response = await PostAsync(client, token, "/api/portal/password",
            new { current = "no-es-esta", next = "nueva-clave-2026", repeat = "nueva-clave-2026" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // La de siempre sigue valiendo: el intento fallido no ha tocado el hash
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestWebApplicationFactory.SeededEmail,
            password = TestWebApplicationFactory.SeededPassword,
            type = "global",
            longDuration = true
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData("corta", "corta")]                       // menos de 8 caracteres
    [InlineData("nueva-clave-2026", "otra-distinta")]    // repetición que no coincide
    [InlineData("", "")]                                 // vacía
    public async Task Contrasena_ConDatosInvalidos_Devuelve400(string next, string repeat)
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client);

        var response = await PostAsync(client, token, "/api/portal/password",
            new { current = TestWebApplicationFactory.SeededPassword, next, repeat });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string route, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    // ══════════════ Empresa (10-business.png) ══════════════

    [Fact]
    public async Task Empresa_DevuelveDatosGeneralesYFiscalesDelClienteDelToken()
    {
        var body = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/business"));

        Assert.Equal("TEST 5", body.GetProperty("name").GetString());
        Assert.Equal("C100057", body.GetProperty("number").GetString());

        var general = body.GetProperty("general");
        Assert.Equal("jilluecasaus@gmail.com", general.GetProperty("email").GetString());
        Assert.Equal("62 999 99 99", general.GetProperty("phone").GetString());
        Assert.Equal("965 00 00 00", general.GetProperty("secondaryPhone").GetString());
        Assert.Equal("https://b2b.lejanbrand.com/es/es/agent/clients/new", general.GetProperty("web").GetString());
        Assert.Equal("TEST 5 COMERCIAL", general.GetProperty("tradeName").GetString());
        Assert.Equal("facturas@C100057.test", general.GetProperty("billingEmail").GetString());

        var fiscal = body.GetProperty("fiscal");
        Assert.Equal("TEST 5", fiscal.GetProperty("fiscalName").GetString());
        Assert.Equal("48718888T", fiscal.GetProperty("fiscalId").GetString());
        Assert.Equal("ES", fiscal.GetProperty("countryIsoId").GetString());
        Assert.Equal("03005", fiscal.GetProperty("zipCode").GetString());
        Assert.Equal("Alicante/Alacant", fiscal.GetProperty("province").GetString());
        Assert.Equal("Alicante", fiscal.GetProperty("city").GetString());
        Assert.Equal("Calle de prueba 2", fiscal.GetProperty("streetAddress").GetString());
        Assert.False(fiscal.GetProperty("recargoEquivalencia").GetBoolean());

        // Ni rastro del otro cliente en la respuesta
        Assert.DoesNotContain("OTRA TIENDA", body.GetRawText());
    }

    [Fact]
    public async Task Empresa_ElCambioSeRegistraComoSolicitudYNoTocaLosDatos()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/portal/business/change-request", new
        {
            section = "general",
            changes = new { phone = "600 11 22 33", web = "https://tienda.test" }
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var request = await JsonAsync(created);
        Assert.Equal("general", request.GetProperty("section").GetString());
        Assert.Equal("pending", request.GetProperty("status").GetString());
        Assert.Equal("600 11 22 33", request.GetProperty("changes").GetProperty("phone").GetString());

        // La ficha sigue diciendo lo que dice BC: el portal no escribe datos maestros
        var business = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/business"));
        Assert.Equal("62 999 99 99", business.GetProperty("general").GetProperty("phone").GetString());
        Assert.Contains(business.GetProperty("pending").EnumerateArray(),
            item => item.GetProperty("id").GetString() == request.GetProperty("id").GetString());
    }

    // Auditoría M8: el bloque "Direcciones de envío" tiene un botón AÑADIR que pide
    // el alta por el mismo canal que los EDITAR. Sin la sección "addresses" la
    // solicitud caía al buzón de contacto y no aparecía entre las pendientes.
    [Fact]
    public async Task Empresa_ElAltaDeDireccionSeRegistraComoSolicitud()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/portal/business/change-request", new
        {
            section = "addresses",
            changes = new
            {
                alias = "GETAFE02",
                streetAddress = "Polígono Sur",
                num = "12",
                zipCode = "28906",
                city = "Getafe",
                province = "Madrid",
                countryIsoId = "ES"
            }
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var request = await JsonAsync(created);
        Assert.Equal("addresses", request.GetProperty("section").GetString());
        Assert.Equal("pending", request.GetProperty("status").GetString());
        Assert.Equal("GETAFE02", request.GetProperty("changes").GetProperty("alias").GetString());
        Assert.Equal("Getafe", request.GetProperty("changes").GetProperty("city").GetString());

        // Sale en la lista de pendientes de /business, como las otras dos secciones
        var business = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/business"));
        Assert.Contains(business.GetProperty("pending").EnumerateArray(),
            item => item.GetProperty("id").GetString() == request.GetProperty("id").GetString()
                    && item.GetProperty("section").GetString() == "addresses");

        // Y no toca las direcciones que manda BC: sigue habiendo las del sync
        Assert.DoesNotContain("GETAFE02",
            business.GetProperty("addresses").GetRawText());
    }

    // El alias es lo que identifica la dirección en la lista: sin él no hay solicitud
    [Fact]
    public async Task Empresa_AltaDeDireccionSinAlias_Devuelve400()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/portal/business/change-request", new
        {
            section = "addresses",
            changes = new { city = "Getafe", zipCode = "28906" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Empresa_SolicitudInvalida_Devuelve400()
    {
        // Un campo de otra sección no se cuela por la de direcciones
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post,
            "/api/portal/business/change-request",
            new { section = "addresses", changes = new { alias = "X", canShop = "true" } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post,
            "/api/portal/business/change-request",
            new { section = "inventada", changes = new { phone = "600" } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post,
            "/api/portal/business/change-request",
            new { section = "general", changes = new { } })).StatusCode);

        // Campo que no pertenece a esa sección: no se acepta a ciegas
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post,
            "/api/portal/business/change-request",
            new { section = "general", changes = new { fiscalId = "X1234567Z" } })).StatusCode);
    }

    [Fact]
    public async Task Empresa_LasSolicitudesNoSeVenEntreClientes()
    {
        await SendAsync(HttpMethod.Post, "/api/portal/business/change-request",
            new { section = "fiscal", changes = new { city = "Solo del cliente A" } });

        var theirs = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/business", token: await OtherTokenAsync()));
        Assert.DoesNotContain("Solo del cliente A", theirs.GetRawText());
        Assert.Equal("OTRA TIENDA", theirs.GetProperty("name").GetString());
    }

    // ══════════════ Estadísticas (11-statistics.png) ══════════════

    [Fact]
    public async Task Estadisticas_AgreganPorMesLasFacturasDelCliente()
    {
        var body = await JsonAsync(await SendAsync(HttpMethod.Get,
            "/api/portal/statistics?from=2026-01-01&to=2026-03-31"));

        var months = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(3, months.Count);   // enero, febrero y marzo: los meses vacíos también salen

        Assert.Equal("2026-01", months[0].GetProperty("month").GetString());
        Assert.Equal(1500m, months[0].GetProperty("amount").GetDecimal());
        Assert.Equal(2, months[0].GetProperty("count").GetInt32());

        Assert.Equal("2026-02", months[1].GetProperty("month").GetString());
        Assert.Equal(0m, months[1].GetProperty("amount").GetDecimal());

        // Marzo: 250 de la factura menos 50 del abono
        Assert.Equal("2026-03", months[2].GetProperty("month").GetString());
        Assert.Equal(200m, months[2].GetProperty("amount").GetDecimal());

        Assert.Equal(1700m, body.GetProperty("total").GetDecimal());
        // La factura de 2024 queda fuera de la ventana pedida
        Assert.DoesNotContain("777", body.GetRawText());
    }

    [Fact]
    public async Task Estadisticas_NoMezclanLasFacturasDeOtroCliente()
    {
        var mine = await JsonAsync(await SendAsync(HttpMethod.Get,
            "/api/portal/statistics?from=2026-01-01&to=2026-01-31"));
        Assert.Equal(1500m, mine.GetProperty("total").GetDecimal());

        var theirs = await JsonAsync(await SendAsync(HttpMethod.Get,
            "/api/portal/statistics?from=2026-01-01&to=2026-01-31", token: await OtherTokenAsync()));
        Assert.Equal(4242m, theirs.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Estadisticas_SinRango_UsanLosUltimosDoceMeses()
    {
        var body = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/portal/statistics"));

        var months = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(13, months.Count);   // el mes en curso más los doce anteriores
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM"), months[^1].GetProperty("month").GetString());
    }

    // ══════════════ Contacto (07-contact.png) ══════════════

    [Fact]
    public async Task Contacto_GuardaLaSolicitudConSuAdjunto()
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Incidencia con el pedido PV00001"), "subject" },
            { new StringContent("tienda@cliente.test"), "email" },
            { new StringContent("Faltan dos pares del albarán AV-2400123."), "message" }
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("albaran;lineas\n1;2\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "detalle.csv");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/portal/contact") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("Incidencia con el pedido PV00001", body.GetProperty("subject").GetString());
        Assert.Equal("tiendas@lejanbrand.com", body.GetProperty("deliveredTo").GetString());
        Assert.Equal("detalle.csv", body.GetProperty("attachment").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ContactMessages.SingleAsync(m => m.Subject == "Incidencia con el pedido PV00001");
        Assert.Equal(ClientA, stored.ClientId);
        Assert.False(string.IsNullOrEmpty(stored.AttachmentPath));
        Assert.True(File.Exists(stored.AttachmentPath));
    }

    [Fact]
    public async Task Contacto_SinAsuntoOSinMensaje_Devuelve400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await ContactAsync("", "Hola")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ContactAsync("Asunto", "   ")).StatusCode);
    }

    [Fact]
    public async Task Contacto_ConAdjuntoDeTipoNoPermitido_Devuelve400()
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Asunto"), "subject" },
            { new StringContent("Mensaje"), "message" }
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<?php echo 1; ?>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "malicioso.php");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/portal/contact") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(request)).StatusCode);
    }

    private async Task<HttpResponseMessage> ContactAsync(string subject, string message)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(subject), "subject" },
            { new StringContent(message), "message" }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/portal/contact") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    // ══════════════ Sesión ══════════════

    [Fact]
    public async Task Cuenta_SinToken_Devuelve401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/business")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/statistics")).StatusCode);
    }
}
