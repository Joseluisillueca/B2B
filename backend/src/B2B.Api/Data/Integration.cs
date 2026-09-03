namespace B2B.Api.Data;

// Configuración de conexiones (singleton, Id=1). Editable desde /manage → Conexiones.
// Business Central (OAuth2 client credentials contra Entra ID) + API REST genérica.
// El email sigue por EmailOptions/env (ya existente).
public class IntegrationSettings
{
    public int Id { get; set; } = 1;
    // Business Central
    public string? BcBaseUrl { get; set; }        // https://api.businesscentral.dynamics.com/v2.0/{tenant}/{env}/api/mitoprojects/b2b/v1.0/companies({companyId})
    public string? BcTokenUrl { get; set; }        // https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
    public string? BcClientId { get; set; }
    public string? BcClientSecret { get; set; }
    public string? BcScope { get; set; } = "https://api.businesscentral.dynamics.com/.default";
    // API REST genérica (headers globales como JSON {clave:valor})
    public string? ApiRestBaseUrl { get; set; }
    public string ApiRestHeadersJson { get; set; } = "{}";

    // Diseño global de los emails: envoltorio HTML de marca compartido por TODOS los
    // correos. Debe contener {{content}} (donde entra el cuerpo del email) y puede usar
    // {{subject}} y {{year}}. Editable desde /manage → Conexiones. Null = layout por defecto.
    public string? EmailLayoutHtml { get; set; }

    // Modo de pedidos: "portal" = el portal es dueño del pedido y lo COMUNICA a Business
    // Central (push al terminar el pedido); "erp" = los pedidos los gobierna BC (el portal no
    // despacha). null = usar `Portal:OrdersMode` de configuración (compatibilidad hacia atrás).
    // Editable desde /manage → Conexiones.
    public string? OrdersMode { get; set; }

    // ── Marca del portal (multi-cliente): cada despliegue lleva su nombre, color y logo ──
    // null = los valores por defecto del producto (MITO PROJECTS). Editable desde /manage →
    // Conexiones → Marca. El color es el acento principal (#rrggbb) del portal, back-office,
    // emails y PDFs. El logo (URL de /media, opcional) sustituye al nombre en las cabeceras.
    public string? BrandName { get; set; }
    public string? BrandColor { get; set; }
    public string? BrandLogoUrl { get; set; }

    // Config de la cinta del catálogo (JSON crudo, editable desde /manage → Catálogo →
    // Cinta): {"attributes":[slugs de atributo que la alimentan],"entries":[overrides por
    // entrada: key, hidden, order, titles por idioma]}. null = cinta autogenerada (solo
    // familias). La computa por actor GET /api/shop/ribbon (VisibilityEndpoints).
    public string? CatalogRibbonJson { get; set; }

    // Valores efectivos (no mapeados por EF): con respaldo a la marca por defecto.
    public string BrandNameOrDefault => string.IsNullOrWhiteSpace(BrandName) ? "MITO PROJECTS" : BrandName!.Trim();
    public string BrandColorOrDefault => string.IsNullOrWhiteSpace(BrandColor) ? "#ec3013" : BrandColor!.Trim();

    public DateTime UpdatedAt { get; set; }

    public bool BcConfigured =>
        !string.IsNullOrWhiteSpace(BcBaseUrl) && !string.IsNullOrWhiteSpace(BcTokenUrl)
        && !string.IsNullOrWhiteSpace(BcClientId) && !string.IsNullOrWhiteSpace(BcClientSecret);
}

// Canal de un evento de notificación. Email (destinatarios con variables) o Business
// Central (endpoint + transformer JUST.net). Editable desde /manage → Notificaciones.
public class NotificationChannel
{
    public Guid Id { get; set; }
    public string EventKey { get; set; } = "";     // p.ej. shoes.purchase_order.updated
    public string ChannelType { get; set; } = "";  // "email" | "business-central"
    public int Order { get; set; }
    public bool Active { get; set; } = true;
    public bool Fixed { get; set; }                // canal "Fijo" (no editable/eliminable)

    // Business Central
    public string? Endpoint { get; set; }          // salesOrders | customers | shipToAddresss
    public string? Transformer { get; set; }       // expresión JUST.net

    // Email (destinatarios; variables {companyEmail}{saleEmail}{clientEmail}{userEmail} o literal)
    public string? ToVars { get; set; }
    public string? CcVars { get; set; }
    public string? BccVars { get; set; }

    // Email — contenido editable. Asunto y cuerpo HTML (solo el CUERPO; la cabecera/pie de
    // marca los pone el layout global). Admiten variables {{clientName}} {{orderRef}} …
    public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
}

// Historial de envíos (Notificaciones → Realizadas).
public class NotificationLog
{
    public Guid Id { get; set; }
    public string EventKey { get; set; } = "";
    public string EntityType { get; set; } = "";   // PurchaseOrder | Customer | ShipToAddress
    public string EntityId { get; set; } = "";
    public string ChannelType { get; set; } = "";  // email | business-central
    public string Status { get; set; } = "";       // completed | errors | simulated | skipped
    public string? Detail { get; set; }            // error o resumen
    public string? PayloadJson { get; set; }       // JSON transformado enviado (para depurar)
    // Para "Reprocesar": guardamos el JSON de ENTRADA (source) y el endpoint BC, de modo que
    // al reintentar se re-aplica el transformer ACTUAL (recogiendo cualquier arreglo) y se
    // reenvía. Solo se rellenan en canales business-central.
    public string? InputJson { get; set; }
    public string? Endpoint { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Origen de documentos (descargas): pedido/albarán/factura → endpoint BC + transformer.
public class DocumentSource
{
    public string DocType { get; set; } = "";      // order | delivery-note | invoice
    public string SourceType { get; set; } = "business-central";
    public string Method { get; set; } = "GET";
    public string Endpoint { get; set; } = "";     // salesDocuments?$filter=systemId eq {id}
    public string Transformer { get; set; } = "";  // { "url": "#valueof($.value[0].url)" }
    public bool Active { get; set; } = true;
}
