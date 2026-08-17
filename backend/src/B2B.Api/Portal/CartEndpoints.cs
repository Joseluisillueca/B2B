using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

/// Una línea del carrito tal como la guarda el portal (modelo, talla, cantidad y precio)
public sealed record CartLine(
    string? ModelId,
    string? ProductId,
    string? Size,
    string? Name,
    string? Reference,
    int Qty,
    decimal Price);

public sealed record CartRequest(string? Name, string? WindowId, string? Reference, CartLine[]? Lines);

// Carritos favoritos (06-shopping-carts.png), corazones del catálogo y el cierre del
// checkout. Todo se acota al cliente del token: el clientId nunca llega por parámetro.
public static class CartEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static void MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Carritos guardados ────────────────────────────────────────────────
        app.MapGet("/api/portal/carts", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var carts = await Scoped(db, actor)
                .Where(c => c.IsFavorite && c.Status == CartStatus.Draft)
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync();

            var owners = await OwnersAsync(db, carts);
            return Results.Ok(new { items = carts.Select(cart => Summary(cart, owners)) });
        }).RequireAuthorization();

        app.MapPost("/api/portal/carts", async (CartRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();
            if (Invalid(body, requireName: true, out var lines, out var problem)) return problem;

            var cart = new Cart
            {
                Id = Guid.NewGuid(),
                ClientId = actor.ClientId,
                UserId = actor.UserId,
                Name = body.Name!.Trim(),
                ServiceWindowId = body.WindowId,
                Reference = body.Reference,
                LinesJson = JsonSerializer.Serialize(lines, Json),
                IsFavorite = true,
                Status = CartStatus.Draft,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Carts.Add(cart);
            await db.SaveChangesAsync();

            return Results.Created($"/api/portal/carts/{cart.Id}", Detail(cart, actor.User.Email));
        }).RequireAuthorization();

        app.MapGet("/api/portal/carts/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var cart = await Scoped(db, actor).SingleOrDefaultAsync(c => c.Id == id);
            if (cart is null) return NotFound();

            var owners = await OwnersAsync(db, [cart]);
            return Results.Ok(Detail(cart, owners.GetValueOrDefault(cart.UserId)));
        }).RequireAuthorization();

        app.MapPut("/api/portal/carts/{id:guid}", async (
            Guid id, CartRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();
            if (Invalid(body, requireName: true, out var lines, out var problem)) return problem;

            var cart = await Scoped(db, actor).SingleOrDefaultAsync(c => c.Id == id);
            if (cart is null) return NotFound();

            cart.Name = body.Name!.Trim();
            cart.ServiceWindowId = body.WindowId ?? cart.ServiceWindowId;
            cart.Reference = body.Reference ?? cart.Reference;
            cart.LinesJson = JsonSerializer.Serialize(lines, Json);
            cart.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var owners = await OwnersAsync(db, [cart]);
            return Results.Ok(Detail(cart, owners.GetValueOrDefault(cart.UserId)));
        }).RequireAuthorization();

        app.MapDelete("/api/portal/carts/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var cart = await Scoped(db, actor).SingleOrDefaultAsync(c => c.Id == id);
            if (cart is null) return NotFound();

            db.Carts.Remove(cart);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        // "DESCARGAR EXCEL" del checkout y del listado de carritos
        app.MapGet("/api/portal/carts/{id:guid}/export.csv", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var cart = await Scoped(db, actor).SingleOrDefaultAsync(c => c.Id == id);
            if (cart is null) return NotFound();

            var lines = Lines(cart);
            var csv = Csv.Build(
                ["Referencia", "Artículo", "Talla", "SKU", "Cantidad", "Precio", "Importe"],
                lines.Select(line => new object?[]
                {
                    line.Reference, line.Name, line.Size, line.ProductId,
                    line.Qty, line.Price, line.Qty * line.Price
                }));

            return Results.File(csv, "text/csv; charset=utf-8", $"{Slug(cart.Name)}.csv");
        }).RequireAuthorization();

        // ── Favoritos de modelo (corazón de la fila del catálogo) ─────────────
        app.MapGet("/api/portal/favorites", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var items = await db.PortalFavorites
                .Where(f => f.UserId == actor.UserId)
                .OrderBy(f => f.CreatedAt)
                .Select(f => f.ModelId)
                .ToListAsync();
            return Results.Ok(new { items });
        }).RequireAuthorization();

        app.MapPut("/api/portal/favorites/{modelId}", async (
            string modelId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var exists = await db.PortalFavorites
                .AnyAsync(f => f.UserId == actor.UserId && f.ModelId == modelId);
            if (!exists)
            {
                db.PortalFavorites.Add(new PortalFavorite
                {
                    UserId = actor.UserId,
                    ModelId = modelId,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapDelete("/api/portal/favorites/{modelId}", async (
            string modelId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var favorite = await db.PortalFavorites
                .SingleOrDefaultAsync(f => f.UserId == actor.UserId && f.ModelId == modelId);
            if (favorite is not null)
            {
                db.PortalFavorites.Remove(favorite);
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }).RequireAuthorization();

        // ── TERMINAR PEDIDO ───────────────────────────────────────────────────
        // El pedido queda registrado y a la espera: la entrega real a Business
        // Central es la Fase BC del plan (POST del contrato 04 §5).
        app.MapPost("/api/portal/orders", async (CartRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();
            if (Invalid(body, requireName: false, out var lines, out var problem)) return problem;

            var order = new Cart
            {
                Id = Guid.NewGuid(),
                ClientId = actor.ClientId,
                UserId = actor.UserId,
                Name = string.IsNullOrWhiteSpace(body.Name) ? DefaultOrderName() : body.Name.Trim(),
                ServiceWindowId = body.WindowId,
                Reference = body.Reference,
                LinesJson = JsonSerializer.Serialize(lines, Json),
                IsFavorite = false,
                Status = CartStatus.PendingBc,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Carts.Add(order);
            await db.SaveChangesAsync();

            return Results.Created($"/api/portal/carts/{order.Id}", Detail(order, actor.User.Email));
        }).RequireAuthorization();
    }

    // ── Ámbito y validación ───────────────────────────────────────────────────

    // Un usuario del cliente A no ve nada del cliente B. El usuario sin cliente
    // vinculado (el de integración) solo alcanza lo suyo.
    private static IQueryable<Cart> Scoped(AppDbContext db, PortalActor actor) =>
        string.IsNullOrEmpty(actor.ClientId)
            ? db.Carts.Where(c => c.ClientId == null && c.UserId == actor.UserId)
            : db.Carts.Where(c => c.ClientId == actor.ClientId);

    private static bool Invalid(CartRequest? body, bool requireName, out CartLine[] lines, out IResult problem)
    {
        lines = [];
        problem = Results.Empty;

        if (requireName && string.IsNullOrWhiteSpace(body?.Name))
        {
            problem = Results.BadRequest(new { error = "El carrito necesita un nombre." });
            return true;
        }

        var candidates = body?.Lines ?? [];
        if (candidates.Length == 0)
        {
            problem = Results.BadRequest(new { error = "El carrito está vacío." });
            return true;
        }

        if (candidates.Any(l => l.Qty <= 0 || string.IsNullOrWhiteSpace(l.ProductId)))
        {
            problem = Results.BadRequest(new { error = "Hay líneas sin producto o con cantidad no válida." });
            return true;
        }

        lines = candidates;
        return false;
    }

    private static IResult Unknown() =>
        Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult NotFound() =>
        Results.NotFound(new { error = "El carrito no existe." });

    // ── Proyecciones ──────────────────────────────────────────────────────────

    private static async Task<Dictionary<Guid, string>> OwnersAsync(AppDbContext db, IReadOnlyCollection<Cart> carts)
    {
        if (carts.Count == 0) return [];
        var ids = carts.Select(c => c.UserId).Distinct().ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);
    }

    private static object Summary(Cart cart, Dictionary<Guid, string> owners) => new
    {
        id = cart.Id,
        name = cart.Name,
        windowId = cart.ServiceWindowId,
        status = cart.Status,
        reference = cart.Reference,
        owner = owners.GetValueOrDefault(cart.UserId),
        units = Units(cart),
        total = Total(cart),
        createdAt = cart.CreatedAt,
        updatedAt = cart.UpdatedAt
    };

    private static object Detail(Cart cart, string? owner) => new
    {
        id = cart.Id,
        name = cart.Name,
        windowId = cart.ServiceWindowId,
        status = cart.Status,
        reference = cart.Reference,
        owner,
        units = Units(cart),
        total = Total(cart),
        createdAt = cart.CreatedAt,
        updatedAt = cart.UpdatedAt,
        lines = Lines(cart)
    };

    private static CartLine[] Lines(Cart cart)
    {
        try { return JsonSerializer.Deserialize<CartLine[]>(cart.LinesJson, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static int Units(Cart cart) => Lines(cart).Sum(l => l.Qty);

    private static decimal Total(Cart cart) => Lines(cart).Sum(l => l.Qty * l.Price);

    private static string DefaultOrderName() => $"Pedido {DateTime.Now:dd/MM/yyyy HH:mm}";

    private static string Slug(string name)
    {
        var clean = new string([.. name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);
        return clean.Trim('-') is { Length: > 0 } slug ? slug : "carrito";
    }
}
