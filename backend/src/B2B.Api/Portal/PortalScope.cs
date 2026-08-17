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

    /// Los documentos del conector que son del cliente del token, y nada más.
    ///
    /// El filtro va en SQL por ParentId (el conector lo cuelga del `clientId` del
    /// propio payload al ingerirlo) y se vuelve a comprobar en memoria contra ese
    /// mismo campo. Los dos lados son el SystemId de BC en mayúsculas y sin llaves
    /// (contrato 01), así que coinciden literalmente; si alguna vez no lo hicieran
    /// el listado saldría vacío — falla cerrado, nunca enseñando lo de otro.
    public static async Task<List<(string Id, JsonObject Payload)>> DocumentsAsync(
        AppDbContext db, ClaimsPrincipal principal, string entityType)
    {
        var actor = await ActorAsync(principal, db);
        var clientId = actor?.ClientId;
        if (string.IsNullOrEmpty(clientId))
            return [];

        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == entityType && d.ParentId == clientId)
            .ToListAsync();

        return
        [
            .. docs
                .Select(doc => (doc.ExternalId, Payload: ClientIdentity.Parse(doc.Payload)))
                .Where(doc => doc.Payload is not null && string.Equals(
                    ClientIdentity.Text(doc.Payload["clientId"]), clientId, StringComparison.OrdinalIgnoreCase))
                .Select(doc => (doc.ExternalId, doc.Payload!))
        ];
    }

    /// El payload del propio cliente del token (sync_documents "client")
    public static async Task<JsonObject?> ClientPayloadAsync(AppDbContext db, string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            return null;

        var doc = await db.SyncDocuments
            .SingleOrDefaultAsync(d => d.EntityType == "client" && d.ExternalId == clientId);
        return doc is null ? null : ClientIdentity.Parse(doc.Payload);
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
