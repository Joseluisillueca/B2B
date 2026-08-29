# Plan — Integración saliente B2B → Business Central + Notificaciones/Canales

/goal (la parte más importante y compleja): que el portal **envíe pedidos a Business
Central**, con **clientes/direcciones** que viajan con un **GUID único** que BC fija como
**SystemId** (idempotencia, sin duplicidad al recomunicar); **descargas** de pedidos/
albaranes/facturas (report de BC → PDF en Azure Blob); y replicar la **configuración de
notificaciones/canales/transformers/conexiones** y el destinatario de los emails, como en
el portal de referencia. Motor de transformación JSON: **JUST.net** (mismo NuGet).

## ESPECIFICACIÓN (auditada: contrato `docs/contrato-api/06`, conector AL, y recon del portal ref.)

### Eventos y canales (Notificaciones → Configuración)
| Evento (interno) | Canales | Endpoint BC | Notas |
|---|---|---|---|
| Usuario creado (`...user.created`) | Email (fijo) | — | To: {userEmail} |
| Reenvío validación | Email (fijo) | — | To: {userEmail} |
| Recordatorio contraseña (`auth.remind-password-requested`) | Email (fijo) | — | To: {userEmail} |
| Selección de pedido enviada | Email | — | To: {clientEmail} |
| **Orden de compra** (`shoes.purchase_order.updated`) | Email + **Business Central** | `salesOrders` | To:{clientEmail} BCC:{companyEmail},{saleEmail} |
| **Registro de clientes** | Business Central | `customers` | |
| **Registro de direcciones** | Business Central | `shipToAddresss` | |
| **Registro por agente** | Email + Business Central | `customers` | createPreClient |
| Devolución SAT | (sin canales) | | |

- **Canal Email** = destinatarios (To/CC/BCC) con variables `{companyEmail}`,`{saleEmail}`,
  `{clientEmail}`,`{userEmail}` (o email literal). NO hay asunto/plantilla en el modal (las
  plantillas de activación/reset ya existen en el portal como código).
- **Canal Business Central** = { Endpoint, JSON base (vista), Transformer JUST.net, Probar }.
- Transformers literales capturados (ref.): `salesOrders`, `customers`, `shipToAddresss`,
  `customers`(agente). Usan `#valueof #loop #currentvalueatpath #ifcondition #existsandnotempty
  #xconcat #customfunction(B2B.Shared…IfNull)`. Se guardan como plantillas por defecto.

### Conexiones (Conectividad → Conexiones)
- **Business Central**: URL base · URL de token · Client ID · Client Secret (OAuth2 client
  credentials contra Entra ID; scope `…dynamics.com/.default`). + tenant/environment/companyId.
- **Email**: producción (SMTP/remitente/seguridad/restricciones) + testing (Mailtrap).
- **APIREST**: URL base + headers globales (genérico, opcional).

### Origen de documentos (descargas)
- 3 filas: **Pedido / Albarán / Factura**, todas: Tipo=Business, Método=GET,
  Endpoint=`salesDocuments?$filter=systemId eq {id}`, Transformer=`{"url":"#valueof($.value[0].url)"}`.
- Soporta `{id}` (id interno = SystemId BC del doc) y `{externalReference}`.

### Contrato BC (de `06-api-odata-bc.md`, verificado con el conector)
- Base: `https://api.businesscentral.dynamics.com/v2.0/{tenant}/{env}/api/mitoprojects/b2b/v1.0/companies({companyId})/{entitySet}`
- POST deep-insert `salesOrders` (cabecera+`items`+`stockServices`); idempotente por `orderId`.
- POST `customers` / `shipToAddresss` (deep insert); BC fuerza SystemId := GUID del portal.
- GET `salesDocuments?$filter=systemId eq {guid}` → `{value:[{url}]}` (PDF en Azure Blob).
- Auth: OAuth2 client credentials (token URL Entra ID). App registration con permission set
  B2BINTEGRATION en el tenant BC.
- GUID→SystemId: customerId/shippingAddressId/modelId(Item)/productId(Item Variant)/saleId
  (Salesperson) = SystemId en BC. orderId/lineId → `"B2B Id"`.

## Huecos en B2BNew (auditados) a cubrir
- Pedido nativo: faltan `incotermId, isDropShipping, salesPayMethodId, priceOriginal` y
  `totalDiscounts` por línea, `stockServiceId` por línea, `stockServices[]` con fechas,
  `totalCart/Transport/CartDiscount`, y **modelId/productId por línea** (SystemId de Item/
  Variant — hoy el CartLine los tiene pero no se vuelcan al doc). Completar `NativeOrder`.
- No hay cliente HTTP a BC, ni OAuth2, ni config BC. No hay motor de canales/transformers.
- Descargas (`salesDocuments`) sin implementar.

## Fases
- **F0 · Entender** — HECHO (3 auditorías + contrato leído).
- **F1 · Datos**: completar el pedido nativo con todos los campos del JSON base (y modelId/
  productId por línea); asegurar GUID estable en pedido; cliente/dirección ya con GUID.
- **F2 · JUST.net**: añadir NuGet `JUST.net`; servicio de transformación + custom function
  `IfNull`; endpoint "Probar transformación" (JSON base + expresión → resultado).
- **F3 · Conexiones**: almacén de config (BC OAuth, Email prod/testing, APIREST) editable.
  Cliente HTTP BC con token OAuth2 client-credentials cacheado.
- **F4 · Motor de canales**: entidades Evento/Canal/Transformer (con plantillas por defecto
  = las de la ref.); despacho: al confirmar pedido / crear cliente / crear dirección →
  por cada canal → transform → POST a BC (o email); idempotente por GUID; **log de
  "Notificaciones realizadas"** (estado COMPLETED/ERRORS por canal).
- **F5 · Descargas**: "Origen de documentos" configurable; endpoint del portal que, para un
  pedido/albarán/factura, llama a BC `salesDocuments` (GET+filtro por systemId), aplica el
  transformer `{url}` y devuelve/redirige la URL del PDF.
- **F6 · UI en /manage**: Notificaciones (Configuración + Realizadas), Conexiones, Origen de
  documentos — replicando el diseño (con el lenguaje propio del back-office ya hecho).
- **F7 · Verificación**: loops de agentes — probar cada transform (JSON base→resultado vs. el
  esperado por BC), la idempotencia GUID→SystemId, el flujo de descargas, y (si el usuario
  aporta credenciales de un entorno BC/sandbox) el envío real end-to-end.

## Restricción conocida
La conexión BC del portal de referencia está VACÍA (sin credenciales) → el envío real a BC
necesita que el usuario aporte tenant/environment/companyId/clientId/secret (App registration
en su tenant). Sin ellas, se construye y prueba todo el pipeline (transform + dispatch + log)
pero el POST real queda inerte/simulado hasta configurarlo.

## Estado
- F0–F6 IMPLEMENTADAS. 438 tests verdes (incluye parity de transformers y pipeline de despacho).
- F7 (auditoría): 1ª vuelta → 1 crítico (items vs salesOrderLines), 2 altos (shipToAddress + shippingAddressId
  embebido), 2 medios, nits → **corregidos**. 2ª vuelta en verificación.
- Motor: entidades + migración `AddBcIntegration`; `Integration/{JsonTransformService,BcClient,
  IntegrationDefaults,SourceJson,NotificationDispatcher}`; `Admin/IntegrationEndpoints`; descargas en
  `Portal/DocumentDownloadEndpoints`; hooks en checkout y alta cliente/dirección; UI `/manage`
  (Configuración, Conexiones, Origen de documentos, Realizadas). NuGet `JUST.net`.
- INERTE hasta configurar Conexiones (BC OAuth); los envíos se registran "simulated".
- Correcciones vs. la referencia: líneas de pedido = `items` (no `salesOrderLines`); direcciones
  embebidas en cliente = `shipToAddress` con `shippingAddressId` (paridad con contrato 06, la
  referencia tenía estos errores → sus notificaciones BC salían en ERRORS).

