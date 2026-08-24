using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using B2B.Api.Payments;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Pago con tarjeta desde el portal (Fase pagos). Dos orígenes: una factura con deuda
// y un pedido del checkout. El flujo es el mismo: se crea un Payment, la pasarela
// (mock o Stripe) devuelve una URL a la que se redirige, y al volver se concilia.
//
// INVARIANTE: como en todo el portal, el ámbito sale del token. Solo se puede pagar
// una factura o un pedido del propio cliente.
public static class PaymentEndpoints
{
    private const decimal Iva = 0.21m;   // Tipo general; el desglose real llega de BC

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Iniciar el pago de una factura pendiente ───────────────────────────────
        app.MapPost("/api/portal/payments/invoice/{invoiceId}", async (
            string invoiceId, ClaimsPrincipal principal, AppDbContext db,
            IPaymentGateway gateway, IConfiguration config, HttpRequest request) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var docs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.InvoiceEntity);
            var found = docs.FirstOrDefault(d => string.Equals(d.Id, invoiceId, StringComparison.OrdinalIgnoreCase));
            if (found.Payload is null)
                return Results.NotFound(new { error = "La factura no existe." });

            var invoice = DocumentProjections.Invoice(found.Id, found.Payload, locale, today);
            if (invoice.Debt <= 0)
                return Results.BadRequest(new { error = "La factura no tiene deuda pendiente." });

            // No cobrar dos veces: si ya hay un pago conciliado de esta factura, se bloquea
            if (await db.Payments.AnyAsync(p => p.ClientId == actor.ClientId
                && p.Kind == PaymentKind.Invoice && p.TargetId == invoiceId && p.Status == PaymentStatus.Paid))
                return Results.BadRequest(new { error = "Esta factura ya consta como pagada." });

            var payment = await GetOrCreateAsync(db, actor, PaymentKind.Invoice, invoiceId,
                $"Factura {invoice.Number}", invoice.Debt, invoice.Currency, gateway.Provider);
            return await StartAsync(payment, db, gateway, config, request);
        }).RequireAuthorization();

        // ── Iniciar el pago de un pedido del checkout ──────────────────────────────
        app.MapPost("/api/portal/payments/order/{orderId:guid}", async (
            Guid orderId, ClaimsPrincipal principal, AppDbContext db,
            IPaymentGateway gateway, IConfiguration config, HttpRequest request) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var order = await db.Carts.SingleOrDefaultAsync(c => c.Id == orderId);
            // 404 si no existe o no es del cliente: no se confirma la existencia del ajeno
            if (order is null || !SameClient(order, actor))
                return Results.NotFound(new { error = "El pedido no existe." });

            var amount = OrderAmount(order.LinesJson);
            if (amount <= 0)
                return Results.BadRequest(new { error = "El pedido no tiene importe a cobrar." });
            if (await db.Payments.AnyAsync(p => p.ClientId == actor.ClientId
                && p.Kind == PaymentKind.Order && p.TargetId == order.Id.ToString() && p.Status == PaymentStatus.Paid))
                return Results.BadRequest(new { error = "Este pedido ya consta como pagado." });

            var payment = await GetOrCreateAsync(db, actor, PaymentKind.Order, order.Id.ToString(),
                order.Name, amount, "EUR", gateway.Provider);
            return await StartAsync(payment, db, gateway, config, request);
        }).RequireAuthorization();

        // ── Estado de un pago (la página de resultado lo consulta al volver) ────────
        app.MapGet("/api/portal/payments/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, IPaymentGateway gateway) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == id);
            // El cuenta sin cliente (admin/agente) se acota por UserId: dos actores sin
            // ClientId no deben verse los pagos entre sí (P3).
            if (payment is null || !PaymentBelongs(payment, actor))
                return Results.NotFound(new { error = "El pago no existe." });

            // Stripe: si sigue pendiente, se concilia contra la sesión (respaldo del webhook)
            if (payment.Status == PaymentStatus.Pending && gateway is IReconcilable reconciler)
            {
                if (await reconciler.IsPaidAsync(payment) && payment.Status == PaymentStatus.Pending)
                {
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }

            return Results.Ok(Projection(payment));
        }).RequireAuthorization();

        // ── Pagos del cliente (para marcar en /invoices lo ya pagado) ───────────────
        app.MapGet("/api/portal/payments", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var scoped = string.IsNullOrEmpty(actor.ClientId)
                ? db.Payments.Where(p => p.ClientId == null && p.UserId == actor.UserId)
                : db.Payments.Where(p => p.ClientId == actor.ClientId);
            var payments = await scoped
                .OrderByDescending(p => p.CreatedAt)
                .Take(500)
                .ToListAsync();
            return Results.Ok(new { items = payments.Select(Projection) });
        }).RequireAuthorization();

        // ── Pasarela SIMULADA (dev): página de pago + confirmación ──────────────────
        // Página "alojada" del mock: sin sesión del portal, aislada. Enseña el importe
        // y dos botones. NO es la SPA (la ruta /pay/... no la captura el fallback).
        app.MapGet("/pay/mock/{id:guid}", async (Guid id, HttpRequest request, AppDbContext db) =>
        {
            var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == id);
            if (payment is null || payment.Provider != "mock")
                return Results.NotFound();
            var secret = request.Query["s"].ToString();
            var ok = request.Query["ok"].ToString();
            var no = request.Query["no"].ToString();
            return Results.Content(MockPage(payment, secret, ok, no), "text/html; charset=utf-8");
        }).AllowAnonymous();

        // Confirmación del mock: la valida el secreto del enlace, no la sesión. Marca el
        // pago pagado/cancelado. Solo actúa sobre pagos de la propia pasarela simulada.
        app.MapPost("/api/pay/mock/{id:guid}", async (Guid id, MockDecision body, AppDbContext db) =>
        {
            var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == id);
            if (payment is null || payment.Provider != "mock" || payment.Secret != (body.Secret ?? ""))
                return Results.NotFound();
            if (payment.Status == PaymentStatus.Pending)
            {
                if (string.Equals(body.Decision, "pay", StringComparison.OrdinalIgnoreCase))
                {
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidAt = DateTime.UtcNow;
                }
                else if (string.Equals(body.Decision, "fail", StringComparison.OrdinalIgnoreCase))
                    payment.Status = PaymentStatus.Failed;
                else
                    payment.Status = PaymentStatus.Canceled;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }).AllowAnonymous();

        // ── Webhook de Stripe (confirmación real) ───────────────────────────────────
        app.MapPost("/api/pay/stripe/webhook", async (HttpRequest request, AppDbContext db, IPaymentGateway gateway) =>
        {
            if (gateway is not IWebhookReceiver receiver)
                return Results.Ok();   // en modo mock no hay webhook que atender

            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync();
            var signature = request.Headers["Stripe-Signature"].ToString();

            // Firma inválida → 400. Evento válido pero no accionable → 200 (ack): así
            // Stripe NO reintenta en bucle los eventos que no nos interesan (P2).
            if (!receiver.TryHandle(json, signature, out var paidPaymentId))
                return Results.BadRequest();

            if (paidPaymentId is { } paymentId)
            {
                var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId);
                if (payment is not null && payment.Status == PaymentStatus.Pending)
                {
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        }).AllowAnonymous();
    }

    // ══════════════ Flujo compartido ══════════════

    // Reutiliza un pago PENDIENTE en curso del mismo destino en vez de crear otro
    // (P1: evita dos sesiones/cobros por doble clic, dos pestañas o reintento). Con
    // Stripe, recrear la sesión con el mismo IdempotencyKey (el id del pago) devuelve
    // la MISMA sesión, así que no hay cargo doble.
    private static async Task<Payment> GetOrCreateAsync(AppDbContext db, PortalActor actor,
        string kind, string targetId, string description, decimal amount, string currency, string provider)
    {
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.ClientId == actor.ClientId
            && p.Kind == kind && p.TargetId == targetId && p.Status == PaymentStatus.Pending);
        if (existing is not null)
        {
            existing.Amount = decimal.Round(amount, 2);
            existing.Description = description;
            return existing;
        }
        var payment = NewPayment(actor, kind, targetId, description, amount, currency, provider);
        db.Payments.Add(payment);
        return payment;
    }

    private static async Task<IResult> StartAsync(
        Payment payment, AppDbContext db, IPaymentGateway gateway, IConfiguration config, HttpRequest request)
    {
        await db.SaveChangesAsync();   // persiste el pago (nuevo o reutilizado) y fija su Id

        var baseUrl = (config["Portal:BaseUrl"] ?? "http://localhost:5199").TrimEnd('/');
        var market = config["Portal:Market"] ?? "es";
        var lang = DocumentProjections.Locale(request.Query["locale"]);
        var success = $"{baseUrl}/{market}/{lang}/pay?id={payment.Id}&r=ok";
        var cancel = $"{baseUrl}/{market}/{lang}/pay?id={payment.Id}&r=cancel";

        try
        {
            var session = await gateway.CreateSessionAsync(payment, success, cancel);
            payment.SessionId = session.SessionId;
            await db.SaveChangesAsync();
            return Results.Ok(new { paymentId = payment.Id, url = session.Url });
        }
        catch (Exception ex) when (ex is Stripe.StripeException or InvalidOperationException or HttpRequestException)
        {
            // La pasarela falló (p. ej. claves mal): no dejamos el pago colgado en pending
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync();
            return Results.Json(new { error = "No se pudo iniciar el pago con la pasarela." },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static Payment NewPayment(PortalActor actor, string kind, string targetId,
        string description, decimal amount, string currency, string provider) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = actor.ClientId,
        UserId = actor.UserId,
        Kind = kind,
        TargetId = targetId,
        Description = description,
        Amount = decimal.Round(amount, 2),
        Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
        Provider = provider,
        Secret = NewSecret(),
        Status = PaymentStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    private static bool PaymentBelongs(Payment p, PortalActor actor) =>
        string.IsNullOrEmpty(actor.ClientId)
            ? p.ClientId == null && p.UserId == actor.UserId
            : string.Equals(p.ClientId, actor.ClientId, StringComparison.OrdinalIgnoreCase);

    private static bool SameClient(Cart order, PortalActor actor) =>
        string.IsNullOrEmpty(actor.ClientId)
            ? order.ClientId == null && order.UserId == actor.UserId
            : string.Equals(order.ClientId, actor.ClientId, StringComparison.OrdinalIgnoreCase);

    private static decimal OrderAmount(string linesJson)
    {
        try
        {
            var lines = JsonSerializer.Deserialize<List<CartLine>>(linesJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            var net = lines.Sum(l => l.Qty * l.Price);
            return decimal.Round(net * (1 + Iva), 2);
        }
        catch (JsonException) { return 0m; }
    }

    private static object Projection(Payment p) => new
    {
        id = p.Id,
        kind = p.Kind,
        targetId = p.TargetId,
        description = p.Description,
        amount = p.Amount,
        currency = p.Currency,
        status = p.Status,
        provider = p.Provider,
        createdAt = p.CreatedAt,
        paidAt = p.PaidAt
    };

    private static string NewSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static IResult Unknown() =>
        Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

    public sealed record MockDecision(string? Secret, string? Decision);

    // Página de la pasarela simulada. Sobria y clara: importe, concepto y dos botones.
    private static string MockPage(Payment payment, string secret, string okUrl, string noUrl)
    {
        var amount = payment.Amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
        var desc = System.Net.WebUtility.HtmlEncode(payment.Description);
        // Valores para el <script>: JSON serializado (con comillas y escapado) para
        // que no se pueda romper/inyectar en el JS de la página.
        var idJs = JsonSerializer.Serialize(payment.Id.ToString());
        var secretJs = JsonSerializer.Serialize(secret);
        var okJs = JsonSerializer.Serialize(okUrl);
        var noJs = JsonSerializer.Serialize(noUrl);
        return $$"""
            <!doctype html><html lang="es"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Pago de prueba — lejan B2B</title>
            <style>
              :root{--g:#1f5c46;--p:#faf6ef}
              body{font-family:Inter,system-ui,Arial,sans-serif;background:var(--p);margin:0;
                min-height:100vh;display:grid;place-items:center;color:#1a1a1a}
              .card{background:#fff;border-radius:16px;box-shadow:0 10px 40px rgba(0,0,0,.12);
                padding:2.4rem 2.2rem;max-width:26rem;width:92%}
              .brand{font-weight:800;font-style:italic;color:var(--g);font-size:1.4rem;margin:0 0 1.4rem}
              .tag{background:#fdf0d5;color:#8a5a00;font-size:.72rem;font-weight:700;letter-spacing:.04em;
                text-transform:uppercase;padding:.2rem .6rem;border-radius:999px}
              h1{font-size:1.15rem;margin:1rem 0 .3rem}
              .amt{font-size:2.4rem;font-weight:700;color:var(--g);margin:.4rem 0 1.6rem}
              .desc{color:#555;margin:0 0 1.6rem}
              button{width:100%;border:none;border-radius:8px;padding:.9rem;font-size:.95rem;font-weight:700;
                cursor:pointer;margin:0 0 .7rem}
              .pay{background:var(--g);color:#fff}.pay:hover{background:#164031}
              .fail{background:#fff;color:#c4633a;border:1px solid #e4d9cd}
              .cancel{background:transparent;color:#666;font-weight:500}
              .hint{font-size:.8rem;color:#999;text-align:center;margin:.6rem 0 0}
            </style></head><body>
            <div class="card">
              <p class="brand">lejan<sup>™</sup></p>
              <span class="tag">Pasarela de prueba</span>
              <h1>{{desc}}</h1>
              <div class="amt">{{amount}} €</div>
              <p class="desc">Este es un pago simulado para desarrollo. No se cobra dinero real.</p>
              <button class="pay" id="pay">Pagar {{amount}} €</button>
              <button class="fail" id="fail">Simular pago rechazado</button>
              <button class="cancel" id="cancel">Cancelar</button>
              <p class="hint">Modo simulado · lejan B2B</p>
            </div>
            <script>
              const id={{idJs}}, secret={{secretJs}}, ok={{okJs}}, no={{noJs}};
              async function decide(decision, back){
                try{ await fetch('/api/pay/mock/'+id,{method:'POST',
                  headers:{'Content-Type':'application/json'},
                  body:JSON.stringify({secret,decision})}); }catch(e){}
                window.location.href = back;
              }
              document.getElementById('pay').onclick=()=>decide('pay',ok);
              document.getElementById('fail').onclick=()=>decide('fail',no);
              document.getElementById('cancel').onclick=()=>decide('cancel',no);
            </script></body></html>
            """;
    }
}

// La pasarela puede saber si un pago ya se cobró (respaldo del webhook al volver)
public interface IReconcilable
{
    Task<bool> IsPaidAsync(Payment payment);
}

// La pasarela puede atender un webhook. Devuelve false SOLO si la firma es inválida;
// true en cualquier otro caso (evento válido, accionable o no). paidPaymentId se
// rellena únicamente cuando el evento confirma un pago cobrado.
public interface IWebhookReceiver
{
    bool TryHandle(string json, string signature, out Guid? paidPaymentId);
}
