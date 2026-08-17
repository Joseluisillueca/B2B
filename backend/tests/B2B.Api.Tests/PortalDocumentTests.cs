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

// Fase 3 del portal: pedidos (03-orders.png), albaranes (04-delivery-notes.png) y
// facturas (05-invoices.png) proyectados desde los payloads que el conector ya deja
// en sync_documents (contrato 05 y contrato 04 §5).
//
// Regla que atraviesa todo el bloque: el clientId sale SIEMPRE del token. Nunca se
// acepta por parámetro y un usuario del cliente A no ve un documento del cliente B.
public class PortalDocumentTests : IClassFixture<PortalDocumentTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "DOC0000A-1111-4111-8111-000000000001";
    private const string ClientB = "DOC0000B-2222-4222-8222-000000000002";
    private const string OtherEmail = "otra-tienda@documentos-b.test";
    private const string OtherPassword = "otra-clave-456";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PortalDocumentTests(Factory factory)
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

            await PutAsync($"/api/clients/{ClientA}",
                """{"name":"TEST 5","externalReference":"C100057","canShop":true,"groupIds":[]}""");
            await PutAsync($"/api/clients/{ClientB}",
                """{"name":"OTRA TIENDA","externalReference":"C100099","canShop":true,"groupIds":[]}""");
            await PutAsync($"/api/clients/{ClientA}/users/admin",
                $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test 5","culture":"es_ES"}""");
            await PutAsync($"/api/clients/{ClientB}/users/admin",
                $$"""{"email":"{{OtherEmail}}","name":"Otra","culture":"es_ES"}""");

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var other = await db.Users.SingleAsync(u => u.Email == OtherEmail);
                other.PasswordHash = new PasswordHasher<AppUser>().HashPassword(other, OtherPassword);
                await db.SaveChangesAsync();
            }

            // ── Pedidos del cliente A: uno por estado del rail de 03-orders.png ──
            await PutAsync("/api/orders/ORD-OPEN", Order("ORD-OPEN", ClientA, "PV00001", "2026-08-02",
                status: "open", purchaseOrderId: "PO-CLIENTE-001", quantity: 10, total: 1210.0m, season: "SS26"));
            await PutAsync("/api/orders/ORD-PARTIAL", Order("ORD-PARTIAL", ClientA, "PV00002", "2026-07-20",
                status: "partially-shipped", purchaseOrderId: "PO-CLIENTE-002", quantity: 6, total: 726.0m));
            await PutAsync("/api/orders/ORD-SHIPPED", Order("ORD-SHIPPED", ClientA, "PV00003", "2026-06-15",
                status: "shipped", purchaseOrderId: "PO-CLIENTE-003", quantity: 4, total: 484.0m));
            await PutAsync("/api/orders/ORD-INVOICED", Order("ORD-INVOICED", ClientA, "PV00004", "2026-05-11",
                status: "invoiced", purchaseOrderId: "PO-CLIENTE-004", quantity: 2, total: 242.0m));
            await PutAsync("/api/orders/ORD-CANCELED", Order("ORD-CANCELED", ClientA, "PV00005", "2026-04-03",
                status: "canceled", purchaseOrderId: "PO-CLIENTE-005", quantity: 1, total: 121.0m));

            // Pedido de devolución (contrato 05 §6): mismo endpoint, type NOT_DEFINED
            // e importes negativos. No es un pedido: /orders no debe listarlo.
            await PutAsync("/api/orders/ORD-RETURN", Order("ORD-RETURN", ClientA, "PD-2400007", "2026-08-05",
                status: "open", purchaseOrderId: "DEV-CLIENTE-001", quantity: -2, total: -121.0m,
                type: "NOT_DEFINED"));

            await PutAsync("/api/orders/ORD-OTHER", Order("ORD-OTHER", ClientB, "PV09999", "2026-08-01",
                status: "open", purchaseOrderId: "PO-AJENO", quantity: 3, total: 363.0m));

            // ── Albaranes ────────────────────────────────────────────────────────
            await PutAsync("/api/documents/delivery-notes/DN-TRACK", DeliveryNote("DN-TRACK", ClientA,
                "AV-2400123", "2026-07-15", invoiced: false, total: 121.0m,
                track: "https://www.seur.com/livetracking/?segOnlineIdentificador=1234567890"));
            await PutAsync("/api/documents/delivery-notes/DN-PLAIN", DeliveryNote("DN-PLAIN", ClientA,
                "AV-2400124", "2026-07-01", invoiced: true, total: 242.0m, track: ""));
            await PutAsync("/api/documents/delivery-notes/DN-OTHER", DeliveryNote("DN-OTHER", ClientB,
                "AV-9999999", "2026-07-10", invoiced: false, total: 99.0m, track: ""));

            // ── Facturas ─────────────────────────────────────────────────────────
            await PutAsync("/api/documents/invoices/INV-PAID", Invoice("INV-PAID", ClientA,
                "FV-2400089", "2026-05-31", "Paid", total: 242.0m, dueDate: "2026-06-30"));
            await PutAsync("/api/documents/invoices/INV-OVERDUE", Invoice("INV-OVERDUE", ClientA,
                "FV-2400090", "2026-06-30", "Unpaid", total: 484.0m, dueDate: "2026-07-30"));
            await PutAsync("/api/documents/invoices/INV-PENDING", Invoice("INV-PENDING", ClientA,
                "FV-2400091", "2026-08-01", "Unpaid", total: 121.0m, dueDate: "2099-12-31"));
            await PutAsync("/api/documents/invoices/INV-OTHER", Invoice("INV-OTHER", ClientB,
                "FV-9999999", "2026-06-01", "Unpaid", total: 55.0m, dueDate: "2099-12-31"));

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    // Payloads con la forma real del conector (contrato 04 §5.1 y contrato 05 §2/§4)
    private static string Order(
        string id, string clientId, string number, string date, string status,
        string purchaseOrderId, decimal quantity, decimal total, string type = "SCHEDULED",
        string season = "") => $$"""
        {
          "id": "{{id}}",
          "clientId": "{{clientId}}",
          "paid": false,
          "transportId": "",
          "payMethodId": "transf30",
          "reference": "Referencia del cliente",
          "observations": "",
          "totals": {
            "totalAmount":   { "code": "EUR", "value": {{Number(total / 1.21m)}} },
            "totalDiscount": { "code": "EUR", "value": 0 },
            "totalTax":      { "code": "EUR", "value": {{Number(total - total / 1.21m)}} },
            "total":         { "code": "EUR", "value": {{Number(total)}} }
          },
          "status": "{{status}}",
          "items": [
            {
              "id": "{{id}}-L1",
              "productId": "AABBCCDD-EEFF-4011-2233-445566778899",
              "productName": { "es_ES": "Zapatilla Runner Azul 42", "en_EN": "Blue Runner 42",
                               "fr_FR": "Zapatilla Runner Azul 42", "it_IT": "Zapatilla Runner Azul 42" },
              "transactionInfo": {
                "info": {
                  "quantity": {{Number(quantity)}},
                  "discount": 5.0,
                  "price":  { "code": "EUR", "value": 100.0 },
                  "amount": { "code": "EUR", "value": {{Number(total / 1.21m)}} }
                },
                "totalDiscounts": { "code": "EUR", "value": 0 },
                "totalTaxes":     { "code": "EUR", "value": {{Number(total - total / 1.21m)}} },
                "taxes": [ { "id": "IVA", "percent": 21.0, "productTaxId": "IVA21" } ],
                "discounts": [], "priceOriginal": null, "offerDiscounts": null
              },
              "clientReference": "C100057",
              "shipDate": "{{date}}T00:00:00",
              "status": "Open",
              "productExternalReference": "ART001",
              "additionalValues": null,
              "quantityDelivered": 0,
              "productInfo": {
                "name": { "es_ES": "Zapatilla Runner Azul 42" },
                "brandId": "", "ean": "8412345678901", "externalReference": "ART001",
                "id": "AABBCCDD-EEFF-4011-2233-445566778899", "image": null,
                "modelId": "00FFEEDD-CCBB-4A99-8877-665544332211",
                "modelExternalReference": "ART001", "sku": "ART00142"
              },
              "stockServiceId": ""
            }
          ],
          "payments": [],
          "type": "{{type}}",
          "source": "ERP",
          "externalReference": "{{number}}",
          "orderedDate": "{{date}}T00:00:00",
          "needRecalculateTotals": true,
          "marketId": "es",
          "clienteExternalReference": "C100057",
          "shippingAddressExternalReference": "",
          "orderDiscount": null,
          "totalWithTransport": { "code": "EUR", "value": {{Number(total)}} },
          "purchaseOrderId": "{{purchaseOrderId}}",
          "seasonId": "{{season}}"
        }
        """;

    private static string DeliveryNote(
        string id, string clientId, string number, string date, bool invoiced,
        decimal total, string track) => $$"""
        {
          "clientId": "{{clientId}}",
          "shippingAddress": {
            "streetAddress": "Calle Mayor 1", "num": "12", "description": "Planta 2",
            "city": "Madrid", "province": "Madrid", "zipCode": "28001", "countryIsoId": "ES",
            "geo": { "latitude": 0, "longitude": 0 },
            "contact": { "name": "Ana Pérez", "lastName": "", "company": "Tienda Central S.L.", "phones": [] }
          },
          "number": "{{number}}",
          "transportId": "SEUR",
          "transportUrlTrack": "{{track}}",
          "paymethodId": "transferencia",
          "deliveryDate": "{{date}}T00:00:00.000Z",
          "isInvoiced": {{(invoiced ? "true" : "false")}},
          "observations": "", "documentUrl": "",
          "totals": {
            "totalAmount":   { "code": "EUR", "value": {{Number(total / 1.21m)}} },
            "totalDiscount": { "code": "EUR", "value": 0 },
            "totalTax":      { "code": "EUR", "value": {{Number(total - total / 1.21m)}} },
            "total":         { "code": "EUR", "value": {{Number(total)}} }
          },
          "type": "SCHEDULED",
          "lines": [
            {
              "id": "{{id}}-L1",
              "productId": "B7C8D9E0-0000-0000-0000-000000000002",
              "externalReference": "ART001",
              "productInfo": {
                "name": { "es_ES": "Zapato piel 40", "en_EN": "Leather shoe 40" },
                "ean": "8412345678905", "externalReference": "ART001",
                "modelExternalReference": "ART001", "sku": "ART00140"
              },
              "transactionInfo": {
                "info": { "quantity": 2, "discount": 0,
                          "price": { "code": "EUR", "value": 50.0 },
                          "amount": { "code": "EUR", "value": 100.0 } },
                "totalDiscounts": { "code": "EUR", "value": 0 },
                "totalTaxes": { "code": "EUR", "value": 21.0 },
                "taxes": [ { "id": "IVA", "percent": 21, "productTaxId": "IVA21" } ]
              }
            }
          ],
          "idProvider": "VEND01",
          "status": "delivered",
          "clientName": "TEST 5"
        }
        """;

    private static string Invoice(
        string id, string clientId, string number, string date, string status,
        decimal total, string dueDate) => $$"""
        {
          "clientId": "{{clientId}}",
          "fiscalInfo": { "alias": "TEST 5", "fiscalName": "TEST 5",
                          "fiscalId": { "type": "nif", "document": "B12345678" } },
          "number": "{{number}}",
          "payMethodName": { "es_ES": "Transferencia bancaria", "en_EN": "Bank transfer",
                             "fr_FR": "Virement", "it_IT": "Bonifico" },
          "issueDate": "{{date}}T00:00:00.000Z",
          "status": "{{status}}",
          "observations": "", "documentUrl": "",
          "totals": {
            "totalAmount":   { "code": "EUR", "value": {{Number(total / 1.21m)}} },
            "totalDiscount": { "code": "EUR", "value": 0 },
            "totalTax":      { "code": "EUR", "value": {{Number(total - total / 1.21m)}} },
            "total":         { "code": "EUR", "value": {{Number(total)}} }
          },
          "lines": [
            {
              "id": "{{id}}-L1",
              "productId": "B7C8D9E0-0000-0000-0000-000000000002",
              "productName": { "es_ES": "Zapato piel 40", "en_EN": "Leather shoe 40" },
              "deliveryNoteLineId": "",
              "transactionInfo": {
                "info": { "quantity": 2, "discount": 0,
                          "price": { "code": "EUR", "value": 50.0 },
                          "amount": { "code": "EUR", "value": 100.0 } },
                "totalDiscounts": { "code": "EUR", "value": 0 },
                "totalTaxes": { "code": "EUR", "value": 21.0 },
                "taxes": [ { "id": "IVA", "percent": 21, "productTaxId": "IVA21" } ]
              },
              "productInfo": {
                "name": { "es_ES": "Zapato piel 40" }, "ean": "8412345678905",
                "externalReference": "ART001", "modelExternalReference": "ART001", "sku": "ART00140"
              }
            }
          ],
          "payments": [
            { "paymentInfo": "", "dueDate": "{{dueDate}}T00:00:00.000Z",
              "emittedAt": "{{date}}T00:00:00.000Z",
              "amount": { "code": "EUR", "value": {{Number(total)}} } }
          ]
        }
        """;

    private static string Number(decimal value) =>
        Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ── Utilidades HTTP ───────────────────────────────────────────────────────

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<string> OtherTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = OtherEmail, password = OtherPassword, type = "global", longDuration = true });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> GetAsync(string route, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", token ?? await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> JsonAsync(string route, string? token = null)
    {
        var response = await GetAsync(route, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] Numbers(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("number").GetString()!)];

    // ══════════════ Pedidos ══════════════

    [Fact]
    public async Task Pedidos_SinDocumentos_DevuelveVacio()
    {
        // Fábrica limpia: la vista pinta "No se han encontrado resultados"
        await using var factory = new Factory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await factory.GetTokenAsync(client));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Pedidos_ProyectaLasColumnasDeLaCaptura()
    {
        var body = await JsonAsync("/api/portal/orders");
        var row = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("number").GetString() == "PV00001");

        // N. DE PEDIDO · FECHA · REF. DE CLIENTE · TIPO · TOTAL UNIDADES · IMPORTE · ESTADO
        Assert.Equal("ORD-OPEN", row.GetProperty("id").GetString());
        Assert.StartsWith("2026-08-02", row.GetProperty("date").GetString());
        Assert.Equal("PO-CLIENTE-001", row.GetProperty("reference").GetString());
        Assert.Equal("SCHEDULED", row.GetProperty("type").GetString());
        Assert.Equal(10m, row.GetProperty("units").GetDecimal());
        Assert.Equal(1210.0m, row.GetProperty("total").GetDecimal());
        Assert.Equal("EUR", row.GetProperty("currency").GetString());
        Assert.Equal("open", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pedidos_ExcluyeDevoluciones()
    {
        var body = await JsonAsync("/api/portal/orders");
        Assert.DoesNotContain("PD-2400007", Numbers(body));
        Assert.DoesNotContain("DEV-CLIENTE-001", body.GetRawText());
        // 5 pedidos del cliente A; la devolución no cuenta en el rail
        Assert.Equal(5, body.GetProperty("counts").GetProperty("all").GetInt32());
    }

    [Fact]
    public async Task Pedidos_ElRailCuentaCadaEstadoYFiltra()
    {
        var counts = (await JsonAsync("/api/portal/orders")).GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("open").GetInt32());
        Assert.Equal(1, counts.GetProperty("partially-shipped").GetInt32());
        Assert.Equal(1, counts.GetProperty("shipped").GetInt32());
        Assert.Equal(1, counts.GetProperty("invoiced").GetInt32());
        Assert.Equal(1, counts.GetProperty("canceled").GetInt32());

        var invoiced = await JsonAsync("/api/portal/orders?status=invoiced");
        Assert.Equal(["PV00004"], Numbers(invoiced));
        // El rail no se recalcula con el estado elegido: sigue enseñando el universo
        Assert.Equal(5, invoiced.GetProperty("counts").GetProperty("all").GetInt32());
    }

    [Fact]
    public async Task Pedidos_BuscaPorNumeroYPorReferenciaDeCliente()
    {
        Assert.Equal(["PV00003"], Numbers(await JsonAsync("/api/portal/orders?search=PV00003")));
        Assert.Equal(["PV00002"], Numbers(await JsonAsync("/api/portal/orders?search=po-cliente-002")));
        Assert.Empty(Numbers(await JsonAsync("/api/portal/orders?search=no-existe")));
    }

    [Fact]
    public async Task Pedidos_FiltraPorRangoDeFechas()
    {
        var body = await JsonAsync("/api/portal/orders?from=2026-06-01&to=2026-07-31");
        Assert.Equal(["PV00002", "PV00003"], Numbers(body).Order().ToArray());
    }

    [Fact]
    public async Task Pedidos_TemporadaSoloSaleCuandoBcLaManda()
    {
        var body = await JsonAsync("/api/portal/orders");
        Assert.Equal(["SS26"], body.GetProperty("seasons").EnumerateArray().Select(s => s.GetString()).ToArray());
        Assert.Equal(["PV00001"], Numbers(await JsonAsync("/api/portal/orders?season=SS26")));
    }

    [Fact]
    public async Task Pedidos_PaginaConLosMasRecientesPrimero()
    {
        var first = await JsonAsync("/api/portal/orders?skip=0&take=2");
        Assert.Equal(5, first.GetProperty("total").GetInt32());
        Assert.Equal(["PV00001", "PV00002"], Numbers(first));

        var second = await JsonAsync("/api/portal/orders?skip=2&take=2");
        Assert.Equal(["PV00003", "PV00004"], Numbers(second));
    }

    [Fact]
    public async Task Pedidos_DetalleDevuelveLasLineasConTallaCantidadYPrecio()
    {
        var body = await JsonAsync("/api/portal/orders/ORD-OPEN");
        Assert.Equal("PV00001", body.GetProperty("number").GetString());
        Assert.Equal("open", body.GetProperty("status").GetString());

        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal("Zapatilla Runner Azul 42", line.GetProperty("name").GetString());
        Assert.Equal("42", line.GetProperty("size").GetString());          // sku ART00142 − ref ART001
        Assert.Equal("ART001", line.GetProperty("reference").GetString());
        Assert.Equal(10m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(0m, line.GetProperty("quantityDelivered").GetDecimal());
        Assert.Equal(100.0m, line.GetProperty("price").GetDecimal());
        Assert.Equal(5.0m, line.GetProperty("discount").GetDecimal());
        Assert.Equal(21.0m, line.GetProperty("taxPercent").GetDecimal());
        Assert.Equal("Open", line.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pedidos_DetalleTraduceElNombreDeLinea()
    {
        var body = await JsonAsync("/api/portal/orders/ORD-OPEN?locale=en");
        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal("Blue Runner 42", line.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Pedidos_UnClienteNoVeLosDeOtro()
    {
        var token = await OtherTokenAsync();

        var theirs = await JsonAsync("/api/portal/orders", token);
        Assert.Equal(["PV09999"], Numbers(theirs));
        Assert.DoesNotContain("PV00001", theirs.GetRawText());

        // Ni por id directo: el documento ajeno simplemente no existe para este token
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("/api/portal/orders/ORD-OPEN", token)).StatusCode);
    }

    [Fact]
    public async Task Pedidos_ExportaCsvConLasColumnasVisiblesYLosFiltrosAplicados()
    {
        var response = await GetAsync("/api/portal/orders/export.csv?status=invoiced");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

        var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains("N. DE PEDIDO", csv);
        Assert.Contains("TOTAL UNIDADES", csv);
        Assert.Contains("PV00004", csv);
        Assert.DoesNotContain("PV00001", csv);    // el filtro del listado viaja al CSV
    }

    // ══════════════ Albaranes ══════════════

    [Fact]
    public async Task Albaranes_ProyectanLasColumnasDeLaCaptura()
    {
        var body = await JsonAsync("/api/portal/delivery-notes");
        var row = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("number").GetString() == "AV-2400123");

        // REFERENCIA · FECHA · FACTURADO · IMPORTE · DIRECCIÓN · ENLACE TRASNPORTE
        Assert.Equal("DN-TRACK", row.GetProperty("id").GetString());
        Assert.StartsWith("2026-07-15", row.GetProperty("date").GetString());
        Assert.False(row.GetProperty("isInvoiced").GetBoolean());
        Assert.Equal(121.0m, row.GetProperty("total").GetDecimal());
        Assert.Contains("Madrid", row.GetProperty("address").GetString());
        Assert.Equal("SEUR", row.GetProperty("transportId").GetString());
        Assert.StartsWith("https://www.seur.com/", row.GetProperty("transportUrlTrack").GetString());
    }

    [Fact]
    public async Task Albaranes_SinSeguimiento_DejanLaCeldaVacia()
    {
        var body = await JsonAsync("/api/portal/delivery-notes");
        var row = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("number").GetString() == "AV-2400124");
        Assert.Equal("", row.GetProperty("transportUrlTrack").GetString());
    }

    [Fact]
    public async Task Albaranes_ElRailSeparaFacturadosDeNoFacturados()
    {
        var body = await JsonAsync("/api/portal/delivery-notes");
        var counts = body.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("all").GetInt32());
        Assert.Equal(1, counts.GetProperty("invoiced").GetInt32());
        Assert.Equal(1, counts.GetProperty("not-invoiced").GetInt32());

        Assert.Equal(["AV-2400124"], Numbers(await JsonAsync("/api/portal/delivery-notes?status=invoiced")));
        Assert.Equal(["AV-2400123"], Numbers(await JsonAsync("/api/portal/delivery-notes?status=not-invoiced")));
    }

    [Fact]
    public async Task Albaranes_UnClienteNoVeLosDeOtro()
    {
        var token = await OtherTokenAsync();
        var theirs = await JsonAsync("/api/portal/delivery-notes", token);
        Assert.Equal(["AV-9999999"], Numbers(theirs));
        Assert.Equal(HttpStatusCode.NotFound,
            (await GetAsync("/api/portal/delivery-notes/DN-TRACK", token)).StatusCode);
    }

    [Fact]
    public async Task Albaranes_DetalleYCsv()
    {
        var detail = await JsonAsync("/api/portal/delivery-notes/DN-TRACK");
        Assert.Equal("AV-2400123", detail.GetProperty("number").GetString());
        var line = Assert.Single(detail.GetProperty("lines").EnumerateArray());
        Assert.Equal("40", line.GetProperty("size").GetString());
        Assert.Equal(2m, line.GetProperty("quantity").GetDecimal());

        var response = await GetAsync("/api/portal/delivery-notes/export.csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("ENLACE TRASNPORTE", csv);   // errata literal del portal actual
        Assert.Contains("AV-2400123", csv);
        Assert.DoesNotContain("AV-9999999", csv);
    }

    // ══════════════ Facturas ══════════════

    [Fact]
    public async Task Facturas_ProyectanImporteDeudaYEstado()
    {
        var body = await JsonAsync("/api/portal/invoices");
        var rows = body.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("number").GetString()!, i => i);

        // Nº DE FACTURA · FECHA · FORMA DE PAGO · IMPORTE · DEUDA PENDIENTE · ESTADO
        var paid = rows["FV-2400089"];
        Assert.StartsWith("2026-05-31", paid.GetProperty("date").GetString());
        Assert.Equal("Transferencia bancaria", paid.GetProperty("payMethod").GetString());
        Assert.Equal(242.0m, paid.GetProperty("total").GetDecimal());
        Assert.Equal(0m, paid.GetProperty("debt").GetDecimal());
        Assert.Equal("paid", paid.GetProperty("status").GetString());

        // Impagada con vencimiento pasado → Vencida
        var overdue = rows["FV-2400090"];
        Assert.Equal(484.0m, overdue.GetProperty("debt").GetDecimal());
        Assert.Equal("overdue", overdue.GetProperty("status").GetString());

        // Impagada dentro de plazo → Pendiente de cobro
        Assert.Equal("pending", rows["FV-2400091"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Facturas_TraducenLaFormaDePago()
    {
        var body = await JsonAsync("/api/portal/invoices?locale=it");
        var row = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("number").GetString() == "FV-2400089");
        Assert.Equal("Bonifico", row.GetProperty("payMethod").GetString());
    }

    [Fact]
    public async Task Facturas_ElRailCuentaYFiltra()
    {
        var body = await JsonAsync("/api/portal/invoices");
        var counts = body.GetProperty("counts");
        Assert.Equal(3, counts.GetProperty("all").GetInt32());
        Assert.Equal(1, counts.GetProperty("overdue").GetInt32());
        Assert.Equal(1, counts.GetProperty("paid").GetInt32());
        Assert.Equal(1, counts.GetProperty("pending").GetInt32());
        // Parcial y A Crédito existen en el rail de 05-invoices.png aunque el
        // contrato de BC todavía no los pueda producir: se enseñan a cero.
        Assert.Equal(0, counts.GetProperty("partial").GetInt32());
        Assert.Equal(0, counts.GetProperty("credit").GetInt32());

        Assert.Equal(["FV-2400090"], Numbers(await JsonAsync("/api/portal/invoices?status=overdue")));
    }

    [Fact]
    public async Task Facturas_OrdenanPorImporteYPorFecha()
    {
        Assert.Equal(["FV-2400091", "FV-2400089", "FV-2400090"],
            Numbers(await JsonAsync("/api/portal/invoices?sort=amount-asc")));
        Assert.Equal(["FV-2400089", "FV-2400090", "FV-2400091"],
            Numbers(await JsonAsync("/api/portal/invoices?sort=date-asc")));
    }

    [Fact]
    public async Task Facturas_UnClienteNoVeLasDeOtro()
    {
        var token = await OtherTokenAsync();
        var theirs = await JsonAsync("/api/portal/invoices", token);
        Assert.Equal(["FV-9999999"], Numbers(theirs));
        Assert.Equal(HttpStatusCode.NotFound,
            (await GetAsync("/api/portal/invoices/INV-PAID", token)).StatusCode);
    }

    [Fact]
    public async Task Facturas_DetalleYCsv()
    {
        var detail = await JsonAsync("/api/portal/invoices/INV-PAID");
        Assert.Equal("FV-2400089", detail.GetProperty("number").GetString());
        Assert.Equal(0m, detail.GetProperty("debt").GetDecimal());
        Assert.Single(detail.GetProperty("lines").EnumerateArray());

        var response = await GetAsync("/api/portal/invoices/export.csv?status=paid");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("DEUDA PENDIENTE", csv);
        Assert.Contains("FV-2400089", csv);
        Assert.DoesNotContain("FV-2400090", csv);
    }

    // ══════════════ Acceso ══════════════

    [Fact]
    public async Task Documentos_SinToken_Devuelven401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/delivery-notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/portal/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/portal/orders/export.csv")).StatusCode);
    }
}
