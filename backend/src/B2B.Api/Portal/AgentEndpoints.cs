using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace B2B.Api.Portal;

// Modelo de agente, Fase 1 (contrato 04 §4). El comercial entra al portal, ve la
// cartera de clientes que su documento `agent` le asigna y suplanta a cualquiera de
// ellos. INVARIANTE de seguridad: un agente SOLO opera sobre los clientes de SU
// cartera; validar la pertenencia es el corazón de /impersonate.
public static class AgentEndpoints
{
    // BC parsea tokenExpiresIn con Evaluate sobre DateTime en sesión es-ES (igual que
    // el login): fecha absoluta local, no segundos de duración.
    private const string BcDateTimeFormat = "dd/MM/yyyy HH:mm:ss";

    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        // Cartera del comercial (pantalla /clients). Solo lectura, solo sus clientes.
        app.MapGet("/api/agent/clients", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var clientIds = await AgentClientIdsAsync(db, actor.User.AgentExternalId);
            var rows = await ClientRowsAsync(db, clientIds);

            // ── Filtros de la barra de /clients ──
            var q = request.Query;
            if (Str(q["search"]) is { Length: > 0 } search)
                rows = [.. rows.Where(r =>
                    r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (r.Number ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))];
            if (Str(q["city"]) is { Length: > 0 } city)
                rows = [.. rows.Where(r => (r.City ?? "").Contains(city, StringComparison.OrdinalIgnoreCase))];
            if (Str(q["segment"]) is { Length: > 0 } segment)
                rows = [.. rows.Where(r => r.Segments.Any(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase)))];
            if (Bool(q["active"]) is { } active)
                rows = [.. rows.Where(r => r.Active == active)];
            if (Bool(q["canShop"]) is { } canShop)
                rows = [.. rows.Where(r => r.CanShop == canShop)];

            var total = rows.Count;
            var skip = Int(q["skip"], 0);
            var take = Int(q["take"], 50);
            var items = rows
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(skip).Take(take)
                .Select(object (r) => new
                {
                    clientId = r.ClientId,
                    number = r.Number,
                    name = r.Name,
                    segment = r.Segments.FirstOrDefault(),
                    segments = r.Segments,
                    country = r.Country,
                    province = r.Province,
                    city = r.City,
                    canShop = r.CanShop,
                    active = r.Active,
                    valid = r.Valid,
                    lastOrderDate = r.LastOrderDate,
                    total = r.Total
                })
                .ToList();

            return Results.Ok(new { total, skip, take, items });
        }).RequireAgent();

        // Suplantación: el comercial elige un cliente de su cartera y recibe un token
        // que opera como ese cliente en todo /api/portal/*. 403 si el cliente no es suyo.
        app.MapPost("/api/agent/impersonate", async (
            ImpersonateRequest body, ClaimsPrincipal principal, AppDbContext db, IConfiguration config) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var clientId = body.ClientId?.Trim();
            if (string.IsNullOrEmpty(clientId))
                return Results.BadRequest(new { error = "clientId requerido." });

            var clientIds = await AgentClientIdsAsync(db, actor.User.AgentExternalId);
            var owned = clientIds.FirstOrDefault(id => string.Equals(id, clientId, StringComparison.OrdinalIgnoreCase));
            if (owned is null)
                // Nunca 404: no se confirma la existencia del cliente ajeno, se niega el acceso
                return Results.Json(new { error = "El cliente no pertenece a la cartera del agente." },
                    statusCode: StatusCodes.Status403Forbidden);

            var locale = DocumentProjections.Locale(actor.User.Culture);
            var card = await PortalEndpoints.ClientCardAsync(db, owned, locale);
            if (card is null)
                return Results.Json(new { error = "El cliente no está disponible." },
                    statusCode: StatusCodes.Status404NotFound);

            var (token, expiresAt) = IssueToken(config, actor.User, owned, card.number, actor.User.AgentExternalId);
            return Results.Ok(new
            {
                token,
                tokenExpiresIn = expiresAt.ToString(BcDateTimeFormat),
                client = card
            });
        }).RequireAgent();

        // Deseleccionar: reemite el token de agente SIN cliente. El front puede volver
        // a usar el token del login, pero esto evita reautenticar tras suplantar.
        app.MapPost("/api/agent/token", async (ClaimsPrincipal principal, AppDbContext db, IConfiguration config) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var (token, expiresAt) = IssueToken(config, actor.User, clientId: null, clientNumber: null, actingAgent: null);
            return Results.Ok(new { token, tokenExpiresIn = expiresAt.ToString(BcDateTimeFormat) });
        }).RequireAgent();

        // Alta de cliente (prealta). El maestro vive en BC y el sync es de una vía, así
        // que esto NO crea el cliente en BC: registra la solicitud para que compras la
        // valide, y de inmediato provisiona el usuario del contacto y le manda el correo
        // de activación (72h) para que fije su acceso sin esperar.
        app.MapPost("/api/agent/clients", async (
            JsonObject? body, ClaimsPrincipal principal, AppDbContext db, ActivationService activation) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);
            if (body is null)
                return Results.BadRequest(new { error = "Cuerpo de la solicitud vacío." });

            var name = ClientIdentity.Text(body["name"]).Trim();
            var email = ClientIdentity.Text(body["email"]).Trim().ToLowerInvariant();
            if (name.Length == 0)
                return Results.BadRequest(new { error = "El nombre de la empresa es obligatorio." });
            if (!LooksLikeEmail(email))
                return Results.BadRequest(new { error = "El email principal no es válido." });

            var preClient = BoolOr(body["createPreClient"] ?? body["preClient"], false);

            var request = new ClientRegistrationRequest
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                AgentExternalId = actor.User.AgentExternalId,
                CreatedByUserId = actor.UserId,
                Name = name,
                Email = email,
                PreClient = preClient,
                PayloadJson = body.ToJsonString(),
                Status = "pending"
            };
            db.ClientRegistrationRequests.Add(request);

            // Provisiona el usuario del contacto por email. Si ya existía (agente u otro
            // cliente), NO se pisa su rol ni su contraseña: solo se manda activación si
            // aún no tiene contraseña. El sync de BC, cuando llegue el cliente, enlazará
            // este mismo email con su ClientExternalId sin tocar la contraseña ya fijada.
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    PasswordHash = "",
                    Role = ClientIdentity.ClientAdminRole,
                    Culture = "es_ES",
                    Name = name
                };
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();

            var activationSent = false;
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                await activation.SendAsync(user, ActivationPurpose.Activation);
                activationSent = true;
            }

            return Results.Created($"/api/agent/clients/requests/{request.Id}", new
            {
                id = request.Id,
                name = request.Name,
                email = request.Email,
                preClient = request.PreClient,
                status = request.Status,
                createdAt = request.CreatedAt,
                activationSent
            });
        }).RequireAgent();

        // Bandeja de solicitudes de registro (prealtas). El agente ve las suyas; el
        // Sales admin (rol admin) ve todas. En el portal real esta ruta da 404: aquí sí
        // funciona.
        app.MapGet("/api/agent/clients/requests", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var isAdmin = string.Equals(actor.User.Role, AdminPolicy.Role, StringComparison.Ordinal);
            var query = db.ClientRegistrationRequests.AsQueryable();
            if (!isAdmin)
                query = query.Where(r => r.AgentExternalId == actor.User.AgentExternalId);

            var items = await query
                .OrderByDescending(r => r.CreatedAt)
                .Take(200)
                .Select(r => new
                {
                    id = r.Id,
                    name = r.Name,
                    email = r.Email,
                    preClient = r.PreClient,
                    status = r.Status,
                    createdAt = r.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(new { items });
        }).RequireAgent();
    }

    // Validación de email deliberadamente laxa: un "@" con algo a cada lado y un punto
    // en el dominio. No se pretende validar RFC 5322, solo descartar erratas obvias.
    private static bool LooksLikeEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 3 && email.IndexOf('.', at) > at;
    }

    public record ImpersonateRequest(string? ClientId);

    // ── Cartera del agente ─────────────────────────────────────────────────────

    /// Los clientIds que el documento `agent` del comercial le asigna (contrato 04 §4).
    /// El documento se localiza por AgentExternalId = id del comercial en BC. Un agente
    /// sin documento (o sin cartera) devuelve vacío: nunca ve clientes de otro.
    private static async Task<List<string>> AgentClientIdsAsync(AppDbContext db, string? agentExternalId)
    {
        if (string.IsNullOrEmpty(agentExternalId))
            return [];

        var doc = await db.SyncDocuments
            .SingleOrDefaultAsync(d => d.EntityType == "agent" && d.ExternalId == agentExternalId);
        if (doc is null || ClientIdentity.Parse(doc.Payload) is not { } payload)
            return [];

        return payload["clientIds"] is JsonArray array
            ? [.. array.Select(ClientIdentity.Text).Where(id => id.Length > 0)]
            : [];
    }

    private sealed record ClientRow(
        string ClientId, string? Number, string Name, string[] Segments,
        string? Country, string? Province, string? City,
        bool CanShop, bool Active, bool Valid, DateTimeOffset? LastOrderDate, decimal Total);

    private static async Task<List<ClientRow>> ClientRowsAsync(AppDbContext db, List<string> clientIds)
    {
        if (clientIds.Count == 0)
            return [];

        // Documentos de cliente de la cartera (case-insensitive contra ExternalId)
        var clientDocs = await db.SyncDocuments
            .Where(d => d.EntityType == "client")
            .ToListAsync();
        var wanted = new HashSet<string>(clientIds, StringComparer.OrdinalIgnoreCase);

        // Pedidos de la cartera, para fecha del último pedido e importe acumulado.
        // El IN va con la lista (Npgsql lo traduce a ANY); el agrupado se hace luego
        // case-insensitive en memoria, por si BC variara el caso del clientId.
        var orderDocs = await db.SyncDocuments
            .Where(d => d.EntityType == "order" && d.ParentId != null && clientIds.Contains(d.ParentId))
            .ToListAsync();
        var ordersByClient = orderDocs
            .Select(d => (d.ParentId, Row: ClientIdentity.Parse(d.Payload) is { } p
                ? DocumentProjections.Order(d.ExternalId, p) : null))
            .Where(x => x.Row is not null)
            .GroupBy(x => x.ParentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Row!).ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<ClientRow>();
        foreach (var doc in clientDocs)
        {
            if (!wanted.Contains(doc.ExternalId) || ClientIdentity.Parse(doc.Payload) is not { } payload)
                continue;

            var address = payload["fiscalInfo"]?["address"];
            ordersByClient.TryGetValue(doc.ExternalId, out var orders);
            var lastOrder = orders?.Where(o => o.Date is not null).Max(o => o.Date);
            var total = orders?.Sum(o => o.Total) ?? 0m;

            var number = ClientIdentity.Text(payload["externalReference"]);
            rows.Add(new ClientRow(
                ClientId: doc.ExternalId,
                Number: number.Length > 0 ? number : null,
                Name: ClientIdentity.Text(payload["name"]),
                Segments: payload["productSegments"] is JsonArray segs
                    ? [.. segs.Select(ClientIdentity.Text).Where(s => s.Length > 0)] : [],
                Country: NullIfEmpty(ClientIdentity.Text(address?["countryIsoId"])),
                Province: NullIfEmpty(ClientIdentity.Text(address?["province"])),
                City: NullIfEmpty(ClientIdentity.Text(address?["city"])),
                CanShop: BoolOr(payload["canShop"], fallback: true),
                // BC no manda estos flags para el cliente hoy: se asumen activos/válidos
                Active: BoolOr(payload["active"], fallback: true),
                Valid: BoolOr(payload["valid"], fallback: true),
                LastOrderDate: lastOrder,
                Total: total));
        }
        return rows;
    }

    // ── Emisión de token ───────────────────────────────────────────────────────

    // Mismo esquema que el login (AuthEndpoints): claims role/culture y, cuando
    // suplanta, clientId/clientNumber del cliente elegido más `actingAgent` (el id del
    // comercial) que marca la suplantación para PortalScope y para la auditoría.
    private static (string Token, DateTime ExpiresAt) IssueToken(
        IConfiguration config, AppUser agent, string? clientId, string? clientNumber, string? actingAgent)
    {
        var hours = config.GetValue("Jwt:LongDurationHours", 24);
        var expiresAt = DateTime.Now.AddHours(hours);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured")));

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, agent.Email),
            new("role", ClientIdentity.AgentRole),
            new("culture", agent.Culture)
        ];
        if (!string.IsNullOrEmpty(clientId))
            claims.Add(new Claim("clientId", clientId));
        if (!string.IsNullOrEmpty(clientNumber))
            claims.Add(new Claim("clientNumber", clientNumber));
        if (!string.IsNullOrEmpty(actingAgent))
            claims.Add(new Claim("actingAgent", actingAgent));

        var jwt = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt.ToUniversalTime(),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }

    // ── Utilidades de query ────────────────────────────────────────────────────

    private static string? Str(Microsoft.Extensions.Primitives.StringValues v)
    {
        var s = v.ToString().Trim();
        return s.Length > 0 ? s : null;
    }

    private static int Int(Microsoft.Extensions.Primitives.StringValues v, int fallback) =>
        int.TryParse(v.ToString(), out var n) && n >= 0 ? n : fallback;

    private static bool? Bool(Microsoft.Extensions.Primitives.StringValues v) => v.ToString().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "si" => true,
        "false" or "0" or "no" => false,
        _ => null
    };

    private static string? NullIfEmpty(string s) => s.Length > 0 ? s : null;

    private static bool BoolOr(JsonNode? node, bool fallback)
    {
        if (node is null)
            return fallback;
        try { return node.GetValue<bool>(); }
        catch (Exception) { return fallback; }
    }
}
