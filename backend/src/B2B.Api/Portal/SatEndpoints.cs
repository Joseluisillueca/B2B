using System.Globalization;
using System.Security.Claims;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

/// Alta de "NUEVA DEVOLUCIÓN" (08-sat.png)
public sealed record ReturnRequestBody(
    string? Type, string? PickupSlot, int Packages, int Items,
    string? Reference, string? Notes, string? PhotoUrl);

// Fase 4 del plan: /sat. Las devoluciones del portal son un flujo propio sobre
// return_requests, no documentos de Business Central (el traslado a BC es la Fase
// BC). Todo acotado al clientId del token: el clientId nunca llega por parámetro.
public static class SatEndpoints
{
    private const int DefaultTake = 12;   // "Mostrar 12" de la referencia
    private const int MaxTake = 500;
    private const int MaxNotes = 1000;

    public static void MapSatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portal/returns", async (
            HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var search = request.Query["search"].ToString().Trim();
            var status = request.Query["status"].ToString().Trim().ToLowerInvariant();
            var skip = Math.Max(0, Int(request.Query["skip"], 0));
            var take = Math.Clamp(Int(request.Query["take"], DefaultTake) is var t && t <= 0 ? DefaultTake : t, 1, MaxTake);

            var rows = await Scoped(db, actor).OrderByDescending(r => r.CreatedAt).ToListAsync();
            var client = await ClientNameAsync(db, actor);

            var matched = rows
                .Where(row => search.Length == 0 || Blob(row).Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // El rail cuenta sobre el conjunto buscado, no sobre el estado elegido:
            // así los contadores siguen visibles después de pinchar un estado.
            var counts = new Dictionary<string, int> { ["all"] = matched.Count };
            foreach (var known in ReturnStatuses.All)
                counts[known] = matched.Count(row => row.Status == known);

            var filtered = ReturnStatuses.All.Contains(status)
                ? matched.Where(row => row.Status == status).ToList()
                : matched;

            return Results.Ok(new
            {
                total = filtered.Count,
                skip,
                take,
                status,
                counts,
                items = filtered.Skip(skip).Take(take).Select(row => Summary(row, client))
            });
        }).RequireAuthorization();

        app.MapPost("/api/portal/returns", async (
            ReturnRequestBody body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var type = (body.Type ?? "").Trim().ToLowerInvariant();
            if (!ReturnTypes.All.Contains(type))
                return Bad($"Tipo de devolución no válido. Admitidos: {string.Join(", ", ReturnTypes.All)}.");

            var slot = (body.PickupSlot ?? "").Trim().ToLowerInvariant();
            if (!ReturnSlots.All.Contains(slot))
                return Bad($"Horario de recogida no válido. Admitidos: {string.Join(", ", ReturnSlots.All)}.");

            if (body.Packages <= 0)
                return Bad("Indica cuántos bultos recogemos.");
            if (body.Items <= 0)
                return Bad("Indica cuántos artículos devuelves.");

            var request = new ReturnRequest
            {
                Id = Guid.NewGuid(),
                Code = await NextCodeAsync(db),
                ClientId = actor.ClientId,
                UserId = actor.UserId,
                CreatedAt = DateTime.UtcNow,
                Type = type,
                PickupSlot = slot,
                Packages = body.Packages,
                Items = body.Items,
                Status = ReturnStatuses.Pending,
                Resolution = "",
                Reference = Trim(body.Reference, 120),
                Notes = Trim(body.Notes, MaxNotes) ?? "",
                PhotoUrl = Trim(body.PhotoUrl, 500)
            };

            db.ReturnRequests.Add(request);
            await db.SaveChangesAsync();

            var client = await ClientNameAsync(db, actor);
            return Results.Created($"/api/portal/returns/{request.Id}",
                Detail(request, client, actor.User.Email));
        }).RequireAuthorization();

        app.MapGet("/api/portal/returns/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var request = await Scoped(db, actor).SingleOrDefaultAsync(r => r.Id == id);
            // La devolución de otro cliente no existe: 404, no un 403 que confirme
            // que existe y de quién es.
            if (request is null) return NotFound();

            var owner = await db.Users
                .Where(u => u.Id == request.UserId)
                .Select(u => u.Email)
                .SingleOrDefaultAsync();

            return Results.Ok(Detail(request, await ClientNameAsync(db, actor), owner));
        }).RequireAuthorization();
    }

    // ── Ámbito ────────────────────────────────────────────────────────────────

    // Un usuario del cliente A no ve nada del cliente B. El usuario sin cliente
    // vinculado (el de integración) solo alcanza lo suyo.
    private static IQueryable<ReturnRequest> Scoped(AppDbContext db, PortalActor actor) =>
        string.IsNullOrEmpty(actor.ClientId)
            ? db.ReturnRequests.Where(r => r.ClientId == null && r.UserId == actor.UserId)
            : db.ReturnRequests.Where(r => r.ClientId == actor.ClientId);

    private static async Task<string> ClientNameAsync(AppDbContext db, PortalActor actor) =>
        await PortalScope.ClientPayloadAsync(db, actor.ClientId) is { } payload
            ? ClientIdentity.Text(payload["name"])
            : "";

    // ── Código correlativo (columna CÓDIGO) ───────────────────────────────────

    /// DEV-2026-0001. Correlativo por año sobre todas las solicitudes: el cliente
    /// lo cita por teléfono, así que tiene que ser corto y no repetirse.
    private static async Task<string> NextCodeAsync(AppDbContext db)
    {
        var prefix = $"DEV-{DateTime.UtcNow:yyyy}-";
        var codes = await db.ReturnRequests
            .Where(r => r.Code.StartsWith(prefix))
            .Select(r => r.Code)
            .ToListAsync();

        var highest = codes
            .Select(code => int.TryParse(code[prefix.Length..], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:0000}";
    }

    // ── Proyecciones ──────────────────────────────────────────────────────────

    // Las 10 columnas de 08-sat.png: IMG · CÓDIGO · FECHA · CLIENTE · TIPO ·
    // HORARIO · BULTOS · ITEMS · ESTADO · RESOLUCIÓN
    private static object Summary(ReturnRequest request, string client) => new
    {
        id = request.Id,
        code = request.Code,
        createdAt = request.CreatedAt,
        client,
        type = request.Type,
        pickupSlot = request.PickupSlot,
        packages = request.Packages,
        items = request.Items,
        status = request.Status,
        resolution = request.Resolution,
        photoUrl = request.PhotoUrl,
        reference = request.Reference
    };

    private static object Detail(ReturnRequest request, string client, string? owner) => new
    {
        id = request.Id,
        code = request.Code,
        createdAt = request.CreatedAt,
        client,
        type = request.Type,
        pickupSlot = request.PickupSlot,
        packages = request.Packages,
        items = request.Items,
        status = request.Status,
        resolution = request.Resolution,
        photoUrl = request.PhotoUrl,
        reference = request.Reference,
        notes = request.Notes,
        owner
    };

    private static string Blob(ReturnRequest request) =>
        $"{request.Code} {request.Reference} {request.Notes}";

    private static string? Trim(string? value, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return null;
        return text.Length > max ? text[..max] : text;
    }

    private static int Int(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static IResult Unknown() =>
        Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult NotFound() =>
        Results.NotFound(new { error = "La devolución no existe." });

    private static IResult Bad(string error) => Results.BadRequest(new { error });
}
