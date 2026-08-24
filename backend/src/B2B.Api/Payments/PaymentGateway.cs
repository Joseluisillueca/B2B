using B2B.Api.Data;
using B2B.Api.Portal;
using Stripe;
using Stripe.Checkout;

namespace B2B.Api.Payments;

// La sesión de pago de la pasarela: a dónde redirigir al usuario y el id con el que
// luego se confirma el cobro.
public sealed record PaymentSession(string SessionId, string Url);

public interface IPaymentGateway
{
    /// "mock" | "stripe"
    string Provider { get; }

    /// Crea la sesión de pago y devuelve la URL a la que redirigir. successUrl/cancelUrl
    /// son las páginas del portal a las que la pasarela devuelve al usuario.
    Task<PaymentSession> CreateSessionAsync(Payment payment, string successUrl, string cancelUrl, CancellationToken ct = default);
}

// Configuración de pagos (sección "Payments"). Por defecto modo "mock": no cobra nada
// real, simula la pasarela en dev para probar el flujo completo. Con Mode=stripe usa
// Stripe Checkout (página alojada por Stripe) con las claves configuradas.
public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// "mock" (por defecto) | "stripe"
    public string Mode { get; set; } = "mock";
    /// Moneda ISO en minúsculas para la pasarela
    public string Currency { get; set; } = "eur";

    public StripeOptions Stripe { get; set; } = new();

    public sealed class StripeOptions
    {
        // Claves del panel de Stripe (modo test: pk_test_/sk_test_). La secret NUNCA
        // va al frontend; con Checkout alojado el navegador solo recibe la URL.
        public string SecretKey { get; set; } = "";
        public string PublishableKey { get; set; } = "";
        /// Secreto del endpoint de webhook (whsec_...) para verificar la firma
        public string WebhookSecret { get; set; } = "";
    }
}

// Pasarela simulada (dev/pruebas): no habla con ninguna pasarela real. Devuelve una
// URL a una página del propio backend (/pay/mock/{id}) donde el usuario "paga" o
// "cancela"; esa página confirma el pago contra la API. Permite probar TODO el flujo
// (crear pago → pagar → conciliar) sin claves ni dinero.
public sealed class MockPaymentGateway(IConfiguration config) : IPaymentGateway
{
    public string Provider => "mock";

    public Task<PaymentSession> CreateSessionAsync(Payment payment, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        var baseUrl = (config["Portal:BaseUrl"] ?? "http://localhost:5199").TrimEnd('/');
        var url = $"{baseUrl}/pay/mock/{payment.Id}"
            + $"?s={Uri.EscapeDataString(payment.Secret)}"
            + $"&ok={Uri.EscapeDataString(successUrl)}"
            + $"&no={Uri.EscapeDataString(cancelUrl)}";
        return Task.FromResult(new PaymentSession($"mock_{payment.Id:N}", url));
    }
}

// Pasarela real: Stripe Checkout (página alojada por Stripe). La secret key vive solo
// aquí; el navegador solo recibe la URL de la sesión. Confirma por webhook y, de
// respaldo, recuperando la sesión al volver (IReconcilable).
public sealed class StripePaymentGateway : IPaymentGateway, IReconcilable, IWebhookReceiver
{
    private readonly PaymentOptions _options;

    public StripePaymentGateway(PaymentOptions options)
    {
        _options = options;
        StripeConfiguration.ApiKey = options.Stripe.SecretKey;
    }

    public string Provider => "stripe";

    public async Task<PaymentSession> CreateSessionAsync(Payment payment, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        // Moneda del propio pago (minúsculas ISO); Stripe cobra en la
                        // unidad mínima (céntimos), entero.
                        Currency = string.IsNullOrWhiteSpace(payment.Currency)
                            ? _options.Currency : payment.Currency.ToLowerInvariant(),
                        UnitAmount = (long)decimal.Round(payment.Amount * 100m, 0),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = payment.Description }
                    }
                }
            ],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            // El id del pago viaja como referencia: así el webhook sabe qué conciliar
            ClientReferenceId = payment.Id.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["paymentId"] = payment.Id.ToString(),
                ["kind"] = payment.Kind,
                ["targetId"] = payment.TargetId
            }
        };

        // Idempotencia: reintentar no crea dos sesiones (ni dos cobros)
        var session = await new SessionService().CreateAsync(
            options, new RequestOptions { IdempotencyKey = payment.Id.ToString() }, ct);
        return new PaymentSession(session.Id, session.Url);
    }

    public async Task<bool> IsPaidAsync(Payment payment)
    {
        if (string.IsNullOrEmpty(payment.SessionId))
            return false;
        try
        {
            var session = await new SessionService().GetAsync(payment.SessionId);
            return session.PaymentStatus == "paid";
        }
        catch (StripeException) { return false; }
    }

    public bool TryHandle(string json, string signature, out Guid? paidPaymentId)
    {
        paidPaymentId = null;
        Event stripeEvent;
        try
        {
            // throwOnApiVersionMismatch:false → no falla si la versión de API del panel
            // difiere de la del SDK (solo nos importa el tipo de evento y su firma).
            stripeEvent = EventUtility.ConstructEvent(json, signature,
                _options.Stripe.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException) { return false; }   // firma inválida

        if (stripeEvent.Type == "checkout.session.completed"
            && stripeEvent.Data.Object is Session session
            && session.PaymentStatus == "paid"
            && Guid.TryParse(session.ClientReferenceId, out var id))
        {
            paidPaymentId = id;
        }
        return true;   // firma válida: accionable o no, se hace ack (200)
    }
}
