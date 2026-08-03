# Contrato API — B2B ⇄ Business Central

Especificación extraída del código del conector **MITO - Conector B2B** (AL, Business Central SaaS). Documenta la API exacta que el B2B actual expone y consume, de modo que el nuevo backend pueda sustituirlo sin tocar BC (solo URL base y credenciales en el Setup).

## Índice

| Doc | Contenido | Dirección |
|---|---|---|
| [01 — Autenticación y convenciones](01-autenticacion-y-convenciones.md) | Login JWT, ciclo de token, headers, PUT-upsert, manejo de errores, orquestador de sync, campos de Setup | BC → B2B |
| [02 — Catálogo](02-catalogo.md) | Modelos, imágenes, productos/variantes, atributos, categorías, familias, case packs, multiidioma | BC → B2B |
| [03 — Stock, ventanas y precios](03-stock-ventanas-precios.md) | Stock por almacén, ventanas REPLENISHMENT/SCHEDULED, ofertas/tarifas (PUT/GET/DELETE), formas de pago, grupos de cliente | BC → B2B |
| [04 — Clientes y agentes](04-clientes-agentes.md) | Clientes, usuarios, direcciones de envío, agentes, consulta de pedidos del portal | BC → B2B |
| [05 — Documentos](05-documentos.md) | Albaranes, facturas, abonos, devoluciones, tracking, entrega de PDFs | BC → B2B |
| [06 — API OData de BC](06-api-odata-bc.md) | API pages que BC expone (`mitoprojects/b2b/v1.0`): pedidos, clientes nuevos, direcciones, contactos, emails, PDFs | B2B → BC |

## Hallazgos clave del contrato actual

Cosas que el backend nuevo debe **replicar tal cual** (el conector las envía así):

- Nombres JSON con erratas literales: `configuragleComponennts`, `isModelAttributte`, `atributes`, `clienteExternalReference`, entity set OData `shipToAddresss` (triple s).
- Rutas con duplicados intencionales: `/api/clients/clients/{clientId}/users/admin`.
- GET con body JSON (consulta de ofertas y de pedidos del portal).
- Multiidioma real: solo `es_ES/en_EN/fr_FR/it_IT` (nota: `en_EN`, no `en_US`); producto-variante solo `es_ES`.
- Asimetrías de mayúsculas/minúsculas entre URL, body e ids (documentadas en cada doc).
- Ids deterministas generados en BC (`B2B Guid Combinations`) y DELETE de ofertas por reconciliación GET-comparar-DELETE.

Cosas **rotas o sin terminar en el conector** que conviene corregir en el corte (requerirán tocar el conector):

- Stock de case packs enviado con `Random(100)` (placeholder).
- `tokenExpiresIn` parseado como fecha absoluta dependiente de configuración regional.
- Sin refresh token (re-login continuo) y sin reintentos ante errores no-2xx.
- `taxId` hardcodeado a `iva-normal`; mercado hardcodeado a `es`.
- Errores del POST de pedidos B2B→BC no expuestos por la API (fire-and-forget; solo quedan en el staging de BC).
