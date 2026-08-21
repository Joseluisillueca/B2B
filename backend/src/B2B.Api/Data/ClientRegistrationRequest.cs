namespace B2B.Api.Data;

// Prealta de cliente creada por un agente desde /agent/clients/new (Fase 2).
//
// El maestro de clientes vive en Business Central y el sync es de UNA vía (BC→B2B):
// el portal no puede crear el cliente en BC. Así que el alta queda registrada aquí
// como SOLICITUD que compras/back-office validará y dará de alta en BC; cuando el
// cliente vuelva por el sync, se enlazará por email con el usuario ya provisionado.
//
// Sí hacemos una cosa de inmediato: provisionar el usuario del contacto (sin
// contraseña) y enviarle el correo de activación, para que pueda fijar su acceso sin
// esperar a que BC procese la prealta.
public class ClientRegistrationRequest
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }

    /// Comercial que la crea (AgentExternalId). null si fuese un autorregistro público
    public string? AgentExternalId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// Nombre de la empresa y email principal, desnormalizados para poder listar la
    /// bandeja sin abrir el payload de cada solicitud
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// Checkbox "Crear precliente": cliente temporal con el que ya se puede operar,
    /// pendiente igualmente de validación por compras
    public bool PreClient { get; set; }

    /// El formulario completo tal cual lo mandó el alta (objetos anidados: fiscalInfo,
    /// shippingAddresses[], secondaryEmails[], contacts[]…)
    public string PayloadJson { get; set; } = "{}";

    /// "pending" | "approved" | "rejected"
    public string Status { get; set; } = "pending";
}
