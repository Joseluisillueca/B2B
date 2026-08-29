using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// API del CMS para la integración: Conexiones, Notificaciones (canales/transformers),
// Origen de documentos, historial y "Probar transformación". Todo bajo policy cms-admin.
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Conexiones ──
        app.MapGet("/api/admin/integration/settings", async (AppDbContext db) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1) ?? new IntegrationSettings();
            // El secreto NUNCA se devuelve en claro; solo si existe (hasSecret).
            return Results.Ok(new
            {
                s.Id, s.BcBaseUrl, s.BcTokenUrl, s.BcClientId, s.BcScope,
                s.ApiRestBaseUrl, s.ApiRestHeadersJson,
                bcConfigured = s.BcConfigured, hasSecret = !string.IsNullOrEmpty(s.BcClientSecret),
            });
        }).RequireAdmin();

        app.MapPut("/api/admin/integration/settings", async (IntegrationSettings body, AppDbContext db) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.BcBaseUrl = body.BcBaseUrl?.Trim();
            s.BcTokenUrl = body.BcTokenUrl?.Trim();
            s.BcClientId = body.BcClientId?.Trim();
            if (!string.IsNullOrWhiteSpace(body.BcClientSecret)) s.BcClientSecret = body.BcClientSecret.Trim();
            s.BcScope = string.IsNullOrWhiteSpace(body.BcScope) ? s.BcScope : body.BcScope.Trim();
            s.ApiRestBaseUrl = body.ApiRestBaseUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(body.ApiRestHeadersJson)) s.ApiRestHeadersJson = body.ApiRestHeadersJson;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, bcConfigured = s.BcConfigured });
        }).RequireAdmin();

        // ── Eventos + canales (Notificaciones → Configuración) ──
        app.MapGet("/api/admin/integration/events", async (AppDbContext db) =>
        {
            var channels = await db.NotificationChannels.OrderBy(c => c.Order).ToListAsync();
            var byEvent = channels.GroupBy(c => c.EventKey).ToDictionary(g => g.Key, g => g.ToList());
            var events = IntegrationDefaults.Events.Select(e => new
            {
                key = e.Key, name = e.Name, description = e.Description, e.Fixed,
                channels = (byEvent.GetValueOrDefault(e.Key) ?? []).Select(Project),
            });
            return Results.Ok(new { events });
        }).RequireAdmin();

        app.MapPost("/api/admin/integration/channels", async (NotificationChannel body, AppDbContext db) =>
        {
            body.Id = Guid.NewGuid();
            body.Fixed = false;
            db.NotificationChannels.Add(body);
            await db.SaveChangesAsync();
            return Results.Ok(Project(body));
        }).RequireAdmin();

        app.MapPut("/api/admin/integration/channels/{id:guid}", async (Guid id, NotificationChannel body, AppDbContext db) =>
        {
            var ch = await db.NotificationChannels.FindAsync(id);
            if (ch is null) return Results.NotFound();
            ch.Endpoint = body.Endpoint?.Trim();
            ch.Transformer = body.Transformer;
            ch.ToVars = body.ToVars?.Trim();
            ch.CcVars = body.CcVars?.Trim();
            ch.BccVars = body.BccVars?.Trim();
            ch.Active = body.Active;
            await db.SaveChangesAsync();
            return Results.Ok(Project(ch));
        }).RequireAdmin();

        app.MapGet("/api/admin/integration/channels/{id:guid}/default", async (Guid id, AppDbContext db) =>
        {
            var ch = await db.NotificationChannels.FindAsync(id);
            if (ch is null) return Results.NotFound();
            var t = IntegrationDefaults.DefaultTransformer(ch.Endpoint);
            return t is null
                ? Results.NotFound(new { error = "No hay plantilla por defecto para este endpoint." })
                : Results.Ok(new { transformer = t });
        }).RequireAdmin();

        app.MapDelete("/api/admin/integration/channels/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var ch = await db.NotificationChannels.FindAsync(id);
            if (ch is null) return Results.NotFound();
            if (ch.Fixed) return Results.BadRequest(new { error = "Este canal es fijo y no se puede eliminar." });
            db.NotificationChannels.Remove(ch);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();

        // ── Origen de documentos (descargas) ──
        app.MapGet("/api/admin/integration/document-sources", async (AppDbContext db) =>
            Results.Ok(new { items = await db.DocumentSources.ToListAsync() })).RequireAdmin();

        app.MapPut("/api/admin/integration/document-sources/{docType}", async (string docType, DocumentSource body, AppDbContext db) =>
        {
            var d = await db.DocumentSources.FindAsync(docType);
            if (d is null) { d = new DocumentSource { DocType = docType }; db.DocumentSources.Add(d); }
            d.SourceType = body.SourceType ?? "business-central";
            d.Method = body.Method ?? "GET";
            d.Endpoint = body.Endpoint ?? "";
            d.Transformer = body.Transformer ?? "";
            d.Active = body.Active;
            await db.SaveChangesAsync();
            return Results.Ok(d);
        }).RequireAdmin();

        // ── Historial (Notificaciones realizadas) ──
        app.MapGet("/api/admin/integration/logs", async (AppDbContext db, string? eventKey, int take = 100) =>
        {
            var q = db.NotificationLogs.AsQueryable();
            if (!string.IsNullOrEmpty(eventKey)) q = q.Where(l => l.EventKey == eventKey);
            var items = await q.OrderByDescending(l => l.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();
            return Results.Ok(new { items });
        }).RequireAdmin();

        // ── Probar transformación (JUST.net) ──
        app.MapPost("/api/admin/integration/test-transform", (TransformTest body) =>
        {
            try { return Results.Ok(new { result = JsonTransformService.Transform(body.Transformer ?? "{}", body.Input ?? "{}") }); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequireAdmin();
    }

    private static object Project(NotificationChannel c) => new
    {
        id = c.Id, eventKey = c.EventKey, channelType = c.ChannelType, order = c.Order,
        active = c.Active, c.Fixed, endpoint = c.Endpoint, transformer = c.Transformer,
        toVars = c.ToVars, ccVars = c.CcVars, bccVars = c.BccVars,
    };
}

public sealed record TransformTest(string? Transformer, string? Input);
