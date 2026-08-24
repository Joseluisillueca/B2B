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

// Pago con tarjeta (modo mock por defecto): pagar una factura con deuda y un pedido
// del checkout. Reglas: el ámbito sale del token (solo lo del propio cliente), no se
// cobra dos veces, y la confirmación del mock exige el secreto del enlace.
public class PaymentTests : IClassFixture<PaymentTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    private const string ClientA = "PAYC-AAAA-0000-4000-9000-000000000001";
    private const string ClientB = "PAYC-BBBB-0000-4000-9000-000000000002";
    private const string UserA = "paga@cliente.test";
    private const string UserB = "pagb@cliente.test";
    private const string Pass = "cliente-pago-123";
    // Una factura por test que paga: el bloqueo "ya pagada" no debe cruzarse entre tests
    private const string InvPay = "INV-PAY-PAY";       // Pay_invoice_creates
    private const string InvAgain = "INV-PAY-AGAIN";   // A_paid_cannot_again
    private const string InvSecret = "INV-PAY-SECRET";  // Mock_confirmation
    private const string InvIso = "INV-PAY-ISO";       // Payments_isolated
    private static Guid _orderA;

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PaymentTests(Factory factory)
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
            var hasher = new PasswordHasher<AppUser>();

            foreach (var (email, clientId) in new[] { (UserA, ClientA), (UserB, ClientB) })
            {
                var user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = "",
                    Role = ClientIdentity.ClientAdminRole, ClientExternalId = clientId, Culture = "es_ES" };
                user.PasswordHash = hasher.HashPassword(user, Pass);
                db.Users.Add(user);
            }

            // Facturas con deuda (Unpaid) del cliente A → deuda = total. Una por test
            // que paga, para que el bloqueo "ya pagada" no cruce resultados.
            foreach (var invId in new[] { InvPay, InvAgain, InvSecret, InvIso })
                db.SyncDocuments.Add(new SyncDocument { EntityType = "invoice", ExternalId = invId, ParentId = ClientA,
                    Payload = $$"""
                    { "number": "F-{{invId}}", "clientId": "{{ClientA}}", "status": "Unpaid",
                      "issueDate": "2026-07-15T00:00:00.000Z",
                      "payMethodName": { "es_ES": "Transferencia" },
                      "payments": [ { "dueDate": "2026-08-15T00:00:00.000Z" } ],
                      "totals": { "total": { "code": "EUR", "value": 1210 } } }
                    """ });
            // Factura ya pagada (sin deuda) del cliente A
            db.SyncDocuments.Add(new SyncDocument { EntityType = "invoice", ExternalId = "INV-PAY-PAID", ParentId = ClientA,
                Payload = $$"""
                { "number": "F-A-0002", "clientId": "{{ClientA}}", "status": "Paid",
                  "issueDate": "2026-07-20T00:00:00.000Z",
                  "totals": { "total": { "code": "EUR", "value": 500 } } }
                """ });

            // Pedido (Cart pending-bc) del cliente A: 2 × 100 € → 242 € con IVA
            _orderA = Guid.NewGuid();
            db.Carts.Add(new Cart { Id = _orderA, ClientId = ClientA, UserId = Guid.NewGuid(),
                Name = "Pedido de pago", Status = CartStatus.PendingBc, IsFavorite = false,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                LinesJson = """[{"productId":"P1","size":"42","qty":2,"price":100}]""" });

            await db.SaveChangesAsync();
            _seeded = true;
        }
        finally { Lock.Release(); }
    }

    private async Task<HttpClient> AsClientAsync(string email)
    {
        await SeedAsync();
        var token = await _factory.LoginAsync(_client, email, Pass);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<(Guid id, string secret)> PaymentSecretAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Payments.SingleAsync(x => x.Id == id);
        return (p.Id, p.Secret);
    }

    // ── Factura ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pay_invoice_creates_pending_payment_then_mock_marks_it_paid()
    {
        var http = await AsClientAsync(UserA);

        var response = await http.PostAsync($"/api/portal/payments/invoice/{InvPay}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = created.GetProperty("paymentId").GetGuid();
        Assert.Contains("/pay/mock/", created.GetProperty("url").GetString());

        var (_, secret) = await PaymentSecretAsync(paymentId);

        // Estado inicial: pendiente
        var pending = await http.GetFromJsonAsync<JsonElement>($"/api/portal/payments/{paymentId}");
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal(1210m, pending.GetProperty("amount").GetDecimal());

        // La pasarela simulada confirma con el secreto del enlace
        var confirm = await _client.PostAsJsonAsync($"/api/pay/mock/{paymentId}", new { secret, decision = "pay" });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var paid = await http.GetFromJsonAsync<JsonElement>($"/api/portal/payments/{paymentId}");
        Assert.Equal("paid", paid.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invoice_without_debt_cannot_be_paid()
    {
        var http = await AsClientAsync(UserA);
        var response = await http.PostAsync("/api/portal/payments/invoice/INV-PAY-PAID", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Foreign_invoice_is_not_found()
    {
        var http = await AsClientAsync(UserB);   // cliente B no tiene la factura de A
        var response = await http.PostAsync($"/api/portal/payments/invoice/{InvPay}", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_paid_invoice_cannot_be_paid_again()
    {
        var http = await AsClientAsync(UserA);
        var first = await http.PostAsync($"/api/portal/payments/invoice/{InvAgain}", null);
        var paymentId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        var (_, secret) = await PaymentSecretAsync(paymentId);
        await _client.PostAsJsonAsync($"/api/pay/mock/{paymentId}", new { secret, decision = "pay" });

        // Ya conciliada → un segundo intento se bloquea
        var again = await http.PostAsync($"/api/portal/payments/invoice/{InvAgain}", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    // ── Pedido ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pay_order_charges_lines_plus_vat()
    {
        var http = await AsClientAsync(UserA);
        var response = await http.PostAsync($"/api/portal/payments/order/{_orderA}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paymentId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();

        var status = await http.GetFromJsonAsync<JsonElement>($"/api/portal/payments/{paymentId}");
        Assert.Equal(242m, status.GetProperty("amount").GetDecimal());   // 2×100 + 21% IVA
    }

    [Fact]
    public async Task Foreign_order_is_not_found()
    {
        var http = await AsClientAsync(UserB);
        var response = await http.PostAsync($"/api/portal/payments/order/{_orderA}", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Confirmación y aislamiento ─────────────────────────────────────────────

    [Fact]
    public async Task Mock_confirmation_requires_the_link_secret()
    {
        var http = await AsClientAsync(UserA);
        var create = await http.PostAsync($"/api/portal/payments/invoice/{InvSecret}", null);
        var paymentId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();

        // Secreto equivocado → no marca nada
        var bad = await _client.PostAsJsonAsync($"/api/pay/mock/{paymentId}", new { secret = "no-es-el-secreto", decision = "pay" });
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);

        var status = await http.GetFromJsonAsync<JsonElement>($"/api/portal/payments/{paymentId}");
        Assert.Equal("pending", status.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Payments_are_isolated_between_clients()
    {
        var httpA = await AsClientAsync(UserA);
        var create = await httpA.PostAsync($"/api/portal/payments/invoice/{InvIso}", null);
        var paymentId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();

        // El cliente B no puede ver el pago de A
        var httpB = await AsClientAsync(UserB);
        var foreign = await httpB.GetAsync($"/api/portal/payments/{paymentId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var listB = await httpB.GetFromJsonAsync<JsonElement>("/api/portal/payments");
        Assert.DoesNotContain(listB.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == paymentId);
    }

    [Fact]
    public async Task Starting_a_payment_requires_authentication()
    {
        var anon = await _client.PostAsync($"/api/portal/payments/invoice/{InvPay}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }
}
