using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Un pedido hecho en el portal y el mismo pedido devuelto por Business Central son UN
// SOLO documento: el conector lo reenvía con el id del portal (campo "B2B Sync Id"), que
// es justo lo que evita el duplicado. Como el upsert reemplaza el payload ENTERO, sin
// guarda la primera vuelta de BC borraría lo que el ERP no conoce.
//
// Contrato de la guarda (Sync/PortalOrderGuard): solo protege documentos "order" nacidos
// en el portal (source == "portal") y solo cuando el valor entrante viene VACÍO. Si BC
// manda un valor de verdad, manda BC — así esto convive con que el conector acabe
// devolviendo esos campos: en cuanto los devuelva, la guarda deja de actuar.
public class PortalOrderGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PortalOrderGuardTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<JsonObject> StoredAsync(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.SyncDocuments.SingleAsync(d => d.EntityType == "order" && d.ExternalId == orderId);
        return (JsonNode.Parse(doc.Payload) as JsonObject)!;
    }

    // Pedido tal y como lo deja el portal (Portal/NativeOrder.Build), recortado a lo que
    // esta prueba mira.
    private static string PedidoDelPortal(string id, string clientId) =>
        $$$"""
        {"clientId":"{{{clientId}}}","externalReference":"PV-{{{id}}}","status":"open","type":"REPLENISHMENT",
         "source":"portal","saleId":"AGENTE-1","observations":"Entregar por la puerta de atrás",
         "payMethodId":"TRANSFERENCIA","reference":"REF-CLIENTE-9","purchaseOrderId":"REF-CLIENTE-9",
         "paid":true,"payments":[{"id":"PAGO-1","amount":120.5}],
         "totals":{"total":{"code":"EUR","value":120.5}}
        }
        """;

    // Lo que devuelve BC: mismo id, y los campos que el ERP no conoce, vacíos.
    private static string PedidoDeBc(string id, string clientId, string observaciones = "") =>
        $$$"""
        {"clientId":"{{{clientId}}}","externalReference":"PV-{{{id}}}","status":"open","type":"REPLENISHMENT",
         "source":"ERP","saleId":"","observations":"{{{observaciones}}}","payMethodId":"","reference":"",
         "purchaseOrderId":"","paid":false,"payments":[],"orderDiscount":null,
         "totals":{"total":{"code":"EUR","value":120.5}}
        }
        """;

    // ── 1. La vuelta de BC no puede borrar lo que puso el comprador ───────────

    [Fact]
    public async Task PedidoDelPortal_QueVuelveDeBcVacio_ConservaLoDelPortal()
    {
        const string clientId = "PGUARD01-0000-4000-9000-000000000001";
        const string orderId = "PGUARD01-0000-4000-9000-0000000000A1";

        await Put($"/api/orders/{orderId}", PedidoDelPortal(orderId, clientId));
        await Put($"/api/orders/{orderId}", PedidoDeBc(orderId, clientId));

        var stored = await StoredAsync(orderId);
        Assert.Equal("Entregar por la puerta de atrás", (string?)stored["observations"]);
        Assert.Equal("AGENTE-1", (string?)stored["saleId"]);          // el "gestionado por" sobrevive
        Assert.Equal("TRANSFERENCIA", (string?)stored["payMethodId"]);
        Assert.Equal("REF-CLIENTE-9", (string?)stored["reference"]);
        Assert.Equal("REF-CLIENTE-9", (string?)stored["purchaseOrderId"]);
        Assert.True((bool?)stored["paid"]);                            // un pedido cobrado no se descobra
        Assert.Equal(1, (stored["payments"] as JsonArray)?.Count);
        Assert.Equal("portal", (string?)stored["source"]);             // siguió naciendo en el portal
    }

    // ── 2. Si BC trae un valor de VERDAD, manda BC ────────────────────────────

    [Fact]
    public async Task PedidoDelPortal_ConDatoRealDeBc_MandaBc()
    {
        const string clientId = "PGUARD02-0000-4000-9000-000000000002";
        const string orderId = "PGUARD02-0000-4000-9000-0000000000A2";

        await Put($"/api/orders/{orderId}", PedidoDelPortal(orderId, clientId));
        await Put($"/api/orders/{orderId}", PedidoDeBc(orderId, clientId, "Anotado en almacén"));

        var stored = await StoredAsync(orderId);
        Assert.Equal("Anotado en almacén", (string?)stored["observations"]);
        Assert.Equal("AGENTE-1", (string?)stored["saleId"]);   // este seguía vacío: se conserva
    }

    // ── 3. Un pedido que NO nació en el portal se reemplaza entero ────────────
    // (la guarda es para el pedido propio; los de BC siguen siendo suyos)

    [Fact]
    public async Task PedidoDeBc_NoSeProtege_SeReemplazaEntero()
    {
        const string clientId = "PGUARD03-0000-4000-9000-000000000003";
        const string orderId = "PGUARD03-0000-4000-9000-0000000000A3";

        await Put($"/api/orders/{orderId}", $$$"""
            {"clientId":"{{{clientId}}}","externalReference":"PV-BC","status":"open","source":"ERP",
             "observations":"Nota vieja del ERP","saleId":"VIEJO","paid":true}
            """);
        await Put($"/api/orders/{orderId}", PedidoDeBc(orderId, clientId));

        var stored = await StoredAsync(orderId);
        Assert.Equal("", (string?)stored["observations"]);
        Assert.Equal("", (string?)stored["saleId"]);
        Assert.False((bool?)stored["paid"]);
        Assert.Equal("ERP", (string?)stored["source"]);
    }

    // ── 4. El pedido sigue siendo UNO: la guarda no crea documentos ───────────

    [Fact]
    public async Task DosVueltasDeBc_SiguenSiendoUnSoloPedido()
    {
        const string clientId = "PGUARD04-0000-4000-9000-000000000004";
        const string orderId = "PGUARD04-0000-4000-9000-0000000000A4";

        await Put($"/api/orders/{orderId}", PedidoDelPortal(orderId, clientId));
        await Put($"/api/orders/{orderId}", PedidoDeBc(orderId, clientId));
        await Put($"/api/orders/{orderId}", PedidoDeBc(orderId, clientId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.SyncDocuments.CountAsync(d => d.EntityType == "order" && d.ExternalId == orderId));

        // Y la guarda no se gasta: si BC pudiera cambiar `source` a "ERP", la SEGUNDA
        // vuelta dejaría de reconocer el pedido como propio y borraría lo del comprador.
        var stored = await StoredAsync(orderId);
        Assert.Equal("Entregar por la puerta de atrás", (string?)stored["observations"]);
        Assert.Equal("AGENTE-1", (string?)stored["saleId"]);
        Assert.Equal("portal", (string?)stored["source"]);
    }
}
