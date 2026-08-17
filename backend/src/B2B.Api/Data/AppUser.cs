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

    // "integration" (usuario técnico del conector) | "client-admin" (usuario del cliente)
    public string Role { get; set; } = "integration";

    // Cultura del portal en el formato del conector (es_ES, en_EN, fr_FR, it_IT)
    public string Culture { get; set; } = "es_ES";

    // NOMBRE de la tarjeta "Mis datos" de /profile. Llega con el usuario admin del
    // cliente (payload {email, name, culture}) y el propio usuario puede cambiarlo.
    public string? Name { get; set; }
}
