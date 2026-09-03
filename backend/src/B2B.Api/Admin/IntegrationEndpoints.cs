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
        app.MapGet("/api/admin/integration/settings", async (AppDbContext db, IConfiguration config) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1) ?? new IntegrationSettings();
            // Modo de pedidos efectivo: la BD manda; si está sin fijar, el env `Portal:OrdersMode`.
            var ordersMode = !string.IsNullOrWhiteSpace(s.OrdersMode) ? s.OrdersMode.ToLowerInvariant()
                : string.Equals(config["Portal:OrdersMode"], "portal", StringComparison.OrdinalIgnoreCase) ? "portal" : "erp";
            // El secreto NUNCA se devuelve en claro; solo si existe (hasSecret).
            return Results.Ok(new
            {
                s.Id, s.BcBaseUrl, s.BcTokenUrl, s.BcClientId, s.BcScope,
                s.ApiRestBaseUrl, s.ApiRestHeadersJson,
                emailLayoutHtml = string.IsNullOrWhiteSpace(s.EmailLayoutHtml) ? EmailTemplate.DefaultLayout : s.EmailLayoutHtml,
                ordersMode,
                // Marca del despliegue (multi-cliente): nombre/color efectivos + logo (o null).
                brandName = s.BrandNameOrDefault, brandColor = s.BrandColorOrDefault, brandLogoUrl = s.BrandLogoUrl,
                // Config de la cinta del catálogo (JSON parseado o null; se guarda con
                // PUT /api/admin/integration/ribbon, en VisibilityEndpoints).
                catalogRibbon = ParseRibbon(s.CatalogRibbonJson),
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
            // Layout de email: si llega vacío → null (vuelve al por defecto); si llega igual al
            // por defecto, tampoco se persiste (así los cambios del código siguen propagándose).
            if (body.EmailLayoutHtml is not null)
                s.EmailLayoutHtml = string.IsNullOrWhiteSpace(body.EmailLayoutHtml) || body.EmailLayoutHtml.Trim() == EmailTemplate.DefaultLayout
                    ? null : body.EmailLayoutHtml;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, bcConfigured = s.BcConfigured });
        }).RequireAdmin();

        // Diseño global del email (layout de marca). Endpoint dedicado para no tocar la
        // config de BC al guardar solo el layout. Vacío o == por defecto → null.
        app.MapPut("/api/admin/integration/email-layout", async (EmailLayoutBody body, AppDbContext db) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.EmailLayoutHtml = string.IsNullOrWhiteSpace(body.Layout) || body.Layout.Trim() == EmailTemplate.DefaultLayout ? null : body.Layout;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        }).RequireAdmin();

        // Modo de pedidos (portal = comunica a BC / erp = los gobierna BC). Endpoint dedicado
        // para el conmutador de Conexiones, sin tocar la configuración de BC.
        app.MapPut("/api/admin/integration/orders-mode", async (OrdersModeBody body, AppDbContext db) =>
        {
            var mode = (body.Mode ?? "").Trim().ToLowerInvariant();
            if (mode != "portal" && mode != "erp") return Results.BadRequest(new { error = "Modo no válido (portal | erp)." });
            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.OrdersMode = mode;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, ordersMode = mode });
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
            // Email: asunto y cuerpo editables (vacío → por defecto vía DefaultXFor).
            ch.Subject = string.IsNullOrWhiteSpace(body.Subject) ? null : body.Subject.Trim();
            ch.BodyHtml = string.IsNullOrWhiteSpace(body.BodyHtml) ? null : body.BodyHtml;
            ch.Active = body.Active;
            await db.SaveChangesAsync();
            return Results.Ok(Project(ch));
        }).RequireAdmin();

        app.MapGet("/api/admin/integration/channels/{id:guid}/default", async (Guid id, AppDbContext db) =>
        {
            var ch = await db.NotificationChannels.FindAsync(id);
            if (ch is null) return Results.NotFound();
            if (ch.ChannelType == "email")
                return Results.Ok(new { subject = EmailTemplate.DefaultSubjectFor(ch.EventKey), bodyHtml = EmailTemplate.DefaultBodyFor(ch.EventKey) });
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
            var items = await q.OrderByDescending(l => l.CreatedAt).Take(Math.Clamp(take, 1, 500))
                .Select(l => new
                {
                    l.Id, l.EventKey, l.EntityType, l.EntityId, l.ChannelType, l.Status, l.Detail, l.PayloadJson, l.CreatedAt,
                    // Se puede reprocesar un envío a BC del que guardamos el JSON de entrada.
                    canReprocess = l.ChannelType == "business-central" && l.InputJson != null,
                }).ToListAsync();
            return Results.Ok(new { items });
        }).RequireAdmin();

        // ── Reprocesar un envío a Business Central (re-transforma con el transformer actual) ──
        app.MapPost("/api/admin/integration/logs/{id:guid}/reprocess",
            async (Guid id, AppDbContext db, BcClient bc) =>
        {
            var settings = await db.IntegrationSettings.FindAsync(1) ?? new IntegrationSettings();
            var (ok, message) = await NotificationDispatcher.ReprocessBcAsync(db, bc, settings, id);
            return ok ? Results.Ok(new { ok, message }) : Results.BadRequest(new { ok, error = message });
        }).RequireAdmin();

        // ── Probar transformación (JUST.net) ──
        app.MapPost("/api/admin/integration/test-transform", (TransformTest body) =>
        {
            try { return Results.Ok(new { result = JsonTransformService.Transform(body.Transformer ?? "{}", body.Input ?? "{}") }); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequireAdmin();

        // ── Previsualizar email (asunto + cuerpo + layout, con variables de ejemplo) ──
        app.MapPost("/api/admin/integration/preview-email", async (EmailPreview body, AppDbContext db) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1);
            var vars = EmailTemplate.WithBrand(s, SampleEmailVars(body.EventKey));
            var subject = System.Net.WebUtility.HtmlDecode(EmailTemplate.Fill(body.Subject ?? "", vars));
            var html = EmailTemplate.RenderHtml(body.Layout ?? s?.EmailLayoutHtml, body.BodyHtml ?? "", vars);
            return Results.Ok(new { subject, html });
        }).RequireAdmin();

        // ── Marca del portal (multi-cliente): nombre, color de acento y logo ──
        // Endpoint dedicado para el bloque "Marca" de Conexiones, sin tocar la config de BC.
        app.MapPut("/api/admin/integration/branding", async (BrandingBody body, AppDbContext db) =>
        {
            var color = body.Color?.Trim();
            if (!string.IsNullOrEmpty(color) && !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                return Results.BadRequest(new { error = "El color debe ser hexadecimal (#rrggbb)." });
            var name = body.Name?.Trim();
            if (name?.Length > 60) return Results.BadRequest(new { error = "El nombre de marca es demasiado largo (máx. 60)." });

            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.BrandName = string.IsNullOrWhiteSpace(name) ? null : name;
            s.BrandColor = string.IsNullOrWhiteSpace(color) ? null : color.ToLowerInvariant();
            s.BrandLogoUrl = string.IsNullOrWhiteSpace(body.LogoUrl) ? null : body.LogoUrl.Trim();
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, name = s.BrandNameOrDefault, color = s.BrandColorOrDefault, logoUrl = s.BrandLogoUrl });
        }).RequireAdmin();

        // Marca PÚBLICA: la leen el portal y el back-office ANTES del login (cabecera, título,
        // color de acento). Solo expone la marca, nada sensible.
        app.MapGet("/api/portal/branding", async (AppDbContext db) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1);
            return Results.Ok(new
            {
                name = s?.BrandNameOrDefault ?? "MITO PROJECTS",
                color = s?.BrandColorOrDefault ?? "#ec3013",
                logoUrl = s?.BrandLogoUrl,
            });
        }).AllowAnonymous();
    }

    private static System.Text.Json.Nodes.JsonNode? ParseRibbon(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.Nodes.JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static object Project(NotificationChannel c) => new
    {
        id = c.Id, eventKey = c.EventKey, channelType = c.ChannelType, order = c.Order,
        active = c.Active, c.Fixed, endpoint = c.Endpoint, transformer = c.Transformer,
        toVars = c.ToVars, ccVars = c.CcVars, bccVars = c.BccVars,
        subject = c.Subject ?? EmailTemplate.DefaultSubjectFor(c.EventKey),
        bodyHtml = c.BodyHtml ?? EmailTemplate.DefaultBodyFor(c.EventKey),
    };

    // Variables de ejemplo para la previsualización de emails.
    private static Dictionary<string, string?> SampleEmailVars(string? eventKey) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["eventName"] = IntegrationDefaults.Event(eventKey ?? "")?.Name ?? "Evento",
        ["ref"] = "PED-1024", ["year"] = DateTime.UtcNow.Year.ToString(),
        ["greeting"] = "Hola", ["name"] = "Ana García",
        ["intro"] = "Se ha creado tu acceso al portal B2B. Para empezar, define tu contraseña:",
        ["button"] = "Definir mi contraseña",
        ["link"] = "https://portal.mitoprojects.com/es/es/activate?token=EJEMPLO",
        ["expiry"] = "El enlace caduca en 72 horas. Si no esperabas este correo, puedes ignorarlo.",
        ["signature"] = "Equipo Mito Projects B2B",
        ["clientEmail"] = "tienda@ejemplo.com", ["companyEmail"] = "ventas@mitoprojects.com",
        ["saleEmail"] = "comercial@mitoprojects.com", ["userEmail"] = "ana@ejemplo.com",
    };
}

public sealed record TransformTest(string? Transformer, string? Input);
public sealed record EmailPreview(string? EventKey, string? Subject, string? BodyHtml, string? Layout);
public sealed record EmailLayoutBody(string? Layout);
public sealed record OrdersModeBody(string? Mode);
public sealed record BrandingBody(string? Name, string? Color, string? LogoUrl);
