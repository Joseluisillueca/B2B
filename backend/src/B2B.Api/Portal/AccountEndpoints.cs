using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

public sealed record ProfileRequest(string? Name, string? Culture, PrefsRequest? Prefs);

public sealed record PrefsRequest(
    string? ShowPrices, string? ListDesktop, string? ListMobile, string? ShippingAddressId);

public sealed record PasswordRequest(string? Current, string? Next, string? Repeat);

public sealed record ChangeRequestBody(string? Section, JsonObject? Changes);

// Fase 4 del plan: cuenta (09-profile.png), empresa (10-business.png),
// estadísticas (11-statistics.png) y contacto (07-contact.png).
//
// INVARIANTE, igual que en el resto del portal: el clientId sale del token. Los
// datos maestros del cliente son de Business Central y aquí NO se escriben: lo que
// el cliente pide cambiar queda registrado como solicitud.
public static class AccountEndpoints
{
    private const int MinPasswordLength = 8;
    private const long MaxAttachmentBytes = 5 * 1024 * 1024;
    private const int MaxMonths = 60;

    // El portal habla en códigos de 2 letras y el conector en culturas de 6
    private static readonly Dictionary<string, string> Cultures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = "es_ES", ["en"] = "en_EN", ["fr"] = "fr_FR", ["it"] = "it_IT"
    };

    // Qué campo puede pedir cambiar cada tarjeta de /business. No es decorativo:
    // sin lista blanca, el formulario podría registrar una "solicitud" de cualquier
    // cosa (canShop, groupIds, límite de crédito…) y colarla al que la revise.
    private static readonly Dictionary<string, string[]> ChangeableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [BusinessSections.General] = ["email", "phone", "secondaryPhone", "web", "tradeName", "billingEmail"],
        [BusinessSections.Fiscal] = ["fiscalName", "fiscalId", "countryIsoId", "zipCode", "province",
                                     "city", "streetAddress", "recargoEquivalencia"],
        // M8: alta de dirección de envío. Mismos campos que el formulario del bloque
        // "Direcciones de envío" (views/business.js, ADDRESS_FIELDS).
        [BusinessSections.Addresses] = ["alias", "streetAddress", "num", "zipCode", "province",
                                        "city", "countryIsoId"]
    };

    /// El alias es lo que identifica la dirección en la lista: sin él no hay solicitud
    private const string AddressKey = "alias";

    // Adjuntos del formulario de contacto: lo que un cliente manda de verdad con una
    // incidencia (foto, albarán en PDF, hoja de cálculo). Sin ejecutables ni SVG.
    private static readonly string[] AllowedAttachments =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf", ".txt", ".csv", ".xlsx", ".xls", ".docx", ".doc", ".zip"];

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Perfil y preferencias (09-profile.png) ────────────────────────────
        app.MapGet("/api/portal/profile", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var prefs = await PortalPrefs.ReadAsync(db, actor.UserId);
            return Results.Ok(await ProfileAsync(db, actor, prefs));
        }).RequireAuthorization();

        app.MapPut("/api/portal/profile", async (
            ProfileRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var user = actor.User;

            if (body.Name is not null)
            {
                var name = body.Name.Trim();
                if (name.Length == 0)
                    return Bad("El nombre no puede quedar vacío.");
                user.Name = name.Length > 200 ? name[..200] : name;
            }

            if (!string.IsNullOrWhiteSpace(body.Culture))
            {
                var culture = NormalizeCulture(body.Culture);
                if (culture is null)
                    return Bad("Idioma no admitido. Los del portal son es, en, fr e it.");
                user.Culture = culture;
            }

            var prefs = await PortalPrefs.ReadAsync(db, actor.UserId, forUpdate: true);
            if (body.Prefs is { } wanted)
            {
                prefs.ShowPrices = PortalPrefValues.Pick(wanted.ShowPrices, PortalPrefValues.Prices, prefs.ShowPrices);
                prefs.ListDesktop = PortalPrefValues.Pick(wanted.ListDesktop, PortalPrefValues.Modes, prefs.ListDesktop);
                prefs.ListMobile = PortalPrefValues.Pick(wanted.ListMobile, PortalPrefValues.Modes, prefs.ListMobile);

                if (wanted.ShippingAddressId is { } addressId)
                {
                    var clean = addressId.Trim();
                    // La dirección por defecto tiene que ser del propio cliente: si no,
                    // el checkout arrancaría apuntando a la nave de otra tienda.
                    if (clean.Length > 0 && !await OwnsAddressAsync(db, actor.ClientId, clean))
                        return Bad("Esa dirección de envío no es de tu cliente.");
                    prefs.ShippingAddressId = clean.Length > 0 ? clean : null;
                }

                prefs.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            return Results.Ok(await ProfileAsync(db, actor, prefs));
        }).RequireAuthorization();

        // Banda gris "Cambiar contraseña" de 09-profile.png
        app.MapPost("/api/portal/password", async (
            PasswordRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var next = body.Next ?? "";
            if (next.Length < MinPasswordLength)
                return Bad($"La nueva contraseña necesita al menos {MinPasswordLength} caracteres.");
            if (next != (body.Repeat ?? ""))
                return Bad("La repetición no coincide con la nueva contraseña.");

            var hasher = new PasswordHasher<AppUser>();
            var user = actor.User;

            // Sin hash el usuario nunca ha tenido contraseña (lo provisionó el sync):
            // no puede "cambiarla" desde aquí sin demostrar que es suya.
            if (string.IsNullOrEmpty(user.PasswordHash) ||
                hasher.VerifyHashedPassword(user, user.PasswordHash, body.Current ?? "") == PasswordVerificationResult.Failed)
                return Bad("La contraseña actual no es correcta.");

            user.PasswordHash = hasher.HashPassword(user, next);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        // ── Empresa (10-business.png) ─────────────────────────────────────────
        app.MapGet("/api/portal/business", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var payload = await PortalScope.ClientPayloadAsync(db, actor.ClientId);
            if (payload is null)
                return Results.NotFound(new { error = "Tu usuario todavía no está asociado a ningún cliente." });

            var pending = await db.BusinessChangeRequests
                .Where(r => r.ClientId == actor.ClientId && r.Status == "pending")
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var addresses = await AddressesAsync(db, actor.ClientId);

            return Results.Ok(new
            {
                id = actor.ClientId,
                number = Text(payload["externalReference"]),
                name = Text(payload["name"]),
                general = General(payload),
                fiscal = Fiscal(payload),
                addresses,
                pending = pending.Select(Projection)
            });
        }).RequireAuthorization();

        // Los datos maestros viven en BC: el portal registra la petición y punto
        // (plan, Fase 4 — "no se escriben en BC en esta fase").
        app.MapPost("/api/portal/business/change-request", async (
            ChangeRequestBody body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var section = (body.Section ?? "").Trim().ToLowerInvariant();
            if (!ChangeableFields.TryGetValue(section, out var allowed))
                return Bad($"Sección desconocida: usa {string.Join(", ",
                    ChangeableFields.Keys.Select(key => $"\"{key}\""))}.");

            var changes = new JsonObject();
            foreach (var (key, value) in body.Changes ?? [])
            {
                if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
                    return Bad($"El campo \"{key}\" no se puede solicitar desde esta sección.");
                changes[key] = value?.DeepClone();
            }

            if (changes.Count == 0)
                return Bad("No has pedido ningún cambio.");

            // Una dirección sin alias no se puede ni listar ni referir después en BC
            if (section == BusinessSections.Addresses
                && string.IsNullOrWhiteSpace(DocumentProjections.Text(changes[AddressKey])))
                return Bad("La dirección necesita un alias.");

            var request = new BusinessChangeRequest
            {
                Id = Guid.NewGuid(),
                ClientId = actor.ClientId,
                UserId = actor.UserId,
                CreatedAt = DateTime.UtcNow,
                Section = section,
                ChangesJson = changes.ToJsonString(),
                Status = "pending"
            };
            db.BusinessChangeRequests.Add(request);
            await db.SaveChangesAsync();

            return Results.Created($"/api/portal/business/change-request/{request.Id}", Projection(request));
        }).RequireAuthorization();

        // ── Estadísticas (11-statistics.png) ──────────────────────────────────
        // "Ventas totales por meses": lo facturado al cliente, mes a mes. Los abonos
        // llegan por el mismo sitio en negativo y restan del mes en que se emiten.
        app.MapGet("/api/portal/statistics", async (
            HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var to = Day(request.Query["to"]) ?? today;
            var from = Day(request.Query["from"]) ?? new DateOnly(to.Year, to.Month, 1).AddMonths(-12);
            if (from > to) (from, to) = (to, from);

            var season = request.Query["season"].ToString().Trim();
            var locale = DocumentProjections.Locale(request.Query["locale"]);

            var docs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.InvoiceEntity);
            var invoices = docs
                .Select(doc => DocumentProjections.Invoice(doc.Id, doc.Payload, locale, today))
                .Where(invoice => invoice.Date is { } date
                    && DateOnly.FromDateTime(date.UtcDateTime) >= from
                    && DateOnly.FromDateTime(date.UtcDateTime) <= to)
                .ToList();

            // Un bucket por mes del rango, vacíos incluidos: si no, el gráfico
            // comprimiría el tiempo y dos meses seguidos con ventas parecerían
            // consecutivos aunque entre ellos hubiera medio año en blanco.
            var buckets = new List<object>();
            var total = 0m;
            var cursor = new DateOnly(from.Year, from.Month, 1);
            var last = new DateOnly(to.Year, to.Month, 1);
            for (var guard = 0; cursor <= last && guard < MaxMonths; cursor = cursor.AddMonths(1), guard++)
            {
                var month = cursor;
                var inMonth = invoices
                    .Where(invoice => invoice.Date is { } date
                        && date.UtcDateTime.Year == month.Year && date.UtcDateTime.Month == month.Month)
                    .ToList();
                var amount = inMonth.Sum(invoice => invoice.Total);
                total += amount;
                buckets.Add(new
                {
                    month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    amount,
                    count = inMonth.Count
                });
            }

            return Results.Ok(new
            {
                from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                currency = "EUR",
                total,
                count = invoices.Count,
                months = buckets,
                // "Temporada"/"Catálogo" no tienen origen en BC todavía (plan §5):
                // sin datos, el select va vacío y deshabilitado en vez de mentir.
                seasons = Array.Empty<string>(),
                catalogs = Array.Empty<string>(),
                season
            });
        }).RequireAuthorization();

        // ── Cuadros de mando (panel del comprador) ────────────────────────────
        // Varios paneles sobre las compras del cliente (o del cliente suplantado por
        // el agente): facturación por mes, top modelos, curva de tallas, reparto por
        // familia y embudo de pedidos. Mismo aislamiento que el resto: los documentos
        // salen de PortalScope.DocumentsAsync (por clientId del token), nunca de la query.
        app.MapGet("/api/portal/dashboard", async (
            HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var to = Day(request.Query["to"]) ?? today;
            var from = Day(request.Query["from"]) ?? new DateOnly(to.Year, to.Month, 1).AddMonths(-12);
            if (from > to) (from, to) = (to, from);
            var locale = DocumentProjections.Locale(request.Query["locale"]);

            bool InRange(DateTimeOffset? moment) => moment is { } dt
                && DateOnly.FromDateTime(dt.UtcDateTime) >= from
                && DateOnly.FromDateTime(dt.UtcDateTime) <= to;

            var invoiceDocs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.InvoiceEntity);
            var orderDocs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.OrderEntity);

            var invoices = invoiceDocs
                .Select(doc => (doc, row: DocumentProjections.Invoice(doc.Id, doc.Payload, locale, today)))
                .Where(entry => InRange(entry.row.Date))
                .ToList();

            // Nombre y familia del catálogo, para agrupar por modelo/familia con un
            // nombre limpio (la línea de factura trae el nombre con la talla dentro).
            var models = await db.CatalogModels.ToListAsync();
            var nameByRef = models
                .GroupBy(m => m.ExternalReference, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            var familyByRef = models
                .GroupBy(m => m.ExternalReference, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().FamilyId, StringComparer.OrdinalIgnoreCase);

            var lines = invoices
                .SelectMany(entry => DocumentProjections.DocumentLines(entry.doc.Payload, locale))
                .ToList();

            // Facturación por mes (mismo criterio y buckets que /api/portal/statistics)
            var months = new List<object>();
            var total = 0m;
            var cursor = new DateOnly(from.Year, from.Month, 1);
            var lastMonth = new DateOnly(to.Year, to.Month, 1);
            for (var guard = 0; cursor <= lastMonth && guard < MaxMonths; cursor = cursor.AddMonths(1), guard++)
            {
                var month = cursor;
                var amount = invoices
                    .Where(entry => entry.row.Date is { } date
                        && date.UtcDateTime.Year == month.Year && date.UtcDateTime.Month == month.Month)
                    .Sum(entry => entry.row.Total);
                total += amount;
                months.Add(new { month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture), amount });
            }

            var topModels = lines
                .GroupBy(line => line.Reference, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    reference = g.Key,
                    name = nameByRef.GetValueOrDefault(g.Key ?? "") ?? g.Key,
                    units = g.Sum(line => line.Quantity),
                    amount = g.Sum(line => line.Amount)
                })
                .OrderByDescending(model => model.amount)
                .Take(8)
                .ToList();

            var sizeCurve = lines
                .Where(line => line.Size.Length > 0)
                .GroupBy(line => line.Size)
                .Select(g => new { size = g.Key, units = g.Sum(line => line.Quantity) })
                .OrderBy(row => int.TryParse(row.size, out var n) ? n : int.MaxValue).ThenBy(row => row.size)
                .ToList();

            var byFamily = lines
                .GroupBy(line => familyByRef.GetValueOrDefault(line.Reference ?? "") is { Length: > 0 } fam ? fam : "—")
                .Select(g => new { family = g.Key, amount = g.Sum(line => line.Amount), units = g.Sum(line => line.Quantity) })
                .OrderByDescending(row => row.amount)
                .ToList();

            var orders = orderDocs
                .Select(doc => DocumentProjections.Order(doc.Id, doc.Payload))
                .OfType<OrderRow>()
                .ToList();
            var funnel = DocumentProjections.OrderStatuses
                .Select(status => new
                {
                    status,
                    count = orders.Count(order => order.Status == status),
                    amount = orders.Where(order => order.Status == status).Sum(order => order.Total)
                })
                .Where(row => row.count > 0)
                .ToList();

            return Results.Ok(new
            {
                from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                currency = "EUR",
                kpis = new
                {
                    invoiced = total,
                    units = lines.Sum(line => line.Quantity),
                    invoices = invoices.Count,
                    orders = orders.Count,
                    avgTicket = invoices.Count > 0 ? total / invoices.Count : 0m
                },
                months,
                topModels,
                sizeCurve,
                byFamily,
                funnel
            });
        }).RequireAuthorization();

        // ── Contacto (07-contact.png) ─────────────────────────────────────────
        app.MapPost("/api/portal/contact", async (
            HttpRequest request, ClaimsPrincipal principal, AppDbContext db,
            IConfiguration config, IWebHostEnvironment env, ILoggerFactory logs) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            if (!request.HasFormContentType)
                return Bad("Envía el formulario como multipart/form-data.");

            var form = await request.ReadFormAsync();
            var subject = Field(form["subject"], 200);
            var message = Field(form["message"], 4000);
            if (subject.Length == 0) return Bad("El asunto es obligatorio.");
            if (message.Length == 0) return Bad("El mensaje es obligatorio.");

            var email = Field(form["email"], 320);
            if (email.Length == 0) email = actor.User.Email;

            string? storedPath = null;
            string? attachmentName = null;
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is { Length: > 0 })
            {
                if (file.Length > MaxAttachmentBytes)
                    return Bad($"El adjunto supera el máximo de {MaxAttachmentBytes / (1024 * 1024)} MB.");

                var extension = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
                if (!AllowedAttachments.Contains(extension))
                    return Bad($"Formato de adjunto no permitido. Admitidos: {string.Join(", ", AllowedAttachments)}.");

                // Fuera de wwwroot a propósito: el adjunto de una incidencia no es
                // contenido público y nadie debe poder abrirlo por URL.
                var root = ContactRoot(config, env);
                Directory.CreateDirectory(root);
                attachmentName = Path.GetFileName(file.FileName ?? $"adjunto{extension}");
                storedPath = Path.Combine(root, $"{Guid.NewGuid():N}{extension}");
                await using var stream = File.Create(storedPath);
                await file.CopyToAsync(stream);
            }

            var inbox = config["Contact:Inbox"] is { Length: > 0 } configured ? configured : "tiendas@lejanbrand.com";

            var stored = new ContactMessage
            {
                Id = Guid.NewGuid(),
                ClientId = actor.ClientId,
                UserId = actor.UserId,
                CreatedAt = DateTime.UtcNow,
                Subject = subject,
                Email = email,
                Message = message,
                AttachmentName = attachmentName,
                AttachmentPath = storedPath,
                DeliveredTo = inbox
            };
            db.ContactMessages.Add(stored);
            await db.SaveChangesAsync();

            // El envío del correo es de la fase de integración; la solicitud ya está
            // guardada, así que aquí solo queda constancia de a dónde va dirigida.
            logs.CreateLogger("Portal.Contact").LogInformation(
                "Solicitud de contacto {Id} del cliente {ClientId} para {Inbox}", stored.Id, stored.ClientId, inbox);

            return Results.Created($"/api/portal/contact/{stored.Id}", new
            {
                id = stored.Id,
                subject = stored.Subject,
                email = stored.Email,
                deliveredTo = stored.DeliveredTo,
                attachment = stored.AttachmentName,
                createdAt = stored.CreatedAt
            });
        }).RequireAuthorization().DisableAntiforgery();
    }

    // ══════════════ Proyecciones ══════════════

    private static async Task<object> ProfileAsync(AppDbContext db, PortalActor actor, PortalUserPrefs prefs)
    {
        var user = actor.User;
        return new
        {
            email = user.Email,
            name = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
            rol = ClientIdentity.RoleLabel(user.Role),
            role = user.Role,
            culture = user.Culture,
            lang = DocumentProjections.Locale(user.Culture),
            clientName = (await PortalScope.ClientPayloadAsync(db, actor.ClientId)) is { } client
                ? Text(client["name"]) : "",
            prefs = PortalPrefs.Projection(prefs),
            shippingAddresses = await AddressesAsync(db, actor.ClientId)
        };
    }

    // Tarjeta "Datos generales": EMAIL · TELÉFONO · TELÉFONO SECUNDARIO ·
    // WEB · NOMBRE COMERCIAL · EMAIL FACTURACIÓN
    private static object General(JsonObject payload) => new
    {
        email = Text(payload["email"]),
        // El teléfono se enseña como en la referencia (solo el número); el prefijo
        // internacional va aparte para quien lo necesite marcar.
        phone = Text(payload["phone"]?["number"]).Trim(),
        phoneCode = Text(payload["phone"]?["code"]).Trim(),
        secondaryPhone = Text(First(payload["secondaryPhones"])?["number"]).Trim(),
        secondaryPhoneCode = Text(First(payload["secondaryPhones"])?["code"]).Trim(),
        web = Text(payload["web"]),
        tradeName = Text(payload["fiscalInfo"]?["alias"]),
        billingEmail = SecondaryEmail(payload, "Invoices")
    };

    // Tarjeta "Datos fiscales de la empresa": RAZÓN SOCIAL · CIF · PAÍS ·
    // CÓDIGO POSTAL · PROVINCIA · CIUDAD · DIRECCIÓN + recargo de equivalencia
    private static object Fiscal(JsonObject payload)
    {
        var fiscal = payload["fiscalInfo"];
        var address = fiscal?["address"];
        var street = Text(address?["streetAddress"]);
        var number = Text(address?["num"]);

        return new
        {
            fiscalName = Text(fiscal?["fiscalName"]),
            fiscalIdType = Text(fiscal?["fiscalId"]?["type"]),
            fiscalId = Text(fiscal?["fiscalId"]?["document"]),
            countryIsoId = Text(address?["countryIsoId"]),
            zipCode = Text(address?["zipCode"]),
            province = Text(address?["province"]),
            city = Text(address?["city"]),
            streetAddress = number.Length > 0 ? $"{street} {number}".Trim() : street,
            addressDetail = Text(address?["description"]),
            // BC lo publica como código del grupo de IVA (taxId): "recargo"/"req"
            // marcan el recargo de equivalencia del 5,2 % de la referencia.
            recargoEquivalencia = IsRecargo(Text(payload["taxId"]))
        };
    }

    private static bool IsRecargo(string taxId) =>
        taxId.Contains("recargo", StringComparison.OrdinalIgnoreCase) ||
        taxId.Contains("req", StringComparison.OrdinalIgnoreCase);

    private static object Projection(BusinessChangeRequest request) => new
    {
        id = request.Id,
        section = request.Section,
        status = request.Status,
        createdAt = request.CreatedAt,
        changes = Parse(request.ChangesJson)
    };

    private static JsonObject Parse(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static async Task<List<object>> AddressesAsync(AppDbContext db, string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            return [];

        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == "shipping-address" && d.ParentId == clientId)
            .OrderBy(d => d.ExternalId)
            .ToListAsync();

        return
        [
            .. docs
                .Select(doc => (doc.ExternalId, Json: ClientIdentity.Parse(doc.Payload)))
                .Where(doc => doc.Json is not null)
                .Select(object (doc) => new
                {
                    id = doc.ExternalId,
                    alias = Text(doc.Json!["alias"]),
                    address = doc.Json["address"]?.DeepClone(),
                    label = DocumentProjections.Address(doc.Json["address"])
                })
        ];
    }

    private static async Task<bool> OwnsAddressAsync(AppDbContext db, string? clientId, string addressId) =>
        !string.IsNullOrEmpty(clientId) && await db.SyncDocuments.AnyAsync(d =>
            d.EntityType == "shipping-address" && d.ParentId == clientId && d.ExternalId == addressId);

    // ══════════════ Lectura del payload y utilidades ══════════════

    private static string Text(JsonNode? node) => ClientIdentity.Text(node);

    private static JsonNode? First(JsonNode? node) =>
        node is JsonArray { Count: > 0 } array ? array[0] : null;

    private static string SecondaryEmail(JsonObject payload, string type) =>
        (payload["secondaryEmails"] as JsonArray ?? [])
        .FirstOrDefault(entry => string.Equals(Text(entry?["type"]), type, StringComparison.OrdinalIgnoreCase))
        is { } found ? Text(found["email"]) : "";

    private static string Field(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static string? NormalizeCulture(string culture)
    {
        var value = culture.Trim();
        if (Cultures.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
            return Cultures[value[..2]];
        return Cultures.TryGetValue(value, out var full) ? full : null;
    }

    private static DateOnly? Day(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    public static string ContactRoot(IConfiguration config, IWebHostEnvironment env) =>
        config["Contact:Root"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(env.ContentRootPath, "uploads", "contact");

    private static IResult Unknown() =>
        Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Bad(string error) => Results.BadRequest(new { error });
}

// Preferencias del usuario (portal_user_prefs). Viven aparte porque las lee tanto
// /profile como /me: el portal necesita saber en el arranque si el catálogo enseña
// PVD o PVP, sin pedir el perfil entero.
public static class PortalPrefs
{
    public static async Task<PortalUserPrefs> ReadAsync(AppDbContext db, Guid userId, bool forUpdate = false)
    {
        var stored = await db.PortalUserPrefs.SingleOrDefaultAsync(p => p.UserId == userId);
        if (stored is not null)
            return stored;

        var fresh = new PortalUserPrefs { UserId = userId, UpdatedAt = DateTime.UtcNow };
        if (forUpdate)
            db.PortalUserPrefs.Add(fresh);
        return fresh;
    }

    public static object Projection(PortalUserPrefs prefs) => new
    {
        showPrices = prefs.ShowPrices,
        listDesktop = prefs.ListDesktop,
        listMobile = prefs.ListMobile,
        shippingAddressId = prefs.ShippingAddressId
    };
}
