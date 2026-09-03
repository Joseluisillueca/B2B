# Contrato API B2B — 04. Clientes, usuarios de cliente, direcciones de envío, agentes y pedidos salientes

> Fuente: conector AL "MITO - Conector B2B" (`c:\BC_Projects\Mito - Conector B2B`).
> Objetivo: que el nuevo backend .NET 8 implemente EXACTAMENTE la misma API que el B2B actual, sin cambios en el conector.
> Convenciones de headers, token y construcción de URLs: ver `01-autenticacion-y-convenciones.md`.

Todos los endpoints de este bloque comparten el mismo pipeline de salida:

```
Report / Job Queue / acción de página
   → B2B Api Orchestrator (Cod80113)
   → B2B Api Manager (Cod80111) — método PUT, headers y log de sync
   → Adapter (BuildModelJson)
```

**Headers en todos los PUT** (`Cod80111.B2BApiManager.al`, líneas 111-128): `Content-Type: application/json` + `Authorization: Bearer {token}`. Timeout del cliente HTTP: **10 segundos**.

**Comportamiento de respuesta común a todos los PUT** (`Cod80111`, líneas 131-157):

- Cualquier **2xx** = éxito. El body de respuesta es opcional; si viene, debe ser JSON parseable (objeto o array). Un body no parseable se trata como **error** aunque el status sea 2xx. El conector no usa el contenido de la respuesta para estas entidades (no persiste ids devueltos), así que un `200 OK` con body vacío o `{}` es suficiente.
- No-2xx → se registra `HTTP {status}: {body}` en `B2B Sync Status` / `B2B Error Log` y la entidad queda marcada como Error en el log de sync. No hay reintento inmediato (salvo pedidos, ver §5).

**Formato de GUIDs**: siempre el SystemId de BC **sin llaves y en MAYÚSCULAS** (`StringifyGuid` = `DelChr(Format(gid),'<>','{}')`, `Cod80122.B2BUtils.al`).

Resumen de endpoints de este bloque:

| # | Entidad | Método | Ruta efectiva |
|---|---|---|---|
| 1 | Cliente | PUT | `{base}/api/clients/{clientId}` (plantilla `Sync Customers URL`) |
| 2 | Usuario admin del cliente | PUT | `{base}/api/clients/clients/{clientId}/users/admin` |
| 3 | Dirección de envío | PUT | `{base}/api/clients/clients/{clientId}/shipping-addresses/{addressId}` (plantilla `Sync Address URL` con `%1`/`%2`) |
| 4 | Agente | PUT | plantilla `Sync Agents URL` con `{agentId}` |
| 5 | Pedido saliente (Order / Blanket Order) | PUT | plantilla `Orders URL` con `{orderId}` |
| 6 | Búsqueda de pedidos (status) | GET (con body) | `Base Url` + `Search Orders URL` (ej. `/api/orders/orders/search`) |

---

## 1. Sincronización de cliente

**Adapter:** `src\codeunits\adapters\Cod80130.B2BCustomerAdapter.al` (codeunit 80161 `B2BCustomerAdapter`).

| | |
|---|---|
| **Método** | `PUT` |
| **Ruta** | Plantilla `Setup."Sync Customers URL"` con el placeholder (`%1` o `{{$guid}}`) sustituido por el **SystemId del Customer**. Valor de configuración esperado: `https://back-mitoprojects.mygo2b.app/api/clients/%1` → ruta efectiva `PUT {base}/api/clients/{clientSystemId}` |
| **Disparo** | Report `Rep80103 B2B Sync Customer Entities` (dataitem `CustomerSync`), lanzable manual o filtrado a un cliente (`SetCustomer`). No hay sync automática por evento de modificación de cliente (la acción de la ficha de cliente está comentada, `PagExt80104.CustomerCardExt.al` línea 121). |
| **Condición** | `Customer."Sync to B2B" = true`. Además `MustSyncToB2B` **exige `Country/Region Code` relleno**; si falta, se loguea "El codigo de pais es obligatorio" en `B2B Error Log` y NO se llama a la API. |

### 1.1 Payload completo (ejemplo)

```json
{
  "name": "Deportes García S.L.",
  "fiscalInfo": {
    "alias": "DEPORTES GARCIA",
    "address": {
      "streetAddress": "Calle Mayor",
      "num": "12",
      "description": "Local 3",
      "city": "Madrid",
      "province": "Madrid",
      "zipCode": "28001",
      "countryIsoId": "ES",
      "geo": { "latitude": 0, "longitude": 0 },
      "contact": {
        "name": "Ana García",
        "lastName": "",
        "company": "Deportes García S.L.",
        "phones": [ { "code": "+34", "number": "912345678" } ]
      }
    },
    "fiscalName": "Deportes García S.L.",
    "fiscalId": { "type": "nif", "document": "B12345678" }
  },
  "creditInfo": { "code": "EUR", "value": 15000.0 },
  "markets": [ "es" ],
  "payMethods": [ "transf30" ],
  "brandAccess": { "allowed": [], "disallowed": [] },
  "externalReference": "C00010",
  "email": "info@deportesgarcia.es",
  "secondaryEmails": [
    { "email": "pedidos@deportesgarcia.es", "type": "Orders",   "emailName": "Orders" },
    { "email": "facturas@deportesgarcia.es", "type": "Invoices", "emailName": "Invoices" },
    { "email": "otros@deportesgarcia.es",    "type": "Other",    "emailName": "Other" }
  ],
  "phone": { "code": "+34", "number": "912345678" },
  "secondaryPhones": [],
  "web": "https://www.deportesgarcia.es",
  "canShop": true,
  "taxId": "iva-general",
  "clientTypeID": "",
  "groupIds": [ "mayorista" ],
  "productSegments": [ "A+" ],
  "visibleAttributes": [
    { "attributeId": "categoria", "valueIds": [ "calzado" ] }
  ],
  "bankInfos": [],
  "incotermId": "fob",
  "originatedAt": "2026-08-02T10:15:30.000Z"
}
```

### 1.2 Campos

Todos los campos se emiten **siempre** (el adapter no omite propiedades; los "opcionales" van como `""`, `[]` o `0`).

| Campo JSON | Tipo | Origen BC | Notas |
|---|---|---|---|
| `name` | string | `Customer.Name` | |
| `fiscalInfo.alias` | string | `Customer."Search Name"` | |
| `fiscalInfo.fiscalName` | string | `Customer.Name` | Mismo valor que `name` |
| `fiscalInfo.fiscalId.type` | string | `Customer."B2B Fiscal ID Type"` (Option ` ,DNI,NIF,NIE,Passport`) | En minúsculas: `dni` / `nif` / `nie` / `passport`. **Default `nif`** si está vacío |
| `fiscalInfo.fiscalId.document` | string | `Customer."VAT Registration No."` | |
| `fiscalInfo.address.streetAddress` | string | `Customer.Address` | |
| `fiscalInfo.address.num` | string | `Customer."B2B Street Number"` (campo extensión) | |
| `fiscalInfo.address.description` | string | `Customer."Address 2"` | |
| `fiscalInfo.address.city` | string | `Customer.City` | |
| `fiscalInfo.address.province` | string | `Post Code.County` buscado por `Post Code` (+ país); fallback `Customer.County` | |
| `fiscalInfo.address.zipCode` | string | `Customer."Post Code"` | |
| `fiscalInfo.address.countryIsoId` | string | `Country/Region."ISO Code"` del país del cliente; fallback el propio `Country/Region Code` | |
| `fiscalInfo.address.geo.latitude/longitude` | number | — | **Siempre `0` / `0`** (BC no tiene geocoding) |
| `fiscalInfo.address.contact.name` | string | `Customer.Contact` | |
| `fiscalInfo.address.contact.lastName` | string | — | Siempre `""` |
| `fiscalInfo.address.contact.company` | string | `Customer.Name` | |
| `fiscalInfo.address.contact.phones[]` | array | `Customer."Phone No."` | 0 o 1 elemento; `code` = `Country/Region."B2B Phone Code"` (ej. `+34`), `number` = teléfono. Array vacío si no hay teléfono |
| `creditInfo.code` | string | `GL Setup."LCY Code"` (fallback `EUR`) | |
| `creditInfo.value` | number | `Customer."Credit Limit (LCY)"` | |
| `markets` | array[string] | `B2B Utils.GetMarketId()` | **Hardcodeado `["es"]`** (Cod80122, línea 163-166) |
| `payMethods` | array[string] | Maestro `B2B Payment Method` matcheado por (`Payment Method Code`, `Payment Terms Code`) del cliente | **Máximo 1 elemento**, código en **minúsculas**. `[]` si no hay mapeo |
| `brandAccess.allowed` / `.disallowed` | array | — | **Siempre vacíos** |
| `externalReference` | string | `Customer."No."` | Clave de correlación BC↔B2B |
| `email` | string | `Customer."E-Mail"` | |
| `secondaryEmails[]` | array | `B2B Orders Mail` / `B2B Invoices Mail` / `B2B Other Mail` | Solo entradas con valor. `type` y `emailName` son literales fijos `Orders` / `Invoices` / `Other` |
| `phone` | object | `Customer."Phone No."` + `B2B Phone Code` del país | Se emite aunque el teléfono esté vacío (`{"code":"","number":""}`) |
| `secondaryPhones` | array | — | **Siempre `[]`** |
| `web` | string | `Customer."Home Page"` | |
| `canShop` | boolean | `Customer."B2B Can Shop"` (InitValue `true`) | |
| `taxId` | string | `VAT Business Posting Group."B2B Tax Id Code"` del grupo registro IVA neg. del cliente | `""` si no hay grupo o no tiene código |
| `clientTypeID` | string | — | **Siempre `""`** |
| `groupIds` | array[string] | `Customer."Customer Price Group"` en **minúsculas** | 0 o 1 elemento |
| `productSegments` | array[string] | `Customer."B2B Customer Segment"` (Enum 80116: ` `, `A+`, `A`, `B`, `C`, `D`) | **El portal lo espera como array pero BC solo tiene UN segmento por cliente** → `["A+"]` o `[]` si está vacío. En **MAYÚSCULAS**, igual que los segmentos que se envían con el modelo (comentario en Cod80130, líneas 135-137) |
| `visibleAttributes` | array[object] | Tabla `B2B Catalog Visibility` (Tab80135) con `Subject Type = Customer` y `Subject Code = Customer."No."` | **Conector NEW, aditivo.** Lista blanca de valores de atributo que el cliente puede VER y COMPRAR: `[{ "attributeId": slug(B2B Code), "valueIds": [slug(valor)...] }]`. **Se emite SIEMPRE**, también `[]` sin reglas (semántica en §4.3). El conector viejo no manda la clave |
| `bankInfos` | array | — | **Siempre `[]`** ("vacío por ahora") |
| `incotermId` | string | `Customer."B2B Tipo Servicio"` (Enum `B2B Service Type`) | `fob` / `usa` en minúsculas; `""` para el resto |
| `originatedAt` | string | `CurrentDateTime` en el momento del sync | ISO 8601 `YYYY-MM-DDTHH:MM:SS.000Z` — **NO es una fecha real del cliente**, es la fecha de envío; los milisegundos son siempre `.000` y la `Z` es literal (la hora es la local del servicio BC) |

---

## 2. Creación/actualización del usuario admin del cliente

**Adapter:** `src\codeunits\adapters\Cod80136.B2B CustomerUserAdapter.al` (codeunit 80138 `B2B Customer User Adapter`).

| | |
|---|---|
| **Método** | `PUT` |
| **Ruta** | Construida por el propio adapter (`EndPointUrl`, líneas 67-77): `StrSubstNo(Setup."Sync Customers URL", 'clients/' + clientSystemId)` + `/users/admin`. Con la plantilla `{base}/api/clients/%1` la ruta efectiva es `PUT {base}/api/clients/clients/{clientSystemId}/users/admin` — **el segmento `clients` duplicado es real y esperado** (el handler legacy `Cod80101.B2BAPIHandler.al` línea 642 lo documenta explícitamente: `/api/clients/clients/{clientId}/users/admin`). |
| **Disparo** | Report `Rep80103 B2B Sync Customer Entities` (dataitem `CustomerUser`, con filtro adicional `E-Mail <> ''`). |
| **Condición** | `Customer."Sync to B2B" = true` **y** `Customer."B2B Create User" = true`. |

⚠️ Este adapter usa `StrSubstNo`, no el reemplazo `{{$guid}}`: la plantilla configurada en `Sync Customers URL` **debe usar el estilo `%1`** para que este endpoint funcione (con `{{$guid}}` la URL saldría sin sustituir).

### 2.1 Payload completo

```json
{
  "email": "info@deportesgarcia.es",
  "name": "info@deportesgarcia.es",
  "culture": "es_ES"
}
```

| Campo JSON | Tipo | Origen BC | Notas |
|---|---|---|---|
| `email` | string | `Customer."E-Mail"` | Identidad del usuario admin del portal |
| `name` | string | `Customer."E-Mail"` | **Mismo valor que `email`** (no se envía el nombre del cliente) |
| `culture` | string | `Country/Region."B2B Culture"` del país del cliente | ⚠️ Nota: NO usa el campo `Customer."B2B User Culture"`; además el `Get` del país no está protegido → si el cliente no tiene país válido el adapter revienta en runtime (el report ya exige país por el paso 1) |

**Respuesta esperada:** 2xx. Semántica upsert: crear el usuario admin si no existe, actualizarlo si existe. El conector no lee nada del body.

---

## 3. Sincronización de dirección de envío (Ship-to Address)

**Adapter:** `src\codeunits\adapters\Cod80131.B2BShippingAddressAdapter.al` (codeunit 80131 `B2B Shipping Address Adapter`).

| | |
|---|---|
| **Método** | `PUT` |
| **Ruta** | Construida por el adapter (`EndPointUrl`, líneas 111-120): `StrSubstNo(Setup."Sync Address URL", clientSystemId, addressSystemId)` — plantilla con **dos** placeholders: `%1` = SystemId del **Customer** propietario, `%2` = SystemId de la **Ship-to Address**. Ruta efectiva equivalente a la legacy (`Cod80101` línea 946): `PUT {base}/api/clients/clients/{clientId}/shipping-addresses/{addressId}` |
| **Disparo** | (a) Report `Rep80103` (dataitem `ShipToAddress`, todas las direcciones con sync del cliente); (b) acción manual en las páginas de dirección de envío (`PagExt80108.ShiptoAddressListExt.al` línea 49 y `PagExt80109.ShiptoAddressCardExt.al` línea 55). |
| **Condición** | `Ship-to Address."Sync to B2B" = true`. |

### 3.1 Payload completo

```json
{
  "address": {
    "streetAddress": "Polígono Industrial Norte",
    "num": "4B",
    "description": "Nave 7",
    "city": "Getafe",
    "province": "Madrid",
    "zipCode": "28905",
    "countryIsoId": "ES",
    "geo": { "latitude": 0, "longitude": 0 },
    "contact": {
      "name": "Luis Martín",
      "lastName": "",
      "company": "Deportes García - Almacén Getafe",
      "phones": [ { "code": "+34", "number": "916543210" } ]
    }
  },
  "alias": "GETAFE01",
  "externalReference": "8F1B3C2A-77E4-4B1E-9C31-0A5D2E9F6B10"
}
```

| Campo JSON | Tipo | Origen BC | Notas |
|---|---|---|---|
| `address.streetAddress` | string | `Ship-to Address.Address` | |
| `address.num` | string | `"B2B Street Number"` (campo extensión de Ship-to Address) | |
| `address.description` | string | `"Address 2"` | |
| `address.city` | string | `City` | |
| `address.province` | string | `County` | Sin lookup a Post Code (a diferencia del cliente) |
| `address.zipCode` | string | `"Post Code"` | |
| `address.countryIsoId` | string | `"Country/Region Code"` **tal cual** | ⚠️ **NO** busca el `ISO Code` del país (inconsistencia con el adapter de cliente, que sí lo hace). Para España `ES` = `ES` y no se nota, pero el backend no debe asumir ISO estricto aquí |
| `address.geo` | object | — | Siempre `{latitude:0, longitude:0}` |
| `address.contact.name` | string | `Contact` | |
| `address.contact.lastName` | string | — | Siempre `""` |
| `address.contact.company` | string | `Ship-to Address.Name` | |
| `address.contact.phones[]` | array | `"Phone No."` + `B2B Phone Code` del país | 0 o 1 elemento; vacío si no hay teléfono |
| `alias` | string | `Ship-to Address.Code` | El código visible de la dirección en BC |
| `externalReference` | string | **SystemId de la Ship-to Address** (GUID sin llaves) | ⚠️ Aquí `externalReference` NO es el código de BC (a diferencia del cliente, donde es el `No.`) — es el mismo GUID que va en la URL |

**Respuesta esperada:** 2xx, semántica upsert por `{addressId}` de la URL. Este mismo id es el que el portal debe devolver como `shippingAddressId` en los pedidos de entrada.

---

## 4. Sincronización de agentes (vendedores)

**Adapter en uso:** `src\codeunits\adapters\Cod80140.B2BAgentMasterAdapter.al` (codeunit 80140 `B2B Agent Master Adapter`, sobre `Salesperson/Purchaser`).
**Tabla de jerarquía:** `src\tables\Tab80104.B2BAgent.al` — mapa `Agent Code` (vendedor) → `Master Code` (vendedor padre/intermediario). Solo los vendedores dados de alta en esta tabla participan en el sync.

| | |
|---|---|
| **Método** | `PUT` |
| **Ruta** | Plantilla `Setup."Sync Agents URL"` con el placeholder (`%1` o `{{$guid}}`) sustituido por el **SystemId del Salesperson/Purchaser** (el mismo GUID que viaja en el body como `id`). Forma esperada: `PUT {base}/api/.../agents/{agentId}` según se configure. |
| **Disparo** | Report `Rep80104 B2B Sync Agents`: recorre TODA la tabla `B2B Agent` y sincroniza cada jerarquía **de maestro a hijo** (recursión `SyncAgentHierarchy`, con set de deduplicación), garantizando que el `parentId` ya existe en el portal cuando llega el hijo. |
| **Condición** | `Salesperson.Code <> ''` (en la práctica, estar en la tabla `B2B Agent` o ser master de alguien). |

### 4.1 Payload completo

```json
{
  "id": "3F2504E0-4F89-41D3-9A0C-0305E82C3301",
  "parentId": "9A1B2C3D-4E5F-4A6B-8C7D-0E1F2A3B4C5D",
  "clientIds": [
    "11111111-2222-3333-4444-555555555555",
    "66666666-7777-8888-9999-AAAAAAAAAAAA"
  ],
  "groupIds": [],
  "payMethods": [],
  "name": "Juan Pérez",
  "email": "jperez@empresa.es",
  "culture": "es_ES",
  "emailsSecondaries": [],
  "markets": [ "es" ],
  "visibleAttributes": [
    { "attributeId": "marca", "valueIds": [ "adidas" ] }
  ]
}
```

| Campo JSON | Tipo | Origen BC | Notas |
|---|---|---|---|
| `id` | string (GUID) | `Salesperson/Purchaser.SystemId` | **Es el mismo GUID que el portal debe mandar como `saleId` en los pedidos de entrada** y que BC manda como `saleId` en pedidos salientes (§5) |
| `parentId` | string (GUID) | SystemId del Salesperson cuyo código es `B2B Agent."Master Code"` | **OPCIONAL: se OMITE del JSON** si el agente no está en `B2B Agent` o no tiene `Master Code`. Permite jerarquía agente → intermediario/maestro |
| `clientIds` | array[GUID] | **UNIÓN** de (a) `Customer.SystemId` con `Salesperson Code` = código del agente y (b) clientes de `B2B Customer Agent` (Tab80134) con ese `Salesperson Code`; en ambos casos `Sync to B2B = true` (`Blocked` NO se mira, igual que en el sync de cliente §1); deduplicado por SystemId | Puede ser `[]`. **Conector NEW:** un mismo cliente puede aparecer en el `clientIds` de VARIOS agentes (multiagente, §4.3). El conector viejo solo manda (a) |
| `visibleAttributes` | array[object] | Tab80135 con `Subject Type = Agent` y `Subject Code = Salesperson.Code` | **Conector NEW, aditivo.** Lista blanca de lo que el agente VE. **Se emite SIEMPRE**, también `[]` (§4.3) |
| `groupIds` | array | — | **Siempre `[]`** |
| `payMethods` | array | — | **Siempre `[]`** |
| `name` | string | `Salesperson.Name` | |
| `email` | string | `Salesperson."E-Mail"` | |
| `culture` | string | `Salesperson."B2B Culture"` (campo extensión) | ej. `es_ES` |
| `emailsSecondaries` | array | — | **Siempre `[]`** |
| `markets` | array[string] | `GetMarketId()` | Hardcodeado `["es"]` |

### 4.2 Limitaciones y código muerto conocido

- Existe un segundo adapter, `Cod80139.B2BAgentAdapter.al` (codeunit 80139, sobre la tabla `B2B Agent`), **que NO está cableado a ningún flujo** (ningún método del orchestrator lo usa; solo se usa `SyncAgentMaster` desde `Rep80104`). Si se reactivara tiene un bug latente: la URL llevaría el SystemId del registro `B2B Agent` pero el body llevaría el SystemId del `Salesperson` (ids distintos).
- En ambos adapters el campo `defaultClientId` está **comentado** (no se envía); en el conector NEW `Cod80140` ya ni lo calcula. El backend no debe esperarlo.

### 4.3 Multiagente y visibilidad de catálogo (conector NEW — estrictamente aditivo)

> Fuente: conector NEW (`C:\BC_Projects\Mito - Conector B2B - NEW`). Spec aprobada: `docs/superpowers/specs/2026-09-03-catalogo-modulable-design.md` §1-2. Nada del contrato anterior cambia: solo se AÑADE la clave `visibleAttributes` a cliente y agente, y `clientIds` de agente pasa a poder solapar entre agentes.

**Multiagente (cartera).** Un cliente puede tener N agentes: el principal sigue siendo `Customer."Salesperson Code"`; los adicionales se dan de alta en la tabla `B2B Customer Agent` (Tab80134, PK `Customer No.` + `Salesperson Code`), editable desde la ficha de cliente (subpágina "Agentes B2B adicionales", `Pag80147`) y desde la ficha del vendedor ("Clientes B2B (adicionales)", `Pag80148`). El agente adicional recibe al cliente en su `clientIds` con **cartera completa** (ve, suplanta y crea pedidos; el pedido queda atribuido al agente que lo creó). El portal NO necesita tabla nueva: cada doc agent conserva su propia lista y **el upsert de un agente no debe "robar" clientes a otro** (dos agentes pueden llevar el mismo `clientId`).

**Visibilidad de catálogo (`visibleAttributes`).** Formato, idéntico en cliente y agente:

```json
"visibleAttributes": [
  { "attributeId": "marca",     "valueIds": [ "adidas" ] },
  { "attributeId": "categoria", "valueIds": [ "calzado", "textil" ] }
]
```

| Elemento | Origen BC | Notas |
|---|---|---|
| Fila de regla | `B2B Catalog Visibility` (Tab80135): PK `Subject Type` (Enum80120: `Customer` / `Agent`) + `Subject Code` + `Attribute ID` + `Attribute Value ID`; clave secundaria (`Attribute ID`, `Attribute Value ID`) | Una fila por valor permitido. Lista general `Pag80149` (Visibilidad de catálogo B2B) + subpágina `Pag80150` en las fichas de cliente (sujeto Customer) y vendedor (sujeto Agent). **Solo atributos MAPEADOS** (con valores en `Item Attribute Value`): el lookup de `Attribute ID` filtra `Sync to B2B = true` y `B2B Item Field Attribute = 0`; los atributos "de campo" (valor leído de un campo del Item) no tienen valores en BC y no se pueden restringir |
| `attributeId` | `SanitizeId(Item Attribute."B2B Code")` | Slug: minúsculas; espacios, `/ \ _ .` → `-`; colapso `--`; trim. Misma regla que `CatalogVocabulary.Slug` del portal y que los `id` de los valores del catálogo de atributos (`SanitizeId` vive ahora en `Cod80122.B2BUtils`, compartido con `Cod80114`). Atributo sin `Sync to B2B` o sin `B2B Code` → la regla se **omite** |
| `valueIds[]` | `SanitizeId(Item Attribute Value.Value)` de cada fila del mismo atributo | Valor que ya no existe en BC → se omite; si un atributo se queda sin valores válidos se omite la regla entera (nunca se manda `valueIds: []`) |
| Builder | `B2B Utils.BuildVisibleAttributesArray(subjectType, subjectCode)` | Lo llaman `Cod80130.BuildCustomerJson` (tras `productSegments`) y `Cod80140.InternalBuildModelJson` (tras `markets`) |

**Semántica (la aplica el portal, whitelist por atributo):** sin regla para un atributo → sin restricción en ese atributo; con reglas → solo esos valores; varios atributos → intersección; sin reglas → se ve todo. Cliente: restringe lo que VE y COMPRA; agente: lo que VE; en suplantación se aplica agente ∩ cliente. Los documentos históricos no se ocultan.

**La clave se emite SIEMPRE.** `visibleAttributes: []` significa "BC no restringe": el portal debe **borrar su fila de origen `bc`** para ese sujeto (así BC puede LEVANTAR una restricción); la fila `manual` de /manage nunca se toca desde la ingesta. Clave AUSENTE (conector viejo / payload parcial) → no tocar nada.

**Frescura (BC → portal).** `Cod80181 "B2B Agent Sync Job"` (Job Queue cada 5 min, categoría `B2BINT`, alta desde B2B Integration Setup → "Activar sync automático de agentes"; también "Marcar todos los agentes para sync" para el bootstrap) procesa los vendedores con el nuevo flag `Salesperson/Purchaser."B2B Needs Sync"` (TabExt80121, campo 50101), cada uno con su jerarquía **maestro-primero** (misma lógica que `Rep80104`, que ahora la reutiliza) y limpia el flag al enviar OK. Marcan el flag: insert/rename/delete en Tab80134 (agente nuevo y anterior); insert/modify/rename/delete en Tab80135 con sujeto Agent; insert/modify (`Master Code`)/rename/delete en `B2B Agent` (Tab80104); cambio de `Customer."Salesperson Code"` (agente anterior Y nuevo) o de `Sync to B2B`; alta/borrado de cliente; cambios de `Name`/`E-Mail`/`B2B Culture` del vendedor; y **cambios en el maestro de atributos** (`Item Attribute`: `B2B Code`/`Name`/`Sync to B2B` o borrado; `Item Attribute Value`: `Value` o borrado) → se marcan TODOS los sujetos con reglas sobre ese atributo/valor (`MarkSubjectsWithRulesOn`, barrido por la clave secundaria de Tab80135). Las filas de Tab80135 con sujeto **Customer** marcan `Customer."B2B Needs Sync"` (lo envía el job de clientes ya existente, `Cod80169`). El borrado de un cliente o de un vendedor limpia sus filas de Tab80134/Tab80135. Cualquier vendedor referenciado por Tab80134/Tab80135 se sincroniza aunque no esté en `B2B Agent` (también en el report manual `Rep80104`). Tab80134 rechaza dar de alta como adicional al vendedor principal del cliente.

---

## 5. Pedidos salientes (BC → B2B): Sales Order y Blanket Order

**Adapter:** `src\codeunits\adapters\Cod80137.B2BOrderAdapter.al` (codeunit 80137 `B2B Order Adapter`).

| | |
|---|---|
| **Método** | `PUT` |
| **Ruta** | Plantilla `Setup."Orders URL"` con placeholder `%1` o `{{$guid}}` (tooltip de `Pag80100`: "include {0} or {{$guid}} placeholder for the order ID") sustituido por el **id de comunicación** del pedido → `PUT {base}/api/orders/orders/{orderId}` según config. |
| **Id de comunicación** | `ModelSystemId()` (líneas 41-53): `Sales Header."B2B Sync Id"` si está relleno (pedido convertido desde un Blanket Order: hereda el id del blanket), si no el `SystemId` propio. **DEBE coincidir con el `id` del body** — comentario del código: si URL y body llevan ids distintos el portal falla al guardar ("error saving entity changes"). |
| **Disparo** | (a) **Job Queue `Cod80164 B2B Order Sync Job`** cada 5 min: procesa `Sales Header` con `B2B Needs Sync = true`; el flag lo activan los suscriptores de `Cod80163.B2BOrderSyncEvents.al` en insert/modify de cabecera y insert/modify/delete de líneas. Control de cambios por **hash SHA del payload**: si el JSON no cambió desde el último envío, se limpia el flag SIN llamar a la API. (b) Sync manual de un pedido (`SyncSingleDocument`). (c) Report `Rep80110 B2B Sync Document Entities`. (d) **Cancelación**: al borrar un Sales Order ya sincronizado (y que no proviene de un posting), `OnBeforeDeleteEvent` envía el mismo PUT con `status: "canceled"` (`CancelOrderInB2B` + `SetForceCancelStatus`); si la API falla, **se aborta el borrado** en BC. |
| **Condición** | `Document Type` ∈ {`Order`, `Blanket Order`} (`MustSyncToB2B`, líneas 25-29). Ambos tipos se comunican. |

### 5.1 Payload completo (ejemplo)

```json
{
  "id": "C4D5E6F7-A8B9-4C0D-9E1F-203040506070",
  "clientId": "11111111-2222-3333-4444-555555555555",
  "fiscalInfo": {
    "alias": "Deportes García S.L.",
    "address": {
      "streetAddress": "Calle Mayor",
      "num": "",
      "description": "Local 3",
      "city": "Madrid",
      "province": "Madrid",
      "zipCode": "28001",
      "countryIsoId": "ES",
      "geo": { "latitude": 0, "longitude": 0 },
      "contact": {
        "name": "Ana García",
        "lastName": "",
        "company": "Deportes García S.L.",
        "phones": [ { "code": "+34", "number": "912345678" } ]
      }
    },
    "fiscalName": "Deportes García S.L.",
    "fiscalId": { "type": "nif", "document": "B12345678" }
  },
  "shippingAddress": {
    "streetAddress": "Polígono Industrial Norte",
    "num": "4B",
    "description": "Nave 7",
    "city": "Getafe",
    "province": "Madrid",
    "zipCode": "28905",
    "countryIsoId": "ES",
    "geo": { "latitude": 0, "longitude": 0 },
    "contact": {
      "name": "Luis Martín",
      "lastName": "",
      "company": "Deportes García - Almacén Getafe",
      "phones": [ { "code": "+34", "number": "916543210" } ]
    }
  },
  "paid": false,
  "transportId": "",
  "saleId": "3F2504E0-4F89-41D3-9A0C-0305E82C3301",
  "payMethodId": "transf30",
  "reference": "Referencia del cliente",
  "observations": "",
  "totals": {
    "totalAmount":   { "code": "EUR", "value": 1000.0 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 210.0 },
    "total":         { "code": "EUR", "value": 1210.0 }
  },
  "transportTotals": {
    "totalAmount":   { "code": "EUR", "value": 15.0 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 0 },
    "total":         { "code": "EUR", "value": 15.0 }
  },
  "status": "open",
  "items": [
    {
      "id": "D1E2F3A4-B5C6-4D7E-8F90-A1B2C3D4E5F6",
      "productId": "AABBCCDD-EEFF-4011-2233-445566778899",
      "productName": {
        "es_ES": "Zapatilla Runner Azul 42",
        "en_EN": "Zapatilla Runner Azul 42",
        "fr_FR": "Zapatilla Runner Azul 42",
        "it_IT": "Zapatilla Runner Azul 42",
        "pt_PT": "Zapatilla Runner Azul 42",
        "de_DE": "Zapatilla Runner Azul 42"
      },
      "transactionInfo": {
        "info": {
          "quantity": 10,
          "discount": 5.0,
          "price":  { "code": "EUR", "value": 100.0 },
          "amount": { "code": "EUR", "value": 950.0 }
        },
        "totalDiscounts": { "code": "EUR", "value": 50.0 },
        "totalTaxes":     { "code": "EUR", "value": 199.5 },
        "taxes": [
          {
            "id": "IVA",
            "name": { "es_ES": "IVA", "en_EN": "IVA", "fr_FR": "IVA", "it_IT": "IVA", "pt_PT": "IVA", "de_DE": "IVA" },
            "percent": 21.0,
            "amount":      { "code": "EUR", "value": 199.5 },
            "taxableBase": { "code": "EUR", "value": 950.0 },
            "productTaxId": "IVA21"
          }
        ],
        "discounts": [],
        "priceOriginal": null,
        "offerDiscounts": null
      },
      "clientReference": "C00010",
      "shipDate": "2026-09-01T00:00:00",
      "status": "Open",
      "productExternalReference": "ART001",
      "additionalValues": null,
      "quantityDelivered": 0,
      "productInfo": {
        "name": { "es_ES": "Zapatilla Runner Azul 42", "en_EN": "Zapatilla Runner Azul 42", "fr_FR": "Zapatilla Runner Azul 42", "it_IT": "Zapatilla Runner Azul 42", "pt_PT": "Zapatilla Runner Azul 42", "de_DE": "Zapatilla Runner Azul 42" },
        "brandId": "",
        "ean": "8412345678901",
        "externalReference": "ART001",
        "id": "AABBCCDD-EEFF-4011-2233-445566778899",
        "image": {
          "uri": "https://cdn.ejemplo.com/images/ART001.jpg",
          "description": { "es_ES": "Zapatilla Runner Azul 42", "en_EN": "Zapatilla Runner Azul 42", "fr_FR": "Zapatilla Runner Azul 42", "it_IT": "Zapatilla Runner Azul 42", "pt_PT": "Zapatilla Runner Azul 42", "de_DE": "Zapatilla Runner Azul 42" },
          "order": 0,
          "path": ""
        },
        "modelId": "00FFEEDD-CCBB-4A99-8877-665544332211",
        "modelExternalReference": "ART001",
        "sku": "ART001AZ42"
      },
      "stockServiceId": ""
    }
  ],
  "payments": [],
  "type": "SCHEDULED",
  "source": "ERP",
  "sourceOrder": null,
  "externalReference": "PV00123",
  "orderedDate": "2026-08-02T00:00:00",
  "needRecalculateTotals": true,
  "marketId": "es",
  "clienteExternalReference": "C00010",
  "shippingAddressExternalReference": "GETAFE01",
  "orderDiscount": null,
  "totalWithTransport": { "code": "EUR", "value": 1210.0 },
  "purchaseOrderId": "PO-CLIENTE-778",
  "seasonId": ""
}
```

### 5.2 Campos de cabecera

| Campo JSON | Tipo | Origen BC | Notas |
|---|---|---|---|
| `id` | string (GUID) | `"B2B Sync Id"` si relleno, si no `SalesHeader.SystemId` | Ver "Id de comunicación" arriba. **Igual al id de la URL** |
| `clientId` | string (GUID) | `Customer.SystemId` del `Sell-to Customer No.` | Mismo GUID con el que se sincronizó el cliente (§1) |
| `fiscalInfo` | object | Datos **Bill-to** de la cabecera (`Bill-to Name/Address/City/County/Post Code/Country`, `VAT Registration No.`) + `B2B Fiscal ID Type` del cliente Bill-to | Misma estructura que en cliente. `num` siempre `""`. Teléfono: `Sell-to Phone No.` con prefijo del país Bill-to |
| `shippingAddress` | object | Si `Ship-to Code <> ''`: registro `Ship-to Address` (incluye `B2B Street Number` como `num` y su teléfono). Si no: campos `Ship-to ...` de la cabecera (`num` = `""`, sin teléfono) | Estructura de dirección con `geo` (0,0) y `contact`. `countryIsoId` aquí SÍ resuelve `ISO Code` con fallback al código |
| `paid` | boolean | — | **Siempre `false`** |
| `transportId` | string | — | **Siempre `""`** |
| `saleId` | string (GUID) **o `null`** | `Salesperson.SystemId` del `Salesperson Code` del pedido | ⚠️ Comentario del código (líneas 234-241): es el mismo GUID que el portal envía como `saleId` en pedidos de entrada. **El portal lo tipa como GUID nullable: sin vendedor hay que mandar `null`, no `""`** (un string vacío no convierte a Guid → HTTP 400) |
| `payMethodId` | string | `B2B Payment Method.Code` matcheado por (`Payment Method Code`, `Payment Terms Code`) del pedido, en minúsculas; fallback `Payment Method Code` estándar en minúsculas | Mismo criterio que `payMethods` del cliente |
| `reference` | string | `"Your Reference"` | |
| `observations` | string | — | Siempre `""` |
| `totals.totalAmount` | money | `SalesHeader.Amount` (base imponible) | Todos los "money" son `{ "code": LCY, "value": n }` |
| `totals.totalDiscount` | money | — | Siempre valor `0` |
| `totals.totalTax` | money | `"Amount Including VAT" - Amount` | |
| `totals.total` | money | `"Amount Including VAT"` | |
| `transportTotals` | object | `Unit Price` de la línea cuyo `No.` = `Setup."Send Product"` (artículo de portes) | `totalAmount` y `total` = importe portes; `totalDiscount`/`totalTax` = 0. Todo 0 si no hay artículo de portes configurado/presente |
| `status` | string | Calculado (`GetStatus`, líneas 172-203) | `invoiced` si hay cantidad facturada > 0; si hay enviado: `shipped` (todo) o `partially-shipped` (parcial); si no, `open`. **`canceled`** cuando el envío proviene de la cancelación por borrado |
| `items` | array | Líneas de venta con `Type = Item` (incluida la línea de portes) | Ver §5.3 |
| `payments` | array | — | **Siempre `[]`** |
| `type` | string | — | **Siempre `"SCHEDULED"`** (`GetOrderType`) |
| `source` | string | — | **Siempre `"ERP"`** |
| `sourceOrder` | null | — | Siempre `null` |
| `externalReference` | string | `SalesHeader."No."` | Nº de documento BC |
| `orderedDate` | string | `"Order Date"` | Formato `YYYY-MM-DDTHH:MM:SS` a medianoche, **sin `Z` ni milisegundos** (`B2B Utils.FormatDate`); si la fecha es 0D usa `Today` |
| `needRecalculateTotals` | boolean | — | **Siempre `true`** |
| `marketId` | string | `GetMarketId()` | Hardcodeado `"es"` |
| `clienteExternalReference` | string | `"Sell-to Customer No."` | ⚠️ El nombre del campo es literalmente **`clienteExternalReference`** (mezcla español/inglés) — respetarlo tal cual |
| `shippingAddressExternalReference` | string | `"Ship-to Code"` | `""` si el pedido no usa dirección de envío codificada |
| `orderDiscount` | null | — | Siempre `null` |
| `totalWithTransport` | money | `"Amount Including VAT"` | Igual que `totals.total` |
| `purchaseOrderId` | string | `"External Document No."` | Nº de pedido de compra del cliente |
| `seasonId` | string | — | Siempre `""` |

### 5.3 Campos de línea (`items[]`)

| Campo JSON | Tipo | Origen BC | Notas |
|---|---|---|---|
| `id` | string (GUID) | `SalesLine."B2B Sync Id"` si relleno (línea heredada de blanket), si no `SalesLine.SystemId` | Mismo criterio de herencia que la cabecera |
| `productId` | string (GUID) | `ItemVariant.SystemId` si la línea tiene variante; si no `Item.SystemId` | El GUID con el que se sincronizó el producto/variante |
| `productName` | object multiidioma | Descripción del Item + descripción de la variante (o código de variante si no tiene descripción) | **El mismo texto se repite en los 6 idiomas**: `es_ES`, `en_EN`, `fr_FR`, `it_IT`, `pt_PT`, `de_DE` (ojo: `en_EN`, no `en_US`/`en_GB`) |
| `transactionInfo.info.quantity` | number | `Quantity` | |
| `transactionInfo.info.discount` | number | `"Line Discount %"` | Porcentaje |
| `transactionInfo.info.price` | money | `"Unit Price"` | |
| `transactionInfo.info.amount` | money | `"Line Amount"` | |
| `transactionInfo.totalDiscounts` | money | `"Line Discount Amount"` | |
| `transactionInfo.totalTaxes` | money | `"Amount Including VAT" - "Line Amount"` | |
| `transactionInfo.taxes[]` | array | 1 elemento fijo: `id: "IVA"`, `name` multiidioma "IVA", `percent` = `"VAT %"`, `amount`, `taxableBase` = `"Line Amount"`, `productTaxId` = `"VAT Prod. Posting Group"` | Solo se modela IVA |
| `transactionInfo.discounts` | array | — | Siempre `[]` |
| `transactionInfo.priceOriginal` / `.offerDiscounts` | null | — | Siempre `null` |
| `clientReference` | string | `"Sell-to Customer No."` de la cabecera | |
| `shipDate` | string | `"Shipment Date"` de la línea | Formato `YYYY-MM-DDTHH:MM:SS` |
| `status` | string | Calculado por línea (`GetLineStatus`) | `Delivered` / `Partial` / `Open` — ⚠️ con **inicial mayúscula**, distinto del status de cabecera (minúsculas con guiones) |
| `productExternalReference` | string | `SalesLine."No."` | |
| `additionalValues` | null | — | Siempre `null` |
| `quantityDelivered` | number | `"Quantity Shipped"` | |
| `productInfo.name` | object multiidioma | Igual que `productName` | |
| `productInfo.brandId` | string | — | Siempre `""` |
| `productInfo.ean` | string | `Item Reference` tipo `Bar Code` para (Item, Variante) | `""` si no hay |
| `productInfo.externalReference` | string | `SalesLine."No."` | |
| `productInfo.id` | string (GUID) | SystemId de variante o item (como `productId`); si el Item no existe, SystemId de la línea | |
| `productInfo.image` | object o null | `uri` = `StrSubstNo(Setup."Image Url", ItemNo)`; `description` multiidioma; `order: 0`; `path: ""` | `null` si el Item no existe |
| `productInfo.modelId` | string (GUID) | `Item.SystemId` (el "modelo" = artículo padre) | `""` si el Item no existe |
| `productInfo.modelExternalReference` | string | `Item."No."` | |
| `productInfo.sku` | string | `"No." + "Variant Code"` concatenados sin separador | |
| `stockServiceId` | string | — | Siempre `""` |

### 5.4 Respuesta esperada y post-proceso en BC

- 2xx (body opcional) = OK. BC entonces limpia `B2B Needs Sync`, guarda `B2B Last Sync DateTime` y el hash del payload (`B2B Last Sync Hash`) para el control de cambios.
- Error → el flag queda a `true` y el Job Queue **reintenta cada 5 minutos** indefinidamente.
- En la cancelación por borrado, un error de la API **aborta el borrado del pedido en BC** (`Error()` en `Cod80163`, línea 110). El backend debe aceptar el PUT con `status: "canceled"` de un pedido ya conocido.
- El portal usa el `{orderId}` de la URL como clave del recurso (upsert). Debe tolerar PUT repetidos e idénticos.

---

## 6. Búsqueda de pedidos (soporte al sync de status)

Definido en el propio `Cod80137.B2BOrderAdapter.al` (`GetUrl` + `GetRequestBody`), ejecutado por `Cod80147.B2BGetApiManager.al` (usado por el proceso de actualización de estados de pedido; el detalle de ese flujo pertenece al bloque de documentos).

| | |
|---|---|
| **Método** | `GET` — ⚠️ **con body JSON** (ver hallazgo §2.2 del doc 01: ASP.NET Core debe aceptar body en GET aquí) |
| **Ruta** | `B2B Utils.GetAbsoluteUrl(Setup."Search Orders URL")` — si la URL configurada no contiene la `Base Url` se antepone. Ejemplo del tooltip de `Pag80100` (línea 167): `https://api.b2b.com/api/orders/orders/search` |
| **Headers** | `Content-Type: application/json` + `Authorization: Bearer {token}` |

**Body enviado** (líneas 96-128 del adapter; la variante por status está comentada en el código):

```json
{ "search": [ { "all": true } ] }
```

**Respuesta esperada:** 2xx con JSON parseable (objeto). Debe devolver el listado de pedidos del portal (todos, sin filtro de status). El código comentado muestra que el contrato también admite entradas `{ "all": true, "status": "open" | "partially-shipped" | "shipped" }` en el array `search`.

---

## 7. Hallazgos y limitaciones a tener en cuenta (resumen)

1. **`productSegments` es array pero BC solo maneja UN segmento por cliente** (`B2B Customer Segment`: `A+`, `A`, `B`, `C`, `D`) → llega `["A+"]` o `[]`, siempre en mayúsculas (comentario explícito en `Cod80130`).
2. **Ruta del usuario admin con `clients` duplicado**: `/api/clients/clients/{clientId}/users/admin` (y direcciones legacy `/api/clients/clients/{clientId}/shipping-addresses/{addressId}`). Es intencional (comentado en `Cod80101`); el nuevo backend debe exponer esas rutas tal cual. El adapter de usuario requiere plantilla estilo `%1` en `Sync Customers URL`.
3. **`saleId` de pedidos es GUID nullable**: `null` cuando el pedido no tiene vendedor; `""` provoca HTTP 400 en el backend actual (comentario en `Cod80137`). El GUID es el `Salesperson.SystemId`, el mismo `id` con que se publican los agentes (§4).
4. **Id de pedido heredado del Blanket Order**: URL y body deben llevar el MISMO id (`B2B Sync Id` si existe); ids distintos hacían fallar el guardado en el portal ("error saving entity changes").
5. **Campos siempre constantes** que el backend no debe exigir con contenido real: `brandAccess` vacío, `secondaryPhones: []`, `bankInfos: []`, `clientTypeID: ""`, `geo` 0/0, `paid: false`, `transportId: ""`, `payments: []`, `type: "SCHEDULED"`, `source: "ERP"`, `needRecalculateTotals: true`, `marketId`/`markets` = `"es"` (hardcodeado en `Cod80122.GetMarketId`).
6. **Inconsistencias del conector a replicar con tolerancia**: `countryIsoId` de la dirección de envío NO resuelve ISO Code (usa el código BC tal cual), `externalReference` de dirección es un GUID mientras que el de cliente es el Nº de cliente, el nombre de campo `clienteExternalReference` va en "spanglish", los status de línea van capitalizados (`Open`/`Partial`/`Delivered`) frente a los de cabecera en minúsculas (`open`/`partially-shipped`/`shipped`/`invoiced`/`canceled`), y `en_EN` (no `en_US`) en los textos multiidioma.
7. **Adapter de agentes Cod80139 es código muerto** (solo se usa `B2B Agent Master Adapter` vía `Rep80104`, que sincroniza la jerarquía maestro-primero); `defaultClientId` está comentado y no se envía.
8. **`culture` del usuario de cliente sale de `Country/Region."B2B Culture"`**, no del campo `B2B User Culture` del cliente.
9. **Paridad de ids en `visibleAttributes` (conector NEW, §4.3) — la resuelve el PORTAL.** Las reglas viajan con `attributeId = slug(B2B Code)` y `valueIds = slug(valor)`, mientras que el JSON del modelo (`Cod80112.BuildAttributesJson`) emite los atributos del producto con clave `Item Attribute.Name` (valores mapeados) o `B2B Code` (atributos de campo) y el valor **en bruto**. El portal canonicaliza la clave del atributo del producto a través del maestro de atributos ya sincronizado (entidad attribute: resuelve `Name`/`code` → `code`) y sluggea clave y valor con `CatalogVocabulary.Slug`, de modo que el predicado casa sin cambios en BC. Buena práctica recomendada (NO requisito): mantener `Name = B2B Code` en los atributos de BC, que además hace más legible la configuración.
