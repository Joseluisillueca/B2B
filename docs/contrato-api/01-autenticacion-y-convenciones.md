# Contrato API B2B — 01. Autenticación, infraestructura HTTP y convenciones generales

> Fuente: conector AL "MITO - Conector B2B" (`c:\BC_Projects\Mito - Conector B2B`).
> Objetivo: que el nuevo backend .NET 8 implemente EXACTAMENTE la misma API que el B2B actual, sin cambios en el conector.

---

## 1. Login y ciclo de vida del token

### 1.1 Endpoint de login

| | |
|---|---|
| **Método** | `POST` |
| **Ruta** | La configurada en `B2B Integration Setup."Login URL"`. Valor por defecto que inicializa la página de Setup: `https://back-mitoprojects.mygo2b.app/api/auth/login` |
| **Headers** | `Content-Type: application/json` (sin Authorization) |
| **Origen** | `src\codeunits\Cod80101.B2BAPIHandler.al` → `CallLoginAPI` · default de URL en `src\pages\Pag80100.B2BIntegrationSetup.al` (`OnOpenPage`) |

**Importante:** la ruta de login **no lleva versión de API** (`/api/auth/login`, no `/api/v1/...`). La URL es un campo de configuración libre; el conector la usa tal cual, sin concatenar nada.

### 1.2 Request (JSON exacto que envía BC)

Construido en `CallLoginAPI` (`Cod80101.B2BAPIHandler.al`, líneas 18-22):

```json
{
  "email": "usuario-integracion@ejemplo.com",
  "password": "********",
  "type": "global",
  "longDuration": true
}
```

- `email` = `Setup."Integration User"` (el tooltip de la página lo define como "User email for B2B authentication").
- `password` = `Setup."Integration Password"`.
- `type` y `longDuration` son literales fijos: siempre `"global"` y `true`.

### 1.3 Response esperada

Parseada en `GetTokenFromResponse` (`src\codeunits\Cod80100.B2BIntegrationMgt.al`, líneas 231-251):

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ...firma",
  "tokenExpiresIn": "02/08/2026 18:30:00"
}
```

- `token` (**obligatorio**): JWT en texto plano. Si falta, el conector falla con "Could not extract token from API response.". Longitud máxima almacenable en BC: **2048 caracteres** (`Tab80100`, campo `Active Token`).
- `tokenExpiresIn` (opcional): **fecha/hora absoluta de expiración**, NO segundos de duración pese al nombre. BC lo lee como texto y hace `Evaluate(ExpiresAt, ExpiresAtText)` sobre un `DateTime`.
  - ⚠️ **Hallazgo**: `Evaluate` sin formato 9 depende de la configuración regional de la sesión de BC. El backend actual devuelve algo que BC-es logra evaluar como DateTime. El nuevo backend debe replicar el mismo formato de cadena que el actual (verificar en el backend viejo; un ISO-8601 `2026-08-02T18:30:00Z` puede no ser parseable por `Evaluate` plano en todas las regiones).
- Cualquier HTTP no-2xx o JSON no parseable → login fallido, se registra el error (ver §2.5).

### 1.4 Dónde se guarda el token en BC

Tabla `B2B Integration Setup` (`src\tables\Tab80100.B2BIntegrationSetup.al`), registro único (singleton):

| Campo | Tipo | Contenido |
|---|---|---|
| `Active Token` | Text[2048] | JWT actual |
| `Token Expiration DateTime` | DateTime | Valor de `tokenExpiresIn` |
| `Last Token Refresh` | DateTime | `CurrentDateTime` en el momento del refresh |

Escrito por `RefreshToken()` en `Cod80100.B2BIntegrationMgt.al` (líneas 27-33).

### 1.5 Renovación del token

**No hay refresh token.** La "renovación" es siempre un re-login completo (`RefreshToken()` → `CallLoginAPI`). Tres mecanismos:

1. **Bajo demanda (lazy)** — `GetToken` en `src\codeunits\b2bManager\Cod80143.B2BBaseApiManager.al`: antes de cada request, si `Active Token = ''` **o** `Token Expiration DateTime < CurrentDateTime`, llama a `RefreshToken()`. Si la integración está deshabilitada (`Enable Integration = false`) devuelve error "B2B Integration is not enabled." sin llamar a la red.
2. **Job Queue programado** — `src\codeunits\Cod80102.B2BTokenRefreshJob.al`: job recurrente cada `Setup."Token Refresh Interval"` minutos (default **60**, mínimo 1), todos los días 00:00-23:59, máx. 3 intentos con 30 s de espera. Ejecuta `CheckAndRefreshTokenIfNeeded()` (`Cod80100`), que renueva si el token está vacío o caduca en **menos de 10 minutos** (margen hardcodeado).
3. **Manual** — acciones "Test Connection" y "Refresh Token Now" en la página de Setup (`Pag80100`), ambas llaman a `RefreshToken()`.

**Implicación para el backend:** el token debe ser válido durante al menos el intervalo de refresh (idealmente > 70 min con la config por defecto). Con `longDuration: true` el backend actual emite tokens largos.

---

## 2. Convenciones HTTP generales

### 2.1 Construcción de URLs

No hay un único patrón `BaseUrl + ruta`. El Setup guarda **una URL por entidad** (campos `... URL` de `Tab80100`) y coexisten dos estilos:

1. **URL completa por entidad con placeholder** (la mayoría). El manager PUT (`src\codeunits\b2bManager\Cod80111.B2BApiManager.al`, `GetEndpointURL`, líneas 181-200) lee el campo de Setup indicado por `Entity.ConfigEndpointFieldNo()` y sustituye el id:
   - Si la URL contiene `{{$guid}}` → se reemplaza literalmente por el id.
   - Si no → `StrSubstNo(url, id)`, es decir, la URL debe contener `%1`.
   - El id es, por prioridad (`B2BEndPointUrl`, líneas 161-178): `Entity.ModelId()` si `HasModelId()`; si no, la URL literal `Entity.EndPointUrl()` (sin sustitución); si no, el **SystemId de BC sin llaves** (`StringifyGuid` = `DelChr(Format(gid),'<>','{}')` → GUID en **MAYÚSCULAS**, formato `12345678-ABCD-...`).
   - Ejemplo configurado: `https://back-mitoprojects.mygo2b.app/api/catalog/models/{{$guid}}`.
2. **`Base Url` + ruta relativa** (patrón nuevo, usado por GET/DELETE de ofertas): `Setup."Base Url" + Setup."Get Offers Url"` y `Setup."Base Url" + Setup."Delete Offer Url"` (esta última con `%1` para el id). Ver `src\codeunits\adapters\Cod80134.B2BOfferAdapterV2.al`, `GetUrl`/`DeleteUrl` (líneas 92-112).

Otros patrones de URL compuestos en `Cod80101.B2BAPIHandler.al` (rutas legadas aún activas):
- Usuario admin de cliente: `Sync Customers URL` con `{{$guid}}` → `clients/{clientGuid}` + `/users/admin` (`CallCreateB2BUserAPI`, líneas 620-695). Ruta resultante: `.../clients/{clientId}/users/admin`.
- Direcciones de envío: `Sync Customers URL` con `{{$guid}}` → `clients/{clientGuid}` + `/shipping-addresses/{addressGuid}` (`CallSyncShippingAddressAPI`, líneas 923-1003).
- Albaranes/facturas legacy: `BaseUrl + '/' + guidSinLlaves` (`CallSyncDeliveryNoteAPI`, `CallSyncInvoiceAPI`).
- Formas de pago legacy: `BaseUrl + '/' + code` (`CallSyncPaymentMethodAPI`).
- Algunas rutas usan placeholder `{id}` (service windows, warehouses: `CallSyncServiceWindowAPI` línea 501, `CallSyncWarehouseAPI` línea 863) y atributos usan `{{$id}}` (línea 432).

### 2.2 Headers en cada request

En **todos** los requests (login incluido):

```
Content-Type: application/json
```

En todos menos el login:

```
Authorization: Bearer {token}
```

No se envía ningún otro header (ni Accept, ni API-Key, ni tenant). Fuente: todos los managers (`Cod80111`, `Cod80147`, `Cod80162`, `Cod80142`) y `Cod80101`.

- En DELETE **no** se envía `Content-Type` ni body (`Cod80142.B2BDeleteApiManager.al`, líneas 76-85).
- ⚠️ **Hallazgo**: los **GET llevan body JSON** (`Cod80147.B2BGetApiManager.al`, líneas 53-73: se asigna `RequestMessage.Content` con `Entity.GetRequestBody()` sobre un método GET). Ejemplo real: GET de ofertas envía `{"modelId": "GUID-DEL-MODELO"}` (`Cod80134.B2BOfferAdapterV2.al`, `GetRequestBody`, líneas 171-180). El nuevo backend (.NET 8 / ASP.NET Core) debe aceptar body en GET para estos endpoints.

### 2.3 Métodos HTTP usados

| Método | Uso | Codeunit |
|---|---|---|
| `POST` | Login | `Cod80101` `CallLoginAPI` |
| `PUT` | **Upsert de todas las entidades** (sync). Semántica: idempotente, crea o actualiza por id de la URL | `Cod80111.B2BApiManager` (`Put`) y todos los `CallSync*API` de `Cod80101` |
| `GET` | Leer estado remoto (p.ej. ofertas de un modelo) — con body JSON | `Cod80147.B2BGetApiManager` |
| `POST` | Búsquedas (p.ej. `Search Orders URL`, ej. `.../api/orders/orders/search`) y posts genéricos | `Cod80162.B2BPostApiManager` |
| `DELETE` | Borrado individual por id en URL, sin body | `Cod80142.B2BDeleteApiManager` |

### 2.4 Formato de request/response y errores

- Body siempre JSON (objeto o **array** — el manager PUT usa `JsonToken` explícitamente "para soportar arrays", `Cod80111` línea 98).
- Response de éxito: cualquier **2xx** (`IsSuccessStatusCode`). El body puede ser vacío, objeto o array; si es parseable como objeto se guarda como `ResponseJson`. Si el body no es JSON válido → el conector lo trata como **error** ("Failed to parse JSON response"), aunque el HTTP fuera 2xx. **El backend nunca debe devolver 2xx con body no-JSON** (puede devolver body vacío).
- Response de error: cualquier no-2xx. El conector no interpreta ningún esquema de error: concatena `HTTP {status}: {bodyCompleto}` en el mensaje de log. No distingue 401/404/500 — **no hay reintento automático ante 401** (el token solo se renueva por expiración local, no por rechazo del servidor). Por tanto el `tokenExpiresIn` devuelto debe ser fiable/conservador.
- **Timeout**: 10 s (`Client.Timeout(10000)`) en el PUT genérico (`Cod80111` línea 111) y en la mayoría de `CallSync*` de `Cod80101`. GET/POST/DELETE de los managers nuevos usan el timeout por defecto de BC.
- **Reintentos**: ninguno a nivel HTTP. Un fallo se registra y la entidad queda en error; se reenviará en la siguiente pasada del report/job correspondiente (los Job Queue reintentan la ejecución completa: token job = 3 intentos/30 s).

### 2.5 Registro de errores y auditoría

Dos tablas:

1. **`B2B Error Log`** (`src\tables\Tab80108.B2BErrorLog.al`): `Entry No.` (autoincrement), `Url` (Text[150]), `Error Date`, `User Id`, `Error Message` (Text[1024]). Lo escriben `LogSyncError` de `Cod80111`/`Cod80143` en: integración deshabilitada, URL vacía, fallo de token, fallo de envío, HTTP no-2xx, JSON inválido.
2. **`B2B Sync Status`** (`src\tables\Tab80109.B2BSyncStatus.al`): una fila por entidad sincronizada, ligada a un `B2B Sync Log`. Campos clave: `SyncLog`, `Sync Type` (enum `Sync|Delete|Get`, `Enum80112`), `Sync Model` (nombre lógico, p.ej. "Offers"), `Table Id`, `Table SystemId`, `Table PKs`/`PK 1..3` (resueltos por RecordRef en OnInsert), `Last Sync Date`, `Sync Status` (enum `""|Success|Error`, `Enum80103`), `Message` (Text[1024], recibe el `HTTP {status}: {body}`), `Request Body` (Blob UTF-8).
   - El PUT guarda el **request body** según `Setup."Save Request Body"` (enum `Never|On Error|On Ok|Always`, `Enum80108`) — `Cod80111` líneas 61-75.
   - GET y POST guardan el **response body** siempre (`Cod80147` línea 113, `Cod80162` línea 114). ⚠️ Bug conocido: `PostEntity` marca `Sync Type := Get` (`Cod80162` líneas 60 y 150).
   - DELETE va acumulando línea a línea `url: true/false` en el blob (`AppendLineToRequestBody`).

Además el handler legacy (`Cod80101.LogError`) escribe `Last Sync Status`/`Last Sync DateTime` en el propio Setup.

---

## 3. Patrón de sincronización (orquestador)

### 3.1 Interface "Interface B2B Sync Entity"

`src\interface\Iface.B2BSyncEntity.al`. Todo adapter (uno por entidad, en `src\codeunits\adapters\`) implementa:

| Método | Papel |
|---|---|
| `MustSyncToB2B()` | Filtro: si false, no se envía ("Entity is not marked for B2B sync.") |
| `BuildModelJson(): Text` | Body del PUT (objeto o array) |
| `ModelSystemId()`, `ModelTableNo()`, `ModelName()` | Metadatos para el Sync Status |
| `ConfigEndpointFieldNo()` | Nº de campo de `Tab80100` donde vive la URL del PUT |
| `ModelId()` / `HasModelId()` / `EndPointUrl()` | Alternativas de resolución del id/URL (ver §2.1) |
| `GetUrl()` / `GetRequestBody()` | Endpoint y body del GET/POST |
| `DeleteUrl(id)` / `ElementsToDelete(...)` / `ConfirmDelete(id)` | Ciclo de borrado |

### 3.2 Flujo del orquestador

`src\codeunits\b2bManager\Cod80113.B2BApiOrchestrator.al`:

1. `InitSyncLog()` crea una cabecera en `B2B Sync Log` (`Tab80113`: Entry No., User ID, Starting/Ending DateTime, y FlowFields Total Elements/Errors contra Sync Status).
2. `SyncXxx(record)` por entidad: crea el adapter, lo castea al interface y llama a `apiManager.SyncEntity(...)` (PUT). Cada llamada inserta su fila de `B2B Sync Status`.
3. `EndSyncLog()` cierra con `Ending DateTime`.

Métodos disponibles (uno por entidad): `SyncModel` (Item), `SyncItemAttribute`, `SyncModelImages`, `SyncFamilies`, `SyncCategories`, `SyncProduct` (Item Variant), `SyncWindowsService`, `SyncOffers`, `SyncInvetory` (sic) / `SyncInventoryChangedOnly` (con hash anti-reenvío) / `SyncInventoryZero`, `SyncCasePack`, `SyncCasePackInventory`, `SyncCasePacksOffers`, `SyncPaymentMethod`, `SyncCustomer`, `SyncCustomerGroup`, `SyncCustomerUser`, `SyncAgentMaster`, `SyncShippingAddress`, `SyncInvoice`, `SyncCrMemo`, `SyncOrder` (limpia flag `B2B Needs Sync`), `SyncReturnOrder`, `SyncShipment`, `SyncReturnReceipt`, `CancelOrderInB2B` (PUT del pedido con status forzado `canceled`), `DeleteOffers`.

### 3.3 Orden de sincronización (reports que lanzan el orquestador)

El orden real lo definen los reports (`src\reports\`), no el orquestador:

1. **Rep80102 B2BSyncMasters**: atributos → ventanas de servicio → formas de pago → grupos de cliente → familias → categorías.
2. **Rep80101 B2BSyncItemEntities**: modelos (Item) → imágenes de modelo → products (variantes/EAN) → casepacks → ofertas → ofertas de casepack → inventario → inventario de casepack.
3. **Rep80103 B2BSyncCustomerEntities**: clientes → usuarios de cliente → direcciones de envío.
4. **Rep80104 B2BSyncAgents**: agentes (Salesperson).
5. **Rep80110 B2BSyncDocumentEntities**: pedidos → pedidos de devolución → facturas → abonos → albaranes → albaranes de devolución.

Jobs automáticos: token (`Cod80102`), procesado de pedidos entrantes cada 5 min (`B2B Order Process Job`), sync de pedidos salientes cada 5 min (`B2B Order Sync Job`, flag `B2B Needs Sync`), sync de stock cada minuto con hash de cambio (`B2B Stock Sync Job` + `SyncInventoryChangedOnly`).

### 3.4 Patrón GET → comparar → PUT/DELETE (ofertas)

Implementado en `Cod80142.B2BDeleteApiManager.al` + `Cod80134.B2BOfferAdapterV2.al`:

1. **GET** `{Base Url}{Get Offers Url}` con body `{"modelId": "GUID-DEL-ITEM"}` → response esperada:
   ```json
   { "items": [ { "id": "GUID-OFERTA-1" }, { "id": "GUID-OFERTA-2" } ] }
   ```
   (el conector solo lee `items[].id`; puede haber más campos).
2. **Comparación local**: los ids remotos se cruzan con la tabla local `B2B Guid Combinations`; los que ya no existen en BC son "elementos a borrar" (`ElementsToDelete`).
3. **DELETE** `{Base Url}{Delete Offer Url}` con `%1` = id (sin body, solo Bearer) por cada obsoleto. 2xx → `ConfirmDelete` borra el registro local; error → fila de Sync Status en Error y se continúa con el siguiente (no aborta el bucle).

---

## 4. Configuración relevante (tabla `B2B Integration Setup`, Tab80100 / Pag80100)

### 4.1 Autenticación y control

| Campo | Tipo | Uso |
|---|---|---|
| `Enable Integration` | Boolean | Interruptor global: si false, nada llama a la API |
| `Integration User` / `Integration Password` | Text[100] / Text[250] (masked) | Credenciales de login (email + password) |
| `Active Token`, `Token Expiration DateTime`, `Last Token Refresh` | — | Estado del token (solo lectura, ver §1.4) |
| `Token Refresh Interval` | Integer, default 60 | Minutos entre ejecuciones del job de refresh |
| `Save Request Body` | Enum Never/On Error/On Ok/Always | Auditoría de bodies en Sync Status |

### 4.2 URLs de endpoints (lo que el nuevo backend debe exponer)

| Campo Setup | Método esperado | Notas de placeholder |
|---|---|---|
| `Login URL` | POST | default `https://back-mitoprojects.mygo2b.app/api/auth/login` |
| `Sync Company URL` | PUT | default `https://back-mitoprojects.mygo2b.app/api/core/b2binfo` |
| `Base Url` | — | Prefijo solo para Get/Delete Offers (patrón nuevo) |
| `Sync Models URL` | PUT | `{{$guid}}` = SystemId del Item |
| `Sync Products URL` | PUT | `{{$guid}}` (variantes/EAN) |
| `Sync Attributes URL` | PUT | `{{$id}}` en handler legacy; `{{$guid}}`/`%1` vía manager genérico |
| `Sync Model Images URL` | PUT | `{{$guid}}` |
| `Sync Categories URL` / `Sync Families URL` | PUT | `{{$guid}}`/`%1` |
| `Sync Offers URL` | PUT | `{{$guid}}` |
| `Get Offers Url` | GET (con body) | relativa a `Base Url` |
| `Delete Offer Url` | DELETE | relativa a `Base Url`, `%1` = id oferta |
| `Sync Service Windows URL` | PUT | `{id}` = Service Window ID |
| `Sync Inventory URL` | PUT | `{{$guid}}`/`%1` |
| `Sync Warehouses URL` | PUT | `{id}` = código de almacén |
| `Sync Customers URL` | PUT | `{{$guid}}`; también base de `/clients/{id}/users/admin` y `/clients/{id}/shipping-addresses/{addrId}` |
| `Sync Address URL` | PUT | direcciones de envío |
| `Orders URL` | PUT | `{{$guid}}` o `%1` = SystemId del pedido |
| `Search Orders URL` | POST | búsqueda de pedidos, ej. `.../api/orders/orders/search` |
| `Delivery Notes URL` | PUT | legacy: `+ '/' + guid` |
| `Invoices URL` | PUT | legacy: `+ '/' + guid` |
| `Payment Methods URL` | PUT | legacy: `+ '/' + code` |
| `Customer Groups URL` | PUT | `{{$guid}}`/`%1` |
| `Sync Agents URL` | PUT | `{{$guid}}`/`%1` |

### 4.3 Otros campos (no API, contexto)

`Image Url` (base de imágenes de artículo), bloque Azure Blob (`Storage Account Url`, `Container Name`, `Sas Token`, carpetas de facturas/pedidos/albaranes — subida de PDFs directa a Azure, no pasa por la API B2B), `Service Window To Sync`, `Reposition Window ID`, `Send Product` (artículo de portes para pedidos entrantes), `Auto Send Email On Release`, `Vendor Ref. No.`.

---

## Apéndice: ejemplo de intercambio completo de login

```
POST /api/auth/login HTTP/1.1
Host: back-mitoprojects.mygo2b.app
Content-Type: application/json

{"email":"integracion@mito.com","password":"secreto","type":"global","longDuration":true}
```

```
HTTP/1.1 200 OK
Content-Type: application/json

{"token":"eyJhbGciOiJIUzI1NiJ9.xxx.yyy","tokenExpiresIn":"<datetime parseable por Evaluate de BC>"}
```

Requests posteriores:

```
PUT /api/catalog/models/08A9C2D1-4F3B-4E2A-9D77-001122334455 HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.xxx.yyy
Content-Type: application/json

{ ...BuildModelJson()... }
```
