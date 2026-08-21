using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Fase 3: vistas agregadas de la cartera del agente (pedidos/albaranes/facturas con
// columna CLIENTE), estadísticas, calendario de citas y jerarquía admin/agente.
// Regla que atraviesa todo: un agente SOLO ve documentos y citas de SU cartera.
public class AgentPortfolioTests : IClassFixture<AgentPortfolioTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    // Cartera del agente A: dos clientes. Cartera del agente B: uno (jamás cruzado).
    private const string CP1 = "CP111111-0000-4000-9000-000000000001";
    private const string CP2 = "CP222222-0000-4000-9000-000000000002";
    private const string CP3 = "CP333333-0000-4000-9000-000000000003";
    private const string AgentA = "PORT-AGENT-A";
    private const string AgentB = "PORT-AGENT-B";
    private const string AgentAEmail = "port-a@agente.test";
    private const string AgentBEmail = "port-b@agente.test";
    private const string Pass = "agente-port-123";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AgentPortfolioTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static bool _seeded;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private async Task SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seeded) return;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            void Doc(string type, string extId, string? parent, string payload) =>
                db.SyncDocuments.Add(new SyncDocument { EntityType = type, ExternalId = extId, ParentId = parent, Payload = payload });

            Doc("client", CP1, null, ClientDoc("C900001", "Calzados Uno"));
            Doc("client", CP2, null, ClientDoc("C900002", "Deportes Dos"));
            Doc("client", CP3, null, ClientDoc("C900003", "Zapatos Tres"));

            Doc("agent", AgentA, null, AgentDoc(AgentA, AgentAEmail, "Comercial Cartera A", CP1, CP2));
            Doc("agent", AgentB, null, AgentDoc(AgentB, AgentBEmail, "Comercial Cartera B", CP3));

            // Pedidos: CP1 ×2 (uno REPLENISHMENT, uno SCHEDULED), CP2 ×1, CP3 ×1
            Doc("order", "PO-1", CP1, OrderDoc("PV0001", CP1, 1000, "REPLENISHMENT", "open"));
            Doc("order", "PO-2", CP1, OrderDoc("PV0002", CP1, 500, "SCHEDULED", "shipped"));
            Doc("order", "PO-3", CP2, OrderDoc("PV0003", CP2, 250, "REPLENISHMENT", "invoiced"));
            Doc("order", "PO-9", CP3, OrderDoc("PV9999", CP3, 999, "REPLENISHMENT", "open"));

            // Facturas del mes actual (para que entren en el rango de estadísticas)
            var month = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 15, 0, 0, 0, DateTimeKind.Utc);
            Doc("invoice", "IN-1", CP1, InvoiceDoc("F0001", CP1, 1210, paid: true, month));
            Doc("invoice", "IN-2", CP2, InvoiceDoc("F0002", CP2, 300, paid: false, month));
            Doc("invoice", "IN-9", CP3, InvoiceDoc("F9999", CP3, 800, paid: false, month));

            Doc("delivery-note", "DN-1", CP1, DeliveryDoc("A0001", CP1, 605, invoiced: true));

            var hasher = new PasswordHasher<AppUser>();
            foreach (var (email, agentId) in new[] { (AgentAEmail, AgentA), (AgentBEmail, AgentB) })
            {
                var user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = "", Role = ClientIdentity.AgentRole, AgentExternalId = agentId, Culture = "es_ES" };
                user.PasswordHash = hasher.HashPassword(user, Pass);
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
            _seeded = true;
        }
        finally { Lock.Release(); }
    }

    private async Task<HttpClient> AgentAsync(string email)
    {
        await SeedAsync();
        var token = await _factory.LoginAsync(_client, email, Pass);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    // ── Documentos agregados + aislamiento ────────────────────────────────────────

    [Fact]
    public async Task Agent_sees_orders_across_the_whole_portfolio_with_client_column()
    {
        var http = await AgentAsync(AgentAEmail);
        var page = await http.GetFromJsonAsync<JsonElement>("/api/agent/documents?type=order");

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);   // CP1 ×2 + CP2 ×1, nunca CP3
        // Cada fila trae su cliente
        var clientNames = items.Select(i => i.GetProperty("client").GetProperty("name").GetString()).ToList();
        Assert.Contains("Calzados Uno", clientNames);
        Assert.Contains("Deportes Dos", clientNames);
        Assert.DoesNotContain("Zapatos Tres", clientNames);
    }

    [Fact]
    public async Task Order_type_filter_narrows_to_selection_orders()
    {
        var http = await AgentAsync(AgentAEmail);
        var page = await http.GetFromJsonAsync<JsonElement>("/api/agent/documents?type=order&orderType=SCHEDULED");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("SCHEDULED", items[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Client_filter_scopes_to_one_client_and_rejects_foreign_ones()
    {
        var http = await AgentAsync(AgentAEmail);

        var one = await http.GetFromJsonAsync<JsonElement>($"/api/agent/documents?type=order&client={CP1}");
        Assert.Equal(2, one.GetProperty("items").GetArrayLength());

        // Cliente ajeno (de la cartera de B): no se filtra a él, se devuelve vacío
        var foreign = await http.GetFromJsonAsync<JsonElement>($"/api/agent/documents?type=order&client={CP3}");
        Assert.Equal(0, foreign.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Invoices_and_delivery_notes_aggregate_too()
    {
        var http = await AgentAsync(AgentAEmail);
        var invoices = await http.GetFromJsonAsync<JsonElement>("/api/agent/documents?type=invoice");
        Assert.Equal(2, invoices.GetProperty("items").GetArrayLength());   // CP1 + CP2
        var notes = await http.GetFromJsonAsync<JsonElement>("/api/agent/documents?type=delivery-note");
        Assert.Equal(1, notes.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Another_agent_only_sees_their_own_portfolio()
    {
        var http = await AgentAsync(AgentBEmail);
        var page = await http.GetFromJsonAsync<JsonElement>("/api/agent/documents?type=order");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);   // solo CP3
        Assert.Equal("Zapatos Tres", items[0].GetProperty("client").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Sales_admin_sees_all_portfolios()
    {
        await SeedAsync();
        var token = await _factory.GetAdminTokenAsync(_client);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var page = await http.GetFromJsonAsync<JsonElement>("/api/agent/documents?type=order");
        // Admin ve los 4 pedidos de las tres carteras
        Assert.Equal(4, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Aggregated_documents_require_the_agent_role()
    {
        var anon = await _client.GetAsync("/api/agent/documents?type=order");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var token = await _factory.GetConnectorTokenAsync(_client);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var forbidden = await http.GetAsync("/api/agent/documents?type=order");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    // ── Estadísticas ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Statistics_aggregate_invoiced_sales_and_top_clients()
    {
        var http = await AgentAsync(AgentAEmail);
        var stats = await http.GetFromJsonAsync<JsonElement>("/api/agent/statistics");

        Assert.Equal(2, stats.GetProperty("count").GetInt32());          // CP1 + CP2
        Assert.Equal(1510m, stats.GetProperty("total").GetDecimal());    // 1210 + 300
        var top = stats.GetProperty("topClients").EnumerateArray().ToList();
        Assert.Equal(2, top.Count);
        Assert.Equal("Calzados Uno", top[0].GetProperty("client").GetString());   // el mayor primero
    }

    // ── Calendario de citas ─────────────────────────────────────────────────────

    [Fact]
    public async Task Agent_creates_and_lists_appointments_isolated_from_other_agents()
    {
        var httpA = await AgentAsync(AgentAEmail);
        var httpB = await AgentAsync(AgentBEmail);

        var created = await httpA.PostAsJsonAsync("/api/agent/appointments", new
        {
            clientId = CP1,
            title = "Visita a Calzados Uno",
            kind = "cita",
            start = "2026-09-01T10:00:00Z",
            durationMinutes = 45
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Calzados Uno", body.GetProperty("clientName").GetString());

        var listA = await httpA.GetFromJsonAsync<JsonElement>("/api/agent/appointments");
        Assert.Contains(listA.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("title").GetString() == "Visita a Calzados Uno");

        // El agente B no ve la cita de A
        var listB = await httpB.GetFromJsonAsync<JsonElement>("/api/agent/appointments");
        Assert.DoesNotContain(listB.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("title").GetString() == "Visita a Calzados Uno");
    }

    [Fact]
    public async Task Appointment_with_a_foreign_client_is_rejected()
    {
        var http = await AgentAsync(AgentAEmail);
        var response = await http.PostAsJsonAsync("/api/agent/appointments", new
        {
            clientId = CP3,   // de la cartera de B
            title = "No debería poder",
            start = "2026-09-02T09:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Constructores de payload ──────────────────────────────────────────────────

    private static string ClientDoc(string number, string name) =>
        $$"""{ "externalReference": "{{number}}", "name": "{{name}}", "groupIds": [] }""";

    private static string AgentDoc(string id, string email, string name, params string[] clientIds) =>
        $$"""
        { "id": "{{id}}", "clientIds": [{{string.Join(",", clientIds.Select(c => $"\"{c}\""))}}],
          "name": "{{name}}", "email": "{{email}}", "culture": "es_ES" }
        """;

    private static string OrderDoc(string number, string clientId, decimal total, string type, string status) =>
        $$"""
        { "externalReference": "{{number}}", "clientId": "{{clientId}}", "type": "{{type}}",
          "status": "{{status}}", "orderedDate": "2026-08-10T00:00:00",
          "purchaseOrderId": "{{number}}",
          "totals": { "total": { "code": "EUR", "value": {{total}} } },
          "items": [ { "transactionInfo": { "info": { "quantity": 3 } } } ] }
        """;

    private static string InvoiceDoc(string number, string clientId, decimal total, bool paid, DateTime date) =>
        $$"""
        { "number": "{{number}}", "clientId": "{{clientId}}", "status": "{{(paid ? "Paid" : "Unpaid")}}",
          "issueDate": "{{date:yyyy-MM-dd}}T00:00:00.000Z",
          "payMethodName": { "es_ES": "Transferencia" },
          "payments": [ { "dueDate": "{{date:yyyy-MM-dd}}T00:00:00.000Z" } ],
          "totals": { "total": { "code": "EUR", "value": {{total}} } } }
        """;

    private static string DeliveryDoc(string number, string clientId, decimal total, bool invoiced) =>
        $$"""
        { "number": "{{number}}", "clientId": "{{clientId}}", "isInvoiced": {{(invoiced ? "true" : "false")}},
          "deliveryDate": "2026-08-12T00:00:00.000Z",
          "shippingAddress": { "streetAddress": "Calle Uno", "city": "Madrid" },
          "totals": { "total": { "code": "EUR", "value": {{total}} } } }
        """;
}
