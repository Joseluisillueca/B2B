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
        app.MapGet("/api/portal/me", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var user = await CurrentUserAsync(principal, db);
            if (user is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            // M5: el nombre de la forma de pago viene traducido del sync. Sin parámetro
            // se usa la cultura del propio usuario, que es la que el portal ya respeta.
            var locale = request.Query.ContainsKey("locale")
                ? DocumentProjections.Locale(request.Query["locale"])
                : DocumentProjections.Locale(user.Culture);

            var client = await ClientCardAsync(db, user.ClientExternalId, locale);
            var role = ClientIdentity.RoleLabel(user.Role);
            // M-4: la etiqueta se queda por compatibilidad y la clave estable es la que
            // traduce el portal (roleKey en el usuario, typeKey/roleKey en la credencial).
            var roleKey = ClientIdentity.RoleKey(user.Role);

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
                        typeKey = ClientIdentity.ClientCredentialType,
                        role,
                        roleKey
                    }
                ];

            return Results.Ok(new
            {
                email = user.Email,
                name = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
                rol = role,
                roleKey,
                culture = user.Culture,
                credentials,
                client,
                // Preferencias de /profile: el catálogo necesita saber ya en el
                // arranque si enseña PVD o PVP (plan, Fase 4).
                prefs = PortalPrefs.Projection(await PortalPrefs.ReadAsync(db, user.Id))
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
        string[] productSegments,
        // M5: {id, name} para el desplegable del checkout. payMethodIds mantiene la
        // lista de códigos sueltos que publicaba el contrato anterior.
        IReadOnlyList<object> payMethods, string[] payMethodIds,
        string[] groupIds, JsonNode? creditInfo,
        IReadOnlyList<object> shippingAddresses);

    /// Una forma de pago del cliente: el código con el que viaja a BC y el nombre que
    /// se lee en el checkout (05-checkout.png, "MÉTODO DE PAGO").
    private sealed record PayMethod(string id, string name);

    private static async Task<ClientCard?> ClientCardAsync(AppDbContext db, string? clientId, string locale)
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
        var payMethodIds = Strings(payload["payMethods"]);
        return new ClientCard(
            id: clientId,
            number: number.Length > 0 ? number : null,
            name: ClientIdentity.Text(payload["name"]),
            fiscalInfo: Detach(payload["fiscalInfo"]),
            canShop: Bool(payload["canShop"], fallback: true),
            productSegments: Strings(payload["productSegments"]),
            payMethods: await PayMethodsAsync(db, payMethodIds, locale),
            payMethodIds: payMethodIds,
            groupIds: Strings(payload["groupIds"]),
            creditInfo: Detach(payload["creditInfo"]),
            shippingAddresses: addresses);
    }

    // M5. El cliente llega del sync con las formas de pago en códigos ("transf30") y el
    // nombre legible vive en el maestro que publica el conector aparte (payment-method,
    // contrato 03 §5). En el navegador no había ninguna otra fuente para casarlos:
    // /invoices trae el nombre sin código y /orders el código sin nombre.
    private static async Task<IReadOnlyList<object>> PayMethodsAsync(
        AppDbContext db, string[] codes, string locale)
    {
        if (codes.Length == 0)
            return [];

        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == "payment-method")
            .ToListAsync();
        var byCode = docs
            .GroupBy(d => d.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ClientIdentity.Parse(g.First().Payload),
                StringComparer.OrdinalIgnoreCase);

        // Se respeta el orden en el que BC manda las formas de pago del cliente
        return [.. codes.Select(object (code) => new PayMethod(code, PayMethodName(byCode, code, locale)))];
    }

    // Sin ficha en el maestro no se inventa nada: el código hace de nombre, que es
    // exactamente lo que el checkout enseñaba antes de esta corrección.
    private static string PayMethodName(
        Dictionary<string, JsonObject?> byCode, string code, string locale)
    {
        if (!byCode.TryGetValue(code, out var payload) || payload is null)
            return code;

        foreach (var field in new[] { "name", "description" })
            if (DocumentProjections.Localized(payload[field], locale) is { Length: > 0 } name)
                return name;

        return code;
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
