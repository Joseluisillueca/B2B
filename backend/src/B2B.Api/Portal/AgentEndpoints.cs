using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Integration;
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
            JsonObject? body, ClaimsPrincipal principal, AppDbContext db, ActivationService activation,
            BcClient bc, IEmailSender emailSender) =>
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
            if (name.Length > 200)
                return Results.BadRequest(new { error = "El nombre de la empresa es demasiado largo (máx. 200)." });
            if (!LooksLikeEmail(email))
                return Results.BadRequest(new { error = "El email principal no es válido." });
            if (email.Length > 320)
                return Results.BadRequest(new { error = "El email es demasiado largo (máx. 320)." });

            // Tope del payload (m-P3-2): la prealta se guarda íntegra en jsonb; sin límite
            // un agente podría hinchar la BD. 64 KB sobra para el formulario real.
            var payloadJson = body.ToJsonString();
            if (payloadJson.Length > 64 * 1024)
                return Results.BadRequest(new { error = "Los datos del formulario son demasiado grandes." });

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
                PayloadJson = payloadJson,
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

            // Empuja el cliente creado por el AGENTE a Business Central (evento
            // "agent.registration" → customers). request.Id es el GUID que BC fija como
            // SystemId. Inerte si BC no está configurado (se registra "simulated").
            try
            {
                var src = body.DeepClone()!.AsObject();
                src["id"] = request.Id.ToString();
                // Cada dirección de envío del formulario (plana) recibe su GUID → BC lo fija
                // como SystemId de la Ship-to Address.
                if (src["shippingAddresses"] is JsonArray sa)
                    foreach (var node in sa)
                        if (node is JsonObject o && string.IsNullOrEmpty(o["shippingAddressId"]?.GetValue<string>()))
                            o["shippingAddressId"] = Guid.NewGuid().ToString();
                var settings = await db.IntegrationSettings.FindAsync(1) ?? new IntegrationSettings();
                var vars = new Dictionary<string, string?> { ["clientEmail"] = email };
                await NotificationDispatcher.DispatchAsync(
                    db, bc, emailSender, settings, "agent.registration", "Customer", request.Id.ToString(), src, vars);
            }
            catch { /* el despacho a BC nunca debe romper el alta del agente */ }

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

        // ── Documentos agregados de la cartera (Fase 3) ────────────────────────────
        // Pedidos / albaranes / facturas de TODOS los clientes del agente en una sola
        // tabla con columna CLIENTE. Jerarquía: el rol admin (Sales admin) ve la cartera
        // completa; el agente, solo la suya. Se puede acotar a un cliente con ?client=.
        app.MapGet("/api/agent/documents", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var entity = (request.Query["type"].ToString().Trim().ToLowerInvariant()) switch
            {
                "order" => DocumentProjections.OrderEntity,
                "delivery-note" => DocumentProjections.DeliveryNoteEntity,
                "invoice" => DocumentProjections.InvoiceEntity,
                _ => null
            };
            if (entity is null)
                return Results.BadRequest(new { error = "type debe ser order, delivery-note o invoice." });

            var portfolio = await PortfolioAsync(db, actor);
            // ?client= acota a un cliente, pero SOLO si es de la cartera (aislamiento)
            var wanted = request.Query["client"].ToString().Trim();
            var ids = wanted.Length > 0
                ? (portfolio.ContainsKey(wanted) ? new[] { wanted } : Array.Empty<string>())
                : [.. portfolio.Keys];

            var query = DocumentQuery.Read(request.Query);
            var clientsList = portfolio.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(object (c) => new { id = c.Id, number = c.Number, name = c.Name })
                .ToList();

            if (ids.Length == 0)
                return Results.Ok(new { total = 0, query.Skip, query.Take, status = query.Status,
                    counts = new Dictionary<string, int> { ["all"] = 0 }, clients = clientsList, items = Array.Empty<object>() });

            var docs = await LoadPortfolioDocsAsync(db, entity, ids);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var orderType = request.Query["orderType"].ToString().Trim().ToUpperInvariant();

            // Local genérica: proyecta cada doc a su fila, la envuelve con su cliente,
            // pagina con el pipeline compartido y proyecta el item de salida.
            IResult Page<T>(Func<string, JsonObject, T?> project, IReadOnlyList<string> statuses,
                Func<AgentDocRow<T>, object> item, Func<T, bool>? keep = null) where T : class, IDocumentRow
            {
                var rows = docs
                    .Select(d => project(d.ExternalId, d.Payload) is { } r && (keep is null || keep(r))
                        ? new AgentDocRow<T>(portfolio[d.ClientId], r) : null)
                    .OfType<AgentDocRow<T>>();
                var page = DocumentProjections.Paginate(rows, query, statuses, DocumentProjections.ByDateDesc);
                return Results.Ok(new
                {
                    page.Total, query.Skip, query.Take, status = query.Status,
                    page.Counts, clients = clientsList,
                    items = page.Items.Select(item)
                });
            }

            return entity switch
            {
                DocumentProjections.OrderEntity => Page<OrderRow>(
                    DocumentProjections.Order, DocumentProjections.OrderStatuses,
                    r => new { client = Client(r), r.Inner.Id, r.Inner.Number, r.Inner.Date,
                        r.Inner.Reference, r.Inner.Type, r.Inner.Units, r.Inner.Total, r.Inner.Currency, r.Inner.Status },
                    keep: orderType.Length > 0 ? row => string.Equals(row.Type, orderType, StringComparison.OrdinalIgnoreCase) : null),

                DocumentProjections.DeliveryNoteEntity => Page<DeliveryNoteRow>(
                    DocumentProjections.DeliveryNote, DocumentProjections.DeliveryNoteStatuses,
                    r => new { client = Client(r), r.Inner.Id, r.Inner.Number, r.Inner.Date,
                        r.Inner.IsInvoiced, r.Inner.Total, r.Inner.Currency, r.Inner.Address, r.Inner.Status }),

                _ => Page<InvoiceRow>(
                    (id, p) => DocumentProjections.Invoice(id, p, query.Locale, today), DocumentProjections.InvoiceStatuses,
                    r => new { client = Client(r), r.Inner.Id, r.Inner.Number, r.Inner.Date,
                        r.Inner.PayMethod, r.Inner.Total, r.Inner.Debt, r.Inner.Currency, r.Inner.Status, r.Inner.DueDate })
            };
        }).RequireAgent();

        // Estadísticas agregadas de la cartera: ventas facturadas por mes + top clientes.
        app.MapGet("/api/agent/statistics", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var portfolio = await PortfolioAsync(db, actor);
            if (portfolio.Count == 0)
                return Results.Ok(new { currency = "EUR", total = 0m, count = 0, months = Array.Empty<object>(), topClients = Array.Empty<object>() });

            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var to = new DateOnly(today.Year, today.Month, 1);
            var from = to.AddMonths(-11);

            var docs = await LoadPortfolioDocsAsync(db, DocumentProjections.InvoiceEntity, [.. portfolio.Keys]);
            var invoices = docs
                .Select(d => (d.ClientId, Row: DocumentProjections.Invoice(d.ExternalId, d.Payload, locale, today)))
                .Where(x => x.Row.Date is { } date
                    && DateOnly.FromDateTime(date.UtcDateTime) >= from)
                .ToList();

            var buckets = new List<object>();
            var grand = 0m;
            for (var cursor = from; cursor <= to; cursor = cursor.AddMonths(1))
            {
                var month = cursor;
                var inMonth = invoices.Where(x => x.Row.Date!.Value.UtcDateTime.Year == month.Year
                    && x.Row.Date!.Value.UtcDateTime.Month == month.Month).ToList();
                var amount = inMonth.Sum(x => x.Row.Total);
                grand += amount;
                buckets.Add(new { month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture), amount, count = inMonth.Count });
            }

            var topClients = invoices
                .GroupBy(x => x.ClientId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { client = portfolio[g.Key].Name, number = portfolio[g.Key].Number, total = g.Sum(x => x.Row.Total) })
                .Where(c => c.total != 0)
                .OrderByDescending(c => c.total)
                .Take(10)
                .ToList();

            return Results.Ok(new
            {
                currency = "EUR",
                total = grand,
                count = invoices.Count,
                clients = portfolio.Count,
                from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                months = buckets,
                topClients
            });
        }).RequireAgent();

        // ── Calendario de citas / partes de visita (Fase 3) ────────────────────────
        // Agenda propia del comercial (no viene de BC). Cada agente ve y gestiona SOLO
        // sus citas; el admin (sin AgentExternalId) las ve todas.
        app.MapGet("/api/agent/appointments", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var mine = db.AgentAppointments.AsQueryable();
            if (!string.IsNullOrEmpty(actor.User.AgentExternalId))
                mine = mine.Where(a => a.AgentExternalId == actor.User.AgentExternalId);

            if (Day(request.Query["from"]) is { } from)
                mine = mine.Where(a => a.Start >= from.ToDateTime(TimeOnly.MinValue));
            if (Day(request.Query["to"]) is { } to)
                mine = mine.Where(a => a.Start < to.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var items = await mine
                .OrderBy(a => a.Start)
                .Take(500)
                .Select(a => new
                {
                    a.Id, a.ClientId, a.ClientName, a.Title, a.Notes, a.Kind,
                    a.Start, a.DurationMinutes, a.Status
                })
                .ToListAsync();

            return Results.Ok(new { items });
        }).RequireAgent();

        app.MapPost("/api/agent/appointments", async (
            AppointmentRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var title = (body.Title ?? "").Trim();
            if (title.Length == 0)
                return Results.BadRequest(new { error = "El título de la cita es obligatorio." });
            if (title.Length > 200) title = title[..200];
            if (body.Start is not { } start)
                return Results.BadRequest(new { error = "La fecha y hora de la cita son obligatorias." });

            // m-F3-P2: las notas también se recortan al límite de la columna (2000),
            // igual que el título; sin esto una nota larga reventaba en Npgsql 22001 (500).
            var notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim();
            if (notes is { Length: > 2000 }) notes = notes[..2000];

            var kind = (body.Kind ?? "").Trim().ToLowerInvariant();
            if (!AppointmentKind.All.Contains(kind)) kind = AppointmentKind.Cita;

            // Si se indica cliente, debe ser de la cartera del agente (aislamiento)
            string? clientId = null, clientName = null;
            var wanted = (body.ClientId ?? "").Trim();
            if (wanted.Length > 0)
            {
                var portfolio = await PortfolioAsync(db, actor);
                if (!portfolio.TryGetValue(wanted, out var info))
                    return Results.Json(new { error = "El cliente no pertenece a la cartera del agente." },
                        statusCode: StatusCodes.Status403Forbidden);
                clientId = info.Id;
                clientName = info.Name;
            }

            var appointment = new AgentAppointment
            {
                Id = Guid.NewGuid(),
                AgentExternalId = actor.User.AgentExternalId ?? "",
                CreatedByUserId = actor.UserId,
                CreatedAt = DateTime.UtcNow,
                ClientId = clientId,
                ClientName = clientName,
                Title = title,
                Notes = notes,
                Kind = kind,
                Start = start.UtcDateTime,
                DurationMinutes = body.DurationMinutes is > 0 and <= 24 * 60 ? body.DurationMinutes.Value : 60,
                Status = "pending"
            };
            db.AgentAppointments.Add(appointment);
            await db.SaveChangesAsync();

            return Results.Created($"/api/agent/appointments/{appointment.Id}", new
            {
                appointment.Id, appointment.ClientId, appointment.ClientName, appointment.Title,
                appointment.Notes, appointment.Kind, appointment.Start, appointment.DurationMinutes, appointment.Status
            });
        }).RequireAgent();

        // Marcar una cita (hecha / cancelada / pendiente). Solo la del propio agente.
        app.MapPost("/api/agent/appointments/{id:guid}/status", async (
            Guid id, AppointmentStatusRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var appointment = await db.AgentAppointments.SingleOrDefaultAsync(a => a.Id == id);
            // 404 si no existe o no es del agente: no se confirma la existencia de la ajena
            if (appointment is null ||
                (!string.IsNullOrEmpty(actor.User.AgentExternalId) && appointment.AgentExternalId != actor.User.AgentExternalId))
                return Results.NotFound(new { error = "La cita no existe." });

            var status = (body.Status ?? "").Trim().ToLowerInvariant();
            if (status is not ("pending" or "done" or "canceled"))
                return Results.BadRequest(new { error = "Estado no válido." });

            appointment.Status = status;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAgent();

        // ── Pedidos de selección de modelos (Fase 3) ───────────────────────────────
        // Catálogo de modelos para el selector "Añadir más modelos" (id, nombre, ref).
        app.MapGet("/api/agent/catalog-models", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var search = request.Query["search"].ToString().Trim();
            var skip = Int(request.Query["skip"], 0);
            var take = Math.Clamp(Int(request.Query["take"], 24), 1, 100);

            var models = await db.CatalogModels.Where(m => m.Active).ToListAsync();
            // Visibilidad (Tarea 5): el selector "Añadir más modelos" listaba TODO el
            // catálogo activo, cliente aparte — un agente restringido podía añadir a la
            // selección lo que ni el propio cliente ve. Scope SOLO del agente (sin cliente:
            // aquí no hay uno concreto, es la cartera entera del comercial).
            var visibility = await Shop.VisibilityStore.ScopeForAsync(db, null, actor.User.AgentExternalId);
            if (visibility.IsRestricted) models = models.Where(visibility.Visible).ToList();
            var imageByModel = (await db.SyncDocuments.Where(d => d.EntityType == "model-image").ToListAsync())
                .ToDictionary(d => d.ExternalId, d => ModelImageUri(d.Payload), StringComparer.OrdinalIgnoreCase);
            var rows = models
                .Select(m => new
                {
                    id = m.ExternalId,
                    name = ModelName(m, locale),
                    reference = m.ExternalReference,
                    image = imageByModel.GetValueOrDefault(m.ExternalId)
                })
                .Where(m => search.Length == 0
                    || m.name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || m.reference.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var page = rows.Skip(skip).Take(take).ToList();
            return Results.Ok(new { total = rows.Count, skip, take, items = page });
        }).RequireAgent();

        // Listado de selecciones del agente (admin ve todas)
        app.MapGet("/api/agent/model-selections", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var isAdmin = string.Equals(actor.User.Role, AdminPolicy.Role, StringComparison.Ordinal);
            var query = db.ModelSelections.AsQueryable();
            if (!isAdmin)
                query = query.Where(s => s.AgentExternalId == actor.User.AgentExternalId);

            var selections = await query.OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync();
            var items = selections.Select(s => new
            {
                s.Id,
                s.Name,
                models = CountJson(s.ModelIdsJson),
                clients = CountJson(s.ClientIdsJson),
                s.Status,
                s.CreatedAt,
                s.SentAt
            });
            return Results.Ok(new { items });
        }).RequireAgent();

        // Detalle de una selección (para poder VERLA tras crearla): nombre, estado, fechas,
        // y sus modelos y clientes resueltos (nombre; marca los que ya no existen).
        app.MapGet("/api/agent/model-selections/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);
            var isAdmin = string.Equals(actor.User.Role, AdminPolicy.Role, StringComparison.Ordinal);
            var sel = await db.ModelSelections.FirstOrDefaultAsync(s => s.Id == id);
            if (sel is null || (!isAdmin && sel.AgentExternalId != actor.User.AgentExternalId))
                return Results.NotFound();

            var locale = DocumentProjections.Locale(actor.User.Culture);
            var modelIds = ParseIds(sel.ModelIdsJson);
            var clientIds = ParseIds(sel.ClientIdsJson);

            // 14a-3: el detalle no enseña lo que ya no está en el surtido del agente (BC
            // puede restringirlo después de creada la selección).
            var visibility = await Shop.VisibilityStore.ScopeForAsync(db, null, actor.User.AgentExternalId);
            var modelRows = await db.CatalogModels.Where(m => modelIds.Contains(m.ExternalId)).ToListAsync();
            var models = modelIds.Select(mid =>
            {
                var m = modelRows.Find(x => x.ExternalId == mid);
                return (m, row: new { id = mid, name = m is null ? mid : ModelName(m, locale), missing = m is null });
            })
            .Where(x => x.m is null || !visibility.IsRestricted || visibility.Visible(x.m))
            .Select(x => x.row)
            .ToList();

            var clients = new List<object>();
            foreach (var cid in clientIds)
            {
                var payload = await PortalScope.ClientPayloadAsync(db, cid);
                clients.Add(new { id = cid, name = payload is null ? cid : ClientIdentity.Text(payload["name"]), missing = payload is null });
            }

            return Results.Ok(new { sel.Id, sel.Name, sel.Status, sel.CreatedAt, sel.SentAt, models, clients });
        }).RequireAgent();

        // Enviar por email una selección ya creada (borrador) a sus clientes.
        app.MapPost("/api/agent/model-selections/{id:guid}/send", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, IEmailSender email) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);
            var isAdmin = string.Equals(actor.User.Role, AdminPolicy.Role, StringComparison.Ordinal);
            var sel = await db.ModelSelections.FirstOrDefaultAsync(s => s.Id == id);
            if (sel is null || (!isAdmin && sel.AgentExternalId != actor.User.AgentExternalId))
                return Results.NotFound();

            var modelIds = ParseIds(sel.ModelIdsJson);
            var clientIds = ParseIds(sel.ClientIdsJson);
            if (modelIds.Count == 0 || clientIds.Count == 0)
                return Results.BadRequest(new { error = "La selección no tiene modelos o clientes." });

            var sentCount = await SendSelectionAsync(db, email, actor, sel.Name, modelIds, clientIds);
            if (sentCount > 0) { sel.Status = "sent"; sel.SentAt = DateTime.UtcNow; }
            await db.SaveChangesAsync();
            return sentCount == 0
                ? Results.BadRequest(new { error = "Ningún cliente de la selección tiene email." })
                : Results.Ok(new { sel.Status, sel.SentAt, sent = sentCount });
        }).RequireAgent();

        // Crear (guardar sin enviar) o crear+enviar el correo de selección a los clientes.
        app.MapPost("/api/agent/model-selections", async (
            ModelSelectionRequest body, ClaimsPrincipal principal, AppDbContext db, IEmailSender email) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null)
                return Results.Json(new { error = "Unknown user" }, statusCode: StatusCodes.Status401Unauthorized);

            var name = (body.Name ?? "").Trim();
            if (name.Length == 0)
                return Results.BadRequest(new { error = "Debe nombrar la selección." });
            if (name.Length > 200) name = name[..200];

            var requestedModels = (body.ModelIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(1000).ToList();
            if (requestedModels.Count == 0)
                return Results.BadRequest(new { error = "Debe añadir al menos un modelo." });
            // m-F3sel-P3-1: los modelos se validan contra el catálogo (se descartan los
            // inexistentes, igual que los clientes contra la cartera) para no guardar basura.
            var modelRows = await db.CatalogModels
                .Where(m => requestedModels.Contains(m.ExternalId))
                .ToListAsync();
            if (modelRows.Count == 0)
                return Results.BadRequest(new { error = "Debe añadir al menos un modelo." });
            // 14a-3: y contra el SURTIDO del agente (scope solo del agente: aquí no hay un
            // cliente concreto). Un modelo fuera de su scope no se descarta en silencio:
            // 400 nombrándolo, para que el comercial sepa qué no puede ofrecer.
            var visibility = await Shop.VisibilityStore.ScopeForAsync(db, null, actor.User.AgentExternalId);
            if (visibility.IsRestricted)
            {
                var outside = modelRows.Where(m => !visibility.Visible(m)).ToList();
                if (outside.Count > 0)
                    return Results.BadRequest(new
                    {
                        error = "Estos modelos no están en tu surtido: " + string.Join(", ",
                            outside.Select(m => m.ExternalReference.Length > 0 ? m.ExternalReference : m.ExternalId)) + ".",
                        modelIds = outside.Select(m => m.ExternalId).ToList()
                    });
            }
            var modelIds = modelRows.Select(m => m.ExternalId).ToList();

            // Los clientes destino DEBEN ser de la cartera del agente (aislamiento)
            var portfolio = await PortfolioAsync(db, actor);
            var clientIds = (body.ClientIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id) && portfolio.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (clientIds.Count == 0)
                return Results.BadRequest(new { error = "Debe seleccionar al menos un cliente." });

            var send = body.Send == true;
            var selection = new ModelSelection
            {
                Id = Guid.NewGuid(),
                AgentExternalId = actor.User.AgentExternalId ?? "",
                CreatedByUserId = actor.UserId,
                CreatedAt = DateTime.UtcNow,
                Name = name,
                ModelIdsJson = new JsonArray([.. modelIds.Select(id => (JsonNode?)JsonValue.Create(id))]).ToJsonString(),
                ClientIdsJson = new JsonArray([.. clientIds.Select(id => (JsonNode?)JsonValue.Create(id))]).ToJsonString(),
                Status = "draft",
                SentAt = null
            };
            db.ModelSelections.Add(selection);

            var sentCount = send ? await SendSelectionAsync(db, email, actor, name, modelIds, clientIds) : 0;
            // m-F3sel-P3-2: "enviada" solo si de verdad salió algún correo. Si se pidió
            // enviar pero ningún cliente tenía email, queda como borrador (no miente).
            if (send && sentCount > 0)
            {
                selection.Status = "sent";
                selection.SentAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();

            return Results.Created($"/api/agent/model-selections/{selection.Id}", new
            {
                selection.Id, selection.Name, models = modelIds.Count, clients = clientIds.Count,
                selection.Status, selection.SentAt, sent = sentCount
            });
        }).RequireAgent();
    }

    public sealed record ModelSelectionRequest(string? Name, List<string>? ModelIds, List<string>? ClientIds, bool? Send);

    // Nombre del modelo en el idioma pedido, con respaldo al nombre por defecto
    private static string ModelName(CatalogModel model, string locale)
    {
        try
        {
            if (JsonNode.Parse(model.NameTranslationsJson) is { } node
                && DocumentProjections.Localized(node, locale) is { Length: > 0 } name)
                return name;
        }
        catch (System.Text.Json.JsonException) { }
        return model.Name;
    }

    // Envía el correo de selección a cada cliente destino con email. 14a-3: la lista de
    // cada cliente se filtra por la INTERSECCIÓN agente ∩ cliente (VisibilityStore.
    // ScopeForAsync con los dos sujetos): un cliente nunca recibe modelos que no puede
    // ver en su catálogo. Sin modelos visibles para ese cliente, no se le escribe.
    // Devuelve cuántos correos salieron (el estado "sent" depende de que sea > 0).
    private static async Task<int> SendSelectionAsync(
        AppDbContext db, IEmailSender email, PortalActor actor, string name,
        List<string> modelIds, List<string> clientIds)
    {
        var locale = DocumentProjections.Locale(actor.User.Culture);
        var models = await db.CatalogModels.Where(m => modelIds.Contains(m.ExternalId)).ToListAsync();
        var mailSettings = await db.IntegrationSettings.FindAsync(1);
        var sentCount = 0;
        foreach (var clientId in clientIds)
        {
            var payload = await PortalScope.ClientPayloadAsync(db, clientId);
            var to = ClientIdentity.Text(payload?["email"]).Trim();
            if (to.Length == 0) continue;

            var visibility = await Shop.VisibilityStore.ScopeForAsync(db, clientId, actor.User.AgentExternalId);
            var modelNames = models
                .Where(m => !visibility.IsRestricted || visibility.Visible(m))
                .Select(m => ModelName(m, locale))
                .ToList();
            if (modelNames.Count == 0) continue;

            var message = SelectionEmail(to, name, modelNames, mailSettings);
            EmailResult result;
            try { result = await email.SendAsync(message); }
            catch (Exception ex) { result = new EmailResult(false, email.Transport, ex.Message); }
            db.SentEmails.Add(new SentEmail
            {
                Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, To = to,
                Subject = message.Subject, Body = message.TextBody,
                Transport = result.Transport, Error = result.Ok ? null : result.Error
            });
            sentCount++;
        }
        return sentCount;
    }

    private static string? ModelImageUri(string payload)
    {
        try { return (JsonNode.Parse(payload) as JsonObject)?["images"]?[0]?["image"]?["uri"]?.GetValue<string>(); }
        catch (System.Text.Json.JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static int CountJson(string json)
    {
        try { return JsonNode.Parse(json) is JsonArray array ? array.Count : 0; }
        catch (System.Text.Json.JsonException) { return 0; }
    }

    private static List<string> ParseIds(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray array
                ? [.. array.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!)]
                : [];
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static EmailMessage SelectionEmail(string to, string name, List<string> modelNames, Data.IntegrationSettings? settings)
    {
        var brand = settings?.BrandNameOrDefault ?? "MITO PROJECTS";
        var subject = $"Selección de modelos: {name}";
        var list = string.Join("\n", modelNames.Select(m => $" · {m}"));
        var text = $"Hola,\n\nTu comercial te ha preparado una selección de modelos «{name}» para tu pedido de temporada:\n\n{list}\n\nEntra en el portal B2B de {brand} para hacer tu pedido.\n\nEquipo {brand} B2B";
        var items = string.Join("", modelNames.Select(m => $"<li style=\"margin:.25em 0\">{System.Net.WebUtility.HtmlEncode(m)}</li>"));
        // Solo el cuerpo; la marca (cabecera/pie) la pone el layout global editable.
        var body = $"""
            <p style="margin:0 0 14px">Tu comercial te ha preparado la selección <b>{System.Net.WebUtility.HtmlEncode(name)}</b> para tu pedido de temporada:</p>
            <ul style="margin:0 0 14px;padding-left:1.2em">{items}</ul>
            <p style="margin:0">Entra en el portal B2B de {System.Net.WebUtility.HtmlEncode(brand)} para hacer tu pedido.</p>
            """;
        var vars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["subject"] = subject, ["year"] = DateTime.UtcNow.Year.ToString(),
        };
        var html = EmailTemplate.RenderHtml(settings?.EmailLayoutHtml, body, EmailTemplate.WithBrand(settings, vars));
        return new EmailMessage(to, subject, html, text);
    }

    public sealed record AppointmentRequest(
        string? ClientId, string? Title, string? Notes, string? Kind,
        DateTimeOffset? Start, int? DurationMinutes);

    public sealed record AppointmentStatusRequest(string? Status);

    private static DateOnly? Day(Microsoft.Extensions.Primitives.StringValues value) =>
        DateOnly.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    // ── Cartera para las vistas agregadas ──────────────────────────────────────
    private sealed record ClientInfo(string Id, string Number, string Name);

    // Envuelve una fila de documento con su cliente; delega el resto en la fila para
    // reutilizar el pipeline de filtrado/paginación. El buscador encuentra además por
    // número y nombre de cliente.
    private sealed record AgentDocRow<T>(ClientInfo Client, T Inner) : IDocumentRow where T : IDocumentRow
    {
        public string Number => Inner.Number;
        public DateTimeOffset? Date => Inner.Date;
        public string Status => Inner.Status;
        public string Season => Inner.Season;
        public string SearchBlob => $"{Inner.SearchBlob} {Client.Number} {Client.Name}";
    }

    private static object Client<T>(AgentDocRow<T> row) where T : IDocumentRow =>
        new { id = row.Client.Id, number = row.Client.Number, name = row.Client.Name };

    /// Cartera del actor como mapa clientId → info. Rol admin (Sales admin) ve TODOS
    /// los clientes; un agente, solo los de su documento `agent`.
    private static async Task<Dictionary<string, ClientInfo>> PortfolioAsync(AppDbContext db, PortalActor actor)
    {
        var isAdmin = string.Equals(actor.User.Role, AdminPolicy.Role, StringComparison.Ordinal);
        HashSet<string>? allowed = null;
        if (!isAdmin)
            allowed = new HashSet<string>(await AgentClientIdsAsync(db, actor.User.AgentExternalId), StringComparer.OrdinalIgnoreCase);

        var clientDocs = await db.SyncDocuments.Where(d => d.EntityType == "client").ToListAsync();
        var map = new Dictionary<string, ClientInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in clientDocs)
        {
            if (allowed is not null && !allowed.Contains(doc.ExternalId)) continue;
            if (ClientIdentity.Parse(doc.Payload) is not { } payload) continue;
            map[doc.ExternalId] = new ClientInfo(doc.ExternalId,
                ClientIdentity.Text(payload["externalReference"]), ClientIdentity.Text(payload["name"]));
        }
        return map;
    }

    private static async Task<List<(string ClientId, string ExternalId, JsonObject Payload)>> LoadPortfolioDocsAsync(
        AppDbContext db, string entity, string[] clientIds)
    {
        if (clientIds.Length == 0) return [];
        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == entity && d.ParentId != null && clientIds.Contains(d.ParentId))
            .ToListAsync();
        return
        [
            .. docs
                .Select(d => (d.ParentId!, d.ExternalId, Payload: ClientIdentity.Parse(d.Payload)))
                .Where(d => d.Payload is not null)
                .Select(d => (d.Item1, d.ExternalId, d.Payload!))
        ];
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
