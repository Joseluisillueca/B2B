using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Integration;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Deduplicación de pedidos BC→portal (visto en producción): el portal crea el pedido nativo
// P00001 (doc "order", ExternalId = su orderId) y lo despacha a BC, que lo inserta con SU
// PROPIO SystemId y responde 201 con el JSON del pedido creado (`id`, `number`). Cuando BC
// re-sincroniza ese pedido (PUT /api/orders/{bcSystemId}) el portal creaba un SEGUNDO doc y
// el cliente veía P00001 y 101001 duplicados. Solución portal-side (el conector no se toca):
//   1) al despachar, se guarda `bcId`/`bcNumber` de la respuesta 201 en el doc nativo;
//   2) en la ingesta, un PUT de pedido cuyo id coincide con un `bcId` conocido ACTUALIZA ese
//      doc (conservando el ExternalId del portal) en vez de crear otro.
public class OrderDedupTests : IClassFixture<OrderDedupTests.BcFactory>
{
    private const string Pass = "cliente-dedup-123";
    private const string BcClientId = "dedup-tests-client-id";

    // Fábrica con Business Central FALSO: sustituye el handler HTTP del BcClient tipado
    // (Program.cs: AddHttpClient<BcClient>) por uno que responde al token OAuth y a los
    // POST de pedidos con lo que cada prueba decida (Responder). Sin red.
    public sealed class BcFactory : TestWebApplicationFactory
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.Created);

        public List<string> Posted { get; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<BcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeBcHandler(this));
            });
        }

        private sealed class FakeBcHandler(BcFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.RequestUri!.AbsolutePath.Contains("/oauth2/", StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"fake-token","expires_in":3600}""", Encoding.UTF8, "application/json")
                    };
                if (request.Content is not null)
                    owner.Posted.Add(await request.Content.ReadAsStringAsync(ct));
                return owner.Responder(request);
            }
        }
    }

    private readonly BcFactory _factory;
    private readonly HttpClient _client;

    public OrderDedupTests(BcFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Utilidades (mismo patrón que VisibilityCheckoutTests) ─────────────────────

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutRaw(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task Put(string route, string json) => (await PutRaw(route, json)).EnsureSuccessStatusCode();

    private Task PutModel(string id, string name, string reference) =>
        Put($"/api/catalog/models/{id}",
            $$"""
            {"name":{"es_ES":"{{name}}"},"active":true,"externalReference":"{{reference}}","familyId":"calzado","productSegments":["A"],"attributes":{} }
            """);

    private Task PutOffer(string offerId, string modelId, decimal pvd)
    {
        var value = pvd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Put("/api/catalog/offers",
            $$$"""[{"id":"{{{offerId}}}","offerData":{"basePrice":{"code":"EUR","value":{{{value}}} },"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{modelId}}}"}}]""");
    }

    private async Task<string> ClientTokenAsync(string clientId)
    {
        var email = $"{clientId}@cliente-dedup.test".ToLowerInvariant();
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

    private async Task SetOrdersModeAsync(string mode) =>
        (await Send(HttpMethod.Put, "/api/admin/integration/orders-mode",
            await _factory.GetAdminTokenAsync(_client), new { mode })).EnsureSuccessStatusCode();

    // Conexión BC "configurada" (BcConfigured = true): así el dispatcher llama de verdad al
    // BcClient, cuyo HttpClient es el falso de la fábrica.
    private async Task ConfigureBcAsync()
    {
        var response = await Send(HttpMethod.Put, "/api/admin/integration/settings",
            await _factory.GetAdminTokenAsync(_client), new
            {
                bcBaseUrl = "https://bc.fake.test/api/mitoprojects/b2b/v1.0/companies(1)",
                bcTokenUrl = "https://login.fake.test/tenant/oauth2/v2.0/token",
                bcClientId = BcClientId,
                bcClientSecret = "fake-secret",
            });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("bcConfigured").GetBoolean());
    }

    private static object Line(string modelId, string productId, string reference, decimal price) => new
    {
        modelId, productId, size = "40", name = reference, reference, qty = 1, price
    };

    // Termina un pedido en modo portal para `clientId` y devuelve su orderId (ExternalId del doc nativo).
    private async Task<string> PlaceOrderAsync(string tag, string clientId)
    {
        var model = $"dedup-model-{tag}".ToLowerInvariant();
        await PutModel(model, $"DEDUP {tag}", $"DEDUP-{tag}-REF");
        await PutOffer($"dedup-offer-{tag}".ToLowerInvariant(), model, 40m);
        await Put($"/api/clients/{clientId}", """{"name":"Cliente dedup","canShop":true}""");
        await SetOrdersModeAsync("portal");
        await ConfigureBcAsync();

        var token = await ClientTokenAsync(clientId);
        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(model, $"PRD-DEDUP-{tag}", $"DEDUP-{tag}-REF", 40m) }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    private async Task<List<SyncDocument>> OrderDocsOfClientAsync(string clientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SyncDocuments.AsNoTracking()
            .Where(d => d.EntityType == "order" && d.ParentId == clientId).ToListAsync();
    }

    private async Task<NotificationLog> LastBcLogAsync(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.NotificationLogs.AsNoTracking()
            .Where(l => l.EventKey == "shoes.purchase_order.updated" && l.ChannelType == "business-central"
                && l.InputJson != null && l.InputJson.Contains(orderId))
            .OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync();
        Assert.NotNull(log);
        return log!;
    }

    private static HttpResponseMessage Created(string json) => new(HttpStatusCode.Created)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    // BC re-sincroniza el pedido: el mismo pedido con SU SystemId en la URL y SU número.
    private static string BcOrderJson(string clientId, string number) => $$$"""
        {"clientId":"{{{clientId}}}","externalReference":"{{{number}}}","orderedDate":"2026-09-03T10:00:00.000Z",
         "type":"REPLENISHMENT","status":"open","seasonId":"","purchaseOrderId":"","payMethodId":"",
         "totals":{"totalAmount":{"code":"EUR","value":40},"totalDiscount":{"code":"EUR","value":0},"totalTax":{"code":"EUR","value":8.4},"total":{"code":"EUR","value":48.4}},
         "items":[]}
        """;

    // ── a. El despacho guarda el enlace de la respuesta 201 ──────────────────────

    [Fact]
    public async Task Despacho_Guarda_BcId_DelaRespuesta201()
    {
        const string clientId = "DEDUPACL-0000-4000-9000-000000000001";
        _factory.Responder = _ => Created("""{"id":"BC-GUID-A","number":"101001","b2bId":"x"}""");

        var orderId = await PlaceOrderAsync("A", clientId);

        var log = await LastBcLogAsync(orderId);
        Assert.Equal("completed", log.Status);

        var docs = await OrderDocsOfClientAsync(clientId);
        var doc = Assert.Single(docs);
        Assert.Equal(orderId, doc.ExternalId);
        var payload = JsonDocument.Parse(doc.Payload).RootElement;
        Assert.Equal("BC-GUID-A", payload.GetProperty("bcId").GetString());
        Assert.Equal("101001", payload.GetProperty("bcNumber").GetString());
        Assert.StartsWith("P", payload.GetProperty("externalReference").GetString());   // el número nativo sigue
    }

    // ── b. La ingesta por el SystemId de BC actualiza el doc del portal (no duplica) ──

    [Fact]
    public async Task IngestaDesdeBc_ConBcId_NoDuplica()
    {
        const string clientId = "DEDUPBCL-0000-4000-9000-000000000002";
        _factory.Responder = _ => Created("""{"id":"bc-guid-b","number":"101002"}""");

        var orderId = await PlaceOrderAsync("B", clientId);
        var before = Assert.Single(await OrderDocsOfClientAsync(clientId));
        var portalNumber = JsonDocument.Parse(before.Payload).RootElement.GetProperty("externalReference").GetString()!;

        // BC re-sincroniza con SU id (en otra caja: la comparación es case-insensitive).
        var response = await PutRaw("/api/orders/BC-GUID-B", BcOrderJson(clientId, "101002"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var docs = await OrderDocsOfClientAsync(clientId);
        var doc = Assert.Single(docs);
        Assert.Equal(orderId, doc.ExternalId);                 // conserva el id del portal
        var payload = JsonDocument.Parse(doc.Payload).RootElement;
        Assert.Equal("101002", payload.GetProperty("externalReference").GetString());   // ya es el de BC
        Assert.Equal("bc-guid-b", payload.GetProperty("bcId").GetString());             // enlace conservado
        Assert.Equal("101002", payload.GetProperty("bcNumber").GetString());
        Assert.Equal(portalNumber, payload.GetProperty("portalNumber").GetString());    // trazabilidad
        Assert.Equal("REPLENISHMENT", payload.GetProperty("type").GetString());

        var token = await ClientTokenAsync(clientId);
        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, orders.GetProperty("total").GetInt32());
        Assert.Equal("101002", orders.GetProperty("items")[0].GetProperty("number").GetString());

        // Un segundo PUT del mismo id de BC sigue sin duplicar.
        (await PutRaw("/api/orders/bc-guid-b", BcOrderJson(clientId, "101002"))).EnsureSuccessStatusCode();
        Assert.Single(await OrderDocsOfClientAsync(clientId));
    }

    // ── c. Sin enlace, la ingesta crea el doc como hoy ─────────────────────────────

    [Fact]
    public async Task IngestaDesdeBc_SinEnlace_CreaComoHoy()
    {
        const string clientId = "DEDUPCCL-0000-4000-9000-000000000003";
        _factory.Responder = _ => Created("""{"id":"BC-GUID-C","number":"101003"}""");

        var orderId = await PlaceOrderAsync("C", clientId);

        var response = await PutRaw("/api/orders/OTRO-GUID-C", BcOrderJson(clientId, "101999"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var docs = await OrderDocsOfClientAsync(clientId);
        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.ExternalId == orderId);
        var created = Assert.Single(docs, d => d.ExternalId == "OTRO-GUID-C");
        var payload = JsonDocument.Parse(created.Payload).RootElement;
        Assert.Equal("101999", payload.GetProperty("externalReference").GetString());
        Assert.False(payload.TryGetProperty("bcId", out _));
        Assert.False(payload.TryGetProperty("portalNumber", out _));
    }

    // ── d. Respuesta 201 sin cuerpo: el despacho se registra OK y no hay enlace ────

    [Fact]
    public async Task RespuestaSinJson_NoRompe()
    {
        const string clientId = "DEDUPDCL-0000-4000-9000-000000000004";
        _factory.Responder = _ => new HttpResponseMessage(HttpStatusCode.Created);

        var orderId = await PlaceOrderAsync("D", clientId);

        var log = await LastBcLogAsync(orderId);
        Assert.Equal("completed", log.Status);

        var doc = Assert.Single(await OrderDocsOfClientAsync(clientId));
        var payload = JsonDocument.Parse(doc.Payload).RootElement;
        Assert.False(payload.TryGetProperty("bcId", out _));
        Assert.False(payload.TryGetProperty("bcNumber", out _));
    }
}
