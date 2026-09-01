using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// API del CMS para "Condiciones de venta / promos": reglas con CONDICIONES (AND) + ACCIONES.
// CRUD + previsualización ("¿qué pasaría con un carrito así?"). Todo bajo policy cms-admin.
public static class SalesRulesEndpoints
{
    public static void MapSalesRulesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/sales-rules", async (AppDbContext db) =>
        {
            var rules = await db.SalesRules.OrderBy(r => r.Priority).ThenBy(r => r.Name).ToListAsync();
            // Devolvemos conditions/actions como JSON (objetos), no como string.
            return Results.Ok(new { items = rules.Select(Project) });
        }).RequireAdmin();

        app.MapPost("/api/admin/sales-rules", async (SalesRuleBody body, AppDbContext db) =>
        {
            if (Validate(body) is { } err) return Results.BadRequest(new { error = err });
            var r = new SalesRule { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            Apply(r, body);
            db.SalesRules.Add(r);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/sales-rules/{r.Id}", Project(r));
        }).RequireAdmin();

        app.MapPut("/api/admin/sales-rules/{id:guid}", async (Guid id, SalesRuleBody body, AppDbContext db) =>
        {
            if (Validate(body) is { } err) return Results.BadRequest(new { error = err });
            var r = await db.SalesRules.FindAsync(id);
            if (r is null) return Results.NotFound(new { error = "La regla no existe." });
            Apply(r, body);
            await db.SaveChangesAsync();
            return Results.Ok(Project(r));
        }).RequireAdmin();

        app.MapDelete("/api/admin/sales-rules/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var r = await db.SalesRules.FindAsync(id);
            if (r is null) return Results.NotFound(new { error = "La regla no existe." });
            db.SalesRules.Remove(r);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();

        // Previsualización: evalúa un carrito de ejemplo contra las reglas y devuelve el resultado.
        app.MapPost("/api/admin/sales-rules/preview", async (SalesPreview body, AppDbContext db) =>
        {
            var rules = await db.SalesRules.ToListAsync();
            var ctx = new SalesContext
            {
                ClientId = body.ClientId,
                GroupIds = string.IsNullOrWhiteSpace(body.GroupId) ? [] : [body.GroupId],
                Market = body.Market,
                CountryIsoId = body.CountryIsoId, OrderType = body.OrderType,
                Units = Math.Max(0, body.Units), Amount = Math.Max(0m, body.Amount),
                CreatedByAgent = body.CreatedByAgent,
                Date = DateOnly.TryParse(body.Date, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
                RateId = body.RateId,
                ModelIds = body.ModelIds ?? [], ProductIds = body.ProductIds ?? [],
                FamilyIds = body.FamilyIds ?? [], BrandIds = body.BrandIds ?? [],
            };
            var res = SalesRules.Evaluate(rules, ctx);
            return Results.Ok(new
            {
                denied = res.Denied, deniedReason = res.DeniedReason,
                freeShipping = res.FreeShipping, fixedTransport = res.FixedTransport,
                transportCost = res.TransportCost,
                lineDiscountPercent = res.LineDiscountPercent, lineDiscountFixed = res.LineDiscountFixed,
                matched = res.MatchedRuleIds,
            });
        }).RequireAdmin();
    }

    private static object Project(SalesRule r) => new
    {
        r.Id, r.Name, r.Active, r.Priority,
        conditions = Parse(r.ConditionsJson),
        actions = Parse(r.ActionsJson),
    };

    private static JsonNode? Parse(string? json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); }
        catch { return new JsonArray(); }
    }

    private static readonly HashSet<string> ConditionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "market", "country", "client", "client_group", "rate", "order_type",
        "models", "products", "families", "brands", "agent_cart",
        "units_lt", "min_units", "cart_total", "date_between",
    };
    private static readonly HashSet<string> ActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "deny", "free_shipping", "fixed_transport", "line_discount_percent", "line_discount_fixed", "set_incoterm",
    };

    private static string? Validate(SalesRuleBody b)
    {
        if (string.IsNullOrWhiteSpace(b.Name)) return "El nombre es obligatorio.";
        if (b.Name!.Trim().Length > 160) return "El nombre es demasiado largo (máx. 160).";
        if (!IsNonEmptyArray(b.Conditions)) return "Debe haber al menos una condición.";
        if (!IsNonEmptyArray(b.Actions)) return "Debe haber al menos una acción.";
        // Cada elemento debe ser un objeto {type} con un tipo conocido (evita reglas inertes).
        if (BadTypes(b.Conditions!.Value, ConditionTypes)) return "Hay una condición con un tipo no válido.";
        if (BadTypes(b.Actions!.Value, ActionTypes)) return "Hay una acción con un tipo no válido.";
        return null;
    }

    private static bool IsNonEmptyArray(JsonElement? el) =>
        el is { ValueKind: JsonValueKind.Array } arr && arr.GetArrayLength() > 0;

    private static bool BadTypes(JsonElement arr, HashSet<string> allowed)
    {
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String
                || !allowed.Contains(t.GetString() ?? ""))
                return true;
        }
        return false;
    }

    private static void Apply(SalesRule r, SalesRuleBody b)
    {
        r.Name = b.Name!.Trim();
        r.Active = b.Active ?? true;
        r.Priority = b.Priority ?? 0;
        r.ConditionsJson = b.Conditions?.GetRawText() ?? "[]";
        r.ActionsJson = b.Actions?.GetRawText() ?? "[]";
        r.UpdatedAt = DateTime.UtcNow;
    }
}

public sealed record SalesRuleBody(
    string? Name, bool? Active, int? Priority, JsonElement? Conditions, JsonElement? Actions);

public sealed record SalesPreview(
    string? ClientId, string? GroupId, string? Market, string? CountryIsoId, string? OrderType,
    int Units, decimal Amount, bool CreatedByAgent, string? Date, string? RateId,
    string[]? ModelIds, string[]? ProductIds, string[]? FamilyIds, string[]? BrandIds);
