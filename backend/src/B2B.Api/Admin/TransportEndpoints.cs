using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// API del CMS para las REGLAS DE TRANSPORTE (portes). CRUD + previsualización ("¿qué transporte
// saldría para un pedido así?"). El coste resultante viaja en el JSON del pedido a BC
// (totalTransport + incotermId). Todo bajo policy cms-admin.
public static class TransportEndpoints
{
    public static void MapTransportEndpoints(this IEndpointRouteBuilder app)
    {
        // Listado (por prioridad; la 1ª que casa gana).
        app.MapGet("/api/admin/transport-rules", async (AppDbContext db) =>
        {
            var items = await db.TransportRules
                .OrderBy(r => r.Priority).ThenBy(r => r.Name)
                .ToListAsync();
            return Results.Ok(new { items });
        }).RequireAdmin();

        // Alta.
        app.MapPost("/api/admin/transport-rules", async (TransportRuleBody body, AppDbContext db) =>
        {
            if (Validate(body) is { } err) return Results.BadRequest(new { error = err });
            var r = new TransportRule { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            Apply(r, body);
            db.TransportRules.Add(r);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/transport-rules/{r.Id}", r);
        }).RequireAdmin();

        // Edición.
        app.MapPut("/api/admin/transport-rules/{id:guid}", async (Guid id, TransportRuleBody body, AppDbContext db) =>
        {
            if (Validate(body) is { } err) return Results.BadRequest(new { error = err });
            var r = await db.TransportRules.FindAsync(id);
            if (r is null) return Results.NotFound(new { error = "La regla no existe." });
            Apply(r, body);
            await db.SaveChangesAsync();
            return Results.Ok(r);
        }).RequireAdmin();

        // Borrado.
        app.MapDelete("/api/admin/transport-rules/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var r = await db.TransportRules.FindAsync(id);
            if (r is null) return Results.NotFound(new { error = "La regla no existe." });
            db.TransportRules.Remove(r);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();

        // Previsualización: dado un pedido de ejemplo, ¿qué regla casa y qué transporte sale?
        app.MapPost("/api/admin/transport-rules/preview", async (TransportPreview body, AppDbContext db) =>
        {
            var rules = await db.TransportRules.ToListAsync();
            var res = TransportRules.Evaluate(
                rules, body.ClientExternalId, body.CountryIsoId, body.OrderType,
                body.Units ?? 0, body.Amount ?? 0m);
            return Results.Ok(new
            {
                matched = res.Matched,
                ruleId = res.RuleId,
                ruleName = res.RuleName,
                cost = res.Cost,
                incotermId = res.IncotermId,
            });
        }).RequireAdmin();
    }

    private static string? Validate(TransportRuleBody b)
    {
        if (string.IsNullOrWhiteSpace(b.Name)) return "El nombre es obligatorio.";
        if (b.Name!.Trim().Length > 120) return "El nombre es demasiado largo (máx. 120 caracteres).";
        if (b.Cost < 0) return "El coste no puede ser negativo.";
        if (b.Cost > 1_000_000m) return "El coste es demasiado alto.";
        if (b.MinUnits is < 0) return "El mínimo de unidades no puede ser negativo.";
        if (b.MinAmount is < 0) return "El mínimo de importe no puede ser negativo.";
        var orderType = b.OrderType?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(orderType) && orderType is not ("REPLENISHMENT" or "SCHEDULED"))
            return "Tipo de pedido no válido (REPLENISHMENT | SCHEDULED).";
        // Business Central solo reconoce fob/usa como servicio (GetServiceType); el resto lo
        // descartaría en silencio, así que lo rechazamos aquí para no engañar.
        var incoterm = b.IncotermId?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(incoterm) && incoterm is not ("fob" or "usa"))
            return "Incoterm no válido: Business Central solo reconoce FOB o USA.";
        return null;
    }

    private static void Apply(TransportRule r, TransportRuleBody b)
    {
        r.Name = b.Name!.Trim();
        r.Active = b.Active ?? true;
        r.Priority = b.Priority ?? 0;
        r.ClientExternalId = Clean(b.ClientExternalId);
        r.CountryIsoId = Clean(b.CountryIsoId)?.ToUpperInvariant();
        r.OrderType = Clean(b.OrderType)?.ToUpperInvariant();
        r.MinUnits = b.MinUnits;
        r.MinAmount = b.MinAmount;
        r.Cost = b.Cost;
        r.PerUnit = b.PerUnit ?? false;
        // fob/usa en minúsculas: así el conector (GetServiceType) lo reconoce.
        r.IncotermId = Clean(b.IncotermId)?.ToLowerInvariant();
        r.UpdatedAt = DateTime.UtcNow;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record TransportRuleBody(
    string? Name, bool? Active, int? Priority,
    string? ClientExternalId, string? CountryIsoId, string? OrderType, int? MinUnits, decimal? MinAmount,
    decimal Cost, bool? PerUnit, string? IncotermId);

public sealed record TransportPreview(
    string? ClientExternalId, string? CountryIsoId, string? OrderType, int? Units, decimal? Amount);
