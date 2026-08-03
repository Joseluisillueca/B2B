# 06 — Dirección B2B → BC: API pages OData expuestas por Business Central

Este documento describe el contrato exacto de las API pages OData que expone la extensión
**"MITO - Conector B2B"** (publisher `Mito Projects SL`) y que el backend B2B (.NET 8) debe
consumir **exactamente igual** que el B2B actual.

Fuentes analizadas (repo `c:\BC_Projects\Mito - Conector B2B`):

| Objeto | Fichero |
|---|---|
| Page 80123 "B2B Sales Orders API" | `src\pages\api\Pag80123.B2BSalesOrderAPI.al` |
| Page 80124 "B2B Sales Order Lines API" | `src\pages\api\Pag80124.B2BSalesOrderLineAPI.al` |
| Page 80139 "B2B SO Stock Service API" | `src\pages\api\Pag80139.B2BSalesOrderStockServiceAPI.al` |
| Page 80115 "B2B API Customers" | `src\pages\api\Pag80115.B2BCustomersAPI.al` |
| Page 80107 "B2B Ship-to Address API" | `src\pages\api\Pag80107.B2BShiptoAddressAPI.al` |
| Page 80108 "B2B Contacts API" | `src\pages\api\Pag80108.B2BContactsAPI.al` |
| Page 80140 "B2B Secondary Email API" | `src\pages\api\Pag80140.B2BSecondaryEmailAPI.al` |
| Page 80106 "B2B Document PDF API" | `src\pages\api\Pag80106.B2BDocumentPDFAPI.al` |
| Page 80132 "B2B Customers Test" | `src\pages\api\Pag80132.B2BCustomersTest.al` (test/diagnóstico) |
| Tablas staging | `Tab80117.B2BSalesOrderHeader.al`, `Tab80118.B2BSalesOrderLine.al`, `Tab80126.B2BSalesOrderStockService.al`, `Tab80122.B2BCustomers.al`, `Tab80123.B2BShiptoAddress.al`, `Tab80127.B2BSecondaryEmail.al`, `Tab80120.B2BContacts.al`, `Tab80105.B2BDocumentPDF.al` |
| Procesamiento | `Cod80119.B2BCreateOrders.al`, `Cod80136.B2BCreateCustomers.al`, `Cod80152.B2BOrderProcessJob.al`, `Cod80155.B2BCustomerAddressJob.al`, `Cod80153.B2BSalesOrderSubscribers.al`, `Rep80106.PostOrders.al`, `Rep80105.B2BPostCustomers.al` |

---

## 1. Base URL y autenticación

### 1.1 URL base común

Todas las API pages comparten el mismo namespace personalizado:

- **APIPublisher:** `mitoprojects`
- **APIGroup:** `b2b`
- **APIVersion:** `v1.0`

URL base (BC SaaS):

```
https://api.businesscentral.dynamics.com/v2.0/{tenantId}/{environment}/api/mitoprojects/b2b/v1.0/companies({companyId})/{entitySet}
```

- `{tenantId}`: GUID del tenant de Entra ID (o dominio `*.onmicrosoft.com`).
- `{environment}`: nombre del entorno BC (p. ej. `Production`, `Sandbox`).
- `{companyId}`: SystemId (GUID) de la empresa. Se obtiene con
  `GET .../api/mitoprojects/b2b/v1.0/companies` (o con la API estándar `/api/v2.0/companies`).

### 1.2 Autenticación (OAuth2 client credentials contra Entra ID)

Flujo *service-to-service* estándar de BC:

1. **App registration** en Entra ID con permiso de aplicación
   `Dynamics 365 Business Central → API.ReadWrite.All` (application permission, con
   admin consent).
2. En BC: registrar la aplicación en la página **Microsoft Entra Applications**
   (Estado = Habilitado) y asignarle grupos de permisos que incluyan el permission set
   **B2BINTEGRATION** de la extensión (`PermissionSet80100.B2BIntegration.al`) además de
   los permisos base (p. ej. `D365 BUS FULL ACCESS` o equivalente).
3. Token:

```
POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id={appId}
&client_secret={secret}
&scope=https://api.businesscentral.dynamics.com/.default
```

4. Llamadas con `Authorization: Bearer {access_token}`, `Content-Type: application/json`.
   Para deep inserts no hace falta ninguna cabecera especial; para lecturas paginadas BC
   devuelve `@odata.nextLink` (server-driven paging).

> Nota: todas las páginas de escritura tienen `DelayedInsert = true`, requisito de BC para
> API pages; el insert se produce al recibir el payload completo (permite deep insert).

---

## 2. Pedidos: `salesOrders` (cabecera) + `salesOrderLines` + `stockServices`

### 2.1 `salesOrders` — Page 80123 "B2B Sales Orders API"

- **URL:** `.../companies({companyId})/salesOrders`
- **EntityName / EntitySetName:** `salesOrder` / `salesOrders`
- **SourceTable:** `B2B Sales Order Header` (Tab80117) — tabla *staging*, no es el pedido real.
- **Métodos:** `GET`, `POST` (deep insert con líneas y stock services), `PATCH`, `DELETE`
  (no hay `InsertAllowed/ModifyAllowed/DeleteAllowed = false`, todos habilitados; el uso
  real del B2B es **POST** con deep insert y GET de consulta).
- **Clave OData:** por defecto `SystemId` (`ODataKeyFields` no definido); la clave funcional
  de negocio es `orderId`.

**Campos:**

| Nombre OData | Tipo | Campo BC (Tab80117) | Oblig. | Notas / validaciones |
|---|---|---|---|---|
| `orderId` | Guid | `Order ID` (PK) | **Sí** | Id del pedido en el portal B2B. `OnInsertRecord` de la page: si ya existe un registro con ese `Order ID` → error `El pedido {0} ya existe en el sistema.` (idempotencia por rechazo de duplicado). |
| `customerId` | Guid | `Customer ID` | **Sí** (funcional) | Debe ser el **SystemId del Customer en BC**. No se valida en el insert; falla después en el procesado (`Customer.GetBySystemId`). |
| `shippingAddressId` | Guid | `Shipping Address ID` | Opcional | SystemId de la `Ship-to Address` de BC. Si no se resuelve, el pedido se crea sin datos de envío alternativos. |
| `reference` | String(50) | `Reference` | Opcional | Pasa a `Your Reference` del pedido de venta. |
| `paymentMethodId` | String(20) | `Payment Method ID` | Opcional | Código que se busca (en mayúsculas) en la tabla `B2B Payment Method`; de ahí salen `Payment Method Code` y `Payment Terms Code` del pedido. |
| `total` | Decimal | `Total` | Opcional | Informativo (no se traslada al pedido). |
| `totalTax` | Decimal | `Total Tax` | Opcional | Informativo. |
| `totalDiscount` | Decimal | `TotalDiscount` | Opcional | Informativo. (Nombre OData generado desde `TotalDiscount`; confirmar casing contra `$metadata`.) |
| `totalCart` | Decimal | `TotalCart` | Opcional | Informativo. |
| `totalTransport` | Decimal | `Total Transport` | Opcional | Si > 0 y `B2B Integration Setup."Send Product"` ≠ '' se añade una línea de artículo de transporte al pedido real (cantidad 1, precio = totalTransport). |
| `totalCartDiscount` | Decimal | `Total Cart Discount` | Opcional | Informativo. |
| `incotermId` | String(20) | `Incoterm ID` | Opcional | Mapeo case-insensitive: `fob` → `B2B Service Type::FOB`, `usa` → `::USA`, otro → `" "`. Se valida en `Sales Header."B2B Tipo Servicio"`. |
| `saleId` | String(50) | `Sale ID` | Opcional | **SystemId (GUID en texto) del Salesperson/Purchaser** que hizo la venta en el portal. Si se resuelve, fija `Salesperson Code` del pedido (prevalece sobre el vendedor por defecto del cliente). Puede ir vacío/null. |
| `items` | Colección | part → Page 80124 | **Sí** (funcional) | Deep insert de líneas. `SubPageLink: "Order ID" = field("Order ID")` — BC rellena `orderId` de cada línea automáticamente. |
| `stockServices` | Colección | part → Page 80139 | Opcional | Deep insert de ventanas de servicio del pedido. Mismo enlace por `Order ID`. |

Campos de la tabla **no expuestos** por la API (uso interno): `Created DateTime` (se fija en
`OnInsert` de la tabla con `CurrentDateTime`), `BCOrder` (nº del pedido BC creado),
`ErrorText` (error del procesado).

**Ejemplo de deep insert (POST `/salesOrders`):**

```json
{
  "orderId": "8f2f6f1e-....",
  "customerId": "c1a2b3c4-....",
  "shippingAddressId": "d5e6f7a8-....",
  "reference": "PEDIDO-WEB-1234",
  "paymentMethodId": "CARD",
  "total": 1210.00,
  "totalTax": 210.00,
  "totalDiscount": 0,
  "totalCart": 1000.00,
  "totalTransport": 15.00,
  "totalCartDiscount": 0,
  "incotermId": "fob",
  "saleId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "items": [
    {
      "lineId": "11111111-....",
      "productId": "22222222-....",
      "modelId": "33333333-....",
      "sku": "SKU-001",
      "qty": 5,
      "name": "Producto X",
      "unitPrice": 180.00,
      "originalUnitPrice": 200.00,
      "amount": 900.00,
      "discountAmount": 100.00,
      "stockServiceId": "SS-001"
    }
  ],
  "stockServices": [
    {
      "stockServiceId": "SS-001",
      "from": "01/09/2026",
      "to": "15/09/2026",
      "baseFrom": "2026-09-01",
      "baseTo": "2026-09-15"
    }
  ]
}
```

### 2.2 `salesOrderLines` — Page 80124 "B2B Sales Order Lines API"

- **URL directa:** `.../companies({companyId})/salesOrderLines` (también accesible como
  entidad contenida del deep insert de `salesOrders`).
- **EntityName / EntitySetName:** `salesOrderLine` / `salesOrderLines`
- **SourceTable:** `B2B Sales Order Line` (Tab80118). PK: `Order ID` + `Line ID`.
- **Métodos:** GET/POST/PATCH/DELETE (sin restricciones declaradas); uso normal: dentro del
  deep insert de la cabecera.

| Nombre OData | Tipo | Campo BC (Tab80118) | Oblig. | Notas |
|---|---|---|---|---|
| `orderId` | Guid | `Order ID` (PK1, TableRelation a cabecera) | **Sí** | En deep insert lo rellena BC desde la cabecera. |
| `lineId` | Guid | `Line ID` (PK2) | **Sí** | Id de línea del portal. Se copia a `Sales Line."B2B Id"`. |
| `productId` | Guid | `Product ID` | **Sí** (funcional) | **SystemId de `Item Variant`** en BC (variante). El procesado hace `ItemVariant.GetBySystemId` → `Variant Code`. |
| `modelId` | Guid | `Model ID` | **Sí** (funcional) | **SystemId de `Item`** en BC (modelo/artículo). `Item.GetBySystemId` → `No.`. |
| `sku` | String(50) | `SKU` | Opcional | Se copia a `Sales Line."B2B SKU"`. |
| `qty` | Decimal | `Qty` | **Sí** | `Validate(Quantity)`. |
| `name` | String(250) | `Name` | Opcional | Descriptivo (el caption AL dice 'Qty' por errata; el nombre OData es `name`). No se traslada a la línea de venta. |
| `unitPrice` | Decimal | `Unit Price` | **Sí** (funcional) | ¡Atención al mapeo!: en el pedido real se valida como **`Line Amount`** (importe de línea con descuento aplicado). |
| `originalUnitPrice` | Decimal | `Original Unit Price` | **Sí** (funcional) | Se valida como **`Unit Price`** (precio unitario sin descuento) de la Sales Line. |
| `amount` | Decimal | `Amount` | Opcional | Informativo. |
| `discountAmount` | Decimal | `Discount Amount` | Opcional | Se valida como `Line Discount Amount`. |
| `stockServiceId` | String(20) | `Stock Service ID` | Opcional* | Código de ventana de servicio (`B2B Service Window`). Determina tipo de documento, almacén (`Location Code`) y fecha de envío. *Necesario para la lógica Blanket/Order. |
| `fromDate` | Date | `From Date` | Opcional | El procesado lo sobreescribe con el `from` parseado del stockService (pedidos SCHEDULED). |
| `toDate` | Date | `To Date` | Opcional | Ídem con `to`. |

No expuestos: `BCOrder`, `BCOrder Line` (trazabilidad hacia la Sales Line creada) y los
FlowFields de lookup (`Model ID Code BC`, `Product ID Code BC`, etc.).

### 2.3 `stockServices` — Page 80139 "B2B SO Stock Service API"

- **URL directa:** `.../companies({companyId})/stockServices` (uso normal: deep insert).
- **EntityName / EntitySetName:** `stockService` / `stockServices`
- **SourceTable:** `B2B Sales Order Stock Service` (Tab80126). PK: `Order ID` + `Stock Service ID`.

| Nombre OData | Tipo | Campo BC (Tab80126) | Oblig. | Notas |
|---|---|---|---|---|
| `orderId` | Guid | `Order ID` (PK1) | **Sí** | Rellenado por BC en deep insert. |
| `stockServiceId` | String(20) | `Stock Service ID` (PK2) | **Sí** | Debe coincidir con el `stockServiceId` de las líneas y con un código de `B2B Service Window`. |
| `from` | **String(20)** | `From Date` (Text[20]) | Opcional | ¡Es TEXTO, no Date! Se parsea con `Evaluate(Date, ...)` (formato regional del servicio; si no parsea → 0D y se usa `baseFrom`). |
| `to` | **String(20)** | `To Date` (Text[20]) | Opcional | Ídem. |
| `baseFrom` | Date | `Base From Date` | Opcional | Fallback de fecha de envío si `from` no parsea. Formato OData `yyyy-MM-dd`. |
| `baseTo` | Date | `Base To Date` | Opcional | — |

### 2.4 Qué ocurre después en BC (flujo de pedidos)

1. **Ingesta**: el POST del B2B solo escribe en las tablas staging (80117/80118/80126).
   El OnInsert de la cabecera fija `Created DateTime`. Ninguna otra validación se ejecuta
   en el momento del POST salvo el control de duplicado por `orderId`.
2. **Procesado asíncrono**: un Job Queue (Codeunit 80152 "B2B Order Process Job",
   categoría `B2BINT`, **cada 5 minutos**, 3 reintentos) recorre las cabeceras con
   `BCOrder = ''` y llama a **Codeunit 80119 "B2B Create Orders"** dentro de `Run()`
   condicional: si falla, guarda `GetLastErrorText` en `ErrorText` de la staging y sigue.
   Existen equivalentes manuales: Report 80106 "B2B Post Orders" (mismo filtro y lógica) y
   Codeunit 80155 que encadena clientes + direcciones + pedidos.
3. **Codeunit 80119 — conversión a documento real**:
   - **Decisión Blanket vs Order** (`GetDocumentType`): toma la primera línea del pedido,
     busca su `Stock Service ID` en `B2B Service Window`; si la ventana es
     `SCHEDULED` **y** su `B2B Status = Open` → crea **Blanket Sales Order**; en cualquier
     otro caso → **Sales Order** normal.
   - Cabecera: `B2B Id` = orderId; cliente por `Customer.GetBySystemId(customerId)`
     (**error si no existe**); dirección de envío copiada campo a campo desde la
     `Ship-to Address` resuelta por SystemId (si existe); `Your Reference` = reference;
     formas/términos de pago desde `B2B Payment Method` (clave = paymentMethodId en
     mayúsculas); `B2B Tipo Servicio` desde incotermId; `Salesperson Code` desde saleId
     (SystemId de Salesperson) si llega y resuelve.
   - Líneas (cada 10000): tipo Item; `No.` desde Item(modelId), `Variant Code` desde
     ItemVariant(productId) — **error si no existen**; `B2B Id` = lineId; cantidad = qty;
     `Unit Price` = originalUnitPrice; `Line Amount` = unitPrice;
     `Line Discount Amount` = discountAmount; `B2B Service Window Id` y `Location Code`
     desde la ventana.
   - **Fecha de envío**: ventana `REPLENISHMENT` → `Today + CountryRegion."B2B Warehouse
     Prep. Days"` (país del cliente); ventana `SCHEDULED` → `from` parseado del
     stockService, o `baseFrom` como fallback (y persiste From/To Date en la línea staging).
   - Línea de transporte si `totalTransport > 0` (artículo `B2B Integration Setup."Send Product"`).
   - Marca trazabilidad: `BCOrder`/`BCOrder Line` en staging; fija el campo externo 80228
     (tipo logístico: 1 = Initial/SCHEDULED, 2 = REPLENISHMENT) si la otra extensión está
     instalada.
   - Si el documento es **Order** (REPLENISHMENT), intenta **Release** del pedido
     (`TryFunction`; si el release falla el pedido queda en Abierto, sin error).
4. **Blanket → Order** (Codeunit 80153, suscriptores de `Blanket Sales Order to Order`):
   al convertir el blanket, el pedido creado hereda el `B2B Sync Id` (SystemId del blanket)
   en cabecera y por línea, se marca `B2B Needs Sync`, se desvinculan las líneas del
   blanket y **el blanket origen se elimina**. Así el portal trata el pedido como
   continuación del mismo documento y no lo duplica.
5. **Errores**: nunca se devuelven al B2B en el POST; quedan en `ErrorText` de la staging
   (visibles en las pages 80129/80133). El B2B puede consultarlos vía GET de `salesOrders`
   solo si se añadieran esos campos (hoy **no** están expuestos) — el contrato actual es
   *fire-and-forget* con control de duplicados.

---

## 3. Clientes: `customers` + `shipToAddresss` + `contacts` + `secondaryEmails`

### 3.1 `customers` — Page 80115 "B2B API Customers"

- **URL:** `.../companies({companyId})/customers`
  (namespace propio `mitoprojects/b2b/v1.0`; no confundir con la API estándar `customers` de `/api/v2.0`).
- **EntityName / EntitySetName:** `customer` / `customers`
- **SourceTable:** `B2B Customers` (Tab80122), staging. PK: `B2BSystemsId`.
- **Métodos:** GET, POST (deep insert con direcciones/contactos/emails), PATCH, DELETE.

| Nombre OData | Tipo | Campo BC (Tab80122) | Oblig. | Notas |
|---|---|---|---|---|
| `b2BSystemsId` | Guid | `B2BSystemsId` (PK) | **Sí** | Id del cliente en el portal B2B. (Nombre de control AL `B2BSystemsId`; verificar casing exacto en `$metadata` — BC lo genera con inicial minúscula.) |
| `systemId` | Guid | `SystemId` (sistema) | Auto | SystemId del registro staging. **Es la clave que enlaza los subparts** (`SubPageLink: B2BSystemsId = field(SystemId)`) y el SystemId que acabará teniendo el Customer real (ver flujo). |
| `name` | String(100) | `Name` | **Sí** | Nombre comercial (se valida en `Customer."Name 2"`). |
| `eMail` | String(100) | `EMail` | Opcional | → `Customer."E-Mail"` (validación de formato email de BC). |
| `homePage` | String(100) | `HomePage` | Opcional | → `Home Page`. |
| `name2` | String(100) | `Name2` | **Sí** (funcional) | Razón fiscal: se valida en `Customer.Name` (¡Name y Name2 van cruzados!). |
| `vatRegistrationNo` | String(20) | `VatRegistrationNo` | Opcional | → `VAT Registration No.` con validación de formato **según el país** (el país se asigna antes precisamente para eso). |
| `searchName` | String(100) | `SearchName` | Opcional | → `Search Name`. |
| `address` | String(100) | `Address` | Opcional | Se concatena con `numeroDireccionFiscal` en `Customer.Address`. |
| `numeroDireccionFiscal` | String(30) | `NumeroDireccionfiscal` | Opcional | Número de la dirección fiscal. |
| `countryRegionCode` | String(20) | `CountryRegionCode` | **Sí** | Debe existir en `Country/Region` y tener `B2B Template Customers` configurada; si no, el procesado falla. |
| `county` | String(50) | `County` | Opcional | — |
| `city` | String(50) | `City` | Opcional | — |
| `postCode` | String(20) | `PostCode` | Opcional | — |
| `phone` | String(30) | `Phone` | Opcional | → `Phone No.` (validación estándar de teléfono). |
| `salesperson` | Guid | `SalespersonId` | Opcional | SystemId del Salesperson/Purchaser de BC; si llega, fija `Salesperson Code`. (Nombre de control AL `Salesperson`.) |
| `ordersMail` | String(100) | `OrdersMail` | Opcional | → `B2B Orders Mail`. |
| `invoicesMail` | String(100) | `InvoicesMail` | Opcional | → `B2B Invoices Mail`. |
| `otherMail` | String(100) | `OtherMail` | Opcional | → `B2B Other Mail`. |
| `createPreClient` | Boolean | `CreatePreClient` | Opcional | Flag de flujo pre-cliente. |
| `market` | String(100) | `Market` | Opcional | Informativo. |
| `payMethod` | String(50) | `PayMethod` | Opcional | Código buscado (mayúsculas) en `B2B Payment Method` → Payment Method/Terms del cliente. |
| `groupId` | String(50) | `GroupId` | Opcional | Informativo. |
| `clientTypeId` | String(50) | `ClientTypeId` | Opcional | Informativo. |
| `taxId` | String(50) | `TaxId` | Opcional | Se busca en `VAT Business Posting Group."B2B Tax Id Code"` → `VAT Bus. Posting Group` del cliente. |
| `rateId` | String(50) | `RateId` | Opcional | Informativo. |
| `productSegments` | **String(20)** | `SegmentId` | Opcional | Segmento del cliente. **Debe llegar como string escalar** (`"A+"`, `"A"`, `"B"`, `"C"`, `"D"`), NO como array (OData no admite arrays sobre el campo). Mapeo case-insensitive al enum `B2B Customer Segment`; valor no reconocido → `" "`. |
| `preClientCreatedId` | Guid | `PreClientCreatedId` | Opcional | Si llega (≠ GUID nulo), el Customer real de BC se crea **con ese SystemId** en lugar del SystemId del staging. |
| `shipToAddress` | Colección | part → Page 80107 | Opcional | Deep insert de direcciones de envío. |
| `contact` | Colección | part → Page 80108 | Opcional | Deep insert de contactos. |
| `secondaryEmails` | Colección | part → Page 80140 | Opcional | Deep insert de emails secundarios. |

No expuestos: `CustomerNo` (nº del cliente BC creado), `SalespersonCode` (FlowField),
`ErrorText`.

### 3.2 `shipToAddresss` — Page 80107 "B2B Ship-to Address API"

- **URL:** `.../companies({companyId})/shipToAddresss` — ojo al EntitySetName con
  **triple "s"** (`shipToAddresss`), tal cual está declarado.
- **SourceTable:** `B2B Ship-to Address` (Tab80123). PK: `No` (autoincremental).

| Nombre OData | Tipo | Campo BC | Oblig. | Notas |
|---|---|---|---|---|
| `clientId` | Guid | `B2BSystemsId` | **Sí** | En deep insert BC lo rellena con el `SystemId` del customer staging padre. |
| `shippingAddressId` | Guid | `ShippingAddressId` | Recomendado | **`OnInsertRecord`: si no es GUID nulo, se fuerza `SystemId := ShippingAddressId`.** Así el GUID que el portal usa como id de dirección coincide con el SystemId del registro (y es el que luego se manda en `salesOrders.shippingAddressId` una vez creada la Ship-to Address real). |
| `addressShip` | String(100) | `AddressShip` | **Sí** (funcional) | → `Ship-to Address.Address`. |
| `numeroDireccionShip` | String(30) | `NumeroDireccionShip` | Opcional | → `B2B Street Number`. |
| `countryRegionCodeShip` | String(20) | `CountryRegionCodeShip` | **Sí** (funcional) | → `Country/Region Code` (validado). |
| `countyShip` | String(50) | `CountyShip` | Opcional | — |
| `cityShip` | String(50) | `CityShip` | Opcional | — |
| `postCodeShip` | String(20) | `PostCodeShip` | Opcional | — |
| `contactNameShip` | String(100) | `ContactNameShip` | Recomendado | Alias/nombre de la dirección: se valida como `Name` de la Ship-to (recortado a 30 chars por límite del operador logístico); si vacío, fallback al nombre del cliente. También forma parte de `Contact`. |
| `contactLastNameShip` | String(100) | `ContactLastNameShip` | Opcional | Concatenado en `Contact`. |
| `contactPhoneShip` | String(100) | `ContactPhoneShip` | Opcional | → `Phone No.`. |

No expuestos: `No` (PK autoincrement), `BCCustomerNo`, `ErrorMessage`.

### 3.3 `contacts` — Page 80108 "B2B Contacts API"

- **URL:** `.../companies({companyId})/contacts`
- **SourceTable:** `B2B Contacts` (Tab80120). PK: `No` (autoincremental).

| Nombre OData | Tipo | Campo BC | Oblig. | Notas |
|---|---|---|---|---|
| `b2BSystemsId` | Guid | `B2BSystemsId` | **Sí** | Rellenado por BC en deep insert (SystemId del customer staging). |
| `name` | String(100) | `Name` | **Sí** (funcional) | Nombre. |
| `lastName` | String(100) | `Last Name` | Opcional | Apellidos (se concatena con name en el Contact de BC). |
| `company` | String(100) | `Company` | Opcional | Informativo. |
| `phone` | String(30) | `Phone` | Opcional | — |
| `eMail` | String(80) | `EMail` | Opcional | — |

(`MobilePhone` está comentado en tabla y page: no existe en el contrato.) No expuesto: `ErrorText`.

### 3.4 `secondaryEmails` — Page 80140 "B2B Secondary Email API"

- **URL:** `.../companies({companyId})/secondaryEmails`
- **SourceTable:** `B2B Secondary Email` (Tab80127). PK: `No` (autoincremental).

| Nombre OData | Tipo | Campo BC | Oblig. | Notas |
|---|---|---|---|---|
| `b2BSystemsId` | Guid | `B2BSystemsId` | **Sí** | Enlace al customer staging (deep insert). |
| `email` | String(100) | `Email` | **Sí** (funcional) | — |
| `type` | String(50) | `Type` | Opcional | Tipo libre (texto). |
| `emailName` | String(100) | `EmailName` | Opcional | — |

### 3.5 Qué ocurre después en BC (flujo de clientes)

1. **Ingesta**: el POST (deep insert recomendado: customer + shipToAddress + contact +
   secondaryEmails en una sola llamada) escribe solo en staging. Sin validaciones de
   negocio en el POST.
2. **Procesado**: Codeunit 80155 "B2B Customer & Address Job" (Job Queue `B2BINT`) recorre
   `B2B Customers` con `CustomerNo = ''` y ejecuta **Codeunit 80136 "B2B Create Customers"**
   (también manual vía Report 80105 "B2B Post Customers", que procesa TODOS los staging,
   con upsert). Errores → `ErrorText` del staging.
3. **Codeunit 80136**:
   - Si ya existe `Customer` con SystemId = SystemId del staging → **update** de datos.
   - Si no: crea el cliente desde la **plantilla por país**
     (`CountryRegion."B2B Template Customers"` → `Customer Templ.`; error si el país o la
     plantilla no existen). El SystemId del Customer se fuerza a `PreClientCreatedId` (si
     llegó) o al SystemId del staging → **el GUID que maneja el portal es directamente el
     SystemId del cliente real**.
   - Datos: `Name` ← name2, `Name 2` ← name (cruzados); país asignado **antes** de validar
     el NIF (para que la validación de formato use el país correcto); dirección =
     address + " " + numeroDireccionFiscal; emails B2B; segmento mapeado; `Sync to B2B` y
     `B2B Create User` a true; vendedor por SystemId; método/términos de pago desde
     `B2B Payment Method`; grupo IVA desde `TaxId`; campos AEP copiados de la plantilla
     (campos externos 80100-80105 → 50000-50004/80344 si la otra extensión existe);
     dimensiones por defecto desde `B2B Default Dimension`.
   - **Ship-to Addresses**: por cada staging del cliente, upsert de `Ship-to Address` con
     `Code = 'SHIP' + No` (autoincremental del staging); nombre = contactNameShip
     (30 chars) o nombre del cliente; `Sync to B2B` = true; marca `BCCustomerNo` en staging.
   - **Contactos**: actualiza el Contact de empresa vinculado (Contact Business Relation)
     con nombre+apellidos, teléfono y email del primer contacto staging.

---

## 4. Documentos PDF: `salesDocuments` — Page 80106 "B2B Document PDF API"

- **URL:** `.../companies({companyId})/salesDocuments`
- **EntityName / EntitySetName:** `salesDocument` / `salesDocuments`
- **SourceTable:** `B2B Document PDF` (Tab80105, **temporal**). `ODataKeyFields = SystemId`.
- **Métodos:** **solo GET** (`InsertAllowed/ModifyAllowed/DeleteAllowed = false`, `Editable = false`).
- Es una API "funcional": el GET genera el PDF bajo demanda y devuelve una URL pública.

**Formas de llamada (obligatorio filtrar; sin filtro → error):**

1. Por SystemId del documento BC (cabecera de pedido, albarán, factura, abono, devolución):
   ```
   GET .../salesDocuments?$filter=systemId eq {guid}
   ```
   El trigger `OnOpenPage` resuelve el GUID probando, en orden: `Sales Header` (Order →
   "Sales Order"; Return Order), `Sales Shipment Header`, `Sales Invoice Header`,
   `Return Receipt Header`, `Sales Cr.Memo Header`.
2. Por tipo + número:
   ```
   GET .../salesDocuments?$filter=documentType eq 'Sales Invoice' and documentNo eq 'FV-2400123'
   ```
   `documentType` es el enum `B2B Document Type`: valores usados en código
   `Sales Order`, `Return Order`, `Sales Shipment`, `Sales Invoice`, `Return Receipt`,
   `Sales Cr.Memo` (más el valor vacío `" "`).

**Campos de respuesta:**

| Nombre OData | Tipo | Origen | Notas |
|---|---|---|---|
| `systemId` | Guid | SystemId del documento BC resuelto | Clave OData. |
| `documentType` | Enum | `B2B Document Type` | — |
| `documentNo` | String(20) | Nº del documento | — |
| `url` | String | Variable de página | **URL pública del PDF en Azure Blob Storage.** En `OnAfterGetRecord` se busca el report en `Report Selections` según el Usage (S.Order / S.Shipment / S.Invoice / S.Return / S.Ret.Rcpt. / S.Cr.Memo), se ejecuta `Report.SaveAs(...PDF...)` y se sube con `B2B Blob Storage Manager.UploadPdfToPublicAzureBlob` a la carpeta configurada en `B2B Integration Setup` (Sales Orders Folder / Sales Shipment Folder / Sales Invoices Folder). |

**Errores posibles:** `Tipo Documento no definido o no encontrado ...`, `Documento no
definido`, `No hay report configurado para el documento {no}, de tipo {tipo}`.

**Advertencias de contrato:**
- Cada GET **regenera y resube** el PDF (coste/latencia por llamada). No cachea.
- La página hace `DeleteAll + Insert` sobre la tabla temporal en `OnOpenPage`: está
  diseñada para devolver **un solo documento por llamada**; no soporta consultas de
  colección sin filtro.

---

## 5. `customerTests` — Page 80132 "B2B Customers Test" (legacy / diagnóstico)

- **URL:** `.../companies({companyId})/customerTests` — EntityName `customerTest`.
- **SourceTable:** `Customer` (tabla real, no staging). Expone solo `systemId`, `name`, `no`.
- **Estado: página de prueba/diagnóstico.** Sirve para verificar conectividad y para
  resolver SystemId ↔ Nº de cliente. **No forma parte del flujo funcional** del B2B y no
  debería usarse en el nuevo backend (usar las APIs estándar de BC si se necesita leer
  clientes reales). Nota: al tener `DelayedInsert = true` y no bloquear inserts,
  técnicamente admite POST sobre la tabla Customer — no usar.

---

## 6. Tablas legacy Tab80106/Tab80107 ("B2B Order Header"/"B2B Order Line")

Las tablas `Tab80106.B2BOrderHeader.al` y `Tab80107.B2BOrderLine.al` son una **versión
anterior** del staging de pedidos (con campos como `Order Type`
Replenishment/Scheduled, `Processed`, `Original Payload`). Hoy **ninguna API page ni
codeunit de procesado las usa**: solo aparecen en el permission set y en la lista
`Pag80109.B2BOrdersList.al`. El contrato vigente de pedidos es el de las tablas
80117/80118/80126 documentado arriba. **El nuevo backend no debe integrarse contra ellas.**

---

## 7. Resumen de endpoints

| EntitySet | Page | Métodos útiles | Uso por el B2B |
|---|---|---|---|
| `salesOrders` | 80123 | POST (deep insert), GET | Enviar pedidos (cabecera + items + stockServices en una llamada). Duplicado por `orderId` → HTTP 400 con mensaje "El pedido ... ya existe". |
| `salesOrderLines` | 80124 | (vía deep insert) | Líneas de pedido. |
| `stockServices` | 80139 | (vía deep insert) | Ventanas de servicio (fechas) del pedido. |
| `customers` | 80115 | POST (deep insert), GET, PATCH | Alta/actualización de clientes del portal. |
| `shipToAddresss` | 80107 | (vía deep insert) | Direcciones de envío (nota: entity set con triple "s"). |
| `contacts` | 80108 | (vía deep insert) | Contactos del cliente. |
| `secondaryEmails` | 80140 | (vía deep insert) | Emails secundarios. |
| `salesDocuments` | 80106 | GET con `$filter` | Obtener URL pública del PDF de un documento de venta. |
| `customerTests` | 80132 | GET | Solo test/diagnóstico. No usar en producción. |

> **Recomendación para el nuevo backend:** verificar los nombres OData exactos contra el
> `$metadata` real del entorno (`GET .../api/mitoprojects/b2b/v1.0/$metadata`), porque BC
> deriva el nombre a partir del nombre de control AL (camelCase con inicial minúscula) y
> algunos controles están declarados con mayúscula inicial (`TotalDiscount`, `TotalCart`,
> `SystemId`, `B2BSystemsId`, `Salesperson`).
