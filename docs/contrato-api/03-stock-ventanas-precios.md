# Contrato API B2B — Bloque 3: Stock por almacén, ventanas de servicio, ofertas/tarifas, formas de pago y grupos de cliente

> Documentado a partir del código AL del conector **MITO - Conector B2B** (`C:\BC_Projects\Mito - Conector B2B`).
> Objetivo: que el nuevo backend B2B (.NET 8) exponga EXACTAMENTE la misma API que consume el conector, sin cambiar el AL.

---

## 0. Convenciones comunes a todos los endpoints de este bloque

Fuente: `src\codeunits\b2bManager\Cod80111.B2BApiManager.al`, `Cod80143.B2BBaseApiManager.al`, `Cod80142.B2BDeleteApiManager.al`, `Cod80147.B2BGetApiManager.al`.

- **Las rutas NO están hardcodeadas en el conector**: cada endpoint se lee de un campo de la tabla `B2B Integration Setup` (Tab80100). El backend nuevo debe exponer rutas y luego configurarlas en BC. En este documento se indica el **campo de setup**, los **placeholders** que el conector sustituye y el patrón de sustitución.
- **Sustitución de placeholders** (`Cod80111.B2BApiManager.GetEndpointURL`): si la URL configurada contiene el literal `{{$guid}}`, se reemplaza por el id; si no, se aplica `StrSubstNo` (es decir, `%1` = id). Algunos flujos legacy usan `{id}` (ver almacenes y ventanas legacy).
- **Método de sincronización: siempre `PUT`** (upsert idempotente identificado por el id de la URL). No hay POST de alta separado.
- **Headers** de toda petición:
  - `Content-Type: application/json`
  - `Authorization: Bearer <token>` — el token se obtiene/renueva contra la `Login URL` (`Cod80143.B2BBaseApiManager.GetToken`; caduca según `Token Expiration DateTime`).
- **Timeout** del cliente: 10 s (`Client.Timeout(10000)`).
- **Comportamiento esperado de la respuesta** (contrato que el backend debe respetar):
  - Éxito = cualquier **HTTP 2xx**. El body de respuesta es opcional; si viene, debe ser JSON parseable (objeto o array; el conector usa `JsonToken.ReadFrom`, un body no-JSON hace fallar el sync). Un body vacío es válido.
  - Error = HTTP no-2xx. El conector registra `HTTP <status>: <body>` en `B2B Sync Status` / `B2B Error Log` y marca la entidad con estado de error. No hay reintento automático inmediato (salvo el job de stock, que reintenta en la siguiente vuelta).
- Los GUID viajan **sin llaves**, en minúsculas del formato BC (`utils.StringifyGuid`, `Cod80122.B2BUtils.al`), p. ej. `a1b2c3d4-e5f6-7890-abcd-ef1234567890`.
- Las fechas "date" van como `YYYY-MM-DD` (`FormatDateForAPI`); las fechas con hora como `YYYY-MM-DDT00:00:00.000Z`.
- Los objetos multiidioma (`name`, `description`) llevan siempre las 4 claves `es_ES`, `en_EN`, `fr_FR`, `it_IT` (sembradas con el texto por defecto y sobrescritas con traducciones de `B2B Translation Entry`; `Cod80122.B2BUtils.GenerateLanguajeObject`).
- `marketId` es siempre `"es"` (`B2BUtils.GetMarketId`, hardcodeado).
- Moneda: `Currency Code` de la línea o, si vacío, `LCY Code` de la empresa (fallback `EUR`) (`B2BUtils.GetCurrencyCode`).

---

## 1. Ventanas de servicio (Service Windows)

### 1.1 Semántica de negocio

Fuente: `Enum80104.B2BServiceWindowType.al`, `Enum80109.B2BServiceWindowStatus.al`, `Tab80101.B2BServiceWindow.al`, `Cod80144.B2BItemWindowPlanMgt.al`.

Una **ventana de servicio** es un periodo de venta/entrega con el que el portal agrupa catálogo, stock y precios. Tipos (`B2B Service Window Type`):

| Valor | Significado |
|---|---|
| `NOT_DEFINED` | Sin definir (el plan lo mapea a SCHEDULED por defecto). |
| `REPLENISHMENT` | Reposición: venta contra stock físico disponible; stock siempre finito. |
| `SCHEDULED` | Programada (campaña/pre-venta): mientras la ventana está **Open** el stock es "infinito" (los pedidos B2B se registran como Blanket Orders); al pasar a **Closed** (corte/cutoff) el stock pasa a finito y se calcula contra inventario+compras. |

Estados (`B2B Service Window Status`): `Open` / `Closed`. La ventana tiene además fechas base (`From/To/Limit Date`), variantes **incoterm** FOB y USA con sus propias fechas, un `Location Code` (almacén contra el que se calcula stock), `Campaign ID` y flag `Sync to B2B`.

**Modo de catálogo** que debe mostrar el portal (`Enum80110.B2BCatalogMode`, calculado en `B2BItemWindowPlanMgt.GetCatalogMode`):
- SCHEDULED + Open → `Infinite`.
- SCHEDULED + Closed → `Finite` si disponible > 0, si no `NotAvailable`.
- REPLENISHMENT → `Finite` si disponible > 0, si no `NotAvailable`.

**Efecto en precios dentro de BC** (`Cod80149.B2BPricingState.al`, `Cod80150.B2BPricingFilterSubs.al`, `Cod80151.B2BPricingMgt.al`): al recalcular precios de un pedido, el singleton `B2B Pricing State` guarda el `Order Type` del pedido y el subscriber de `Price Calculation - V16` elimina del buffer las tarifas cuya cabecera (`Price List Header."B2B Order Type Filter"`, `Enum80114`: `All`/`REPLENISHMENT`/`SCHEDULED`) no sea compatible. Las líneas con `B2B Price Source = External` (`Enum80113`: precio venido del portal) no se recalculan. El portal debe replicar la misma lógica: una oferta con `orderType` solo aplica a pedidos de ese tipo; sin `orderType` aplica a todos.

### 1.2 Endpoint

- **Método**: `PUT`
- **Ruta** (campo `Sync Service Windows URL` de Tab80100): plantilla con placeholder de id, p. ej. `{base}/.../service-windows/%1` o `.../{{$guid}}`. El id sustituido es el **`Service Window ID` tal cual** (sin pasar a minúsculas) — vía `ModelId()` del adapter (`Cod80126`). *Ojo*: el campo `id` del body sí va en minúsculas (ver hallazgos, §7).
- **Disparo**:
  - Report **Rep80102 B2BSyncMasters** (sync masivo de maestros) → `B2B Api Orchestrator.SyncWindowsService` → adapter `Cod80126.B2BServiceWindowAdapter.al`. Solo si `Sync to B2B = true` (`MustSyncToB2B`).
  - Acciones de las páginas `Pag80101.B2BServiceWindows` / `Pag80102.B2BServiceWindowCard` → flujo **legacy** `Cod80106.B2BServiceWindowSync.al` (PUT a la misma URL con `{id}` reemplazado por el Service Window ID, pero con un **payload reducido**: solo `from`, `to`, `limit`, `orderType` en minúscula `scheduled` o `REPLENISHMENT`). Exige `Sync to B2B` y `Active`.

### 1.3 Payload completo (adapter V2, `Cod80126.B2BServiceWindowAdapter.al`)

```json
{
  "id": "ss26",
  "name": {
    "es_ES": "Spring/Summer 2026",
    "en_EN": "Spring/Summer 2026",
    "fr_FR": "Spring/Summer 2026",
    "it_IT": "Spring/Summer 2026"
  },
  "showUntil": "2026-03-31T00:00:00.000Z",
  "from": "2026-01-15",
  "to": "2026-03-15",
  "limit": "2026-03-31",
  "limitDays": 75,
  "orderType": "SCHEDULED",
  "incoterms": [
    {
      "id": "ss26 fob",
      "incotermId": "fob",
      "name": {
        "es_ES": "SS26 FOB", "en_EN": "SS26 FOB", "fr_FR": "SS26 FOB", "it_IT": "SS26 FOB"
      },
      "data": {
        "from": "2026-01-01",
        "to": "2026-02-28",
        "limit": "2026-03-10",
        "limitDays": 68
      }
    },
    {
      "id": "ss26 usa",
      "incotermId": "usa",
      "name": { "es_ES": "SS26 USA", "en_EN": "SS26 USA", "fr_FR": "SS26 USA", "it_IT": "SS26 USA" },
      "data": { "from": "2026-01-05", "to": "2026-02-20", "limit": "2026-03-05", "limitDays": 59 }
    }
  ]
}
```

| Campo JSON | Tipo | Origen BC (`B2B Service Window`) | Obligatorio |
|---|---|---|---|
| `id` | string | `LowerCase("Service Window ID")` | Sí |
| `name` | objeto multiidioma | `Description` + traducciones (tabla 80101, campo Description) | Sí |
| `showUntil` | string datetime `...T00:00:00.000Z` | `Limit Date` | **Opcional** — solo si `Limit Date` ≠ 0D |
| `from` | string `YYYY-MM-DD` (o `""` si 0D) | `From Date` | Sí |
| `to` | string `YYYY-MM-DD` | `To Date` | Sí |
| `limit` | string `YYYY-MM-DD` | `Limit Date` | Sí |
| `limitDays` | integer ≥ 1 | `Limit Date - From Date` (mínimo 1; 1 si alguna fecha es 0D) | Sí |
| `orderType` | string | `Format("Order Type")`: `NOT_DEFINED` \| `REPLENISHMENT` \| `SCHEDULED` | Sí |
| `incoterms` | array | Un elemento por incoterm informado (`FOB Name` ≠ '' → `fob`; `USA Name` ≠ '' → `usa`) | Sí (puede ser `[]`) |
| `incoterms[].id` | string | `LowerCase(FOB Name / USA Name)` | Sí |
| `incoterms[].incotermId` | string | Literal `"fob"` o `"usa"` | Sí |
| `incoterms[].name` | objeto multiidioma | Nombre del incoterm (sin traducciones, 4 claves iguales) | Sí |
| `incoterms[].data.from/to/limit` | string `YYYY-MM-DD` | `From/To/Limit FOB Date` o `From/To/Limit USA Date` | Sí |
| `incoterms[].data.limitDays` | integer ≥ 1 | `limit - from` del incoterm | Sí |

**Respuesta esperada**: 2xx; body opcional. El conector guarda estado en `B2B Last Sync Status` de la ventana.

---

## 2. Stock por almacén / ventana (Inventory)

### 2.1 Modelo: plan artículo-ventana

Fuente: `Tab80125.B2BItemWindowPlan.al`, `Tab80124.B2BServiceWindowItem.al`, `Cod80144.B2BItemWindowPlanMgt.al`.

El stock B2B **no es el stock BC por almacén sin más**: cada combinación *(Item, Variante, Ventana de servicio)* tiene una fila de plan (`B2B Item Window Plan`) que se crea al asignar el artículo a la ventana (`B2B Service Window Item.OnInsert`). El conector publica **una cifra de stock por variante Y ventana**.

**Cálculo del disponible** (`B2BItemWindowPlanMgt.CalculateAvailableQty`):
- Ventana **SCHEDULED + Open** → devuelve **10000** (valor convencional = "ilimitado" durante la fase abierta).
- Ventana **SCHEDULED + Closed** o **REPLENISHMENT** → fórmula "no reservado" contra el `Location Code` de la ventana:
  `(Inventario − reservado sobre inventario) − (líneas de pedido de venta pendientes NO reservadas, de cualquier canal) + (líneas de pedido de compra pendientes de ESTA ventana − reservado sobre ellas)`, con suelo en 0.
- Ventana inexistente → 0.

### 2.2 Endpoint (stock de variante)

- **Método**: `PUT`
- **Ruta** (campo `Sync Inventory URL`): plantilla con **dos** placeholders `%1` y `%2` resuelta en el adapter (`Cod80125.B2BWarehouseStocksAdapter.EndPointUrl`):
  - `%1` = literal **`REPOSIC`** (hardcodeado en el adapter, no varía por ventana).
  - `%2` = **SystemId del `Item Variant`** (GUID sin llaves) — es el `productId` del portal.
  - Es decir, la ruta identifica el producto; la ventana concreta viaja en el body (`stockServiceId`). El backend debe hacer upsert por (producto, stockServiceId).
- **Disparo**:
  - **Job Queue cada 1 minuto** (`Cod80168.B2BStockSyncJob.al`): recorre variantes con `B2B Stock Needs Sync = true` (flag que ponen los movimientos de producto) y llama a `Orchestrator.SyncInventoryChangedOnly` (`Cod80113.B2BApiOrchestrator.al`), que por cada fila de plan recalcula el disponible, lo hashea (SHA-256 del valor en formato invariante) y **solo hace el PUT si el hash difiere del último publicado** (`Last Published Hash`). Éxito → actualiza hash y `Last Published To B2B`.
  - Report **Rep80101 B2BSyncItemEntities** (sync manual/masivo por artículo) → `SyncInvetory`: PUT de TODAS las filas de plan de la variante, sin control de cambios.
  - **Stock a cero al desasignar**: `B2B Service Window Item.OnDelete` → `SyncInventoryZero` (adapter con `SetForceZeroStock(true)`) manda `stock: 0` por cada variante/ventana antes de borrar el plan, para no dejar stock fantasma en el portal. Si el PUT falla, el borrado en BC se aborta.
- **Condición de sync** (`MustSyncToB2B`): `Item."Sync to B2B" = true` y variante no bloqueada (el job exige además `B2B Active`).

### 2.3 Payload (`Cod80125.B2BWarehouseStocksAdapter.InternalBuildModelJson`)

```json
{
  "stock": 142,
  "type": "Inventory",
  "entryDate": "2026-08-02",
  "stockServiceId": "SS26",
  "orderType": "SCHEDULED"
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio |
|---|---|---|---|
| `stock` | decimal | `CalculateAvailableQty(plan)` (10000 = infinito en Scheduled+Open; 0 forzado en el flujo de limpieza) | Sí |
| `type` | string | Literal `"Inventory"` | Sí |
| `entryDate` | string `YYYY-MM-DD` | Fecha actual del sync | Sí |
| `stockServiceId` | string | `Plan."Service Window ID"` — **tal cual, sin lowercase** (ver hallazgos) | Sí |
| `orderType` | string | `Format(Plan."Order Type")`: `NOT_DEFINED` \| `REPLENISHMENT` \| `SCHEDULED` | Sí |

**Respuesta esperada**: 2xx, body opcional. Un fallo deja el flag `B2B Stock Needs Sync` activo → reintento en la siguiente pasada del job.

### 2.4 Stock de case packs

Fuente: `Cod80128.B2BWhseStocksCPAdapter.al`. Un **case pack** es un `Item` con `B2B Parent Item` relleno (artículo "caja" que agrupa unidades del artículo padre); en el portal es un producto más colgando del modelo del padre.

- **Método/Ruta**: `PUT` a la misma plantilla `Sync Inventory URL`, con `%1` = `REPOSIC` y `%2` = **SystemId del Item case pack**.
- **Disparo**: Rep80101 (dataitem `CasePackInventory`, items con `B2B Parent Item` = artículo y `Sync to B2B` = true) → `Orchestrator.SyncCasePackInventory`.
- **Payload**:

```json
{
  "stock": 57,
  "type": "Inventory",
  "entryDate": "2026-08-02",
  "stockServiceId": "REPOSIC"
}
```

| Campo | Origen | Nota |
|---|---|---|
| `stock` | **`Random(100)`** | ⚠️ Valor ALEATORIO 1..100 — placeholder nunca terminado (ver hallazgos §7). |
| `stockServiceId` | `Setup."Service Window To Sync"` → `B2B Service Window."Service Window ID"` | Ventana fija de setup, no por plan. |
| `type`, `entryDate` | Como en §2.3 | Sin campo `orderType`. |

---

## 3. Almacenes (Warehouses)

Fuente: `Cod80105.B2BWarehouseSync.al` (builder del JSON) + `Cod80101.B2BAPIHandler.CallSyncWarehouseAPI` (HTTP).

- **Método**: `PUT`
- **Ruta** (campo `Sync Warehouses URL`): plantilla con placeholder `{id}`, reemplazado por **`Location.Code`** (p. ej. `{base}/.../warehouses/{id}`).
- **Disparo**: acción de la página extendida de almacén (`PagExt80103.LocationCardExt.al`) → `SyncWarehouseToB2B`. Requiere `Enable Integration` y `Location."Sync to B2B"`. Guarda resultado en `Location."B2B Last Sync Status"` / `DateTime`.

### Payload completo

```json
{
  "code": "ALM01",
  "description": {
    "es_ES": "Almacén Central",
    "en_EN": "Almacén Central",
    "fr_FR": "Almacén Central",
    "it_IT": "Almacén Central"
  },
  "active": true,
  "address": {
    "city": "Elche",
    "province": "A",
    "streetAddress": "Calle Industria 5",
    "zipCode": "03203",
    "num": "5",
    "description": "Nave 3",
    "countryIsoId": "ES",
    "geo": { "latitude": 38.2699, "longitude": -0.7126 },
    "contact": {
      "name": "Juan Pérez",
      "lastName": "",
      "company": "Almacén Central",
      "phones": [ { "code": "+34", "number": "966000000" } ]
    }
  },
  "transportIds": [],
  "markets": [ "es" ],
  "countries": [ "ES" ],
  "zipCodes": []
}
```

| Campo JSON | Tipo | Origen BC (`Location`) | Obligatorio |
|---|---|---|---|
| `code` | string | `Code` | Sí |
| `description` | objeto multiidioma | `Name` (mismo texto en los 4 idiomas, sin traducciones) | Sí |
| `active` | boolean | Literal `true` (si está marcado para sync) | Sí |
| `address.city` | string | `City` | Sí (puede ser `""`) |
| `address.province` | string | `Post Code."County Code"` buscado por CP+país; fallback `County` | Sí |
| `address.streetAddress` | string | `Address` | Sí |
| `address.zipCode` | string | `Post Code` | Sí |
| `address.num` | string | `B2B Street Number` (campo extendido) | Sí |
| `address.description` | string | `Address 2` | Sí |
| `address.countryIsoId` | string | `Country/Region."ISO Code"`; fallback `Country/Region Code` | Sí |
| `address.geo.latitude` / `longitude` | decimal | `B2B Latitude` / `B2B Longitude` | Sí |
| `address.contact.name` | string | `Contact` | Sí |
| `address.contact.lastName` | string | `""` literal | Sí |
| `address.contact.company` | string | `Name` | Sí |
| `address.contact.phones[]` | array | 1 elemento si `Phone No.` ≠ ''; `code` = prefijo país (`B2BUtils.GetCountryPhoneCode`), `number` = `Phone No.` | Sí (puede ser `[]`) |
| `transportIds` | array | Siempre `[]` | Sí |
| `markets` | array de string | `[LowerCase("Country/Region Code")]` si hay país; si no `[]` | Sí |
| `countries` | array de string | `["Country/Region Code"]` (mayúsculas originales) | Sí |
| `zipCodes` | array | Siempre `[]` | Sí |

**Respuesta esperada**: 2xx; body opcional JSON.

---

## 4. Ofertas / tarifas (Offers)

Fuente: `Cod80134.B2BOfferAdapterV2.al` (variantes), `Cod80135.B2BCPOfferAdapterV2.al` (case packs), spec funcional en `price-discount-export-spec.md` y `PVP.md` del repo.

### 4.1 Origen de datos y semántica

Las ofertas salen de **`Price List Line`** (tabla 7001, BC v21+) con:
- `Asset Type = Item`, `Status = Active`, `Amount Type ∈ {Price, Any}` (líneas de precio) y `Amount Type ∈ {Discount, Any}` (líneas de descuento candidatas).
- Solo tarifas cuya cabecera `Price List Header` tenga **`B2B Sync to B2B` = true**. Una tarifa que deja de estar marcada hace que TODAS sus ofertas publicadas se marquen para borrado en el portal.
- Filtro de variante: líneas con `Variant Code` de la variante o vacío.

**Desdoblamiento**: cada línea de precio se "explota" en N ofertas:
1. **Tramos de cantidad** (`stock`): puntos de corte = `Minimum Quantity` del precio + cada `Minimum Quantity` mayor de los descuentos candidatos. La oferta con `stock = X` significa "a partir de X unidades".
2. **Segmentos de fecha**: line-sweep con los puntos de corte de `Starting/Ending Date` de precio y descuentos (fin de descuento → nuevo segmento al día siguiente). En cada segmento se aplica el mejor descuento vigente.
3. **Contextos de cliente**: el contexto **base** hereda el destino del precio (`All Customers` → sin `clientId`/`clientGroupId`; `Customer Price Group` → `clientGroupId`; `Customer` → `clientId`). Además, si el precio NO es de cliente individual, cada descuento de `Customer Disc. Group` o `Customer` genera ofertas adicionales **por cliente específico** (`clientId` = SystemId del cliente), porque su descuento difiere del base.

**Selección del mejor descuento** en cada (tramo, segmento, contexto): 1) mayor `%`, 2) mayor especificidad (`All Customers`=0 < `Customer Disc. Group`=1 < `Customer`=2), 3) mayor `Minimum Quantity`. El contexto base de precios All Customers/Price Group solo admite descuentos `All Customers` (los más específicos se van a su contexto de cliente); un precio de cliente individual absorbe todos los descuentos aplicables sin desdoblar. Un descuento solo es candidato si `Line Discount %` ≠ 0 y su origen es compatible con el del precio (comparte clientes).

**PVP**: si la línea tiene `MITO Precio PVP > 0`, se emite **una oferta adicional informativa** con `priceType: "PVP"` (precio recomendado de venta al público): sin descuentos, sin desdoblar por cliente ni por tramos; fechas = las de la propia línea; id determinista distinto (GUID centinela `50565000-0000-0000-0000-000000000000`, "PVP" en ASCII, como tercer componente de la combinación). El resto de ofertas llevan `priceType: "PVD"` (precio de venta a distribuidor).

**Ids deterministas y ciclo de vida**: el `id` de cada oferta es el SystemId de una fila de `B2B Guid Combinations` (Tab80110) que asocia (tabla 7001, SystemId de la línea de precio, SystemId del cliente o de la variante o vacío[, centinela PVP]). Cada sync refresca `Sync Date` de las combinaciones vigentes y marca `For Delete` las que quedaron obsoletas (`MarkForDeleteGuidCombinations` / `MarkAllGuidCombinationsForDelete`). Eso alimenta el flujo de borrado (§4.5).

**Case packs y ofertas**: para un case pack (`Item` con `B2B Parent Item`), `productId` = SystemId del Item case pack y `modelId` = SystemId del **item padre**. Para una variante normal, `productId` = SystemId del `Item Variant` (si la línea tiene `Variant Code`) y `modelId` = SystemId del `Item`. Si la línea de precio no tiene variante y el item no es case pack, **no se emite `productId`** (oferta a nivel de modelo).

**orderType de la oferta**: viene de `Price List Header."B2B Order Type Filter"` (`Enum80114`): `All` → **no se emite el campo** (aplica a cualquier tipo de pedido); si no, `"REPLENISHMENT"` o `"SCHEDULED"`. Así se consigue tarifa distinta por ventana de reposición vs. programación.

### 4.2 Endpoint de publicación

- **Método**: `PUT`
- **Ruta** (campo `Sync Offers URL`, usada **tal cual, sin sustitución de id**): el body es un **array** con todas las ofertas del producto.
- **Disparo**:
  - Report **Rep80101 B2BSyncItemEntities** → `Orchestrator.SyncOffers(ItemVariant)` (adapter `Cod80134`) y `Orchestrator.SyncCasePacksOffers(Item)` (adapter `Cod80135`, que reutiliza `ProcessPriceLine` del 80134).
  - Condición: `Item."Sync to B2B"` y variante no bloqueada (case packs: siempre true en el adapter, filtrados por el report a `Sync to B2B = true`).

### 4.3 Payload completo (array de ofertas)

```json
[
  {
    "id": "0f8fad5b-d9cb-469f-a165-70867728950e",
    "offerData": {
      "stock": 12,
      "basePrice": { "code": "EUR", "value": 21.5 },
      "pricesPerUnit": [],
      "fromDate": "2026-02-01T00:00:00.000Z",
      "toDate": "2026-02-28T00:00:00.000Z",
      "productId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "clientGroupId": "mayorista",
      "priority": 3,
      "marketId": "es",
      "priceType": "PVD",
      "tag": "",
      "discounts": [
        {
          "percent": 10,
          "description": {
            "es_ES": "Discount", "en_EN": "Discount", "fr_FR": "Discount",
            "it_IT": "Discount", "pt_PT": "Discount", "de_DE": "Discount"
          }
        }
      ],
      "priceOriginal": { "code": "EUR", "value": 21.5 },
      "modelId": "9b2c1a77-1111-4a5b-8c3d-2e4f5a6b7c8d",
      "orderType": "SCHEDULED"
    }
  },
  {
    "id": "3b5e2d10-aaaa-bbbb-cccc-000000000001",
    "offerData": {
      "stock": 0,
      "basePrice": { "code": "EUR", "value": 49.9 },
      "pricesPerUnit": [],
      "priority": 1,
      "marketId": "es",
      "priceType": "PVP",
      "tag": "",
      "discounts": [],
      "priceOriginal": { "code": "EUR", "value": 49.9 },
      "modelId": "9b2c1a77-1111-4a5b-8c3d-2e4f5a6b7c8d"
    }
  }
]
```

| Campo JSON (`offerData.*`) | Tipo | Origen BC | Obligatorio |
|---|---|---|---|
| `id` (raíz) | string GUID | SystemId de `B2B Guid Combinations` (determinista por línea+contexto) | Sí |
| `stock` | decimal | Tramo de `Minimum Quantity` (cantidad mínima desde la que aplica). PVP: `Minimum Quantity` de la línea | Sí |
| `basePrice.code` | string | `Currency Code` de la línea o LCY | Sí |
| `basePrice.value` | decimal | `Unit Price` (PVD) / `MITO Precio PVP` (PVP) | Sí |
| `pricesPerUnit` | array | Siempre `[]` | Sí |
| `fromDate` | string datetime | Inicio del segmento + `T00:00:00.000Z` | **Opcional** — omitido si sin límite (0D) |
| `toDate` | string datetime | Fin del segmento | **Opcional** — omitido si sin límite (0D / 9999-12-31) |
| `productId` | string GUID | SystemId del `Item Variant` (línea con variante) o del `Item` case pack | **Opcional** — omitido en ofertas a nivel de modelo |
| `clientId` | string GUID | SystemId del `Customer` (contexto cliente específico o precio `Source Type = Customer`; `""` si el cliente no existe) | **Opcional** — presente solo en esos contextos; acompañado de `clientGroupId: ""` |
| `clientGroupId` | string | `LowerCase(priceLine."Source No.")` cuando `Source Type = Customer Price Group` | **Opcional** — omitido para All Customers |
| `priority` | integer | Contador incremental 1..n dentro del procesamiento de la línea (orden de emisión) | Sí |
| `marketId` | string | Literal `"es"` | Sí |
| `priceType` | string | `"PVD"` (ofertas normales) o `"PVP"` (informativa) | Sí |
| `tag` | string | Literal `""` | Sí |
| `discounts` | array | 0 o 1 elemento `{percent, description}`; `percent` = mejor `Line Discount %`; description fija "Discount" en **6** idiomas (incluye `pt_PT`, `de_DE`) | Sí (`[]` si no hay descuento; siempre `[]` en PVP) |
| `priceOriginal` | objeto | Igual que `basePrice` (código+valor) | Sí |
| `modelId` | string GUID | SystemId del `Item` (o del `B2B Parent Item` si es case pack) | Sí |
| `orderType` | string | `Price List Header."B2B Order Type Filter"`: `REPLENISHMENT`/`SCHEDULED` | **Opcional** — omitido cuando el filtro es `All` |

**Respuesta esperada**: 2xx; body opcional.

### 4.4 Endpoint GET de ofertas publicadas (reconciliación)

- **Método**: `GET` — ⚠️ **con body JSON** (`Cod80147.B2BGetApiManager.GetEntity` adjunta contenido a la petición GET; el backend debe aceptarlo o exponer el filtro equivalentemente).
- **Ruta**: `Base Url` + `Get Offers Url` (concatenación literal, sin placeholders).
- **Body de la petición**: `{ "modelId": "<SystemId del Item, GUID sin llaves>" }` (`Cod80134.GetRequestBody`).
- **Respuesta esperada** (obligatoria, la parsea el conector):

```json
{ "items": [ { "id": "0f8fad5b-d9cb-469f-a165-70867728950e" }, { "id": "..." } ] }
```

Debe devolver **todas** las ofertas que el portal tiene para ese modelo (los objetos pueden llevar más campos; el conector solo lee `id`).

### 4.5 Endpoint DELETE de oferta

- **Método**: `DELETE` (sin body; header `Authorization` únicamente) — `Cod80142.B2BDeleteApiManager`.
- **Ruta**: `Base Url` + `Delete Offer Url`, donde `Delete Offer Url` es plantilla con `%1` = id de la oferta (GUID sin llaves), p. ej. `/offers/offers/%1`.
- **Disparo**: Rep80101, dataitem `DeleteOldOffers` → `Orchestrator.DeleteOffers`. Flujo (`Cod80134.ElementsToDelete`): GET de §4.4 → por cada id devuelto se borra si (a) no existe en `B2B Guid Combinations`, (b) existe pero está `For Delete`, o (c) su `Price List Line` origen ya no existe. Tras DELETE 2xx el conector borra la combinación local (`ConfirmDelete`).
- **Respuesta esperada**: 2xx (idempotente; conviene que un id inexistente no sea error).

---

## 5. Formas de pago (Payment Methods)

Fuente: `Cod80132.B2BPaymentMethodsAdapter.al`, `Tab80119.B2BPaymentMethod.al`.

Maestro propio `B2B Payment Method` (no el estándar de BC): cada código mapea una pareja `Payment Method Code` + `Payment Terms Code` de BC. El **código en minúsculas** es el id del portal, el mismo que después llega en los pedidos entrantes como `paymentMethodId`.

- **Método**: `PUT`
- **Ruta** (campo `Payment Methods URL`): plantilla con placeholder (`%1` o `{{$guid}}`) sustituido por **`LowerCase(Code)`** (`ModelId()`), p. ej. `{base}/.../payment-methods/%1`.
- **Disparo**: Report **Rep80102 B2BSyncMasters** → `Orchestrator.SyncPaymentMethod`. `MustSyncToB2B` devuelve siempre `true` (se sincronizan todas las filas del maestro). Existe también un flujo legacy `CallSyncPaymentMethodAPI` (`Cod80101.B2BAPIHandler`) que hace PUT a `BaseUrl + '/' + code`.

### Payload completo

```json
{
  "name": {
    "es_ES": "Transferencia 30 días",
    "en_EN": "Transferencia 30 días",
    "fr_FR": "Transferencia 30 días",
    "it_IT": "Transferencia 30 días"
  },
  "description": {
    "es_ES": "Transferencia 30 días",
    "en_EN": "Transferencia 30 días",
    "fr_FR": "Transferencia 30 días",
    "it_IT": "Transferencia 30 días"
  },
  "order": 10,
  "allowCredit": true,
  "requiredForConfirm": false,
  "requiresStock": false,
  "externalReference": "TRANSF30",
  "discount": {
    "percent": 2,
    "amount": 0,
    "description": "Descuento pronto pago"
  }
}
```

| Campo JSON | Tipo | Origen BC (`B2B Payment Method`) | Obligatorio |
|---|---|---|---|
| `name` | objeto multiidioma | `Description` + traducciones | Sí |
| `description` | objeto multiidioma | `Description`; si vacía, `Code` | Sí |
| `order` | integer | `Order` (posición en el checkout) | Sí |
| `allowCredit` | boolean | `Allow Credit` | Sí |
| `requiredForConfirm` | boolean | `Required For Confirm` | Sí |
| `requiresStock` | boolean | `Requires Stock` | Sí |
| `externalReference` | string | `External Reference` | Sí |
| `discount` | objeto | `{percent, amount, description}` de los campos `Discount *` **solo si** `Discount Amount > 0` o `Discount Percent > 0`; si no, **objeto vacío `{}`** | Sí (puede ser `{}`) |

**Respuesta esperada**: 2xx; body opcional.

---

## 6. Grupos de cliente (Customer Groups)

Fuente: `Cod80133.B2BCustomerGroupAdapter.al`, `Tab80121.B2BCustomerGroupPaymentMethod.al`.

El grupo de cliente del portal es el **`Customer Price Group`** de BC (con el campo extendido `B2B Sync to B2B`). Es la clave del cálculo de tarifas por segmento: las ofertas con `clientGroupId` (§4.3) referencian este id, y el cliente sincronizado lleva su grupo. Cada grupo declara además qué formas de pago tiene disponibles (tabla puente `B2B Customer Group Pmt. Method`).

- **Método**: `PUT`
- **Ruta** (campo `Customer Groups URL`): plantilla con placeholder sustituido por **`LowerCase(Code)`** del grupo, p. ej. `{base}/.../client-groups/%1`. El mismo valor en minúsculas es el `clientGroupId` que viaja en las ofertas.
- **Disparo**: Report **Rep80102 B2BSyncMasters** → `Orchestrator.SyncCustomerGroup`. Condición: `B2B Sync to B2B = true` en el grupo.

### Payload completo

```json
{
  "name": {
    "es_ES": "Mayoristas",
    "en_EN": "Wholesalers",
    "fr_FR": "Mayoristas",
    "it_IT": "Mayoristas"
  },
  "externalReference": "MAYORISTA",
  "paymentMethods": [ "transf30", "tarjeta" ]
}
```

| Campo JSON | Tipo | Origen BC (`Customer Price Group`) | Obligatorio |
|---|---|---|---|
| `name` | objeto multiidioma | `Description` + traducciones (tabla 6, campo Description) | Sí |
| `externalReference` | string | `Code` (mayúsculas originales) | Sí |
| `paymentMethods` | array de string | `B2B Customer Group Pmt. Method` del grupo → `LowerCase("Payment Method Code")` (códigos del maestro B2B, §5) | Sí (puede ser `[]`) |

**Respuesta esperada**: 2xx; body opcional.

---

## 7. Hallazgos y asimetrías que el backend nuevo debe conocer

1. **Stock de case packs es aleatorio**: `Cod80128.B2BWhseStocksCPAdapter` envía `stock: Random(100)` — claramente un placeholder sin terminar. El backend nuevo no debe fiarse de ese valor; conviene decidir si se reimplementa el cálculo real o se ignora el endpoint para case packs.
2. **Inconsistencia de mayúsculas en ids de ventana**: el sync de ventanas manda `id` en minúsculas en el body pero sustituye el `Service Window ID` **sin lowercase** en la URL (`Cod80126.ModelId`); el stock manda `stockServiceId` **sin lowercase** (`Cod80125`). El backend debe tratar el id de ventana case-insensitive (o normalizar) para que ambos casen.
3. **URL de stock con literal `REPOSIC`**: el primer placeholder de `Sync Inventory URL` siempre se rellena con el literal `'REPOSIC'` aunque el body lleve otra ventana; la ventana real va solo en `stockServiceId`. El upsert debe basarse en (productId de la ruta, `stockServiceId` del body).
4. **GET con body**: el GET de ofertas (§4.4) envía `{"modelId": ...}` como body de la petición GET. Muchos frameworks .NET lo descartan por defecto; hay que soportarlo explícitamente.
5. **Doble implementación de ventanas**: las páginas usan el flujo legacy `Cod80106` (payload mínimo `from/to/limit/orderType`, con `scheduled` en minúscula) y el report de maestros usa el adapter completo `Cod80126` (payload rico con incoterms). Ambos hacen PUT a la misma URL; el backend debe aceptar ambos formatos o se debe retirar el flujo legacy.
6. **`discounts[].description` usa 6 idiomas** (`pt_PT`, `de_DE` incluidos) mientras el resto de objetos multiidioma usan 4.
7. **Sentinela de infinito**: stock `10000` significa "ilimitado" (ventana Scheduled abierta); no es un stock real.
8. **Payloads como array**: el endpoint de ofertas recibe un array JSON en la raíz (no un objeto). El `B2B Api Manager` genérico ya soporta ambos.
9. **Formas de pago sin filtro**: se sincronizan TODAS las filas del maestro `B2B Payment Method` (no hay flag `Sync to B2B`).
10. **Borrado de ofertas por reconciliación**: no hay DELETE directo al desactivar una tarifa; el portal se limpia comparando su listado (GET) contra `B2B Guid Combinations` con el flag `For Delete`. El GET debe devolver el universo completo de ofertas del modelo para que la reconciliación funcione.
