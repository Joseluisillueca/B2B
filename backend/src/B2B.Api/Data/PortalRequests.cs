namespace B2B.Api.Data;

// Lo que el cliente PIDE desde el portal y no puede cambiar por sí mismo.
//
// Los datos maestros del cliente viven en Business Central: el portal no los
// escribe (plan §4, Fase 4 — "los cambios se registran como solicitud"). Lo que
// hace /business es dejar constancia de qué quiere cambiar y quién lo pide, para
// que atención al cliente lo aplique en BC y vuelva por el sync.
public class BusinessChangeRequest
{
    public Guid Id { get; set; }

    public string? ClientId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }

    /// "general" (datos de contacto) | "fiscal" (datos fiscales)
    public string Section { get; set; } = BusinessSections.General;

    /// { campo: valorSolicitado } tal cual lo mandó el formulario
    public string ChangesJson { get; set; } = "{}";

    /// "pending" | "applied" | "rejected"
    public string Status { get; set; } = "pending";
}

public static class BusinessSections
{
    public const string General = "general";
    public const string Fiscal = "fiscal";

    public static readonly string[] All = [General, Fiscal];
}

// Formulario de /contact (07-contact.png). Se guarda siempre: el correo a
// tiendas@lejanbrand.com puede fallar, pero la solicitud del cliente no se pierde.
public class ContactMessage
{
    public Guid Id { get; set; }

    public string? ClientId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public string Subject { get; set; } = "";

    /// "Email de contacto" del formulario; si va vacío se usa el del usuario
    public string Email { get; set; } = "";
    public string Message { get; set; } = "";

    /// Adjunto guardado fuera de wwwroot: no se sirve como estático
    public string? AttachmentName { get; set; }
    public string? AttachmentPath { get; set; }

    /// Buzón al que va dirigida (Contact:Inbox)
    public string DeliveredTo { get; set; } = "";
}
