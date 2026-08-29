using System.Text.Json.Nodes;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Integration;
using B2B.Api.Notifications;
using B2B.Api.Sync;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// CRUD manual del CMS para el portal autónomo (clientes SIN ERP). El CMS escribe el
// MISMO documento que produciría el conector (mismo entityType, mismo JSON) y reutiliza
// la tubería de ingesta (crudo + normalización + identidad). El portal lo lee sin
// distinguir el origen. Precedente: Admin/ModelImageEndpoints.cs (para model-image).
//
// El id lo decide el llamante (el CMS), igual que el conector usa el SystemId de BC:
// GUID para model/product/offer/client/agent; slug/código para family/category/etc.
public static class EntityCrudEndpoints
{
    // Entidades que el CMS puede crear/editar como documento genérico. Las de provisión
    // especial (client-user con activación, order nativo) van por su propio endpoint en
    // fases posteriores. shipping-address necesita clientId (ParentId) → query param.
    private static readonly HashSet<string> Editable = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "product", "attribute", "category", "family",
        "offer", "inventory", "service-window", "warehouse",
        "payment-method", "client-group", "client", "agent", "shipping-address",
    };

    public static void MapEntityCrudEndpoints(this IEndpointRouteBuilder app)
    {
        // Upsert por id (crear = elegir un id nuevo; editar = reusar el existente).
        app.MapPut("/api/admin/entities/{entityType}/{id}",
            async (string entityType, string id, HttpRequest request, AppDbContext db,
                   BcClient bc, IEmailSender email, string? parentId) =>
        {
            if (!Editable.Contains(entityType))
                return Results.BadRequest(new { error = $"Tipo no editable desde el CMS: {entityType}" });

            if (await ReadJson(request) is not JsonObject payload)
                return Results.BadRequest(new { error = "El cuerpo debe ser un objeto JSON válido." });

            // Validación de obligatorios EN EL SERVIDOR: el front valida, pero el modo
            // "Avanzado (JSON)" y las llamadas directas a la API la saltaban, colando
            // registros vacíos al catálogo. Aquí no pasan.
            if (Validate(entityType, payload) is { } error)
                return Results.BadRequest(new { error });

            await SyncEndpoints.IngestDocumentAsync(db, entityType, id, parentId, payload);
            await db.SaveChangesAsync();

            // Despacho a Business Central (Registro de clientes / direcciones). Inerte si
            // la conexión BC no está configurada (se registra como "simulado").
            await DispatchRegistrationAsync(db, bc, email, entityType, id, parentId, payload);

            return Results.Ok(new { id });
        }).RequireAdmin().DisableAntiforgery();

        // Borrar: quita el documento crudo y su proyección de dominio (si la tiene).
        app.MapDelete("/api/admin/entities/{entityType}/{id}",
            async (string entityType, string id, AppDbContext db) =>
        {
            if (!Editable.Contains(entityType))
                return Results.BadRequest(new { error = $"Tipo no editable desde el CMS: {entityType}" });

            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == entityType && d.ExternalId == id);
            if (doc is null)
                return Results.NotFound(new { error = "No existe ese registro." });

            db.SyncDocuments.Remove(doc);
            CatalogNormalizer.Remove(db, entityType, id);       // limpia dominio (con cascada)
            await CascadeDeleteDocsAsync(db, entityType, id);   // limpia documentos hijos huérfanos
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();
    }

    private static async Task<JsonNode?> ReadJson(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        try { return JsonNode.Parse(body); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // Al crear/editar un cliente o una dirección, despacha el evento de registro hacia
    // Business Central (transform → POST). Inerte si BC no está configurado.
    private static async Task DispatchRegistrationAsync(
        AppDbContext db, BcClient bc, IEmailSender email,
        string entityType, string id, string? parentId, JsonObject payload)
    {
        string eventKey, entityLabel;
        JsonObject source;
        var vars = new Dictionary<string, string?>();

        if (entityType == "client")
        {
            var addrDocs = await db.SyncDocuments
                .Where(d => d.EntityType == "shipping-address" && d.ParentId == id).ToListAsync();
            var addrs = addrDocs
                .Select(d => (d.ExternalId, Payload: Portal.ClientIdentity.Parse(d.Payload)))
                .Where(x => x.Payload is not null)
                .Select(x => (x.ExternalId, x.Payload!));
            source = SourceJson.Client(id, payload, addrs);
            eventKey = "client.registration"; entityLabel = "Customer";
            vars["clientEmail"] = CatalogNormalizer.Text(payload["email"]);
        }
        else if (entityType == "shipping-address")
        {
            source = SourceJson.Address(id, parentId, payload);
            eventKey = "address.registration"; entityLabel = "ShipToAddress";
        }
        else return;

        var settings = await db.IntegrationSettings.FindAsync(1) ?? new IntegrationSettings();
        await NotificationDispatcher.DispatchAsync(db, bc, email, settings, eventKey, entityLabel, id, source, vars);
    }

    // ── Validación de obligatorios por entidad (espejo del `req` del front) ──────
    private static readonly Dictionary<string, string[]> RequiredFields = new()
    {
        ["model"] = ["name", "externalReference"],
        ["product"] = ["modelId", "name", "sku"],
        ["offer"] = ["modelId", "basePrice.value"],
        ["inventory"] = ["stockServiceId"],
        ["service-window"] = ["id", "name"],
        ["family"] = ["code", "name"],
        ["category"] = ["name"],
        ["attribute"] = ["code", "name"],
        ["client"] = ["name", "externalReference"],
        ["warehouse"] = ["code", "description"],
        ["payment-method"] = ["externalReference", "name"],
        ["client-group"] = ["externalReference", "name"],
        ["agent"] = ["email", "name"],
        ["shipping-address"] = ["alias"],
    };

    private static string? Validate(string entityType, JsonObject payload)
    {
        if (!RequiredFields.TryGetValue(entityType, out var fields)) return null;
        foreach (var field in fields)
            if (IsEmpty(Dig(payload, field)))
                return $"Falta un campo obligatorio: «{field}».";
        return null;
    }

    private static JsonNode? Dig(JsonObject payload, string path)
    {
        JsonNode? node = payload;
        foreach (var key in path.Split('.'))
        {
            node = (node as JsonObject)?[key];
            if (node is null) return null;
        }
        return node;
    }

    // Vacío = null, texto en blanco, u objeto multiidioma sin ningún valor con texto.
    private static bool IsEmpty(JsonNode? node)
    {
        if (node is null) return true;
        if (node is JsonObject obj) return obj.All(kv => IsEmpty(kv.Value));
        return node.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => string.IsNullOrWhiteSpace(node.GetValue<string>()),
            System.Text.Json.JsonValueKind.Null => true,
            _ => false,
        };
    }

    // ── Cascada de borrado de documentos hijos (evita acumular basura en sync) ───
    private static async Task CascadeDeleteDocsAsync(AppDbContext db, string entityType, string id)
    {
        if (entityType == "model")
        {
            var products = (await db.SyncDocuments.Where(d => d.EntityType == "product").ToListAsync())
                .Where(d => ModelOf(d.Payload) == id).ToList();
            var productIds = products.Select(p => p.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            db.SyncDocuments.RemoveRange(products);
            db.SyncDocuments.RemoveRange(await db.SyncDocuments
                .Where(d => d.EntityType == "inventory" && productIds.Contains(d.ExternalId)).ToListAsync());
            db.SyncDocuments.RemoveRange((await db.SyncDocuments.Where(d => d.EntityType == "offer").ToListAsync())
                .Where(d => OfferModelOf(d.Payload) == id));
        }
        else if (entityType == "product")
        {
            db.SyncDocuments.RemoveRange(await db.SyncDocuments
                .Where(d => d.EntityType == "inventory" && d.ExternalId == id).ToListAsync());
            db.SyncDocuments.RemoveRange((await db.SyncDocuments.Where(d => d.EntityType == "offer").ToListAsync())
                .Where(d => OfferProductOf(d.Payload) == id));
        }
    }

    private static string ModelOf(string payload) =>
        CatalogNormalizer.Text(Portal.ClientIdentity.Parse(payload)?["modelId"]);

    private static string OfferModelOf(string payload) =>
        CatalogNormalizer.Text(OfferData(payload)?["modelId"]);

    private static string OfferProductOf(string payload) =>
        CatalogNormalizer.Text(OfferData(payload)?["productId"]);

    private static JsonObject? OfferData(string payload)
    {
        var obj = Portal.ClientIdentity.Parse(payload);
        return (obj?["offerData"] as JsonObject) ?? obj;
    }
}
