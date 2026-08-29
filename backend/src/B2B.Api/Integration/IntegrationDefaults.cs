using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Integration;

// Catálogo de eventos y siembra de canales/transformers/orígenes por defecto, con los
// LITERALES del portal de referencia (paridad 1:1, editables desde /manage).
public static class IntegrationDefaults
{
    public sealed record EventDef(string Key, string Name, string Description, bool Fixed);

    public static readonly EventDef[] Events =
    [
        new("user.created", "Usuario creado", "Alta de usuario con email de validación.", true),
        new("auth.validation-resent", "Reenvío de validación", "Renovación del token de validación de usuario.", true),
        new("auth.remind-password-requested", "Recordatorio de contraseña", "Solicitud de recuperación de contraseña.", true),
        new("order.selection-sent", "Selección de pedido enviada", "Envío de selección de pedido por email.", false),
        new("shoes.purchase_order.updated", "Orden de compra", "Actualización de orden de compra hacia integraciones.", false),
        new("client.registration", "Registro de clientes", "Alta o actualización de registro de cliente.", false),
        new("address.registration", "Registro de direcciones", "Alta o actualización de direcciones de cliente.", false),
        new("agent.registration", "Registro por agente", "Alta o actualización de registros creados por agente.", false),
        new("sat.return.updated", "Devolución SAT", "Actualización de devoluciones SAT.", false),
    ];

    public static EventDef? Event(string key) => Array.Find(Events, e => e.Key == key);

    // ── Transformers por defecto (literales de la referencia) ──
    public const string OrderTransformer = """
    {
      "orderId": "#valueof($.id)",
      "customerId": "#valueof($.clientId)",
      "shippingAddressId": "#valueof($.shippingAddressId)",
      "reference": "#valueof($.referenceOrder)",
      "paymentMethodId": "#valueof($.payMethodId)",
      "incotermId": "#valueof($.incotermId)",
      "saleId": "#valueof($.saleId)",
      "total": "#valueof($.total.value)",
      "totalTax": "#valueof($.totalTax.value)",
      "totalDiscount": "#valueof($.totalDiscount.value)",
      "totalCart": "#valueof($.totalCart.value)",
      "totalTransport": "#valueof($.totalTransport.value)",
      "totalCartDiscount": "#valueof($.totalCartDiscount.value)",
      "items": {
        "#loop($.items)": {
          "lineId": "#currentvalueatpath($.id)",
          "productId": "#currentvalueatpath($.productId)",
          "modelId": "#currentvalueatpath($.modelId)",
          "sku": "#currentvalueatpath($.sku)",
          "qty": "#currentvalueatpath($.quantity)",
          "name": "#currentvalueatpath($.productName.es_ES)",
          "unitPrice": "#currentvalueatpath($.price.value)",
          "originalUnitPrice": "#currentvalueatpath($.priceOriginal.value)",
          "amount": "#currentvalueatpath($.amount.value)",
          "discountAmount": "#currentvalueatpath($.totalDiscounts.value)",
          "stockServiceId": "#currentvalueatpath($.stockServiceId)"
        }
      },
      "stockServices": {
        "#loop($.stockServices)": {
          "stockServiceId": "#currentvalueatpath($.stockServiceId)",
          "from": "#currentvalueatpath($.from)",
          "to": "#currentvalueatpath($.to)",
          "baseFrom": "#currentvalueatpath($.baseFrom)",
          "baseTo": "#currentvalueatpath($.baseTo)"
        }
      }
    }
    """;

    public const string CustomerTransformer = """
    {
      "b2BSystemsId": "#valueof($.id)",
      "name": "#valueof($.name)",
      "eMail": "#valueof($.email)",
      "homePage": "#valueof($.web)",
      "phone": "#valueof($.phone.number)",
      "vatRegistrationNo": "#valueof($.fiscalInfo.fiscalId.document)",
      "name2": "#valueof($.fiscalInfo.fiscalName)",
      "searchName": "#valueof($.fiscalInfo.alias)",
      "address": "#valueof($.fiscalInfo.address.streetAddress)",
      "numeroDireccionFiscal": "#valueof($.fiscalInfo.address.num)",
      "countryRegionCode": "#valueof($.fiscalInfo.address.countryIsoId)",
      "county": "#valueof($.fiscalInfo.address.province)",
      "city": "#valueof($.fiscalInfo.address.city)",
      "postCode": "#valueof($.fiscalInfo.address.zipCode)",
      "shipToAddress": {
        "#loop($.shippingAddresses)": {
          "shippingAddressId": "#currentvalueatpath($.shippingAddressId)",
          "addressShip": "#currentvalueatpath($.streetAddress)",
          "numeroDireccionShip": "#currentvalueatpath($.num)",
          "countryRegionCodeShip": "#currentvalueatpath($.countryIsoId)",
          "countyShip": "#currentvalueatpath($.province)",
          "cityShip": "#currentvalueatpath($.city)",
          "postCodeShip": "#currentvalueatpath($.zipCode)",
          "contactNameShip": "#currentvalueatpath($.alias)"
        }
      }
    }
    """;

    public const string AddressTransformer = """
    {
      "clientId": "#valueof($.clientID)",
      "addressShip": "#valueof($.shippingAddress.streetAddress)",
      "numeroDireccionShip": "#valueof($.shippingAddress.num)",
      "countryRegionCodeShip": "#valueof($.shippingAddress.countryIsoId)",
      "countyShip": "#valueof($.shippingAddress.province)",
      "cityShip": "#valueof($.shippingAddress.city)",
      "postCodeShip": "#valueof($.shippingAddress.zipCode)",
      "contactNameShip": "#valueof($.shippingAddressAlias)",
      "shippingAddressId": "#valueof($.shippingAddressId)"
    }
    """;

    public const string DocUrlTransformer = """{ "url": "#valueof($.value[0].url)" }""";

    // Transformer por defecto según el endpoint (para "restaurar por defecto" en la UI).
    public static string? DefaultTransformer(string? endpoint) => endpoint switch
    {
        "salesOrders" => OrderTransformer,
        "customers" => CustomerTransformer,
        "shipToAddresss" => AddressTransformer,
        _ => null,
    };

    public static async Task SeedAsync(AppDbContext db)
    {
        // Settings singleton
        if (!await db.IntegrationSettings.AnyAsync())
            db.IntegrationSettings.Add(new IntegrationSettings { Id = 1, UpdatedAt = DateTime.UtcNow });

        // Canales por defecto (solo si no hay ninguno)
        if (!await db.NotificationChannels.AnyAsync())
        {
            void Email(string ev, string? to, string? bcc = null, bool fix = false) =>
                db.NotificationChannels.Add(new NotificationChannel
                {
                    Id = Guid.NewGuid(), EventKey = ev, ChannelType = "email", Order = 0,
                    Active = true, Fixed = fix, ToVars = to, BccVars = bcc,
                });
            void Bc(string ev, string endpoint, string transformer, int order = 1) =>
                db.NotificationChannels.Add(new NotificationChannel
                {
                    Id = Guid.NewGuid(), EventKey = ev, ChannelType = "business-central", Order = order,
                    Active = true, Endpoint = endpoint, Transformer = transformer,
                });

            Email("user.created", "{userEmail}", fix: true);
            Email("auth.validation-resent", "{userEmail}", fix: true);
            Email("auth.remind-password-requested", "{userEmail}", fix: true);
            Email("order.selection-sent", "{clientEmail}");
            Email("shoes.purchase_order.updated", "{clientEmail}", "{companyEmail},{saleEmail}");
            Bc("shoes.purchase_order.updated", "salesOrders", OrderTransformer);
            Bc("client.registration", "customers", CustomerTransformer);
            Bc("address.registration", "shipToAddresss", AddressTransformer);
            Email("agent.registration", "{clientEmail}");
            Bc("agent.registration", "customers", CustomerTransformer);
        }

        // AUTO-CORRECCIÓN de transformers por defecto sembrados con un bug conocido, para
        // que el arreglo llegue a BDs ya sembradas (incl. despliegues). Solo toca canales
        // que AÚN tienen el literal viejo → nunca pisa una edición del usuario.
        foreach (var ch in await db.NotificationChannels.Where(c => c.ChannelType == "business-central").ToListAsync())
        {
            if (ch.Transformer is not { } t) continue;
            if (t.Contains("\"salesOrderLines\"")) ch.Transformer = OrderTransformer;         // líneas: salesOrderLines→items
            else if (t.Contains("shipToAddresss")) ch.Transformer = CustomerTransformer;       // embebido: →shipToAddress + shippingAddressId
        }

        // Orígenes de documentos (pedido/albarán/factura)
        if (!await db.DocumentSources.AnyAsync())
        {
            foreach (var (type, _) in new[] { ("order", "Pedido"), ("delivery-note", "Albarán"), ("invoice", "Factura") })
                db.DocumentSources.Add(new DocumentSource
                {
                    DocType = type, SourceType = "business-central", Method = "GET",
                    Endpoint = "salesDocuments?$filter=systemId eq {id}", Transformer = DocUrlTransformer, Active = true,
                });
        }

        await db.SaveChangesAsync();
    }
}
