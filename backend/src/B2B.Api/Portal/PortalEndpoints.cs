using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// API del portal del cliente. Todo lo que sale de aquí está acotado al cliente
// del token: el clientId NUNCA se acepta por parámetro.
public static class PortalEndpoints
{
    public static void MapPortalEndpoints(this IEndpointRouteBuilder app)
    {
        // Ficha de sesión: quién soy, con qué credenciales entro y qué cliente represento.
        // Se proyecta de los sync_documents client y shipping-address.
        app.MapGet("/api/portal/me", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var user = await CurrentUserAsync(principal, db);
            if (user is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var client = await ClientCardAsync(db, user.ClientExternalId);
            var role = ClientIdentity.RoleLabel(user.Role);

            // Un usuario = una credencial hoy; el agente multi-cliente llega en fase posterior
            object[] credentials = client is null
                ? []
                :
                [
                    new
                    {
                        clientId = client.id,
                        clientNumber = client.number,
                        name = client.name,
                        type = "CLIENTE",
                        role
                    }
                ];

            return Results.Ok(new
            {
                email = user.Email,
                rol = role,
                culture = user.Culture,
                credentials,
                client
            });
        }).RequireAuthorization();

        // Contenido publicado (plan §3): lo que el CMS ha dejado listo para este
        // idioma. El portal nunca ve elementos apagados ni fuera de su ventana de
        // publicación, así que una campaña caducada desaparece sola de la portada.
        app.MapGet("/api/portal/content/{key}", async (string key, string? locale, AppDbContext db) =>
        {
            if (!PortalContentModel.IsKnownKey(key))
                return Results.BadRequest(new { error = "Clave de contenido desconocida." });

            var requested = PortalContentModel.NormalizeLocale(locale) ?? PortalContentModel.DefaultLocale;

            // Idioma pedido → contenido común (*) → idioma principal
            var candidates = new[] { requested, PortalContentModel.CommonLocale, PortalContentModel.DefaultLocale }
                .Distinct().ToArray();
            var blocks = await db.PortalContents
                .Where(c => c.Key == key && candidates.Contains(c.Locale))
                .ToListAsync();

            var block = candidates
                .Select(candidate => blocks.SingleOrDefault(b => b.Locale == candidate))
                .FirstOrDefault(found => found is not null);

            var items = block is null
                ? new JsonArray()
                : PortalContentModel.Published(block.Json, DateTimeOffset.UtcNow);

            return Results.Ok(new { key, locale = block?.Locale ?? requested, items });
        }).RequireAuthorization();
    }

    private static async Task<AppUser?> CurrentUserAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        // JwtBearer mapea "sub" a NameIdentifier salvo que se desactive MapInboundClaims
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(id, out var userId)
            ? await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            : null;
    }

    // Proyección canónica del cliente: lo que el portal necesita en cada vista
    // (nombre y número para la cabecera, canShop para el catálogo, segmentos y
    // métodos de pago para precios y checkout, direcciones para el envío).
    private sealed record ClientCard(
        string id, string? number, string name, JsonNode? fiscalInfo, bool canShop,
        string[] productSegments, string[] payMethods, string[] groupIds, JsonNode? creditInfo,
        IReadOnlyList<object> shippingAddresses);

    private static async Task<ClientCard?> ClientCardAsync(AppDbContext db, string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            return null;

        var doc = await db.SyncDocuments
            .SingleOrDefaultAsync(d => d.EntityType == "client" && d.ExternalId == clientId);
        var payload = doc is null ? null : ClientIdentity.Parse(doc.Payload);
        if (payload is null)
            return null;

        // Solo las direcciones colgadas de este cliente (ParentId del upsert del conector)
        var addressDocs = await db.SyncDocuments
            .Where(d => d.EntityType == "shipping-address" && d.ParentId == clientId)
            .OrderBy(d => d.ExternalId)
            .ToListAsync();

        var addresses = addressDocs
            .Select(d => (d.ExternalId, Json: ClientIdentity.Parse(d.Payload)))
            .Where(x => x.Json is not null)
            .Select(object (x) => new
            {
                id = x.ExternalId,
                alias = ClientIdentity.Text(x.Json!["alias"]),
                address = Detach(x.Json["address"])
            })
            .ToList();

        var number = ClientIdentity.Text(payload["externalReference"]);
        return new ClientCard(
            id: clientId,
            number: number.Length > 0 ? number : null,
            name: ClientIdentity.Text(payload["name"]),
            fiscalInfo: Detach(payload["fiscalInfo"]),
            canShop: Bool(payload["canShop"], fallback: true),
            productSegments: Strings(payload["productSegments"]),
            payMethods: Strings(payload["payMethods"]),
            groupIds: Strings(payload["groupIds"]),
            creditInfo: Detach(payload["creditInfo"]),
            shippingAddresses: addresses);
    }

    // Un JsonNode no puede tener dos padres: se clona antes de devolverlo
    private static JsonNode? Detach(JsonNode? node) => node?.DeepClone();

    private static string[] Strings(JsonNode? node) => node is JsonArray array
        ? [.. array.Select(ClientIdentity.Text).Where(s => s.Length > 0)]
        : [];

    private static bool Bool(JsonNode? node, bool fallback)
    {
        if (node is null)
            return fallback;
        try { return node.GetValue<bool>(); }
        catch (Exception) { return fallback; }
    }
}
