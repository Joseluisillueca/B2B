# Manual de configuración — Integración B2B ↔ Business Central

Guía paso a paso para dejar el envío de **pedidos, clientes y direcciones** del portal a
Business Central, y las **descargas** de pedido/albarán/factura. Sigue las partes en orden.

> Resumen del flujo: el portal genera un **GUID** para cada cliente/dirección/pedido; BC lo
> fija como **SystemId** de su tabla → al recomunicar no se duplica. El pedido se transforma
> con **JUST.net** al JSON de BC y se envía por su API OData con **OAuth2**.

---

## PARTE 1 · Business Central (se hace una vez)

### 1.1 Registrar la aplicación en Entra ID (OAuth2 S2S)
En `portal.azure.com` → **Microsoft Entra ID** → **App registrations** → **New registration**:
1. Nombre: `B2B Portal → BC`. Tipo: *Single tenant*. **Register**.
2. Apunta el **Application (client) ID** y el **Directory (tenant) ID**.
3. **Certificates & secrets** → **New client secret** → copia el **Value** (el secreto; solo se ve una vez).
4. **API permissions** → **Add a permission** → **Dynamics 365 Business Central** →
   **Application permissions** → marca **API.ReadWrite.All** → **Add**.
5. **Grant admin consent** (botón). Debe quedar en verde.

### 1.2 Autorizar la app dentro de BC
En BC busca **"Microsoft Entra Applications"** → **New**:
- **Client Id** = el *Application (client) ID* del paso 1.1.
- **Description**: `Portal B2B`. **State** = *Enabled*.
- En **User Permission Sets** añade: **B2BINTEGRATION** (de la extensión del conector) y
  **D365 BUS FULL ACCESS** (o el conjunto equivalente de tu implantación).

### 1.3 Datos que necesitarás para el portal
- **tenant** = Directory (tenant) ID.
- **environment** = nombre del entorno BC (p. ej. `Production` o `Sandbox`; lo ves en el Admin Center o en la URL de BC).
- **companyId** = SystemId (GUID) de la empresa. Obténlo con:
  `GET https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/api/mitoprojects/b2b/v1.0/companies`
  (o con la API estándar `/api/v2.0/companies`). Coge el `id` de tu empresa.

### 1.4 Setup del conector — DESCARGAS (Azure Blob) y Job Queue
En **B2B Integration Setup** del conector:
- Bloque de descargas (Azure Blob): **Storage Account Url**, **Container Name**, **Sas Token**,
  **Sales Orders Folder**, **Sales Shipment Folder**, **Sales Invoices Folder**.
- Comprueba que **Report Selections** tiene un report asignado para cada uso: `S.Order`,
  `S.Shipment`, `S.Invoice` (y devoluciones si aplica).
- **Job Queue**: verifica que están activas (categoría **B2BINT**, cada 5 min): "B2B Order
  Process Job" (procesa pedidos) y "B2B Customer & Address Job" (procesa clientes/direcciones).
- (Opcional) **Save Request Body** = ON para depurar los payloads recibidos.

### 1.5 Prerrequisitos de datos en BC (para que el pedido "cuaje")
- Cada **Country/Region** que uses debe tener **"B2B Template Customers"** (plantilla de cliente).
- **B2B Payment Method**: los códigos de forma de pago que envíe el portal deben existir.
- **VAT Business Posting Group** con **"B2B Tax Id Code"** para mapear el `taxId`.
- Los **artículos y variantes** (Item / Item Variant) deben existir y su **SystemId** haber
  llegado ya al portal por el sync (así `modelId`/`productId` del pedido resuelven en BC).
- El cliente y la dirección del pedido deben existir en BC **antes** de mandar el pedido
  (créalos primero desde el portal, o en la misma tanda — ver Parte 3).

---

## PARTE 2 · Portal B2B (`/manage`)

### 2.1 Conexiones
`/manage` → entra como admin → menú **Integración → Conexiones** → **Business Central**:
- **URL base**:
  `https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/api/mitoprojects/b2b/v1.0/companies({companyId})`
- **URL de token**: `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token`
- **Client ID**: el Application ID (1.1).
- **Client Secret**: el secreto (1.1).
- **Scope**: `https://api.businesscentral.dynamics.com/.default` (por defecto).
- **Guardar conexiones** → debe aparecer el chip **"Configurado"**. (Mientras esté "Sin
  configurar", todo se registra como *simulado* y no sale nada a BC.)

### 2.2 Modo de pedidos
Para que el checkout **envíe el pedido a BC**, el despliegue debe estar en modo portal:
- Variable de entorno **`Portal__OrdersMode=portal`** (en local ya la usamos; en Railway se
  pone en Variables del servicio). En ese modo el pedido se guarda en el portal **y** se
  despacha a BC. (En modo `erp` no se despacha.)

### 2.3 Notificaciones (canales y transformers) — normalmente NO hay que tocar nada
`/manage → Integración → Configuración`. Ya vienen sembrados y corregidos:
- **Orden de compra** → Email + **Business Central** (`salesOrders`).
- **Registro de clientes** → **Business Central** (`customers`).
- **Registro de direcciones** → **Business Central** (`shipToAddresss`).
- Puedes abrir cualquier canal, ver/editar el **transformer** (JUST.net) y usar **"Probar
  transformación"** (JSON base → resultado) o **"Restaurar por defecto"**.

### 2.4 Origen de documentos (descargas) — normalmente NO hay que tocar nada
`/manage → Integración → Origen de documentos`. Pedido/Albarán/Factura ya apuntan a
`salesDocuments?$filter=systemId eq {id}` con el transformer `{"url": ...}`.

---

## PARTE 3 · Prueba end-to-end (el orden importa)

1. **Cliente**: crea un cliente en `/manage → Clientes → Nuevo` (o edítalo). En
   **Integración → Notificaciones realizadas** debe salir `Registro de clientes` en
   **completed**. Comprueba en BC (esperando ~5 min al Job Queue) que el **Customer** existe y
   que su **SystemId = el id del cliente del portal**.
2. **Dirección**: añade una dirección de envío al cliente → `Registro de direcciones`
   *completed* → **Ship-to Address** en BC con el mismo GUID como SystemId.
3. **Pedido**: entra al **portal como usuario de ese cliente**, haz un pedido (TERMINAR
   PEDIDO). En *Notificaciones realizadas* → `Orden de compra` **completed**. Espera ~5 min
   (Job Queue) → aparece el **Sales Order** en BC (con `B2B Id` = orderId del portal).
4. **Descarga**: en el portal del cliente → **Pedidos/Facturas** → botón de descarga → abre el
   **PDF** (URL del blob que devuelve BC).

> Idempotencia: reenviar el mismo pedido/cliente NO duplica (BC rechaza el orderId repetido y
> resuelve clientes/direcciones por SystemId).

---

## PARTE 4 · Catálogo y marca de una instancia (ejemplo real: BLOCCO 5 en Way2Growth)

Lo que sigue es lo que se configuró para que el portal de Way2Growth enseñe la colección
BLOCCO 5 con su identidad. Vale como plantilla para cualquier marca.

### 4.1 URLs del conector (Setup de BC → Sync URLs)
Todas llevan `%1` al final salvo ofertas, empresa y búsqueda de pedidos. Sustituye el dominio
por el de la instancia:

| Entidad | URL |
|---|---|
| Artículos (modelos) | `https://<instancia>/api/catalog/models/%1` |
| Variantes | `https://<instancia>/api/catalog/products/%1` |
| Imágenes | `https://<instancia>/api/catalog/model-images/%1` |
| Atributos | `https://<instancia>/api/catalog/attributes/%1` |
| Familias / categorías | `.../api/catalog/families/%1` · `.../api/catalog/categories/%1` |
| Ofertas (tarifas) | `https://<instancia>/api/catalog/offers` (sin %1; admite lotes) |
| Stock | `https://<instancia>/api/stock/inventory/%1` |
| Ventanas de servicio | `https://<instancia>/api/core/service-windows/%1` |
| Formas de pago / almacenes | `.../api/core/payment-methods/%1` · `.../api/core/warehouses/%1` |
| Clientes / grupos / direcciones | `.../api/clients/%1` · `.../api/clients/groups/%1` |
| Agentes | `https://<instancia>/api/agents/%1` |
| Pedidos (envío y búsqueda) | `.../api/orders/%1` · `.../api/orders/search` (sin %1) |
| Albaranes / facturas | `.../api/documents/delivery-notes/%1` · `.../api/documents/invoices/%1` |
| Empresa | `https://<instancia>/api/core/b2binfo` (sin %1) |

Un `404` en el log de sincronización casi siempre es una URL sin el `%1`.

### 4.2 Atributos: qué marcar en BC (Item Attributes)
- **Sync to B2B** en todos los atributos que el comprador deba ver o filtrar. En Blocco 5:
  `LINE` (BUND RETRO / BUND FANTASY / VAGUE / ELAN), `COLOR NAME`, `COLOR CODE`,
  `STYLE CODE`, `UPPER MATERIAL`, `GENDER` y la talla.
- **B2B Related Products** en el atributo que agrupa "el mismo modelo en otro color". En
  Blocco 5 es `LINE`: cada artículo llega con sus hermanos de línea y la ficha enseña la
  carta de colores. Marca UNO solo.
- **B2B Cross Selling** en un atributo que agrupe artículos distintos que se venden juntos
  (colección, temporada, familia de material). Tiene que ser un atributo cuyo VALOR compartan
  varios artículos: uno que sea distinto en cada artículo (un código, un nombre de color) no
  genera ninguna sugerencia. Si no hay ninguno con sentido, no marques nada.
- Al cambiar cualquiera de estas dos casillas hay que **reenviar los artículos**: las
  relaciones se calculan al enviar cada uno, no se recalculan solas.

### 4.3 Nombre de color en la ficha
El portal saca el color de cada artículo del atributo de color (`COLOR`, `COLOR NAME`,
`COLOUR`…). Si el ERP no manda ese atributo, usa la convención del nombre "MODELO — COLOR"
(raya con espacios). Con el atributo no hace falta renombrar nada en BC.

### 4.4 Cinta del catálogo (Gestión → Cinta del catálogo)
Si la marca tiene una sola familia en BC (Blocco 5 tiene `sneaker`), la cinta se queda sin
navegación. Marca el atributo que estructura la colección (en Blocco 5, `LINE`), oculta la
familia única y ordena las entradas como las presenta la marca: BUND RETRO · BUND FANTASY ·
VAGUE · ELAN. Los nombres de línea, familia y color **no se traducen**: el mismo texto en
todos los idiomas.

### 4.5 Marca (Gestión → Conexiones → Marca)
Nombre, color de acento, logotipo (y su versión clara para fondos oscuros), favicon y los
tokens de estilo. Para Blocco 5: papel blanco, superficie `#f0efed`, tinta `#111111`,
cabecera blanca, radios `0px`, tipografía Archivo con rango de pesos
(`https://fonts.googleapis.com/css2?family=Archivo:wght@400..900&display=swap`), filetes de
sección rojos de `1px`, fondo de paneles `#f0efed` y acento secundario igual al de marca.
Mayúsculas automáticas y espaciado entre letras se dejan vacíos: los nombres de producto
van en caja de título. Todo se aplica en vivo; vaciar un token vuelve al valor por defecto.

### 4.6 Escaparate
- **Solo artículos con foto** (Conexiones → Catálogo): actívalo cuando el ERP ya haya subido
  las imágenes; con él encendido, un artículo sin foto no aparece en ningún sitio.
- El **catálogo en PDF**, el **line-sheet** y la **ficha técnica** salen ya con la paleta y el
  nombre de la marca de la instancia.

## Diagnóstico (si algo falla)
- **Notificaciones realizadas** (`/manage`): estado por canal. `errors` trae el detalle (p. ej.
  `HTTP 401` = token/permA; `HTTP 400` = payload/propiedad; `HTTP 404` = URL base/companyId).
- **401 Unauthorized**: revisa client id/secret, el *admin consent* (1.1.5) y que la app esté
  *Enabled* con el permission set B2BINTEGRATION (1.2).
- **El pedido no aparece en BC tras el POST 2xx**: es asíncrono (Job Queue cada 5 min). Si sigue
  sin salir, mira **ErrorText** en las tablas staging de BC (pages *B2B Sales Orders*
  80129/80133): normalmente falta el cliente, la dirección, el artículo/variante o la plantilla
  de país.
- **Cliente/dirección duplicados**: no debería pasar (SystemId = GUID del portal). Si ves un
  SystemId distinto, revisa que la app usa la API `mitoprojects/b2b/v1.0` (no la estándar).
- **Descarga sin PDF**: revisa Report Selections (1.4) y la config de Azure Blob del Setup.

## Referencias
- Contrato exacto de la API OData de BC: `docs/contrato-api/06-api-odata-bc.md`.
- Estado/arquitectura de la integración: `docs/plan-integracion-bc.md`.
- Conector AL: `C:\BC_Projects\Mito - Conector B2B`.
