using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Vínculo AppUser ↔ cliente. El conector no gestiona usuarios del portal: solo
// publica el "usuario admin" de cada cliente (contrato 04 §2, payload
// {email, name, culture}). De ahí sale la identidad con la que el cliente entra
// al portal; la contraseña nunca viaja por el sync.
public static class ClientIdentity
{
    public const string IntegrationRole = "integration";
    public const string ClientAdminRole = "client-admin";

    // Etiquetas de la pantalla "SELECCIONA AHORA TUS CREDENCIALES" (01-tras-login.png)
    public static string RoleLabel(string? role) => role switch
    {
        ClientAdminRole => "Administrador",
        IntegrationRole => "Integración",
        _ => "Usuario"
    };

    // Se llama dentro del upsert del sync, antes del SaveChanges: el documento crudo
    // y la identidad derivada nunca divergen.
    public static async Task ApplyAsync(AppDbContext db, string entityType, string externalId, JsonNode? payload)
    {
        if (payload is not JsonObject obj)
            return;

        switch (entityType)
        {
            case "client-user":
                await ProvisionUserAsync(db, clientId: externalId, obj);
                break;
            case "client":
                await RefreshClientNumberAsync(db, clientId: externalId, obj);
                break;
        }
    }

    private static async Task ProvisionUserAsync(AppDbContext db, string clientId, JsonObject payload)
    {
        var email = Text(payload["email"]).Trim().ToLowerInvariant();
        if (email.Length == 0)
            return;   // El report de BC filtra E-Mail <> '', pero el backend no lo da por hecho

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            // Sin hash: no puede iniciar sesión hasta que se le asigne contraseña
            user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = string.Empty };
            db.Users.Add(user);
        }

        user.ClientExternalId = clientId;
        user.ClientNumber = await ClientNumberAsync(db, clientId) ?? user.ClientNumber;
        user.Role = ClientAdminRole;

        var culture = Text(payload["culture"]);
        if (culture.Length > 0)
            user.Culture = culture;

        // NOMBRE de /profile. El sync solo lo rellena si BC lo trae: un nombre que
        // el propio usuario haya cambiado en el portal no se pisa con un vacío.
        var name = Text(payload["name"]).Trim();
        if (name.Length > 0)
            user.Name = name;
    }

    // El cliente puede llegar (o volver a llegar) después que su usuario admin
    private static async Task RefreshClientNumberAsync(AppDbContext db, string clientId, JsonObject payload)
    {
        var number = Text(payload["externalReference"]);
        if (number.Length == 0)
            return;

        var users = await db.Users.Where(u => u.ClientExternalId == clientId).ToListAsync();
        foreach (var user in users)
            user.ClientNumber = number;
    }

    private static async Task<string?> ClientNumberAsync(AppDbContext db, string clientId)
    {
        var doc = await db.SyncDocuments
            .SingleOrDefaultAsync(d => d.EntityType == "client" && d.ExternalId == clientId);
        if (doc is null)
            return null;

        var number = Text(Parse(doc.Payload)?["externalReference"]);
        return number.Length > 0 ? number : null;
    }

    internal static JsonObject? Parse(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    internal static string Text(JsonNode? node)
    {
        if (node is null)
            return string.Empty;
        try { return node.GetValue<string>(); }
        catch (Exception) { return node.ToString(); }
    }
}
