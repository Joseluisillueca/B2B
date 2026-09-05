using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
                // Tokens de diseño de la instancia (JSON parseado o null si no hay ninguno).
                brandTokens = VisibilityEndpoints.ParseNode(s.BrandTokensJson),
                // Config de la cinta del catálogo (JSON parseado o null; se guarda con
                // PUT /api/admin/integration/ribbon, en VisibilityEndpoints).
                catalogRibbon = VisibilityEndpoints.ParseNode(s.CatalogRibbonJson),
                // Catálogo: ocultar los artículos que todavía no tienen foto.
                s.RequireModelImage,
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
        // Catálogo de la instancia: por ahora, solo la regla de la foto. Endpoint propio y
        // pequeño, como el de la cinta o el de la marca: el PUT de settings es de la conexión
        // con BC y no debe cargarse ajustes de escaparate.
        app.MapPut("/api/admin/integration/catalog", async (CatalogOptionsBody body, AppDbContext db) =>
        {
            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.RequireModelImage = body.RequireModelImage;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, s.RequireModelImage });
        }).RequireAdmin();

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
        // `tokens` (opcional) lleva el resto del diseño de la instancia; ausente, null o {}
        // limpian los tokens guardados y el portal vuelve al diseño por defecto.
        app.MapPut("/api/admin/integration/branding", async (BrandingBody body, AppDbContext db) =>
        {
            var color = body.Color?.Trim();
            if (!string.IsNullOrEmpty(color) && !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                return Results.BadRequest(new { error = "El color debe ser hexadecimal (#rrggbb)." });
            var name = body.Name?.Trim();
            if (name?.Length > 60) return Results.BadRequest(new { error = "El nombre de marca es demasiado largo (máx. 60)." });
            // Se valida ANTES de tocar nada: un token inválido no guarda media marca.
            var (tokensJson, tokensError) = NormalizeBrandTokens(body.Tokens);
            if (tokensError is not null) return Results.BadRequest(new { error = tokensError });

            var s = await db.IntegrationSettings.FindAsync(1);
            if (s is null) { s = new IntegrationSettings { Id = 1 }; db.IntegrationSettings.Add(s); }
            s.BrandName = string.IsNullOrWhiteSpace(name) ? null : name;
            s.BrandColor = string.IsNullOrWhiteSpace(color) ? null : color.ToLowerInvariant();
            s.BrandLogoUrl = string.IsNullOrWhiteSpace(body.LogoUrl) ? null : body.LogoUrl.Trim();
            s.BrandTokensJson = tokensJson;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                ok = true, name = s.BrandNameOrDefault, color = s.BrandColorOrDefault, logoUrl = s.BrandLogoUrl,
                tokens = VisibilityEndpoints.ParseNode(s.BrandTokensJson),
            });
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
                // Resto del diseño de la instancia; null = diseño por defecto (MITO PROJECTS).
                tokens = VisibilityEndpoints.ParseNode(s?.BrandTokensJson),
            });
        }).AllowAnonymous();
    }

    // ── Tokens de marca (theming multi-cliente, fase 2) ───────────────────────
    // Lista CERRADA de tokens de diseño: lo que no esté en ella se descarta EN SILENCIO
    // (así el front puede mandar de más sin romper nada), pero un token conocido con un
    // valor inválido devuelve 400 y no se guarda nada. Todos son opcionales y su ausencia
    // significa "el valor por defecto de app.css": sin tokens el portal queda EXACTAMENTE
    // como el de MITO PROJECTS. Los valores acaban en variables CSS de :root, en atributos
    // src/href y dentro del url("…") de un @font-face, así que la validación es también la
    // barrera anti-inyección. Se valida por el lado ESTRICTO: el servidor nunca es más laja
    // que su espejo del portal (portal/js/branding.js), porque lo que él acepta y el portal
    // descarta se guarda con éxito y luego no se aplica, sin aviso ninguno.
    // Devuelve (JSON crudo a guardar | null para limpiar, error de validación | null).

    private const int BrandTokensMaxChars = 4096;                 // 4 KB (sobre el JSON COMPACTO)
    private const int BrandTokensRawMaxChars = 64 * 1024;         // corte barato del texto crudo
    private const int BrandTokenUrlMax = 500;

    // Colores (#rrggbb). `card` (fondo de paneles), `rule` (color de los filetes de capítulo)
    // y `accent` (segundo acento: favoritos, barras de cuadros de mando, avisos) entraron con
    // la extensión de BLOCCO 5; van en esta misma lista para reutilizar el validador de color
    // y no añadir ninguna regla nueva. Viven en la columna JSON BrandTokensJson, así que
    // ampliar la lista NO exige migración y una instancia que no los manda no se entera.
    private static readonly string[] BrandColorTokens =
        ["paper", "surface", "ink", "headerBg", "headerInk", "card", "rule", "accent"];
    private static readonly string[] BrandUrlTokens = ["logoUrlDark", "faviconUrl", "fontUrl"];
    // Medidas CSS con unidad. `ruleWidth` es el grosor de los filetes de capítulo y va junto a
    // `rule` a propósito: el color sin el grosor da un filete rojo de 2px, que ya no es un
    // filete sino una barra (el gesto de marca es la hairline).
    private static readonly string[] BrandLengthTokens = ["tracking", "radius", "radiusButton", "ruleWidth"];
    // Ronda 1 de crítica de BLOCCO 5 (tres cadenas con regla propia, ver abajo):
    //   heroStyle     → lista CERRADA de recetas de app.css: acaba en un atributo del <html>
    //                   que selecciona CSS, así que solo se admite lo que app.css conoce.
    //   displayWeight → peso de los titulares, una centena de 100 a 900 (font-weight).
    //   legal         → texto legal del login; como tagline pero con sitio para dos frases.
    private static readonly string[] BrandHeroStyles = ["paper"];
    private static readonly Regex BrandWeight = new("^[1-9]00$", RegexOptions.Compiled);
    private const int BrandLegalMax = 400;

    private static readonly Regex BrandHexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    // Medida CSS de verdad: un solo punto decimal y al menos un dígito. "..px" y "1.2.3px"
    // pasaban y dejaban el portal SIN radios (var() inválida → 0) sin ningún aviso.
    private static readonly Regex BrandCssLength =
        new(@"^-?(\d+(\.\d+)?|\.\d+)(px|rem|em|%)$", RegexOptions.Compiled);
    // Correo: el mismo patrón que asEmail() del portal (exige dominio con punto).
    private static readonly Regex BrandEmail =
        new(@"^[^\s@<>""']+@[^\s@<>""']+\.[^\s@<>""']+$", RegexOptions.Compiled);

    internal static (string? Json, string? Error) NormalizeBrandTokens(JsonElement? input)
    {
        // Ausente o null → se limpian los tokens guardados (vuelta al diseño por defecto).
        if (input is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return (null, null);
        if (element.ValueKind != JsonValueKind.Object)
            return (null, "tokens debe ser un objeto (o null para limpiar).");
        // El tope de verdad se mide al final sobre el JSON COMPACTO: la indentación con la
        // que venga la petición no debe contar, y el error concreto de un token («tagline»
        // es demasiado largo) tiene que ganar siempre al genérico. Aquí solo se corta lo
        // absurdo, antes de recorrer nada.
        if (element.GetRawText().Length > BrandTokensRawMaxChars)
            return (null, "Los tokens de marca ocupan demasiado (máx. 4 KB).");

        var tokens = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            var key = property.Name;
            var value = property.Value;
            if (value.ValueKind is JsonValueKind.Null) continue;   // null = ese token no se fija

            if (key == "caps")
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return (null, "«caps» debe ser un booleano (true | false).");
                tokens["caps"] = value.GetBoolean();
                continue;
            }

            var known = BrandColorTokens.Contains(key) || BrandUrlTokens.Contains(key)
                || BrandLengthTokens.Contains(key)
                || key is "heroFilter" or "fontFamily" or "tagline" or "supportEmail"
                || key is "heroStyle" or "displayWeight" or "legal";
            if (!known) continue;                                  // token desconocido: se ignora

            if (value.ValueKind != JsonValueKind.String)
                return (null, $"«{key}» debe ser una cadena de texto.");
            var text = value.GetString()!.Trim();
            if (text.Length == 0) continue;                        // vacío = ese token no se fija

            string? error = null;
            if (BrandColorTokens.Contains(key))
            {
                if (BrandHexColor.IsMatch(text)) text = text.ToLowerInvariant();
                else error = $"«{key}» debe ser un color hexadecimal (#rrggbb).";
            }
            else if (BrandUrlTokens.Contains(key))
            {
                error = text.Length > BrandTokenUrlMax
                    ? $"«{key}» es demasiado largo (máx. {BrandTokenUrlMax})."
                    : HasDangerousScheme(text) ? $"«{key}» no admite URLs javascript: ni data:."
                    : HasUnsafeUrlChars(text)
                        ? $"«{key}» no admite espacios, comillas, paréntesis, «<», «>», «\\», «;» ni llaves."
                        : null;
            }
            else if (BrandLengthTokens.Contains(key))
            {
                if (text.Length > 20 || !BrandCssLength.IsMatch(text))
                    error = $"«{key}» debe ser una medida CSS con unidad (px, rem, em o %).";
            }
            else if (key == "heroFilter")
            {
                error = text.Length > 120 ? "«heroFilter» es demasiado largo (máx. 120)."
                    : HasCssInjection(text)
                        ? "«heroFilter» no admite «;», llaves, «<», «\\», comentarios CSS ni «url(»."
                        : null;
            }
            else if (key == "fontFamily")
            {
                // Se emite entre comillas: --brand-font: "…". Fuera todo lo que pueda cerrarlas
                // o escaparlas (el portal, asFamily(), borra esos mismos caracteres: si el
                // servidor los dejara pasar, lo guardado y lo aplicado no coincidirían).
                error = text.Length > 60 ? "«fontFamily» es demasiado largo (máx. 60)."
                    : HasCssStringInjection(text)
                        ? "«fontFamily» no admite «;», llaves, «<», «>», comillas ni «\\»."
                        : null;
            }
            else if (key == "tagline")
            {
                // Texto plano: hoy el login lo pinta escapado, pero es un valor de administrador
                // que se publica y que mañana puede acabar en un email o en un meta; nada de
                // HTML crudo (coherente con el resto de tokens de texto).
                error = text.Length > 120 ? "«tagline» es demasiado largo (máx. 120)."
                    : text.Contains('<') || text.Contains('>') ? "«tagline» no admite «<» ni «>»." : null;
            }
            else if (key == "supportEmail")
            {
                error = text.Length > 120 ? "«supportEmail» es demasiado largo (máx. 120)."
                    : !BrandEmail.IsMatch(text)
                        ? "«supportEmail» debe ser una dirección de correo válida (o vacío)."
                        : null;
            }
            else if (key == "heroStyle")
            {
                // Se guarda en minúsculas: es el valor literal del selector CSS.
                var style = text.ToLowerInvariant();
                if (BrandHeroStyles.Contains(style)) text = style;
                else error = $"«heroStyle» solo admite {string.Join(", ", BrandHeroStyles.Select(s => $"«{s}»"))} (o vacío).";
            }
            else if (key == "displayWeight")
            {
                if (!BrandWeight.IsMatch(text))
                    error = "«displayWeight» debe ser un peso tipográfico en centenas, de 100 a 900 (p. ej. 900).";
            }
            else if (key == "legal")
            {
                // Mismo criterio que tagline (texto plano publicado): nada de HTML crudo.
                error = text.Length > BrandLegalMax ? $"«legal» es demasiado largo (máx. {BrandLegalMax})."
                    : text.Contains('<') || text.Contains('>') ? "«legal» no admite «<» ni «>»." : null;
            }
            if (error is not null) return (null, error);
            tokens[key] = text;
        }

        // Tope de tamaño, ya con cada token validado: se mide el JSON COMPACTO de la petición
        // (sin la indentación del cliente). Medirlo sobre el normalizado sería inalcanzable
        // —con los topes por token el máximo posible ronda los 2 KB—, y lo que hay que acotar
        // es lo que manda el cliente.
        if (JsonSerializer.Serialize(element).Length > BrandTokensMaxChars)
            return (null, "Los tokens de marca ocupan demasiado (máx. 4 KB).");

        // {} (o un objeto entero de tokens desconocidos) también limpia: no configura nada.
        return tokens.Count == 0 ? (null, null) : (tokens.ToJsonString(), null);
    }

    /// URLs de marca: se pintan en el portal, así que se cierran los esquemas ejecutables
    /// (comparando sin espacios ni caracteres de control, que es como los lee el navegador).
    private static bool HasDangerousScheme(string url)
    {
        var probe = new string([.. url.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))]).ToLowerInvariant();
        return probe.StartsWith("javascript:") || probe.StartsWith("data:") || probe.StartsWith("vbscript:");
    }

    /// URLs de marca: acaban en un atributo src/href del portal y dentro del url("…") del
    /// @font-face que se inyecta. Además del esquema se cierran los caracteres que rompen esos
    /// contextos, exactamente los mismos que rechaza asUrl() en el portal.
    private static bool HasUnsafeUrlChars(string url) =>
        url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)
            || c is '"' or '\'' or '(' or ')' or '<' or '>' or '\\' or ';' or '{' or '}');

    /// Valor que acaba dentro de una declaración CSS: no puede cerrarla, ni escapar un carácter
    /// (en CSS «\75» es una «u», así que «\75rl(…)» era un url() válido que se colaba), ni abrir
    /// un comentario, ni disparar peticiones.
    private static bool HasCssInjection(string css)
    {
        if (css.Contains(';') || css.Contains('}') || css.Contains('{') || css.Contains('<')
            || css.Contains('\\') || css.Contains("/*") || css.Contains("*/")) return true;
        return new string([.. css.Where(c => !char.IsWhiteSpace(c))]).ToLowerInvariant().Contains("url(");
    }

    /// Valor que acaba DENTRO de una cadena CSS entre comillas (fontFamily).
    private static bool HasCssStringInjection(string text) =>
        text.Any(c => c is '"' or '\'' or '\\' or '<' or '>' or '{' or '}' or ';');

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
public sealed record CatalogOptionsBody(bool RequireModelImage);

public sealed record BrandingBody(string? Name, string? Color, string? LogoUrl, JsonElement? Tokens = null);
