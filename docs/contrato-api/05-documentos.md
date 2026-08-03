# 05 — Documentos: albaranes, facturas, abonos, devoluciones y PDFs

Contrato API que el nuevo backend B2B (.NET 8) debe implementar para que el conector BC
"MITO - Conector B2B" funcione sin cambios. Este bloque cubre los documentos registrados
(albaranes, facturas, abonos, albaranes de devolución), los pedidos de devolución y la
entrega de PDFs.

Ficheros AL de referencia (repo `c:\BC_Projects\Mito - Conector B2B`):

| Documento | Adapter (payload) | Codeunit legacy |
|---|---|---|
| Albarán de venta | `src\codeunits\adapters\Cod80157.B2BShipmentAdapter.al` | `src\codeunits\Cod80120.B2BShipmentSync.al` |
| Factura de venta | `src\codeunits\adapters\Cod80156.B2BInvoiceAdapter.al` | `src\codeunits\Cod80121.B2BInvoiceSync.al` |
| Abono de venta | `src\codeunits\adapters\Cod80160.B2BCreditMemoAdapter.al` | — |
| Pedido de devolución | `src\codeunits\adapters\Cod80158.B2BReturnOrderAdapter.al` | — |
| Albarán de devolución | `src\codeunits\adapters\Cod80159.B2BReturnReceiptAdapter.al` | — |
| PDFs | `src\pages\api\Pag80106.B2BDocumentPDFAPI.al` + `src\tables\Tab80105.B2BDocumentPDF.al` | — |

Transporte HTTP: `src\codeunits\b2bManager\Cod80111.B2BApiManager.al` (método `Put`) y
`src\codeunits\b2bManager\Cod80143.B2BBaseApiManager.al` (token).

---

## 1. Mecánica común de envío (BC → B2B)

Todos los documentos de este bloque se envían con el mismo pipeline
(`B2B Api Orchestrator` → `B2B Api Manager.SyncEntity` → `Put`), ver
`Cod80113.B2BApiOrchestrator.al` y `Cod80111.B2BApiManager.al`:

- **Método HTTP: `PUT`** (siempre, también para altas — el B2B debe hacer upsert).
- **Headers**: `Content-Type: application/json` y `Authorization: Bearer {token}`.
  El token sale de `B2B Integration Setup."Active Token"` y se refresca si caducó
  (`Cod80143.B2BBaseApiManager.al`, `GetToken`). Timeout del cliente: 10 s.
- **URL**: plantilla guardada en la tabla `B2B Integration Setup`
  (`src\tables\Tab80100.B2BIntegrationSetup.al`). El placeholder `{{$guid}}` (o `%1`)
  se sustituye por el **SystemId del documento en BC, sin llaves y en mayúsculas**
  (`B2BApiManager.GetEndpointURL` + `StringifyGuid`). Es decir, la clave del recurso
  en el B2B es el SystemId de la cabecera BC:

| Campo de setup | Usado por | Ruta con placeholder (forma) |
|---|---|---|
| `Delivery Notes URL` (field 150) | Albarán de venta **y** albarán de devolución | `PUT {base}/delivery-notes/{documentSystemId}` |
| `Invoices URL` (field 160) | Factura **y** abono | `PUT {base}/invoices/{documentSystemId}` |
| `Orders URL` (field 29) | Pedido de devolución (y pedidos) | `PUT {base}/orders/{documentSystemId}` |

  (Las rutas concretas son configuración; el conector solo exige que la URL admita el
  placeholder del GUID. El tooltip del setup lo confirma: *"URL for PUT/sync of individual
  orders (include {0} or {{$guid}} placeholder for the order ID)"*,
  `src\pages\Pag80100.B2BIntegrationSetup.al`.)

- **Respuesta esperada**: cualquier `2xx`. Si hay cuerpo, debe ser JSON válido (objeto o
  array); si no parsea, el conector lo registra como error. No se procesa ningún campo de
  la respuesta para estos documentos.

### Evento de BC que dispara cada envío

**Importante**: el registro (posting) NO dispara envíos automáticos. El subscriber
`src\codeunits\subscribers\Cod80141.B2BPostingEvents.al` solo maneja eventos de compras
(reposición), y `Cod80148.B2BPricingEventSubs.al` solo propaga campos de pricing a las
cabeceras registradas. El envío de documentos es **manual/bajo demanda** mediante el
report `80110 "B2B Sync Documents Entity"` (`src\reports\Rep80110.B2BSyncDocumentEntities.al`),
lanzado desde acciones "Sync to B2B" en las páginas:

| Página BC (acción "Sync to B2B") | Page extension | Documento enviado |
|---|---|---|
| Posted Sales Shipment | `PagExt80113.SalesShipmentExt.al` | Albarán |
| Posted Sales Invoice | `PagExt80114.SalesInvoiceExt.al` | Factura |
| Posted Sales Credit Memo | `Pag-Ext80126.SalesCreditMemoExt.al` | Abono |
| Posted Return Receipt | `Pag-Ext80122.PostedReturnReceiptEXT.al` | Albarán de devolución |
| Sales Return Order | `Pag-Ext80127.SalesReturnOrderEXT.al` | Pedido de devolución |
| Role Center (masivo, con filtros) | `Pag-Ext80111.B2BSOrderProcessorRoleCenter.al` | Todos |

### Convenciones de formato comunes

- **GUIDs**: SystemId de BC sin llaves, en mayúsculas (p.ej. `5E9C36DA-BB3E-ED11-9DB4-000D3A2FEEBD`).
- **Fechas de documentos** (`deliveryDate`, `issueDate`, `dueDate`, `emittedAt`):
  `YYYY-MM-DDT00:00:00.000Z` (la fecha BC a medianoche; si la fecha está vacía se usa hoy).
  Excepción: en el pedido de devolución, `orderedDate` y `shipDate` van **sin** sufijo
  `.000Z` (`B2B Utils.FormatDate`): `YYYY-MM-DDT00:00:00`.
- **Objeto moneda** (`Money`): `{ "code": "EUR", "value": 12.34 }`. `code` = divisa local
  de la empresa (`General Ledger Setup."LCY Code"`, fallback `EUR`) — `B2B Utils.GetCurrencyCode`
  en `src\codeunits\Cod80122.B2BUtils.al`.
- **Texto traducido de 6 idiomas** (`TranslatedText6`): objeto con claves
  `es_ES, en_EN, fr_FR, it_IT, pt_PT, de_DE`, todas con el mismo valor. Usado en
  `productInfo.name`, `taxes[].name`, `payMethodName`, `image.description`.
- **Texto traducido de 4 idiomas** (`TranslatedText4`): claves `es_ES, en_EN, fr_FR, it_IT`,
  sembradas con el texto por defecto y sobreescritas con traducciones reales de la tabla
  `B2B Translation Entry` (`B2B Utils.GenerateLanguajeObject`). Usado solo en
  `lines[].productName` de factura/abono e `items[].productName` del pedido de devolución.
- **Impuestos de línea**: siempre un array `taxes` con exactamente 1 elemento, `id: "IVA"`,
  `productTaxId` = "VAT Prod. Posting Group" de BC.
- **countryIsoId**: `Country/Region."ISO Code"` (fallback: el propio código BC).
- **phones[].code**: prefijo telefónico del país (`Country/Region."B2B Phone Code"`).

---

## 2. PUT Albarán de venta

- **Método/Ruta**: `PUT {Delivery Notes URL}/{shipmentSystemId}` — SystemId de
  `Sales Shipment Header`.
- **Disparo**: acción manual "Sync to B2B" en Posted Sales Shipment (o report masivo).
- **Payload**: `Cod80157.B2BShipmentAdapter.al`, `BuildShipmentJson`.
- **Líneas incluidas**: solo `Type = Item` y `Quantity <> 0` (excluye la línea de
  transporte y evita el 404 `product-not-found` del portal).
- **Precios de línea**: si la línea de pedido origen aún existe (`Sales Line` del pedido),
  se toman de ella `Unit Price`, `Line Discount %`, `VAT %`, `VAT Prod. Posting Group`;
  si no, de la propia línea de albarán (con `VAT % = 0`). Importes redondeados a 0.01:
  `lineAmount = qty*price*(1-dto/100)`, `amtInclVAT = lineAmount*(1+iva/100)`.
- **Totales de cabecera**: suma de los importes calculados de las líneas (no flowfields).

### Ejemplo JSON completo

```json
{
  "clientId": "5E9C36DA-BB3E-ED11-9DB4-000D3A2FEEBD",
  "shippingAddress": {
    "streetAddress": "Calle Mayor 1",
    "num": "12",
    "description": "Planta 2",
    "city": "Madrid",
    "province": "Madrid",
    "zipCode": "28001",
    "countryIsoId": "ES",
    "geo": { "latitude": 0, "longitude": 0 },
    "contact": {
      "name": "Ana Pérez",
      "lastName": "",
      "company": "Tienda Central S.L.",
      "phones": [ { "code": "+34", "number": "600123456" } ]
    }
  },
  "number": "AV-2400123",
  "transportId": "SEUR",
  "transportUrlTrack": "https://www.seur.com/livetracking/?segOnlineIdentificador=1234567890",
  "paymethodId": "transferencia",
  "deliveryDate": "2026-07-15T00:00:00.000Z",
  "isInvoiced": false,
  "observations": "",
  "documentUrl": "",
  "totals": {
    "totalAmount":   { "code": "EUR", "value": 100.00 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 21.00 },
    "total":         { "code": "EUR", "value": 121.00 }
  },
  "transportTotals": {
    "totalAmount":   { "code": "EUR", "value": 0 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 0 },
    "total":         { "code": "EUR", "value": 0 }
  },
  "type": "SCHEDULED",
  "lines": [
    {
      "id": "A1B2C3D4-0000-0000-0000-000000000001",
      "productId": "B7C8D9E0-0000-0000-0000-000000000002",
      "orderItemId": "C3D4E5F6-0000-0000-0000-000000000003",
      "externalReference": "ART001",
      "productInfo": {
        "name": {
          "es_ES": "Zapato piel 40", "en_EN": "Zapato piel 40", "fr_FR": "Zapato piel 40",
          "it_IT": "Zapato piel 40", "pt_PT": "Zapato piel 40", "de_DE": "Zapato piel 40"
        },
        "brandId": "",
        "ean": "8412345678905",
        "externalReference": "ART001",
        "id": "B7C8D9E0-0000-0000-0000-000000000002",
        "image": {
          "uri": "https://cdn.miempresa.com/img/ART001.jpg",
          "description": {
            "es_ES": "Zapato piel 40", "en_EN": "Zapato piel 40", "fr_FR": "Zapato piel 40",
            "it_IT": "Zapato piel 40", "pt_PT": "Zapato piel 40", "de_DE": "Zapato piel 40"
          },
          "order": 0,
          "path": ""
        },
        "modelId": "D4E5F6A7-0000-0000-0000-000000000004",
        "modelExternalReference": "ART001",
        "sku": "ART00140"
      },
      "transactionInfo": {
        "info": {
          "quantity": 2,
          "discount": 0,
          "price":  { "code": "EUR", "value": 50.00 },
          "amount": { "code": "EUR", "value": 100.00 }
        },
        "totalDiscounts": { "code": "EUR", "value": 0 },
        "totalTaxes":     { "code": "EUR", "value": 21.00 },
        "taxes": [
          {
            "id": "IVA",
            "name": {
              "es_ES": "IVA", "en_EN": "IVA", "fr_FR": "IVA",
              "it_IT": "IVA", "pt_PT": "IVA", "de_DE": "IVA"
            },
            "percent": 21,
            "amount":      { "code": "EUR", "value": 21.00 },
            "taxableBase": { "code": "EUR", "value": 100.00 },
            "productTaxId": "IVA21"
          }
        ],
        "discounts": [],
        "priceOriginal": { "code": "EUR", "value": 50.00 },
        "offerDiscounts": []
      }
    }
  ],
  "idProvider": "VEND01",
  "status": "delivered",
  "clientName": "Tienda Central S.L."
}
```

### Campos (cabecera)

Todos los campos se emiten **siempre** (el adapter añade todas las claves; los "opcionales"
van como `""`, `[]` o `null`).

| Campo JSON | Tipo | Origen en BC | Notas |
|---|---|---|---|
| `clientId` | string (GUID) | `Customer.SystemId` del "Sell-to Customer No." | Siempre |
| `shippingAddress` | objeto | Ver detalle abajo | Siempre |
| `number` | string | `Sales Shipment Header."No."` | Siempre |
| `transportId` | string | `"Shipping Agent Code"` | `""` si no hay transportista |
| `transportUrlTrack` | string | Ver **Tracking** abajo | `""` si no calculable |
| `paymethodId` | string | `LowerCase("Payment Method Code")` | En minúsculas |
| `deliveryDate` | string fecha | `"Posting Date"` | `YYYY-MM-DDT00:00:00.000Z` |
| `isInvoiced` | bool | Existe `Sales Invoice Line` con ese `Shipment No.` | |
| `observations` | string | Constante `""` | |
| `documentUrl` | string | Constante `""` | El PDF se obtiene vía API OData (§7) |
| `totals` | objeto Totals | Suma de líneas calculadas | 4 Money: totalAmount (base), totalDiscount (0), totalTax, total |
| `transportTotals` | objeto Totals | Constante todo 0 | |
| `type` | string | Constante `"SCHEDULED"` | |
| `lines` | array | `Sales Shipment Line` (Item, qty<>0) | Ver campos de línea |
| `idProvider` | string | `"Salesperson Code"` | |
| `status` | string | Constante `"delivered"` | |
| `clientName` | string | `"Sell-to Customer Name"` | |

`shippingAddress`: si el albarán tiene `Ship-to Code` y existe el registro `Ship-to Address`,
los datos salen de ese registro (`num` = campo custom `"B2B Street Number"`, teléfono con
prefijo país); si no, de los campos Ship-to de la cabecera (`num` = `""`, `description` =
"Ship-to Address 2", `phones` vacío). Estructura: `streetAddress, num, description, city,
province, zipCode, countryIsoId, geo{latitude:0, longitude:0}, contact{name, lastName:"",
company, phones[{code, number}]}`.

### Campos (línea)

| Campo JSON | Tipo | Origen en BC |
|---|---|---|
| `id` | string (GUID) | `Sales Shipment Line.SystemId` |
| `productId` | string (GUID) | `Item Variant.SystemId` si hay variante; si no `Item.SystemId` |
| `orderItemId` | string (GUID) | SystemId de la `Sales Line` del pedido origen; fallback `Sales Line Archive`; `""` si no existe |
| `externalReference` | string | `Sales Shipment Line."No."` (nº de artículo) |
| `productInfo.name` | TranslatedText6 | `Item.Description` + descripción (o código) de la variante |
| `productInfo.brandId` | string | Constante `""` |
| `productInfo.ean` | string | `Item Reference` tipo "Bar Code" del artículo+variante; `""` si no hay |
| `productInfo.externalReference` | string | Nº de artículo |
| `productInfo.id` | string (GUID) | SystemId de variante o artículo (`""` si el artículo no existe) |
| `productInfo.image` | objeto o null | `uri` = plantilla `setup."Image Url"` con el nº de artículo; `description` = name; `order`:0; `path`:"". `null` si el artículo no existe |
| `productInfo.modelId` | string (GUID) | `Item.SystemId` (`""` si no existe) |
| `productInfo.modelExternalReference` | string | `Item."No."` |
| `productInfo.sku` | string | Nº artículo + código variante concatenados |
| `transactionInfo.info.quantity` | number | `Quantity` (positiva) |
| `transactionInfo.info.discount` | number | `Line Discount %` |
| `transactionInfo.info.price` | Money | Precio unitario (sin dto, sin IVA) |
| `transactionInfo.info.amount` | Money | Importe línea sin IVA (con dto aplicado) |
| `transactionInfo.totalDiscounts` | Money | Importe del descuento de línea |
| `transactionInfo.totalTaxes` | Money | IVA de la línea |
| `transactionInfo.taxes[0]` | objeto Tax | `{id:"IVA", name:TranslatedText6("IVA"), percent, amount, taxableBase, productTaxId}` |
| `transactionInfo.discounts` | array | Siempre `[]` |
| `transactionInfo.priceOriginal` | Money | = precio unitario |
| `transactionInfo.offerDiscounts` | array | Siempre `[]` |

### Tracking (`transportUrlTrack`)

`Cod80157.B2BShipmentAdapter.al`, `GetTrackingUrl`: replica el botón estándar
"Seguimiento paquete" de BC. Toma la `"Internet Address"` (URL de seguimiento de paquete)
del `Shipping Agent` del albarán y sustituye su `%1` por
`Sales Shipment Header."Package Tracking No."`. Devuelve `""` si falta transportista,
nº de seguimiento o URL. Solo el albarán de venta lo rellena; en el albarán de devolución
va siempre `""`.

---

## 3. PUT Albarán de devolución (Return Receipt)

- **Método/Ruta**: `PUT {Delivery Notes URL}/{returnReceiptSystemId}` — **mismo endpoint
  que los albaranes**; SystemId de `Return Receipt Header`. El backend distingue el tipo
  por el contenido (importes negativos, `status`/`type`).
- **Disparo**: acción manual "Sync to B2B" en Posted Return Receipt.
- **Payload**: `Cod80159.B2BReturnReceiptAdapter.al`. Misma estructura que el albarán (§2)
  con estas diferencias:

| Campo | Valor en devolución |
|---|---|
| `number` | `Return Receipt Header."No."` |
| `transportUrlTrack` | Siempre `""` |
| `deliveryDate` | `"Posting Date"` |
| `isInvoiced` | Existe `Sales Cr.Memo Line` con ese `Return Receipt No.` (es decir, "abonado") |
| `type` | `"NOT_DEFINED"` (en vez de `"SCHEDULED"`) |
| `status` | `"received"` (en vez de `"delivered"`) |
| `lines[].orderItemId` | SystemId de la línea del **pedido de devolución** (`Sales Line` tipo Return Order; fallback `Sales Line Archive`) |
| Signo | **-1**: `quantity`, `amount`, `totalDiscounts`, `totalTaxes`, `taxes[].amount`, `taxes[].taxableBase` y los 4 totales de cabecera van **negativos**. `price`/`priceOriginal` quedan positivos |
| Filtro de líneas | `Type = Item` (sin filtro de cantidad <> 0) |
| Precios de línea | De la `Sales Line` del pedido de devolución si existe; fallback a la propia `Return Receipt Line` (que sí conserva `VAT %`) |

Ejemplo (solo lo que cambia respecto a §2):

```json
{
  "number": "RC-2400045",
  "transportUrlTrack": "",
  "isInvoiced": false,
  "type": "NOT_DEFINED",
  "status": "received",
  "totals": {
    "totalAmount":   { "code": "EUR", "value": -100.00 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": -21.00 },
    "total":         { "code": "EUR", "value": -121.00 }
  },
  "lines": [ { "transactionInfo": { "info": { "quantity": -2, "price": { "code": "EUR", "value": 50.00 }, "amount": { "code": "EUR", "value": -100.00 } } } } ]
}
```

---

## 4. PUT Factura de venta

- **Método/Ruta**: `PUT {Invoices URL}/{invoiceSystemId}` — SystemId de
  `Sales Invoice Header`.
- **Disparo**: acción manual "Sync to B2B" en Posted Sales Invoice.
- **Payload**: `Cod80156.B2BInvoiceAdapter.al`, `BuildInvoiceJson`. Líneas: `Type = Item`.
- **Totales de cabecera**: flowfields `Amount` / `"Amount Including VAT"` de la cabecera.

### Ejemplo JSON completo

```json
{
  "clientId": "5E9C36DA-BB3E-ED11-9DB4-000D3A2FEEBD",
  "fiscalInfo": {
    "alias": "Tienda Central S.L.",
    "address": {
      "streetAddress": "Calle Mayor 1",
      "num": "12",
      "description": "Planta 2",
      "city": "Madrid",
      "province": "Madrid",
      "zipCode": "28001",
      "countryIsoId": "ES",
      "geo": { "latitude": 0, "longitude": 0 },
      "contact": {
        "name": "Ana Pérez",
        "lastName": "",
        "company": "Tienda Central S.L.",
        "phones": [ { "code": "+34", "number": "600123456" } ]
      }
    },
    "fiscalName": "Tienda Central S.L.",
    "fiscalId": { "type": "nif", "document": "B12345678" }
  },
  "number": "FV-2400089",
  "payMethodName": {
    "es_ES": "Transferencia bancaria", "en_EN": "Transferencia bancaria",
    "fr_FR": "Transferencia bancaria", "it_IT": "Transferencia bancaria",
    "pt_PT": "Transferencia bancaria", "de_DE": "Transferencia bancaria"
  },
  "issueDate": "2026-07-31T00:00:00.000Z",
  "status": "Unpaid",
  "observations": "",
  "documentUrl": "",
  "totals": {
    "totalAmount":   { "code": "EUR", "value": 100.00 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 21.00 },
    "total":         { "code": "EUR", "value": 121.00 }
  },
  "transportTotals": {
    "totalAmount":   { "code": "EUR", "value": 0 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 0 },
    "total":         { "code": "EUR", "value": 0 }
  },
  "lines": [
    {
      "id": "E5F6A7B8-0000-0000-0000-000000000005",
      "productId": "B7C8D9E0-0000-0000-0000-000000000002",
      "productName": {
        "es_ES": "Zapato piel 40", "en_EN": "Leather shoe 40",
        "fr_FR": "Zapato piel 40", "it_IT": "Zapato piel 40"
      },
      "deliveryNoteLineId": "A1B2C3D4-0000-0000-0000-000000000001",
      "transactionInfo": {
        "info": {
          "quantity": 2,
          "discount": 0,
          "price":  { "code": "EUR", "value": 50.00 },
          "amount": { "code": "EUR", "value": 100.00 }
        },
        "totalDiscounts": { "code": "EUR", "value": 0 },
        "totalTaxes":     { "code": "EUR", "value": 21.00 },
        "taxes": [
          {
            "id": "IVA",
            "name": {
              "es_ES": "IVA", "en_EN": "IVA", "fr_FR": "IVA",
              "it_IT": "IVA", "pt_PT": "IVA", "de_DE": "IVA"
            },
            "percent": 21,
            "amount":      { "code": "EUR", "value": 21.00 },
            "taxableBase": { "code": "EUR", "value": 100.00 },
            "productTaxId": "IVA21"
          }
        ],
        "discounts": [],
        "priceOriginal": { "code": "EUR", "value": 50.00 },
        "offerDiscounts": []
      },
      "productInfo": {
        "name": {
          "es_ES": "Zapato piel 40", "en_EN": "Zapato piel 40", "fr_FR": "Zapato piel 40",
          "it_IT": "Zapato piel 40", "pt_PT": "Zapato piel 40", "de_DE": "Zapato piel 40"
        },
        "brandId": "",
        "ean": "8412345678905",
        "externalReference": "ART001",
        "id": "B7C8D9E0-0000-0000-0000-000000000002",
        "image": {
          "uri": "https://cdn.miempresa.com/img/ART001.jpg",
          "description": {
            "es_ES": "Zapato piel 40", "en_EN": "Zapato piel 40", "fr_FR": "Zapato piel 40",
            "it_IT": "Zapato piel 40", "pt_PT": "Zapato piel 40", "de_DE": "Zapato piel 40"
          },
          "order": 0,
          "path": ""
        },
        "modelId": "D4E5F6A7-0000-0000-0000-000000000004",
        "modelExternalReference": "ART001",
        "sku": "ART00140"
      }
    }
  ],
  "payments": [
    {
      "paymentInfo": "",
      "dueDate": "2026-08-30T00:00:00.000Z",
      "emittedAt": "2026-07-31T00:00:00.000Z",
      "amount": { "code": "EUR", "value": 121.00 }
    }
  ]
}
```

### Campos (cabecera)

| Campo JSON | Tipo | Origen en BC | Notas |
|---|---|---|---|
| `clientId` | string (GUID) | `Customer.SystemId` del Sell-to | Siempre |
| `fiscalInfo` | objeto | Cliente del `"Bill-to Customer No."` | `{}` vacío si el cliente no existe |
| `fiscalInfo.alias` / `fiscalName` | string | `Customer.Name` | |
| `fiscalInfo.address` | objeto | Dirección del cliente (`num` = `"B2B Street Number"`) | Misma forma que shippingAddress |
| `fiscalInfo.fiscalId.type` | string | `Customer."B2B Fiscal ID Type"` → `dni`\|`nif`\|`nie`\|`passport` (default `nif`) | minúsculas |
| `fiscalInfo.fiscalId.document` | string | `Customer."VAT Registration No."` | |
| `number` | string | `Sales Invoice Header."No."` | |
| `payMethodName` | TranslatedText6 | `Payment Method.Description` (fallback: el código) | |
| `issueDate` | string fecha | `"Posting Date"` | `.000Z` |
| `status` | string | `Cust. Ledger Entry."Remaining Amount"` = 0 → `"Paid"`; si no `"Unpaid"` | |
| `observations` / `documentUrl` | string | Constante `""` | PDF vía API OData (§7) |
| `totals` | Totals | Flowfields de cabecera (base, 0, IVA, total) | |
| `transportTotals` | Totals | Todo 0 | |
| `lines` | array | `Sales Invoice Line` tipo Item | |
| `payments` | array (1 elem.) | `{paymentInfo:"", dueDate:"Due Date", emittedAt:"Posting Date", amount: total con IVA}` | |

### Campos (línea) — diferencias respecto a la línea de albarán

| Campo JSON | Origen en BC |
|---|---|
| `id` | `Sales Invoice Line.SystemId` |
| `productId` | SystemId de variante o artículo |
| `productName` | **TranslatedText4 con traducciones reales** (`B2B Utils.GenerateLanguajeObject` sobre `Item.Description`) — nótese: 4 idiomas, no 6 |
| `deliveryNoteLineId` | SystemId de la `Sales Shipment Line` origen (por `Shipment No.`+`Shipment Line No.`; fallback por `Order No.`+`Order Line No.`; `""` si no hay) |
| `transactionInfo` | Igual que albarán pero con importes directos de la línea de factura (`Unit Price`, `Line Discount %`, `Line Amount`, `Line Discount Amount`, `Amount Including VAT`, `VAT %`) |
| `productInfo` | Idéntico al de albarán (name 6 idiomas, ean, image con URL de setup, sku…) |
| Sin `orderItemId` ni `externalReference` a nivel de línea | (la referencia externa va dentro de `productInfo`) |

---

## 5. PUT Abono de venta (Credit Memo)

- **Método/Ruta**: `PUT {Invoices URL}/{crMemoSystemId}` — **mismo endpoint que las
  facturas**; SystemId de `Sales Cr.Memo Header`. El backend debe aceptar abonos en el
  endpoint de facturas y distinguirlos por los importes negativos.
- **Disparo**: acción manual "Sync to B2B" en Posted Sales Credit Memo.
- **Payload**: `Cod80160.B2BCreditMemoAdapter.al`. Misma estructura que la factura (§4)
  con estas diferencias:

| Campo | Valor en abono |
|---|---|
| `number` | `Sales Cr.Memo Header."No."` |
| `status` | Constante `"Paid"` |
| Signo | **-1** en totales de cabecera, `quantity`, `amount`, `totalDiscounts`, `totalTaxes`, `taxes[].amount`, `taxes[].taxableBase` y `payments[].amount`. `price`/`priceOriginal` positivos |
| `lines[].deliveryNoteLineId` | SystemId de la `Return Receipt Line` origen (por `Return Receipt No.`+`Line No.`; fallback por `Return Order No.`+`Return Order Line No.`; `""` si no hay) |
| `payments[0].dueDate` | `"Posting Date"` (no hay Due Date) |
| `payments[0].emittedAt` | `"Posting Date"` |

Ejemplo (solo lo que cambia):

```json
{
  "number": "AB-2400012",
  "status": "Paid",
  "totals": {
    "totalAmount":   { "code": "EUR", "value": -100.00 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": -21.00 },
    "total":         { "code": "EUR", "value": -121.00 }
  },
  "lines": [ { "deliveryNoteLineId": "F6A7B8C9-0000-0000-0000-000000000006",
               "transactionInfo": { "info": { "quantity": -2, "amount": { "code": "EUR", "value": -100.00 } } } } ],
  "payments": [
    { "paymentInfo": "", "dueDate": "2026-07-31T00:00:00.000Z",
      "emittedAt": "2026-07-31T00:00:00.000Z", "amount": { "code": "EUR", "value": -121.00 } }
  ]
}
```

---

## 6. PUT Pedido de devolución (Sales Return Order)

- **Método/Ruta**: `PUT {Orders URL}/{returnOrderSystemId}` — **mismo endpoint que los
  pedidos de venta**; SystemId de `Sales Header` (Document Type = Return Order).
- **Disparo**: acción manual "Sync to B2B" en la página Sales Return Order
  (documento NO registrado). `MustSyncToB2B` valida que sea Return Order.
- **Payload**: `Cod80158.B2BReturnOrderAdapter.al` — usa el **contrato de pedido**
  (mismo shape que el Order Adapter del bloque de pedidos), con signo **-1** en
  cantidades/importes.

### Ejemplo JSON completo

```json
{
  "id": "0A1B2C3D-0000-0000-0000-000000000007",
  "clientId": "5E9C36DA-BB3E-ED11-9DB4-000D3A2FEEBD",
  "fiscalInfo": {
    "alias": "Tienda Central S.L.",
    "address": {
      "streetAddress": "Calle Mayor 1",
      "num": "",
      "description": "Planta 2",
      "city": "Madrid",
      "province": "Madrid",
      "zipCode": "28001",
      "countryIsoId": "ES",
      "geo": { "latitude": 0, "longitude": 0 },
      "contact": {
        "name": "Ana Pérez", "lastName": "", "company": "Tienda Central S.L.",
        "phones": [ { "code": "+34", "number": "600123456" } ]
      }
    },
    "fiscalName": "Tienda Central S.L.",
    "fiscalId": { "type": "nif", "document": "B12345678" }
  },
  "shippingAddress": {
    "streetAddress": "Calle Mayor 1", "num": "12", "description": "",
    "city": "Madrid", "province": "Madrid", "zipCode": "28001", "countryIsoId": "ES",
    "geo": { "latitude": 0, "longitude": 0 },
    "contact": { "name": "Ana Pérez", "lastName": "", "company": "Tienda Central S.L.", "phones": [] }
  },
  "paid": false,
  "transportId": "",
  "payMethodId": "transferencia",
  "reference": "DEV-CLIENTE-001",
  "observations": "",
  "totals": {
    "totalAmount":   { "code": "EUR", "value": -100.00 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": -21.00 },
    "total":         { "code": "EUR", "value": -121.00 }
  },
  "transportTotals": {
    "totalAmount":   { "code": "EUR", "value": 0 },
    "totalDiscount": { "code": "EUR", "value": 0 },
    "totalTax":      { "code": "EUR", "value": 0 },
    "total":         { "code": "EUR", "value": 0 }
  },
  "status": "open",
  "items": [
    {
      "id": "1B2C3D4E-0000-0000-0000-000000000008",
      "productId": "B7C8D9E0-0000-0000-0000-000000000002",
      "productName": {
        "es_ES": "Zapato piel 40", "en_EN": "Leather shoe 40",
        "fr_FR": "Zapato piel 40", "it_IT": "Zapato piel 40"
      },
      "transactionInfo": {
        "info": {
          "quantity": -2,
          "discount": 0,
          "price":  { "code": "EUR", "value": 50.00 },
          "amount": { "code": "EUR", "value": -100.00 }
        },
        "totalDiscounts": { "code": "EUR", "value": 0 },
        "totalTaxes":     { "code": "EUR", "value": -21.00 },
        "taxes": [
          {
            "id": "IVA",
            "name": {
              "es_ES": "IVA", "en_EN": "IVA", "fr_FR": "IVA",
              "it_IT": "IVA", "pt_PT": "IVA", "de_DE": "IVA"
            },
            "percent": 21,
            "amount":      { "code": "EUR", "value": -21.00 },
            "taxableBase": { "code": "EUR", "value": -100.00 },
            "productTaxId": "IVA21"
          }
        ],
        "discounts": [],
        "priceOriginal": null,
        "offerDiscounts": null
      },
      "clientReference": "C00123",
      "shipDate": "2026-08-10T00:00:00",
      "status": "Open",
      "productExternalReference": "ART001",
      "additionalValues": null,
      "quantityDelivered": 0,
      "productInfo": {
        "name": {
          "es_ES": "40", "en_EN": "40", "fr_FR": "40",
          "it_IT": "40", "pt_PT": "40", "de_DE": "40"
        },
        "brandId": "",
        "ean": "8412345678905",
        "externalReference": "ART001",
        "id": "B7C8D9E0-0000-0000-0000-000000000002",
        "image": null,
        "modelId": "D4E5F6A7-0000-0000-0000-000000000004",
        "modelExternalReference": "ART001",
        "sku": "ART00140"
      },
      "stockServiceId": ""
    }
  ],
  "payments": [],
  "type": "NOT_DEFINED",
  "source": "ERP",
  "externalReference": "PD-2400007",
  "orderedDate": "2026-08-01T00:00:00",
  "needRecalculateTotals": true,
  "marketId": "es",
  "clienteExternalReference": "C00123",
  "shippingAddressExternalReference": "DIR01",
  "orderDiscount": null,
  "totalWithTransport": { "code": "EUR", "value": -121.00 },
  "purchaseOrderId": "DEV-CLIENTE-001",
  "seasonId": ""
}
```

### Campos (cabecera)

| Campo JSON | Tipo | Origen en BC | Notas |
|---|---|---|---|
| `id` | string (GUID) | `Sales Header.SystemId` | Además del id en la URL |
| `clientId` | string (GUID) | `Customer.SystemId` (Sell-to) | |
| `fiscalInfo` | objeto | Datos Bill-to de la cabecera + cliente Bill-to (`fiscalId.type` del cliente; `document` = `SalesHeader."VAT Registration No."`) | `num` siempre `""` |
| `shippingAddress` | objeto | `Ship-to Address` si hay `Ship-to Code`; si no, campos Ship-to de cabecera | |
| `paid` | bool | Constante `false` | |
| `transportId` | string | Constante `""` | |
| `payMethodId` | string | `LowerCase("Payment Method Code")` | |
| `reference` | string | `"External Document No."` | |
| `observations` | string | `""` | |
| `totals` | Totals | Flowfields `Amount` / `"Amount Including VAT"` × −1 | |
| `transportTotals` | Totals | Precio de la línea del producto de transporte (`setup."Send Product"`) × −1; 0 si no hay | totalDiscount/totalTax = 0 |
| `status` | string | Por cantidades: `open` \| `partially-shipped` \| `shipped` \| `invoiced` | minúsculas |
| `items` | array | `Sales Line` tipo Item | Ver abajo |
| `payments` | array | Siempre `[]` | |
| `type` | string | Constante `"NOT_DEFINED"` | (los pedidos normales llevan otro type) |
| `source` | string | Constante `"ERP"` | |
| `externalReference` | string | `Sales Header."No."` | |
| `orderedDate` | string fecha | `"Order Date"` | **Sin** `.000Z` |
| `needRecalculateTotals` | bool | Constante `true` | |
| `marketId` | string | Constante `"es"` (`B2B Utils.GetMarketId`) | |
| `clienteExternalReference` | string | `"Sell-to Customer No."` | (sic, "cliente…") |
| `shippingAddressExternalReference` | string | `"Ship-to Code"` | |
| `orderDiscount` | null | Constante `null` | |
| `totalWithTransport` | Money | `"Amount Including VAT"` × −1 | |
| `purchaseOrderId` | string | `"External Document No."` | |
| `seasonId` | string | Constante `""` | |

### Campos (item)

| Campo JSON | Origen en BC | Notas |
|---|---|---|
| `id` | `Sales Line.SystemId` | |
| `productId` | SystemId de variante o artículo | |
| `productName` | TranslatedText4 con traducciones | |
| `transactionInfo` | Importes de `Sales Line` × −1 | `priceOriginal: null`, `offerDiscounts: null` (a diferencia de los documentos registrados) |
| `clientReference` | `"Sell-to Customer No."` | |
| `shipDate` | `Sales Line."Shipment Date"` | Sin `.000Z` |
| `status` | `Open` \| `Partial` \| `Delivered` por `Quantity Shipped` | Con mayúscula inicial |
| `productExternalReference` | `Sales Line."No."` | |
| `additionalValues` | `null` | |
| `quantityDelivered` | `"Quantity Shipped"` × −1 | |
| `productInfo` | Como en albarán pero: `name` = `Sales Line.Description` (6 idiomas, sin componer con variante) e `image` **siempre `null`** | |
| `stockServiceId` | `""` | |

---

## 7. Entrega de PDFs (B2B → BC, API OData)

Los payloads anteriores envían siempre `documentUrl: ""`: el conector **no** adjunta el
PDF en el sync. El B2B lo obtiene bajo demanda llamando a la **API OData** publicada por
BC — página API `Pag80106.B2BDocumentPDFAPI.al` sobre la tabla temporal
`Tab80105.B2BDocumentPDF.al`.

- **Método/Ruta** (estándar de páginas API de BC, autenticación OAuth2/S2S de BC):

```
GET {bcBase}/api/mitoprojects/b2b/v1.0/companies({companyId})/salesDocuments?$filter=...
```

  (`APIPublisher = mitoprojects`, `APIGroup = b2b`, `APIVersion = v1.0`,
  `EntitySetName = salesDocuments`; solo lectura: Insert/Modify/Delete no permitidos.)

- **Dos formas de seleccionar el documento** (trigger `OnOpenPage`):
  1. **Por SystemId** (la habitual): `$filter=systemId eq {documentSystemId}` — el mismo
     GUID que el conector usó como clave del recurso en los PUT anteriores. La página
     busca ese SystemId por orden en: `Sales Header` (Order / Return Order) →
     `Sales Shipment Header` → `Sales Invoice Header` → `Return Receipt Header` →
     `Sales Cr.Memo Header`, y deduce el tipo.
  2. **Por tipo + número**: `$filter=documentType eq 'Sales Invoice' and documentNo eq 'FV-2400089'`
     (valores del enum `B2B Document Type`: `Sales Order`, `Return Order`, `Sales Shipment`,
     `Sales Invoice`, `Return Receipt`, `Sales Cr.Memo`).

- **Qué hace BC al leer** (trigger `OnAfterGetRecord`):
  1. Resuelve el report configurado en `Report Selections` según el tipo
     (`S.Order`, `S.Shipment`, `S.Invoice`, `S.Return`, `S.Ret.Rcpt.`, `S.Cr.Memo`).
     Error si no hay report configurado.
  2. Genera el PDF en memoria (`Report.SaveAs ... ReportFormat::Pdf`).
  3. **Lo sube a Azure Blob Storage público** (`Cod80107.B2BBlobStorageManager.al`,
     `UploadPdfToPublicAzureBlob`): `PUT {Storage Account Url}/{Container Name}/{carpeta}/{documentNo}.pdf?{SAS Token}`
     con header `x-ms-blob-type: BlockBlob`. La carpeta por tipo sale del setup:
     `Sales Orders Folder` (pedidos y ped. devolución), `Sales Shipment Folder`
     (albaranes y devoluciones), `Sales Invoices Folder` (facturas y abonos).
  4. Devuelve en la respuesta OData el campo `url` = **URL pública del blob, sin SAS**.

- **Respuesta** (un único registro):

```json
{
  "@odata.context": ".../$metadata#companies(...)/salesDocuments",
  "value": [
    {
      "systemId": "b7c8d9e0-0000-0000-0000-000000000009",
      "documentType": "Sales Invoice",
      "documentNo": "FV-2400089",
      "url": "https://mistorageaccount.blob.core.windows.net/b2b-pdfs/invoices/FV-2400089.pdf"
    }
  ]
}
```

**Conclusión para el nuevo backend**: los PDF **no** se entregan embebidos ni en base64
(el `base64Text` está comentado en el código); se entregan como **URL pública de Azure
Blob**, generada on-the-fly cuando el B2B consulta este endpoint OData de BC. El nuevo
backend no tiene que implementar este endpoint (lo sirve BC); solo debe seguir llamándolo
igual, o consumir directamente las URLs de blob si conserva la misma cuenta de storage.

---

## 8. Rutas legacy (no implementar, solo conocer)

`Cod80120.B2BShipmentSync.al` y `Cod80121.B2BInvoiceSync.al` son la primera versión
manual del sync (llaman a `B2B API Handler.CallSyncDeliveryNoteAPI` /
`CallSyncInvoiceAPI` en `src\codeunits\Cod80101.B2BAPIHandler.al`, que hace
`PUT {BaseUrl}/{systemId}` con Bearer token). Sus payloads difieren de los adapters
actuales (moneda fija `EUR`, `transportId`/`transportUrlTrack` vacíos, `type: "-"`,
`productId` sacado del `Item Reference`, fechas sin `.000Z` en el albarán, status
`Delivered`/`Invoiced` con mayúscula, payments sin `amount`…). Ningún flujo de la app los
invoca desde páginas actuales; el contrato vigente es el de los adapters (§2–§6).

---

## 9. Resumen de decisiones que el backend debe respetar

1. **Upsert por PUT**: todos los documentos llegan como `PUT {recurso}/{systemIdBC}`;
   reenvíos del mismo documento deben sobrescribir.
2. **Endpoints compartidos**: devoluciones de albarán → endpoint de albaranes; abonos →
   endpoint de facturas; pedidos de devolución → endpoint de pedidos. La distinción es por
   contenido (signo de importes, `status`, `type`).
3. **Signos**: albarán y factura en positivo; devolución, abono y pedido de devolución con
   cantidades e importes en negativo (precios unitarios siempre positivos).
4. **Todos los campos siempre presentes**; opcionales como `""`, `[]` o `null` explícito.
5. **`transportUrlTrack`** solo se rellena en albaranes de venta con transportista +
   nº de seguimiento + URL configurada en el `Shipping Agent`.
6. **PDF por URL**: `documentUrl` del payload siempre vacío; el PDF se resuelve vía la API
   OData `salesDocuments` de BC, que lo publica en Azure Blob y devuelve la URL pública.
