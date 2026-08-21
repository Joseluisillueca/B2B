namespace B2B.Api.Data;

public class AppUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;

    // Vínculo con el cliente del portal. Lo provisiona el conector con
    // PUT /api/clients/{clientId}/users/admin (contrato 04 §2). El usuario de
    // integración de BC no tiene cliente: estos campos quedan a null.
    public string? ClientExternalId { get; set; }   // SystemId del Customer en BC
    public string? ClientNumber { get; set; }       // Customer."No." (C100057)

    // Vínculo con el comercial (Salesperson) del modelo de agente. Lo provisiona el
    // sync `agent` (contrato 04 §4): es el SystemId del comercial en BC, el mismo id
    // con el que llega su documento a /api/agents/{id}. Un usuario de cliente lo deja
    // a null; un agente no tiene ClientExternalId (no representa a un solo cliente,
    // sino a la cartera de clientes que lleva su documento `agent`).
    public string? AgentExternalId { get; set; }

    // "integration" (usuario técnico del conector) | "client-admin" (usuario del cliente)
    // | "agent" (comercial que suplanta a los clientes de su cartera)
    public string Role { get; set; } = "integration";

    // Cultura del portal en el formato del conector (es_ES, en_EN, fr_FR, it_IT)
    public string Culture { get; set; } = "es_ES";

    // NOMBRE de la tarjeta "Mis datos" de /profile. Llega con el usuario admin del
    // cliente (payload {email, name, culture}) y el propio usuario puede cambiarlo.
    public string? Name { get; set; }
}
