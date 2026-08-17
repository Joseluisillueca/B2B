namespace B2B.Api.Data;

// Devoluciones de /sat (08-sat.png). NO son los documentos de devolución de
// Business Central: el portal actual pide bultos, horario de recogida, foto y una
// resolución, que BC no tiene. Es un flujo propio → tabla propia (plan §1 y §4).
//
// El traslado a BC de la devolución aceptada es la Fase BC del plan.
public class ReturnRequest
{
    public Guid Id { get; set; }

    /// CÓDIGO de la tabla: correlativo legible por el cliente (DEV-2026-0001)
    public string Code { get; set; } = "";

    // Ámbito: el cliente del token manda; UserId identifica a quién la registró
    public string? ClientId { get; set; }
    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// "return" (devolución) | "exchange" (cambio) | "defect" (producto defectuoso)
    public string Type { get; set; } = ReturnTypes.Return;

    /// HORARIO de recogida: "morning" | "afternoon"
    public string PickupSlot { get; set; } = ReturnSlots.Morning;

    /// BULTOS e ITEMS del listado
    public int Packages { get; set; }
    public int Items { get; set; }

    /// "pending" | "confirmed" | "rejected" — los 3 estados con color del rail
    public string Status { get; set; } = ReturnStatuses.Pending;

    /// RESOLUCIÓN: lo que responde el equipo de atención. Vacía mientras está pendiente.
    public string Resolution { get; set; } = "";

    /// IMG: foto del artículo que el cliente adjunta al abrir la solicitud
    public string? PhotoUrl { get; set; }

    /// Referencia del albarán o pedido al que se refiere la devolución
    public string? Reference { get; set; }

    public string Notes { get; set; } = "";
}

public static class ReturnStatuses
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";

    /// Orden del rail de 08-sat.png bajo "Todos"
    public static readonly string[] All = [Confirmed, Pending, Rejected];
}

public static class ReturnTypes
{
    public const string Return = "return";
    public const string Exchange = "exchange";
    public const string Defect = "defect";

    public static readonly string[] All = [Return, Exchange, Defect];
}

public static class ReturnSlots
{
    public const string Morning = "morning";
    public const string Afternoon = "afternoon";

    public static readonly string[] All = [Morning, Afternoon];
}
