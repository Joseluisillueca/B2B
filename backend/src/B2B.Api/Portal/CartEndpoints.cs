using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using B2B.Api.Data;
using B2B.Api.Notifications;
using B2B.Api.Shop;
using B2B.Api.Sync;
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

public sealed record CartRequest(
    string? Name, string? WindowId, string? Reference, CartLine[]? Lines,
    string? PayMethod = null, string? ShippingAddressId = null, string? Notes = null);

// Petición de previsualización de transporte (checkout): el carrito actual del cliente.
public sealed record TransportPreviewRequest(
    string? WindowId, string? ShippingAddressId, int Units, decimal Amount);

// Carritos favoritos (06-shopping-carts.png), corazones del catálogo y el cierre del
// checkout. Todo se acota al cliente del token: el clientId nunca llega por parámetro.
public static class CartEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    // Serializa la emisión del nº de pedido nativo (leer el máximo + guardar) para que
    // dos checkouts simultáneos no resuelvan el mismo número. Suficiente en despliegue
    // de una instancia (Railway); un despliegue multi-instancia pediría una secuencia BD.
    private static readonly SemaphoreSlim OrderNumberLock = new(1, 1);

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

            // 14a-6: solo se marca lo que existe, está activo y el actor puede ver — no se
            // guardan favoritos fantasma ni de modelos ocultos para su cuenta.
            var model = await db.CatalogModels.SingleOrDefaultAsync(m => m.ExternalId == modelId);
            if (model is null || !model.Active)
                return Results.BadRequest(new { error = "El modelo no existe en el catálogo." });
            var visibility = await Shop.VisibilityStore.ScopeForAsync(db, actor.ClientId, actor.User.AgentExternalId);
            if (visibility.IsRestricted && !visibility.Visible(model))
                return Results.BadRequest(new { error = "Este modelo no está disponible para tu cuenta." });

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
        // En modo ERP (por defecto) el pedido queda registrado y a la espera de su
        // envío a Business Central (Fase BC). En modo PORTAL (cliente sin ERP,
        // Portal:OrdersMode=portal) el pedido se guarda ADEMÁS como documento "order"
        // nativo: se ve al instante en /orders y se gestiona su estado desde el CMS.
        app.MapPost("/api/portal/orders", async (
            CartRequest body, ClaimsPrincipal principal, AppDbContext db, IConfiguration config,
            Integration.BcClient bc, IEmailSender email) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();
            // Integridad (Tarea 6b): sin cliente de ámbito no hay a quién atribuir el pedido
            // (un agente que no ha suplantado, o el usuario de integración/admin). Antes de
            // CUALQUIER validación o SaveChanges: hoy sin esto se colaba un Cart con ClientId
            // null.
            if (string.IsNullOrEmpty(actor.ClientId))
                return Results.BadRequest(new { error = "El pedido necesita un cliente: entra como cliente o suplanta a uno." });
            if (Invalid(body, requireName: false, out var lines, out var problem)) return problem;

            // Visibilidad + catálogo real (Tarea 5, endurecido en 14a-1): el checkout arma el
            // pedido con las líneas que manda el CLIENTE, sin pasar por CatalogService.QueryAsync
            // (donde la Tarea 4 enchufó el filtro). Un único punto, COMÚN a los dos modos
            // (portal/erp): aquí, con `lines` ya resuelto y ANTES de la primera bifurcación y
            // de cualquier SaveChanges. La unidad de verdad es el PRODUCTO (la talla que se
            // compra): cada línea se resuelve por su productId contra CatalogProducts, el
            // modelId se deriva del producto (si la línea no lo trae) o se exige coherente con
            // él, y la visibilidad se evalúa sobre el modelo DEL PRODUCTO. Antes se validaba el
            // modelId que declaraba la línea: bastaba declarar un modelo visible y colar el
            // productId de otro oculto. Todo corre SIEMPRE (no solo con reglas): un producto o
            // modelo desconocido o inactivo no es comprable en ningún caso — en modo erp nada
            // más valida las líneas (no hay RepriceAsync).
            var visibility = await Shop.VisibilityStore.ScopeForAsync(db, actor.ClientId, actor.User.AgentExternalId);
            var (resolved, blocked, blockedModelIds) = await ResolveLinesAsync(db, lines, visibility);
            if (blocked.Count > 0)
                return Results.BadRequest(new
                {
                    error = $"Estos artículos no están disponibles para tu cuenta: {string.Join(", ", blocked)}.",
                    // UX-M3: ids de modelo de las líneas bloqueadas, para que el front marque
                    // las líneas del carrito y ofrezca "Quitar artículos no disponibles".
                    blockedModelIds
                });
            lines = resolved;

            var settings = await db.IntegrationSettings.FindAsync(1) ?? new Data.IntegrationSettings();
            var portalMode = PortalOrdersMode(settings, config);
            string? orderType = null;
            Data.ServiceWindow? window = null;
            JsonObject? address = null;

            // Modo autónomo: el pedido guardado ES la fuente de verdad, así que el servidor
            // NO se fía del cliente. Re-tarifica cada línea contra el catálogo, resuelve el
            // tipo por la ventana real y valida dirección y forma de pago del cliente.
            if (portalMode)
            {
                if (!string.IsNullOrWhiteSpace(body.WindowId))
                    window = await db.ServiceWindows.SingleOrDefaultAsync(w => w.ExternalId == body.WindowId.ToLowerInvariant());
                orderType = string.IsNullOrWhiteSpace(window?.OrderType) ? null : window!.OrderType;

                var (priced, priceError) = await RepriceAsync(db, actor, orderType, lines);
                if (priceError is not null) return Results.BadRequest(new { error = priceError });
                lines = priced;

                address = await ShippingAddressAsync(db, actor.ClientId, body.ShippingAddressId);
                if (!string.IsNullOrEmpty(body.ShippingAddressId) && address is null)
                    return Results.BadRequest(new { error = "La dirección de envío indicada no pertenece al cliente." });

                if (!await PayMethodValidAsync(db, actor.ClientId, body.PayMethod))
                    return Results.BadRequest(new { error = "La forma de pago no está disponible para el cliente." });
            }

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

            JsonObject? sourceJson = null;
            var orderNumber = "";
            if (portalMode)
            {
                // "Condiciones de venta / promos": ÚNICO motor de reglas de venta. Puede DENEGAR el
                // carrito, fijar el transporte (portes gratis / importe fijo / incoterm) y aplicar
                // DESCUENTOS por línea. Nunca rompe el checkout (el pedido ya es válido).
                var units = lines.Sum(l => l.Qty);
                var amount = lines.Sum(l => l.Qty * l.Price);
                // País de envío: el de la dirección de envío; si viene vacío (envío = fiscal), el fiscal.
                var shipCountry = Str(address?["countryIsoId"]);
                var country = string.IsNullOrWhiteSpace(shipCountry) ? await ClientCountryAsync(db, actor.ClientId) : shipCountry;
                var cartModelIds = lines.Select(l => l.ModelId ?? "").Where(s => s.Length > 0).Distinct().ToArray();
                var cartFamilyIds = await FamilyIdsAsync(db, cartModelIds);
                Integration.SalesResult sales;
                try
                {
                    sales = Integration.SalesRules.Evaluate(await db.SalesRules.ToListAsync(), new Integration.SalesContext
                    {
                        ClientId = actor.ClientId, GroupIds = actor.GroupIds, Market = config["Portal:Market"],
                        CountryIsoId = country, OrderType = orderType, Units = units, Amount = amount,
                        Date = DateOnly.FromDateTime(DateTime.UtcNow),
                        // el carrito es "de agente" si el usuario es un comercial (suplantación).
                        CreatedByAgent = !string.IsNullOrEmpty(actor.User.AgentExternalId),
                        ModelIds = cartModelIds, FamilyIds = cartFamilyIds,
                        ProductIds = [.. lines.Select(l => l.ProductId ?? "").Where(s => s.Length > 0).Distinct()],
                    });
                }
                catch { sales = new Integration.SalesResult(); }

                // Una regla puede bloquear el pedido (el carrito no se guarda: aún no hay SaveChanges).
                if (sales.Denied)
                    return Results.BadRequest(new { error = string.IsNullOrWhiteSpace(sales.DeniedReason)
                        ? "Este pedido no cumple las condiciones de venta." : sales.DeniedReason });

                // Descuentos por línea (promos): rebajan el precio de cada línea → se reflejan en el
                // pedido nativo (lo ve el cliente) y en el JSON a BC.
                if (sales.LineDiscountPercent > 0 || sales.LineDiscountFixed > 0)
                    lines = ApplyLineDiscounts(lines, sales.LineDiscountPercent, sales.LineDiscountFixed);

                // Transporte e incoterm resultantes de las condiciones de venta (0 = portes gratis
                // por defecto si ninguna regla toca el transporte).
                var transportCost = sales.TransportCost ?? 0m;
                var incoterm = sales.Incoterm ?? "";

                // El cerrojo abarca leer el número + guardar: el siguiente pedido ya ve
                // este número emitido y toma el siguiente (nunca dos iguales).
                await OrderNumberLock.WaitAsync();
                try
                {
                    orderNumber = await NextOrderNumberAsync(db);
                    var doc = NativeOrder.Build(
                        orderId: order.Id.ToString(), number: orderNumber,
                        clientId: actor.ClientId, orderType: orderType, reference: body.Reference,
                        payMethodId: body.PayMethod, notes: body.Notes,
                        shippingAddress: address, lines: lines, now: DateTime.UtcNow,
                        transportCost: transportCost, saleId: actor.User.AgentExternalId ?? "");
                    await SyncEndpoints.IngestDocumentAsync(db, "order", order.Id.ToString(), actor.ClientId, doc);

                    // JSON de origen (forma "cart" de la referencia) para el transformer a BC.
                    // saleId = SystemId del comercial que crea el pedido suplantando al cliente
                    // (Multiagente §7); vacío para un pedido de cliente normal. BC resolverá ese
                    // SystemId a su Salesperson para atribuir la venta.
                    sourceJson = Integration.SourceJson.Order(
                        order.Id.ToString(), actor.ClientId, body.ShippingAddressId, body.Reference,
                        body.PayMethod, incotermId: incoterm, saleId: actor.User.AgentExternalId ?? "",
                        lines: lines, window: window, transportCost: transportCost);
                    order.SourceJson = sourceJson.ToJsonString();

                    await db.SaveChangesAsync();
                }
                finally { OrderNumberLock.Release(); }

                // Despacho a los canales del evento "Orden de compra" (BC + email).
                // Inerte si la conexión BC no está configurada (se registra "simulado").
                var vars = new Dictionary<string, string?>
                {
                    ["clientEmail"] = actor.User.Email,
                    ["userEmail"] = actor.User.Email,
                    ["companyEmail"] = config["Email:From"],
                    ["saleEmail"] = null,
                };
                await Integration.NotificationDispatcher.DispatchAsync(
                    db, bc, email, settings, "shoes.purchase_order.updated",
                    "PurchaseOrder", orderNumber, sourceJson!, vars);
            }
            else
            {
                await db.SaveChangesAsync();
            }

            return Results.Created($"/api/portal/carts/{order.Id}", Detail(order, actor.User.Email, portalMode));
        }).RequireAuthorization();

        // Previsualización del transporte para el CLIENTE: dado el carrito actual (ventana,
        // dirección de envío, unidades e importe), evalúa las reglas de portes y devuelve el
        // coste, para mostrarlo en el checkout antes de terminar el pedido. Solo informativo;
        // el coste definitivo se recalcula al terminar (con las líneas re-tarifadas).
        app.MapPost("/api/portal/transport-preview", async (
            TransportPreviewRequest body, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            if (actor is null) return Unknown();

            var window = string.IsNullOrWhiteSpace(body.WindowId) ? null
                : await db.ServiceWindows.SingleOrDefaultAsync(w => w.ExternalId == body.WindowId!.ToLowerInvariant());
            var orderType = string.IsNullOrWhiteSpace(window?.OrderType) ? null : window!.OrderType;
            var address = await ShippingAddressAsync(db, actor.ClientId, body.ShippingAddressId);
            var shipCountry = Str(address?["countryIsoId"]);
            var country = string.IsNullOrWhiteSpace(shipCountry) ? await ClientCountryAsync(db, actor.ClientId) : shipCountry;

            var units = Math.Max(0, body.Units);
            var amount = Math.Max(0m, body.Amount);
            // Un solo motor: "Condiciones de venta / promos".
            Integration.SalesResult sales;
            try
            {
                sales = Integration.SalesRules.Evaluate(await db.SalesRules.ToListAsync(), new Integration.SalesContext
                {
                    ClientId = actor.ClientId, GroupIds = actor.GroupIds, CountryIsoId = country, OrderType = orderType,
                    Units = units, Amount = amount, Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    CreatedByAgent = !string.IsNullOrEmpty(actor.User.AgentExternalId),
                });
            }
            catch { sales = new Integration.SalesResult(); }

            var cost = sales.TransportCost ?? 0m;
            return Results.Ok(new
            {
                cost,
                matched = sales.TransportCost is not null,
                denied = sales.Denied,
                deniedReason = sales.DeniedReason,
            });
        }).RequireAuthorization();
    }

    // El modo de pedidos: "portal" = autónomo (el portal comunica el pedido a BC);
    // cualquier otro valor = "erp" (los pedidos los gobierna BC). Manda la configuración de BD
    // (editable en /manage → Conexiones); si está sin fijar, se usa `Portal:OrdersMode` (env).
    private static bool PortalOrdersMode(Data.IntegrationSettings settings, IConfiguration config) =>
        !string.IsNullOrWhiteSpace(settings.OrdersMode)
            ? string.Equals(settings.OrdersMode, "portal", StringComparison.OrdinalIgnoreCase)
            : string.Equals(config["Portal:OrdersMode"], "portal", StringComparison.OrdinalIgnoreCase);

    // Tipo de pedido de la ventana de servicio elegida (SCHEDULED/REPLENISHMENT/...).
    private static async Task<string?> OrderTypeAsync(AppDbContext db, string? windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId)) return null;
        var key = windowId.ToLowerInvariant();
        var window = await db.ServiceWindows.SingleOrDefaultAsync(w => w.ExternalId == key);
        return string.IsNullOrWhiteSpace(window?.OrderType) ? null : window!.OrderType;
    }

    // Integridad de las líneas del checkout (14a-1). Devuelve las líneas con el modelId
    // resuelto (derivado del producto cuando la línea no lo trae), la lista de etiquetas
    // bloqueadas para el mensaje (referencia del modelo, o de la línea, o el productId) y
    // los modelId bloqueados (para que el front marque las líneas). Se bloquea:
    //  - producto desconocido, inactivo o case pack (aquí solo se compran tallas);
    //  - producto que no pertenece al modelId que declara la línea;
    //  - modelo del producto desconocido o inactivo;
    //  - modelo del producto fuera del VisibilityScope del actor (si está restringido).
    private static async Task<(CartLine[] Lines, List<string> Blocked, List<string> BlockedModelIds)> ResolveLinesAsync(
        AppDbContext db, CartLine[] lines, Shop.VisibilityScope visibility)
    {
        var productIds = lines.Select(l => l.ProductId!).Distinct().ToList();
        var products = (await db.CatalogProducts.Where(p => productIds.Contains(p.ExternalId)).ToListAsync())
            .GroupBy(p => p.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // SIN filtrar Active: un modelo desactivado debe listarse aquí para bloquearlo
        // (no para dejarlo pasar como "desconocido" con otro mensaje).
        var modelIds = products.Values.Select(p => p.ModelExternalId)
            .Concat(lines.Select(l => l.ModelId ?? ""))
            .Where(s => s.Length > 0).Distinct().ToList();
        var models = (await db.CatalogModels.Where(m => modelIds.Contains(m.ExternalId)).ToListAsync())
            .GroupBy(m => m.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var resolved = new CartLine[lines.Length];
        var blocked = new List<string>();
        var blockedModelIds = new List<string>();
        void Block(string label, string? modelId)
        {
            if (!blocked.Contains(label)) blocked.Add(label);
            if (!string.IsNullOrEmpty(modelId) && !blockedModelIds.Contains(modelId, StringComparer.OrdinalIgnoreCase))
                blockedModelIds.Add(modelId);
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineLabel = !string.IsNullOrWhiteSpace(line.Reference) ? line.Reference : line.ProductId!;

            if (!products.TryGetValue(line.ProductId!, out var product) || !product.Active || product.IsCasePack)
            {
                Block(lineLabel, line.ModelId);
                continue;
            }

            var modelId = string.IsNullOrWhiteSpace(line.ModelId) ? product.ModelExternalId : line.ModelId;
            if (!string.Equals(product.ModelExternalId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                Block(lineLabel, modelId);   // la talla no es de ese modelo
                continue;
            }

            if (!models.TryGetValue(product.ModelExternalId, out var model) || !model.Active)
            {
                Block(model?.ExternalReference is { Length: > 0 } reference ? reference : lineLabel, modelId);
                continue;
            }

            if (visibility.IsRestricted && !visibility.Visible(model))
            {
                Block(model.ExternalReference.Length > 0 ? model.ExternalReference : lineLabel, model.ExternalId);
                continue;
            }

            resolved[i] = line with { ModelId = model.ExternalId };
        }

        return (resolved, blocked, blockedModelIds);
    }

    // Re-tarifica cada línea con el MISMO motor que el catálogo (CatalogPricing): el
    // precio sale de las ofertas publicadas para ese cliente/ventana, nunca del cliente.
    // Si una línea no tiene tarifa aplicable, el pedido se rechaza (no hay precio válido).
    private static async Task<(CartLine[] Lines, string? Error)> RepriceAsync(
        AppDbContext db, PortalActor actor, string? orderType, CartLine[] lines)
    {
        var context = new PriceContext(actor.ClientId, actor.GroupIds, orderType);
        var now = DateTimeOffset.UtcNow;

        var modelIds = lines.Select(l => l.ModelId).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
        var offers = await db.Offers.Where(o => modelIds.Contains(o.ModelId)).ToListAsync();
        var byModel = offers.GroupBy(o => o.ModelId).ToDictionary(g => g.Key, g => g.ToList());

        var priced = new CartLine[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var modelOffers = byModel.GetValueOrDefault(line.ModelId ?? "", []);
            var price = CatalogPricing.Resolve(modelOffers, "PVD", line.ProductId, context, now);
            if (price is null)
                return ([], $"El artículo «{line.Name ?? line.Reference ?? line.ProductId}» no tiene precio en el catálogo para este cliente.");
            priced[i] = line with { Price = price.Value };
        }
        return (priced, null);
    }

    // Forma de pago válida: vacía o "card" (Stripe) siempre; en otro caso debe estar
    // entre las del cliente (si el cliente no tiene ninguna configurada, no se restringe).
    private static async Task<bool> PayMethodValidAsync(AppDbContext db, string? clientId, string? payMethod)
    {
        if (string.IsNullOrWhiteSpace(payMethod) || string.Equals(payMethod, "card", StringComparison.OrdinalIgnoreCase))
            return true;
        var client = await PortalScope.ClientPayloadAsync(db, clientId);
        var methods = (client?["payMethods"] as JsonArray ?? [])
            .Select(m => m is JsonObject o ? ClientIdentity.Text(o["id"] ?? o["code"]) : ClientIdentity.Text(m))
            .Where(s => s.Length > 0).ToList();
        return methods.Count == 0 || methods.Contains(payMethod, StringComparer.OrdinalIgnoreCase);
    }

    // Número de pedido visible, único y creciente. Se calcula sobre el MÁXIMO ya emitido
    // (no el recuento: borrar un pedido no debe reciclar su número) y se sube hasta uno
    // libre, de modo que nunca haya dos "P#####" iguales.
    private static async Task<string> NextOrderNumberAsync(AppDbContext db)
    {
        var payloads = await db.SyncDocuments
            .Where(d => d.EntityType == "order")
            .Select(d => d.Payload)
            .ToListAsync();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var max = 0;
        foreach (var payload in payloads)
        {
            var number = ClientIdentity.Text(ClientIdentity.Parse(payload)?["externalReference"]);
            if (number.Length == 0) continue;
            used.Add(number);
            if (number.Length > 1 && (number[0] is 'P' or 'p') && int.TryParse(number[1..], out var n))
                max = Math.Max(max, n);
        }

        var next = max + 1;
        string candidate;
        do { candidate = $"P{next++:D5}"; } while (used.Contains(candidate));
        return candidate;
    }

    // Snapshot de la dirección de envío elegida (documento shipping-address del cliente),
    // para que el pedido conserve a dónde iba aunque luego cambie la ficha del cliente.
    private static async Task<JsonObject?> ShippingAddressAsync(AppDbContext db, string? clientId, string? addressId)
    {
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(addressId))
            return null;
        var doc = await db.SyncDocuments.SingleOrDefaultAsync(d =>
            d.EntityType == "shipping-address" && d.ExternalId == addressId && d.ParentId == clientId);
        if (doc is null) return null;
        return ClientIdentity.Parse(doc.Payload)?["address"] as JsonObject;
    }

    // País (ISO) de la dirección FISCAL del cliente. Se usa como respaldo del país de envío
    // cuando el pedido no lleva dirección de envío propia (envío = fiscal), para las reglas de
    // transporte por país.
    private static async Task<string?> ClientCountryAsync(AppDbContext db, string? clientId)
    {
        if (string.IsNullOrEmpty(clientId)) return null;
        var doc = await db.SyncDocuments.SingleOrDefaultAsync(d => d.EntityType == "client" && d.ExternalId == clientId);
        if (doc is null) return null;
        return Str(ClientIdentity.Parse(doc.Payload)?["fiscalInfo"]?["address"]?["countryIsoId"]);
    }

    // Lee un nodo JSON como string (o null si no es un string). Evita excepciones de GetValue.
    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // Familias de los modelos del carrito (para las "Condiciones de venta" por familia).
    private static async Task<string[]> FamilyIdsAsync(AppDbContext db, IReadOnlyCollection<string> modelIds)
    {
        if (modelIds.Count == 0) return [];
        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == "model" && modelIds.Contains(d.ExternalId)).ToListAsync();
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
            if (Str(ClientIdentity.Parse(doc.Payload)?["familyId"]) is { Length: > 0 } fam) families.Add(fam);
        return [.. families];
    }

    // Aplica los descuentos por línea (promos): primero el % y luego el importe fijo (repartido
    // por unidad). El precio unitario nunca baja de 0.
    private static CartLine[] ApplyLineDiscounts(CartLine[] lines, decimal percent, decimal fixedPerLine) =>
        [.. lines.Select(l =>
        {
            var price = l.Price;
            if (percent > 0) price *= 1 - percent / 100m;
            if (fixedPerLine > 0 && l.Qty > 0) price -= fixedPerLine / l.Qty;
            return l with { Price = Math.Max(0m, Math.Round(price, 2, MidpointRounding.AwayFromZero)) };
        })];

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

    private static object Detail(Cart cart, string? owner, bool sentToBc = false) => new
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
        lines = Lines(cart),
        // true cuando el pedido se ha COMUNICADO a BC (modo portal); el frontend elige el aviso.
        sentToBc
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
