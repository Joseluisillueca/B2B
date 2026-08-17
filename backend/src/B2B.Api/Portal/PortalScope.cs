using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Quién pregunta y con qué cliente. Todo el portal (catálogo, precios, carritos)
// se acota con esto: el clientId sale SIEMPRE del token, nunca de la query.
public sealed record PortalActor(AppUser User, string? ClientId, string[] GroupIds)
{
    public Guid UserId => User.Id;
}

public static class PortalScope
{
    /// El usuario del token, con el cliente y los grupos de tarifa que le aplican.
    /// null cuando el token apunta a un usuario borrado o desactivado.
    public static async Task<PortalActor?> ActorAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        // JwtBearer mapea "sub" a NameIdentifier salvo que se desactive MapInboundClaims
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(id, out var userId))
            return null;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null)
            return null;

        return new PortalActor(user, user.ClientExternalId, await GroupIdsAsync(db, user.ClientExternalId));
    }

    // Los grupos de tarifa (contrato 04: groupIds del cliente) deciden qué ofertas
    // con clientGroupId aplican. Viven en el payload crudo del sync.
    private static async Task<string[]> GroupIdsAsync(AppDbContext db, string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            return [];

        var doc = await db.SyncDocuments
            .SingleOrDefaultAsync(d => d.EntityType == "client" && d.ExternalId == clientId);
        if (doc is null)
            return [];

        try
        {
            return (JsonNode.Parse(doc.Payload)?["groupIds"] as JsonArray) is { } groups
                ? [.. groups.Select(g => g?.ToString() ?? "").Where(g => g.Length > 0)]
                : [];
        }
        catch (System.Text.Json.JsonException) { return []; }
    }
}
