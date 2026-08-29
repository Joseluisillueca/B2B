# Plan — Portal B2B autónomo (clientes sin ERP)

> **ESTADO (28-ago-2026): IMPLEMENTADO y auditado.** Fases 0–4 completas. Dos vueltas de
> auditoría adversaria (8/8 hallazgos corregidos, 0 críticos/altos/medios). Batería
> 434/434 verde (13 tests nuevos en `PortalAutonomoTests.cs`).
>
> **Activar el modo autónomo en un despliegue:** variable `Portal__OrdersMode=portal`
> (por defecto `erp` = comportamiento clásico, no se toca lejan). En modo `portal` el
> checkout guarda el pedido nativo (re-tarificado en servidor con `CatalogPricing`),
> visible en `/orders` y gestionable desde el CMS (estado + eliminar). El CMS da de alta
> a mano las 14 entidades (`/api/admin/entities/{tipo}/{id}`) y los accesos
> (`/api/admin/users`: admin, usuario de cliente con contraseña/activación, comercial).


Objetivo: que un cliente **sin Business Central** pueda gestionar todo desde el CMS
(crear catálogo, clientes y usuarios a mano) y que **los pedidos se guarden y se vean
en el propio portal**, sin depender de que el ERP los procese.

## Idea rectora
El portal ya es **agnóstico del origen**: `conector → documento JSON → normalizador →
tablas de dominio → portal`. El conector es solo *un* productor de documentos.
**El CMS pasa a ser un segundo productor del MISMO documento** (mismo `entityType`,
mismo JSON). Precedente exacto ya en el repo: `Admin/ModelImageEndpoints.cs`.

Se reutiliza toda la capa de lectura existente (`CatalogNormalizer`,
`DocumentProjections`, `ClientIdentity`). La única pieza realmente nueva es la
**gestión de pedidos nativos** (hoy el pedido terminado queda huérfano en `Cart`
con `Status="pending-bc"` esperando a BC).

## Base técnica (transversal a todas las fases)
- `SyncEndpoints.IngestDocumentAsync(...)`: tubería de ingesta reutilizable
  (crudo → `CatalogNormalizer.Normalize` → `ClientIdentity.ApplyAsync`). La usan el
  conector y el CMS.
- `Admin/EntityCrudEndpoints.cs`: `PUT/DELETE /api/admin/entities/{entityType}/{id}`
  (policy `cms-admin`). El CMS decide el `id` (GUID para model/product/offer;
  slug/código para family/category/attribute/…), como hace el conector con el SystemId.
- `CatalogNormalizer.Remove(...)`: al borrar, limpia también la tabla de dominio.
- Formularios en `admin.html`: por relevancia (campos que usa el portal) + autocompletado
  del "ruido de ERP" + modo avanzado (JSON) opcional. Patrón de `renderContent`/`renderLookbook`.

## Entidades: qué se rellena vs. qué se autocompleta
Solo estas 5 tienen tabla de dominio y requieren `Normalize`: **model, product,
inventory, service-window, offer**. El resto vive como documento crudo.

| Entidad | entityType | id | Campos a mano | Autocompletado |
|---|---|---|---|---|
| Modelo | `model` | GUID | name, description, active, externalReference, familyId, attributes, productSegments | brandId, cross/upSelling, idiomas repetidos |
| Variante | `product` | GUID | modelId, name(es_ES), active, sku, ean, attributes.tallas, taxId | description, spareParts, stockAlerts |
| Familia | `family` | slug | code, name | atributes[] |
| Categoría | `category` | `catalog.x.y` | name, active, search.familyIds | models[], search.* resto, slug |
| Atributo | `attribute` | code (`tallas`) | code, name, type, visibleFormat, visibleWeb, values[] | isModelAttributte, color/image |
| Oferta/precio | `offer` | GUID | modelId, productId?, clientId?/clientGroupId?, priceType, basePrice, stock(minQty), discounts[0].percent, from/toDate, orderType?, priority | pricesPerUnit, marketId, priceOriginal |
| Stock | `inventory` | productId | stock, stockServiceId, orderType, entryDate | type |
| Ventana | `service-window` | slug | id, name, orderType, from/to/limit | showUntil, incoterms |
| Almacén | `warehouse` | code | code, description, active, address.* | transportIds, markets |
| Forma de pago | `payment-method` | slug | name, order, allowCredit, externalReference, requiredForConfirm | description |
| Grupo cliente | `client-group` | slug | externalReference, name, paymentMethods[] | — |
| Cliente | `client` | GUID | name, externalReference, email, canShop, groupIds, payMethods, address, fiscalInfo, taxId | brandAccess, markets, geo |
| Usuario cliente | `client-user` | clientId | email, name, culture | (contraseña vía activación) |
| Agente | `agent` | GUID | email, name, culture, clientIds | markets, groupIds |
| Dir. envío | `shipping-address` | GUID (ParentId=clientId) | alias, address.* | — |

FKs para los selectores: product.modelId→model · offer.modelId/productId/clientId/
clientGroupId/orderType · inventory: productId + stockServiceId(window) · client.groupIds→
client-group · client-user/shipping-address/order cuelgan de client por clientId.

## Fases
- **Fase 0 (base)**: `IngestDocumentAsync` + `EntityCrudEndpoints` + `Normalizer.Remove` + registro.
- **Fase 1 (catálogo)**: formularios CMS de model, product, family, category, attribute,
  service-window, offer, inventory, warehouse, payment-method. Con selectores por FK.
- **Fase 2 (clientes)**: client, client-group, client-user (con activación/set de contraseña),
  agent, shipping-address. + **gestión de usuarios admin del CMS** (hoy no hay alta de admin).
- **Fase 3 (pedidos nativos)**: promover el pedido a entidad propia (o extender `Cart`),
  guardar snapshot completo (envío, forma de pago, totales, IVA), listado nativo para el
  cliente, y **gestión de estado en el CMS** (confirmar/servir/facturar/cancelar) + nº de pedido.
- **Fase 4 (modo + pulido)**: ajuste por tenant "pedidos en portal vs. entregar a ERP",
  tests (unit + e2e Playwright), i18n, revisión de diseño.

## Pedidos: el circuito a cerrar (Fase 3)
Hoy: `POST /api/portal/orders` (CartEndpoints) escribe `Cart` pending-bc → nadie lo lista.
`GET /api/portal/orders` (DocumentEndpoints) lee sync_documents `order` del ERP.
Cierre: listar los pedidos nativos + proyectarlos a la forma `OrderRow`/`DocumentLine`
(reutiliza la UI de `orders.js`) + estados gestionables desde el CMS.
