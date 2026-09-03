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

// Tarea 5: el VisibilityScope de VisibilityStore.ScopeForAsync (el mismo que la Tarea 4
// enchufó en CatalogService.QueryAsync) cierra las dos costuras que el catálogo filtrado
// NO tapaba: el checkout de POST /api/portal/orders (que arma el pedido a partir de las
// líneas que manda el CLIENTE, sin pasar por el catálogo) y el selector de modelos del
// agente en GET /api/agent/catalog-models (que listaba TODO el catálogo activo, cliente
// aparte). Ambos consultan el mismo scope; nada nuevo que mantener.
public class VisibilityCheckoutTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Pass = "cliente-checkout-vis-123";
    private const string AgentPass = "comercial-checkout-vis-123";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VisibilityCheckoutTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Utilidades de siembra (mismo patrón que VisibilityCatalogTests/PortalAutonomoTests) ──

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
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

    private Task PutOffer(string offerId, string modelId, decimal pvd)
    {
        var value = pvd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Put("/api/catalog/offers",
            $$$"""[{"id":"{{{offerId}}}","offerData":{"basePrice":{"code":"EUR","value":{{{value}}} },"priceType":"PVD","stock":0,"priority":1,"modelId":"{{{modelId}}}"}}]""");
    }

    // Variante (talla) real del catálogo: el checkout (14a-1) resuelve cada línea por su
    // productId contra CatalogProducts, así que las líneas de prueba necesitan una detrás.
    private Task PutProduct(string id, string modelId, string size, string sku, bool active = true) =>
        Put($"/api/catalog/products/{id}",
            $$"""{"modelId":"{{modelId}}","name":{"es_ES":"Talla {{size}}"},"active":{{(active ? "true" : "false")}},"sku":"{{sku}}","ean":"{{sku}}","attributes":{"tallas":"{{size}}"},"taxId":"iva-normal"}""");

    // El campo visibleAttributes es el que proyecta VisibilityStore.ProjectFromPayloadAsync
    // (Tarea 3) a la fila "bc" de CatalogVisibility, tanto para "client" como para "agent".
    private Task PutClientVisibility(string clientId, string rulesJsonArray) =>
        Put($"/api/clients/{clientId}",
            $$"""{"name":"Cliente checkout","canShop":true,"visibleAttributes":{{rulesJsonArray}} }""");

    // Token de un cliente real del portal (no el de integración), mismo patrón que
    // VisibilityCatalogTests.ClientTokenAsync: un AppUser "client-admin" con
    // ClientExternalId = clientId, del que PortalScope.ActorAsync saca el ClientId.
    private async Task<string> ClientTokenAsync(string clientId)
    {
        var email = $"{clientId}@cliente-checkout.test".ToLowerInvariant();
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

    // Token de un comercial (rol "agent"), provisto por el sync de /api/agents/{id} (mismo
    // patrón que AgentModelTests): visibleAttributes va en el propio documento del agente,
    // por el mismo hook de ingesta que el del cliente (VisibilityStore.ProjectFromPayloadAsync).
    private async Task<string> AgentTokenAsync(string agentId, string email, string? rulesJsonArray = null) =>
        await AgentTokenAsync(agentId, email, clientIds: [], rulesJsonArray);

    // Variante con cartera (clientIds): la necesita /api/agent/impersonate, que 403 si el
    // cliente no pertenece a la cartera del comercial (AgentEndpoints.AgentClientIdsAsync).
    private async Task<string> AgentTokenAsync(string agentId, string email, string[] clientIds, string? rulesJsonArray = null)
    {
        var rules = rulesJsonArray is null ? "" : $$""", "visibleAttributes": {{rulesJsonArray}}""";
        var ids = string.Join(",", clientIds.Select(c => $"\"{c}\""));
        await Put($"/api/agents/{agentId}",
            $$"""{"id":"{{agentId}}","parentId":null,"clientIds":[{{ids}}],"name":"Comercial","email":"{{email}}","culture":"es_ES"{{rules}} }""");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, AgentPass);
            await db.SaveChangesAsync();
        }

        return await _factory.LoginAsync(_client, email, AgentPass);
    }

    // Token de suplantación (agente → cliente de su cartera), vía /api/agent/impersonate:
    // mismo patrón que AgentModelTests.Impersonate_*.
    private async Task<string> ImpersonateAsync(string agentToken, string clientId)
    {
        var response = await Send(HttpMethod.Post, "/api/agent/impersonate", agentToken, new { clientId });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    // Conmutador de Conexiones (rol admin, endpoint dedicado en IntegrationEndpoints):
    // fija el modo en la fila IntegrationSettings de BD, que manda sobre Portal:OrdersMode.
    private async Task SetOrdersModeAsync(string mode) =>
        (await Send(HttpMethod.Put, "/api/admin/integration/orders-mode",
            await _factory.GetAdminTokenAsync(_client), new { mode })).EnsureSuccessStatusCode();

    private static object Line(string? modelId, string productId, string reference, decimal price, int qty = 1) => new
    {
        modelId, productId, size = "40", name = reference, reference, qty, price
    };

    private static List<string?> BlockedModelIds(JsonElement body) =>
        [.. body.GetProperty("blockedModelIds").EnumerateArray().Select(e => e.GetString())];

    // ── 1. Checkout (modo portal) bloquea una línea fuera de scope ─────────────

    [Fact]
    public async Task CheckoutBloqueaLineaFueraDeScope_ModoPortal()
    {
        const string clientId = "VISCK1CL-0000-4000-9000-000000000001";
        const string nike = "visck1n0-0000-4000-9000-000000000002";

        // Con oferta y precio válidos: si el checkout la bloquea, es SOLO por visibilidad,
        // no porque a la línea le faltara tarifa (RepriceAsync ya la rechazaría por eso).
        await PutModel(nike, "VISCK1 NIKE", "VCK1-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");
        await PutProduct("PRD-VCK1-NIKE", nike, "40", "VCK1-NIKE-40");
        await PutOffer("visck1of-0000-4000-9000-000000000003", nike, 40m);
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("portal");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(nike, "PRD-VCK1-NIKE", "VCK1-NIKE-REF", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("VCK1-NIKE-REF", body.GetProperty("error").GetString());
        // UX-M3: el 400 nombra además los modelId bloqueados para que el front marque las líneas.
        Assert.Equal([nike], BlockedModelIds(body));

        // Nada de SaveChanges: el pedido no existe.
        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", token))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }

    // ── 2. Checkout (modo erp) también bloquea ──────────────────────────────────

    [Fact]
    public async Task CheckoutBloquea_ModoErp()
    {
        const string clientId = "VISCK2CL-0000-4000-9000-000000000003";
        const string nike = "visck2n0-0000-4000-9000-000000000004";

        await PutModel(nike, "VISCK2 NIKE", "VCK2-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");
        await PutProduct("PRD-VCK2-NIKE", nike, "40", "VCK2-NIKE-40");
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("erp");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(nike, "PRD-VCK2-NIKE", "VCK2-NIKE-REF", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("VCK2-NIKE-REF", body.GetProperty("error").GetString());

        // Nada de SaveChanges tampoco en modo erp: el pedido no existe.
        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", token))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }

    // ── 2b. El producto desconocido se bloquea SIEMPRE, aunque el actor no tenga reglas ──
    // (en modo erp nada más re-tarifica/valida las líneas: sin esto un productId fantasma
    // se guardaba tal cual). Se nombra por la referencia de la línea (lo que ve el cliente).

    [Fact]
    public async Task CheckoutBloqueaProductoDesconocido_SinReglas_ModoErp()
    {
        const string clientId = "VISCK2BC-0000-4000-9000-000000000012";
        const string fantasma = "visck2bf-0000-4000-9000-000000000013";

        // Sin PutClientVisibility: este cliente no tiene reglas de visibilidad.
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("erp");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(fantasma, "PRD-VCK2B-FANT", "REF-FANTASMA", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("REF-FANTASMA", body.GetProperty("error").GetString());
        Assert.Equal([fantasma], BlockedModelIds(body));

        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", token))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }

    // ── 2c. ALTO (auditoría 14a): la visibilidad se evalúa sobre el modelo DEL PRODUCTO.
    // Una línea que declara un modelId visible pero cuyo productId pertenece a otro
    // modelo (oculto o no) se bloquea en los dos modos: el par (modelId, productId) tiene
    // que ser coherente con el catálogo.

    [Theory]
    [InlineData("portal")]
    [InlineData("erp")]
    public async Task CheckoutBloqueaProductoDeOtroModelo(string mode)
    {
        var suffix = mode == "portal" ? "1" : "2";
        var clientId = $"VISCK2DC-0000-4000-9000-00000000002{suffix}";
        var adidas = $"visck2da-0000-4000-9000-00000000003{suffix}";
        var nike = $"visck2dn-0000-4000-9000-00000000004{suffix}";
        var nikeProduct = $"PRD-VCK2D-NIKE-{mode}";

        await PutModel(adidas, "VISCK2D ADIDAS", $"VCK2D-ADI-{mode}", "calzado", """{"Marca":"ADIDAS"}""");
        await PutOffer($"visck2do-0000-4000-9000-00000000005{suffix}", adidas, 40m);
        await PutModel(nike, "VISCK2D NIKE", $"VCK2D-NIKE-{mode}", "calzado", """{"Marca":"NIKE"}""");
        await PutProduct(nikeProduct, nike, "40", $"VCK2D-NIKE-40-{mode}");
        await PutOffer($"visck2dp-0000-4000-9000-00000000006{suffix}", nike, 40m);
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync(mode);

        // modelId = ADIDAS (visible) pero productId = talla de NIKE (oculto).
        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(adidas, nikeProduct, $"VCK2D-ADI-{mode}", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains($"VCK2D-ADI-{mode}", body.GetProperty("error").GetString());
        Assert.Equal([adidas], BlockedModelIds(body));

        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", token))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }

    // ── 2d. Línea SIN modelId: se deriva del producto y se evalúa la visibilidad sobre
    // ese modelo (un cliente no puede "olvidar" el modelId para saltarse el filtro).

    [Fact]
    public async Task CheckoutLineaSinModelId_DerivaDelProducto_YBloqueaSiOculto_ModoErp()
    {
        const string clientId = "VISCK2EC-0000-4000-9000-000000000031";
        const string nike = "visck2en-0000-4000-9000-000000000032";

        await PutModel(nike, "VISCK2E NIKE", "VCK2E-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");
        await PutProduct("PRD-VCK2E-NIKE", nike, "40", "VCK2E-NIKE-40");
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("erp");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(null, "PRD-VCK2E-NIKE", "VCK2E-NIKE-REF", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("VCK2E-NIKE-REF", body.GetProperty("error").GetString());
        Assert.Equal([nike], BlockedModelIds(body));
    }

    // ── 2e. Línea SIN modelId con producto VISIBLE: el pedido se crea con el modelId
    // derivado (y en modo portal se re-tarifica con la oferta de ese modelo).

    [Fact]
    public async Task CheckoutLineaSinModelId_ProductoVisible_Crea_ModoPortal()
    {
        const string clientId = "VISCK2FC-0000-4000-9000-000000000041";
        const string adidas = "visck2fa-0000-4000-9000-000000000042";

        await PutModel(adidas, "VISCK2F ADIDAS", "VCK2F-ADI-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutProduct("PRD-VCK2F-ADI", adidas, "40", "VCK2F-ADI-40");
        await PutOffer("visck2fo-0000-4000-9000-000000000043", adidas, 40m);
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("portal");

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(null, "PRD-VCK2F-ADI", "VCK2F-ADI-REF", 0.01m) }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40m, body.GetProperty("total").GetDecimal());
        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(adidas, line.GetProperty("modelId").GetString());
    }

    // ── 3. Comprar solo lo visible funciona (modo portal, con re-tarificación real) ──

    [Fact]
    public async Task CheckoutPermite_SoloAdidas()
    {
        const string clientId = "VISCK3CL-0000-4000-9000-000000000005";
        const string adidas = "visck3a0-0000-4000-9000-000000000006";

        await PutModel(adidas, "VISCK3 ADIDAS", "VCK3-ADI-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutProduct("PRD-VCK3-ADI", adidas, "40", "VCK3-ADI-40");
        await PutOffer("visck3of-0000-4000-9000-000000000007", adidas, 40m);
        await PutClientVisibility(clientId, """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var token = await ClientTokenAsync(clientId);
        await SetOrdersModeAsync("portal");

        // El precio de la línea (0,01) lo debe pisar la re-tarificación en servidor: la
        // validación de visibilidad, colocada ANTES, no debe romper ese pipeline.
        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(adidas, "PRD-VCK3-ADI", "VCK3-ADI-REF", 0.01m) }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40m, body.GetProperty("total").GetDecimal());
    }

    // ── 4. Selector de modelos del agente (GET /api/agent/catalog-models) filtrado ──

    [Fact]
    public async Task CatalogModelsDelAgenteFiltrado()
    {
        const string restrictedAgentId = "VISCK4AG-0000-4000-9000-000000000008";
        const string openAgentId = "VISCK4AG-0000-4000-9000-000000000009";
        const string adidas = "visck4a0-0000-4000-9000-000000000010";
        const string nike = "visck4n0-0000-4000-9000-000000000011";

        await PutModel(adidas, "VISCK4 ADIDAS", "VCK4-ADI-REF", "calzado", """{"Marca":"ADIDAS"}""");
        await PutModel(nike, "VISCK4 NIKE", "VCK4-NIKE-REF", "calzado", """{"Marca":"NIKE"}""");

        // Agente con reglas: solo ve ADIDAS
        var restrictedToken = await AgentTokenAsync(
            restrictedAgentId, "comercial-visck4-restringido@agente.test",
            """[{"attributeId":"marca","valueIds":["adidas"]}]""");
        var restrictedBody = await (await Send(HttpMethod.Get, "/api/agent/catalog-models?take=100", restrictedToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        var restrictedRefs = restrictedBody.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("reference").GetString()).ToList();
        Assert.Contains("VCK4-ADI-REF", restrictedRefs);
        Assert.DoesNotContain("VCK4-NIKE-REF", restrictedRefs);

        // Agente sin reglas: ve de todo (incluidos ambos modelos de esta prueba)
        var openToken = await AgentTokenAsync(openAgentId, "comercial-visck4-abierto@agente.test");
        var openBody = await (await Send(HttpMethod.Get, "/api/agent/catalog-models?take=100", openToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        var openRefs = openBody.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("reference").GetString()).ToList();
        Assert.Contains("VCK4-ADI-REF", openRefs);
        Assert.Contains("VCK4-NIKE-REF", openRefs);
    }

    // ── 5. Atribución del pedido al agente creador (Multiagente §7, Tarea 6) ────────

    // Lee el JSON que el portal habría mandado a BC para el evento "shoes.purchase_order.
    // updated" (PayloadJson, ya transformado con el transformer de "Orden de compra" — que
    // copia $.saleId literal, así que refleja el SourceJson.Order() de origen). En test no
    // hay Conexión BC configurada, así que el despacho queda "simulated" (NotificationDispatcher.
    // DispatchBcAsync) pero SÍ registra el JSON que se habría enviado: es el mecanismo real
    // para inspeccionar el saliente sin BC real. Se localiza por "customerId" (= clientId,
    // un GUID de prueba único) para no cruzarse con los pedidos de otros tests de esta clase.
    private async Task<JsonElement> LastOutboundOrderPayloadAsync(string clientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.NotificationLogs
            .Where(l => l.EventKey == "shoes.purchase_order.updated" && l.ChannelType == "business-central"
                && l.PayloadJson != null && l.PayloadJson.Contains(clientId))
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("simulated", log!.Status);   // BC no configurado en test: se registra, no se manda
        return JsonDocument.Parse(log.PayloadJson!).RootElement.Clone();
    }

    // Doc nativo "order" (Tarea 6b, auditoría §7): el mismo que ve /manage y /orders,
    // guardado por SyncEndpoints.IngestDocumentAsync — se lee del scope de BD, igual que
    // LastOutboundOrderPayloadAsync lee el saliente simulado.
    private async Task<JsonElement> NativeOrderPayloadAsync(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.SyncDocuments.SingleOrDefaultAsync(d => d.EntityType == "order" && d.ExternalId == orderId);
        Assert.NotNull(doc);
        return JsonDocument.Parse(doc!.Payload).RootElement.Clone();
    }

    [Fact]
    public async Task PedidoDeAgente_LlevaSaleIdDelCreador()
    {
        const string agentId = "VISCK5AG-0000-4000-9000-000000000001";
        const string clientId = "VISCK5CL-0000-4000-9000-000000000002";
        const string model = "visck5m0-0000-4000-9000-000000000003";

        await PutModel(model, "VISCK5 MODELO", "VCK5-MOD-REF", "calzado", "{}");
        await PutProduct("PRD-VCK5-MOD", model, "40", "VCK5-MOD-40");
        await PutOffer("visck5of-0000-4000-9000-000000000004", model, 40m);
        await Put($"/api/clients/{clientId}", """{"name":"Cliente agente","canShop":true}""");
        await SetOrdersModeAsync("portal");

        var agentToken = await AgentTokenAsync(agentId, "comercial-visck5@agente.test", clientIds: [clientId]);
        var impersonatedToken = await ImpersonateAsync(agentToken, clientId);

        var response = await Send(HttpMethod.Post, "/api/portal/orders", impersonatedToken, new
        {
            windowId = "reposic",
            lines = new[] { Line(model, "PRD-VCK5-MOD", "VCK5-MOD-REF", 40m) }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = body.GetProperty("id").GetString()!;

        var payload = await LastOutboundOrderPayloadAsync(clientId);
        Assert.Equal(agentId, payload.GetProperty("saleId").GetString());

        // Auditoría (Tarea 6b, §7): el doc nativo (el que ve /manage) también lleva el
        // agente creador, mismo valor que el saliente a BC.
        var nativeDoc = await NativeOrderPayloadAsync(orderId);
        Assert.Equal(agentId, nativeDoc.GetProperty("saleId").GetString());
    }

    [Fact]
    public async Task PedidoDeClienteNormal_SaleIdVacio()
    {
        const string clientId = "VISCK6CL-0000-4000-9000-000000000001";
        const string model = "visck6m0-0000-4000-9000-000000000002";

        await PutModel(model, "VISCK6 MODELO", "VCK6-MOD-REF", "calzado", "{}");
        await PutProduct("PRD-VCK6-MOD", model, "40", "VCK6-MOD-40");
        await PutOffer("visck6of-0000-4000-9000-000000000003", model, 40m);
        await Put($"/api/clients/{clientId}", """{"name":"Cliente normal","canShop":true}""");
        await SetOrdersModeAsync("portal");

        var token = await ClientTokenAsync(clientId);

        var response = await Send(HttpMethod.Post, "/api/portal/orders", token, new
        {
            windowId = "reposic",
            lines = new[] { Line(model, "PRD-VCK6-MOD", "VCK6-MOD-REF", 40m) }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = body.GetProperty("id").GetString()!;

        var payload = await LastOutboundOrderPayloadAsync(clientId);
        Assert.Equal("", payload.GetProperty("saleId").GetString());

        var nativeDoc = await NativeOrderPayloadAsync(orderId);
        Assert.Equal("", nativeDoc.GetProperty("saleId").GetString());
    }

    // ── 6. Integridad: el pedido exige un cliente de ámbito (Tarea 6b) ──────────────
    // Un agente que NO ha suplantado (o un admin/integración) no tiene ClientId de
    // ámbito: sin este guard, POST /api/portal/orders colaba un Cart con ClientId=null
    // (visible solo para su propio UserId, pero de todos modos inconsistente: un pedido
    // sin cliente no tiene a quién atribuirse en BC ni en /manage).

    [Fact]
    public async Task PedidoSinCliente_AgenteSinSuplantar_400()
    {
        const string agentId = "VISCK7AG-0000-4000-9000-000000000001";
        const string clientId = "VISCK7CL-0000-4000-9000-000000000002";
        const string model = "visck7m0-0000-4000-9000-000000000003";

        await PutModel(model, "VISCK7 MODELO", "VCK7-MOD-REF", "calzado", "{}");
        await PutProduct("PRD-VCK7-MOD", model, "40", "VCK7-MOD-40");
        await PutOffer("visck7of-0000-4000-9000-000000000004", model, 40m);
        await Put($"/api/clients/{clientId}", """{"name":"Cliente del agente","canShop":true}""");
        await SetOrdersModeAsync("portal");

        // Cartera con un cliente, pero SIN /api/agent/impersonate: el token de agente no
        // lleva clientId, así que PortalScope.ActorAsync deja ClientId a null.
        var agentToken = await AgentTokenAsync(agentId, "comercial-visck7@agente.test", clientIds: [clientId]);

        var response = await Send(HttpMethod.Post, "/api/portal/orders", agentToken, new
        {
            windowId = "reposic",
            lines = new[] { Line(model, "PRD-VCK7-MOD", "VCK7-MOD-REF", 40m) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("El pedido necesita un cliente: entra como cliente o suplanta a uno.",
            body.GetProperty("error").GetString());

        // Nada de SaveChanges: el agente (sin cliente de ámbito) no ve ningún pedido suyo.
        var orders = await (await Send(HttpMethod.Get, "/api/portal/orders", agentToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, orders.GetProperty("total").GetInt32());
    }
}
