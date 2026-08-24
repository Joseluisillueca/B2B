namespace B2B.Api.Data;

// Pago con tarjeta iniciado desde el portal (factura pendiente o pedido). El maestro
// de cobros vive en Business Central: esto NO concilia en BC, registra que el cliente
// pagó con tarjeta y por qué pasarela, para que back-office lo case. Hasta que BC
// devuelva el estado, el portal muestra el pago como "registrado / pendiente de
// conciliación".
public class Payment
{
    public Guid Id { get; set; }

    // Ámbito: el cliente del token. Un usuario nunca ve/paga lo de otro cliente.
    public string? ClientId { get; set; }
    public Guid UserId { get; set; }

    /// "invoice" (factura pendiente) | "order" (pedido del checkout)
    public string Kind { get; set; } = PaymentKind.Invoice;
    /// Id del documento de factura o del pedido (Cart) que se paga
    public string TargetId { get; set; } = "";
    /// Texto legible del concepto (nº de factura / nombre del pedido)
    public string Description { get; set; } = "";

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";

    /// "mock" | "stripe": pasarela que atendió el pago
    public string Provider { get; set; } = "mock";
    /// Id de sesión de la pasarela (Stripe Checkout Session id, o el de mock)
    public string? SessionId { get; set; }
    /// Secreto opaco para confirmar el pago simulado sin sesión del portal
    public string Secret { get; set; } = "";

    /// "pending" | "paid" | "failed" | "canceled"
    public string Status { get; set; } = PaymentStatus.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

public static class PaymentKind
{
    public const string Invoice = "invoice";
    public const string Order = "order";
    public static readonly string[] All = [Invoice, Order];
}

public static class PaymentStatus
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}
