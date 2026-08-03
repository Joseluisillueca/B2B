# Contrato API B2B — Bloque 2: Catálogo

**Entidades**: Modelos, Imágenes de modelo, Productos (variantes), Case Packs, Atributos, Familias, Categorías.

Documento generado a partir del código AL del conector **MITO - Conector B2B** (`c:\BC_Projects\Mito - Conector B2B`). Cada sección cita los ficheros de origen. El objetivo es que el nuevo backend .NET 8 implemente exactamente los mismos endpoints que consume BC.

---

## 1. Mecánica común de todas las llamadas de catálogo

Fuente: `src\codeunits\b2bManager\Cod80111.B2BApiManager.al`, `Cod80143.B2BBaseApiManager.al`.

- **Método HTTP**: siempre `PUT` (upsert). El conector **no distingue crear/actualizar**: el backend debe hacer *upsert* por el id de la URL.
- **Headers**:
  - `Content-Type: application/json`
  - `Authorization: Bearer {accessToken}` (token obtenido en el endpoint de login — ver bloque de autenticación; se refresca automáticamente si está vacío o caducado).
- **URL**: cada entidad tiene su URL plantilla en la tabla de configuración `B2B Integration Setup` (`src\tables\Tab80100.B2BIntegrationSetup.al`). La plantilla contiene un placeholder que BC sustituye por el **id de la entidad** (`Cod80111.B2BApiManager.al`, `GetEndpointURL`):
  - Si la plantilla contiene `{{$guid}}` → se reemplaza literalmente.
  - Si no → se aplica `StrSubstNo(url, id)`, es decir, placeholder estilo `%1`.
  - El id es `ModelId()` del adapter si `HasModelId()=true`; si no, el `SystemId` del registro BC **sin llaves** (formato `XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX`, en mayúsculas tal como lo formatea BC).
  - Convención de plantillas (campos de setup): `Sync Models URL`, `Sync Model Images URL`, `Sync Products URL`, `Sync Attributes URL`, `Sync Families URL`, `Sync Categories URL`. Ejemplo típico: `https://{host}/api/models/%1`.
- **Timeout** del cliente BC: 10 segundos. El backend debe responder rápido.
- **Respuesta esperada**:
  - Éxito = cualquier **2xx**. El body es opcional; si viene, **debe ser JSON válido** (objeto o array) — un body no parseable se registra como error en BC aunque el HTTP sea 2xx. El conector **no usa el contenido** de la respuesta para nada. Recomendación: `200 OK` con `{}` o con la entidad guardada.
  - Error = cualquier código no-2xx. BC registra `HTTP {código}: {body}` en `B2B Sync Status` / `B2B Error Log`. Un mensaje de error legible en el body ayuda al diagnóstico.
- **DELETE**: **ninguna entidad de catálogo tiene borrado**. En todos los adapters de este bloque `DeleteUrl()` devuelve vacío y `ElementsToDelete()` no devuelve nada (el `B2B Delete Api Manager`, `Cod80142`, solo se usa para ofertas/precios, fuera de este bloque). Las bajas se comunican vía `active: false` o dejando de sincronizar.

### 1.1 Multiidioma

Fuente: `src\codeunits\Cod80122.B2BUtils.al` (`GenerateLanguajeObject`), `src\tables\Tab80115.B2BTranslationLanguage.al`, `Tab80116.B2BTranslationEntry.al`.

Los campos de texto multiidioma son objetos JSON con clave = código de idioma. **BC siempre envía exactamente estas 4 claves**: `es_ES`, `en_EN`, `fr_FR`, `it_IT`. Aunque el esquema del portal actual admite también `pt_PT` y `de_DE`, **BC nunca las envía**; el backend debe aceptarlas como opcionales pero no exigirlas.

Dos variantes en BC:

1. **Sin traducciones** (`GenerateLanguajeObject(texto)`): el mismo texto por defecto en los 4 idiomas.
2. **Con traducciones** (`GenerateLanguajeObject(texto, tableId, fieldId, systemId)`): siembra los 4 idiomas con el texto por defecto y sobreescribe cada idioma que tenga fila en la tabla `B2B Translation Entry` (clave: Table ID + Field ID + SystemId del registro + Language Code). El `External Code` del idioma (`B2B Translation Language`) es la clave JSON (`es_ES`/`en_EN`/`fr_FR`/`it_IT`), normalizada a esas grafías exactas.

Usan la variante **con traducciones**: nombre/descripción de **Modelo** y de **Categoría**. Usan la variante **sin traducciones**: Atributo, Familia, Case Pack. El **Producto** (variante) es un caso especial: solo envía `es_ES` (ver §4).

### 1.2 Disparadores en BC

- **Report 80101 "B2B Sync Models"** (`src\reports\Rep80101.B2BSyncItemEntities.al`): sincroniza por cada Item con `Sync to B2B=true` y `B2B Parent Item=''`, en este orden: **Modelo → Imágenes → Productos (variantes no bloqueadas) → Case Packs → Ofertas → Borrado de ofertas obsoletas → Ofertas de case packs**. Se lanza desde el Role Center B2B (`Pag-Ext80111`) o desde la ficha de producto filtrado a ese item (`PagExt80101.ItemCardExt.al`).
- **Report 80102 "B2B Sync Masters"** (`src\reports\Rep80102.B2BSyncMasters.al`): sincroniza maestros: **Atributos → (otros maestros) → Familias → Categorías** (familias y categorías salen ambas de `Item Category` con `Sync to B2B=true`).
- **Página de atributos** (`src\pageextensions\PagExt80102.ItemAttributeExt.al`): acción "Sync to B2B" para un atributo individual.
- Todo pasa por `Cod80113.B2BApiOrchestrator.al` → `Cod80111.B2BApiManager.al`.

---

## 2. Modelo (Item)

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Models URL}` con `{{$guid}}`/`%1` = **SystemId del Item** (GUID sin llaves). Ej.: `PUT /api/models/{itemSystemId}` |
| **Adapter** | `src\codeunits\adapters\Cod80112.B2BModelAdapter.al` |
| **Origen BC** | Tabla `Item` con `Sync to B2B = true` y `B2B Parent Item = ''` (los items con padre son case packs, ver §5) |
| **Disparo** | Report 80101 (primer dataitem), ficha de producto |

### Payload

```json
{
  "name": {
    "es_ES": "Camiseta básica",
    "en_EN": "Basic T-Shirt",
    "fr_FR": "T-shirt basique",
    "it_IT": "T-shirt basic"
  },
  "description": {
    "es_ES": "Camiseta de algodón 100%",
    "en_EN": "100% cotton t-shirt",
    "fr_FR": "T-shirt 100% coton",
    "it_IT": "T-shirt 100% cotone"
  },
  "active": true,
  "externalReference": "ART-00123",
  "attributes": {
    "Color": "Azul",
    "Material": "Algodón",
    "temporada": "Verano 2026"
  },
  "familyId": "camisetas",
  "brandId": "",
  "crossSellingIds": [],
  "upSellingIds": [],
  "configuragleComponennts": [],
  "productSegments": ["A+", "A"]
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `name` | objeto multiidioma | `Item.Description` + traducciones (`B2B Translation Entry`, tabla 27, campo Description) | Sí | 4 claves siempre |
| `description` | objeto multiidioma | `Item."Description 2"` + traducciones | Sí | Puede llevar los 4 idiomas con cadena vacía |
| `active` | boolean | `NOT Item.Blocked` | Sí | |
| `externalReference` | string | `Item."No."` | Sí | Código del artículo en BC |
| `attributes` | objeto {nombre: valor} | Dos fuentes: (1) `Item Attribute Value Mapping` → clave = `Item Attribute.Name` tal cual, valor = `Item Attribute Value.Value` (se omiten valores vacíos); (2) atributos "de campo": `Item Attribute` con `Sync to B2B=true` y `B2B Item Field Attribute<>0` → clave = `Item Attribute."B2B Code"`, valor = `Format()` del campo del Item referenciado (se omite si el campo no existe o el valor es vacío) | Sí (puede ser `{}`) | Ojo: la fuente (1) usa el **Name** del atributo como clave; la (2) usa el **B2B Code** |
| `familyId` | string | `LowerCase(Item."Item Category Code")` | Sí | Enlaza con la Familia (§7). `""` si el item no tiene categoría |
| `brandId` | string | Fijo `""` | Sí | Siempre vacío |
| `crossSellingIds` | array | Fijo `[]` | Sí | |
| `upSellingIds` | array | Fijo `[]` | Sí | |
| `configuragleComponennts` | array | Fijo `[]` | Sí | **El nombre lleva ese typo exacto en el JSON** — mantener |
| `productSegments` | array de string | Tabla `B2B Item Segment` (Item No.) → valores del enum `B2B Customer Segment` en MAYÚSCULAS: `"A+"`, `"A"`, `"B"`, `"C"`, `"D"` | Sí (puede ser `[]`) | `Tab80129.B2BItemSegment.al`, `Enum80116.B2BCustomerSegment.al` |

> **Vía legacy**: `src\codeunits\Cod80103.B2BProductSync.al` (`SyncProductToB2B`) hace el mismo `PUT` a `Sync Models URL` con un payload casi idéntico pero sin traducciones (`description` en en/fr/it = `"-"`), `familyId: ""` y `productSegments: []`. Es el camino antiguo (acción individual antigua); el contrato del backend debe ser compatible con ambos, lo cual es automático si se valida de forma laxa.

---

## 3. Imágenes de modelo

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Model Images URL}` con placeholder = **SystemId del Item** (GUID sin llaves). Ej.: `PUT /api/models/{itemSystemId}/images` |
| **Adapter** | `src\codeunits\adapters\Cod80115.B2BModelImageAdapter.al` |
| **Origen BC** | Tabla `Item` (`Sync to B2B = true`) |
| **Disparo** | Report 80101 (segundo dataitem, tras el modelo) |

### Payload

```json
{
  "images": [
    {
      "id": "8A3F2C1D-5717-4562-B3FC-2C963F66AFA6",
      "image": {
        "uri": "https://cdn.miempresa.com/items/ART-00123.jpg",
        "description": {
          "es_ES": "ART-00123",
          "en_EN": "ART-00123",
          "fr_FR": "ART-00123",
          "it_IT": "ART-00123"
        },
        "order": 0
      }
    }
  ]
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `images` | array | — | Sí | BC envía **siempre exactamente 1 elemento** |
| `images[].id` | string (GUID sin llaves) | `Item.SystemId` | Sí | Mismo GUID que el modelo |
| `images[].image.uri` | string | `StrSubstNo(Setup."Image Url", Item."No.")` — plantilla con `%1` = nº de artículo, configurada en `B2B Integration Setup` campo `Image Url` | Sí | |
| `images[].image.description` | objeto multiidioma | `Item."No."` en los 4 idiomas (no hay traducción real) | Sí | |
| `images[].image.order` | integer | Fijo `0` | Sí | |
| `images[].image.path` | string | **No se envía** (comentado en el código) | No | El esquema del portal lo admite |
| `images[].attributesBinded` | objeto | **No se envía** | No | El esquema del portal lo admite (mapa atributo → [valores]) |

---

## 4. Producto (Item Variant)

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Products URL}` con placeholder = **SystemId del Item Variant** (GUID sin llaves). Ej.: `PUT /api/products/{variantSystemId}` |
| **Adapter** | `src\codeunits\adapters\Cod80117.B2BProductAdapter.al` |
| **Origen BC** | Tabla `Item Variant` (no bloqueada) de items marcados `Sync to B2B` |
| **Disparo** | Report 80101 (tercer dataitem) |
| **Validación previa** | La variante debe tener un atributo de variante cuyo `Item Attribute."B2B Code"` sea `tallas` (case-insensitive). Si no, **no se envía** y se registra en `B2B Error Log` |

### Payload

```json
{
  "modelId": "8A3F2C1D-5717-4562-B3FC-2C963F66AFA6",
  "name": {
    "es_ES": "Camiseta básica Azul T-M"
  },
  "description": {
    "es_ES": "Camiseta básica Azul T-M"
  },
  "active": true,
  "sku": "8412345678905",
  "externalReference": "8412345678905",
  "attributes": {
    "tallas": "M",
    "color": "Azul"
  },
  "ean": "8412345678905",
  "stockAlerts": [],
  "spareParts": [],
  "brandId": "",
  "crossSellingIds": [],
  "upSellingIds": [],
  "taxId": "iva-normal"
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `modelId` | string (GUID sin llaves) | `Item.SystemId` del item padre de la variante | Sí | Debe existir el modelo (§2) |
| `name` | objeto multiidioma | `Item.Description + ' ' + ItemVariant.Description` | Sí | **Solo clave `es_ES`** — a diferencia del modelo, aquí no se envían en/fr/it |
| `description` | objeto multiidioma | Igual que `name` | Sí | Solo `es_ES` |
| `active` | boolean | `ItemVariant."B2B Active"` (ext. `TabExt80120.ItemVariantExt.al`, por defecto `true`) | Sí | |
| `sku` | string | `Item Reference."Reference No."` — referencia tipo **Bar Code** del item+variante | Sí | Si no hay referencia, va `""` |
| `externalReference` | string | Igual que `sku` | Sí | |
| `attributes` | objeto {código: valor} | `Item Variant Attribute` (Item No. + Variant Code) → clave = `LowerCase(Item Attribute."B2B Code")`, valor = `Attribute Value` | Sí | Siempre incluye `tallas` (validación previa) |
| `ean` | string | Igual que `sku` | Sí | |
| `stockAlerts` | array | Fijo `[]` | Sí | |
| `spareParts` | array | Fijo `[]` | Sí | |
| `brandId` | string | Fijo `""` | Sí | |
| `crossSellingIds` | array | Fijo `[]` | Sí | |
| `upSellingIds` | array | Fijo `[]` | Sí | |
| `taxId` | string | Fijo `"iva-normal"` (hardcoded) | Sí | El backend debe reconocer este id de impuesto |
| `familyId`, `bundle`, `purchaseOptions`, `seasons` | — | **No se envían** en esta vía | No | El esquema del portal los admite |

> **Vía legacy**: `src\codeunits\Cod80108.B2BProductRefSync.al` (`SyncProductReferenceToB2B`) hace `PUT {Sync Products URL}` con `{{$guid}}` = **SystemId del Item Reference** (no de la variante) y un payload similar con diferencias: incluye `"familyId": "null"` (string literal "null"), `name` con los 4 idiomas repetidos y `attributes` con clave = `Attribute Name` (no B2B Code). Camino antiguo lanzado desde la lista de referencias; conviene soportarlo solo si se mantiene esa acción.

---

## 5. Case Pack (Item con padre)

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Products URL}` (**mismo endpoint que Producto**) con placeholder = **SystemId del Item case pack** (GUID sin llaves) |
| **Adapter** | `src\codeunits\adapters\Cod80123.B2BCasePackAdapter.al` |
| **Origen BC** | Tabla `Item` con `Sync to B2B = true` y `B2B Parent Item <> ''`. Sus componentes salen de `BOM Component` (líneas tipo Item) |
| **Disparo** | Report 80101 (cuarto dataitem: case packs cuyo `B2B Parent Item` = item en curso) |

Un case pack es un "producto" más para el portal, con `bundle` que referencia variantes por su GUID.

### Payload

```json
{
  "modelId": "8A3F2C1D-5717-4562-B3FC-2C963F66AFA6",
  "name": {
    "es_ES": "Caja 12 uds camiseta básica",
    "en_EN": "Caja 12 uds camiseta básica",
    "fr_FR": "Caja 12 uds camiseta básica",
    "it_IT": "Caja 12 uds camiseta básica"
  },
  "description": {
    "es_ES": "Caja 12 uds camiseta básica",
    "en_EN": "Caja 12 uds camiseta básica",
    "fr_FR": "Caja 12 uds camiseta básica",
    "it_IT": "Caja 12 uds camiseta básica"
  },
  "active": true,
  "sku": "18412345678902",
  "externalReference": "18412345678902",
  "attributes": {},
  "ean": "18412345678902",
  "stockAlerts": [],
  "spareParts": [],
  "brandId": "",
  "crossSellingIds": [],
  "upSellingIds": [],
  "taxId": "iva-normal",
  "bundle": {
    "products": {
      "1B2C3D4E-1111-2222-3333-444455556666": 6,
      "9F8E7D6C-AAAA-BBBB-CCCC-DDDDEEEEFFFF": 6
    },
    "isVirtual": false
  }
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `modelId` | string (GUID) | `Item.SystemId` del **item padre** (`B2B Parent Item`) | Sí | |
| `name` / `description` | objeto multiidioma | `Item.Description` del case pack, **mismo texto en los 4 idiomas** (sin traducciones) | Sí | |
| `active` | boolean | Fijo `true` | Sí | |
| `sku` / `externalReference` / `ean` | string | `Item Reference` tipo Bar Code del item case pack | Sí | |
| `attributes` | objeto | Fijo `{}` | Sí | |
| `stockAlerts`, `spareParts`, `crossSellingIds`, `upSellingIds` | array | Fijo `[]` | Sí | |
| `brandId` | string | Fijo `""` | Sí | |
| `taxId` | string | Fijo `"iva-normal"` | Sí | |
| `bundle.products` | objeto {GUID variante: cantidad} | `BOM Component` del case pack: clave = `ItemVariant.SystemId` (GUID sin llaves) del componente, valor = `Quantity per` (número) | Sí | Las variantes deben existir como productos (§4) |
| `bundle.isVirtual` | boolean | Fijo `false` | Sí | |

---

## 6. Atributo (Item Attribute)

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Attributes URL}` con placeholder = **`Item Attribute."B2B Code"`** (no un GUID). Ej.: `PUT /api/attributes/tallas` |
| **Adapter** | `src\codeunits\adapters\Cod80114.B2BItemAttributeAdapter.al` |
| **Origen BC** | Tabla `Item Attribute` con `Sync to B2B = true` (ext. `TabExt80102.ItemAttributeExt.al`) |
| **Disparo** | Report 80102 (primer dataitem) o acción individual en la página de atributos (`PagExt80102.ItemAttributeExt.al`) |

### Payload

```json
{
  "name": {
    "es_ES": "Tallas",
    "en_EN": "Tallas",
    "fr_FR": "Tallas",
    "it_IT": "Tallas"
  },
  "type": "ListString",
  "isModelAttributte": false,
  "code": "tallas",
  "visibleWeb": true,
  "visibleFormat": "List",
  "values": [
    { "order": 1, "id": "s" },
    { "order": 2, "id": "m" },
    { "order": 3, "id": "l" }
  ]
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `name` | objeto multiidioma | `Item Attribute.Name`, mismo texto en los 4 idiomas (sin traducciones) | Sí | |
| `type` | string | Enum `B2B Attribute Type` (`Enum80105`): `Int`, `ListString`, `String`, `StringCulture`, `Double`, `Date`, `Boolean`, `Currency` | Sí | Se envía el texto del enum tal cual |
| `isModelAttributte` | boolean | `Item Attribute."B2B Is Model Attribute"` | Sí | **Typo exacto en el nombre JSON** — mantener |
| `code` | string | `Item Attribute."B2B Code"` | Sí | Mismo valor que el id de la URL |
| `visibleWeb` | boolean | `Item Attribute."B2B Visible Web"` (por defecto true) | Sí | |
| `visibleFormat` | string | Enum `B2B Attribute Format` (`Enum80106`): `List` o `Box` | Sí | |
| `values` | array | `Item Attribute Value` del atributo | Sí (puede ser `[]`) | |
| `values[].order` | integer | Contador secuencial 1..n en orden de lectura de BC | Sí | |
| `values[].id` | string | `Item Attribute Value.Value` **sanitizado**: minúsculas; espacios, `/`, `\`, `_`, `.` → `-`; colapsa `--`; recorta `-` en extremos | Sí | Ej.: `Azul Marino` → `azul-marino` |
| `values[].color`, `values[].image`, `values[].name` | — | **No se envían** (comentados en el código) | No | |

---

## 7. Familia (Item Category)

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Families URL}` con placeholder = **`LowerCase(Item Category.Code)`**. Ej.: `PUT /api/families/camisetas` |
| **Adapter** | `src\codeunits\adapters\Cod80124.B2BFamilyAdapter.al` |
| **Origen BC** | Tabla `Item Category` con `Sync to B2B = true` (ext. `Tab-Ext80106.B2BItemCategory.al`) |
| **Disparo** | Report 80102 (dataitem Families, **antes** que Categories) |

### Payload

```json
{
  "name": {
    "es_ES": "Camisetas",
    "en_EN": "Camisetas",
    "fr_FR": "Camisetas",
    "it_IT": "Camisetas"
  },
  "code": "camisetas",
  "atributes": []
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `name` | objeto multiidioma | `Item Category.Description`, mismo texto en los 4 idiomas (**sin** traducciones — usa la versión de 1 argumento) | Sí | |
| `code` | string | `LowerCase(Item Category.Code)` | Sí | Es el `familyId` que referencian Modelo (§2) y Categoría (§8) |
| `atributes` | array | Fijo `[]` | Sí | **Typo exacto** (`atributes`, una sola "t") — mantener |

---

## 8. Categoría (Item Category)

| | |
|---|---|
| **Método/Ruta** | `PUT {Sync Categories URL}` con placeholder = **id jerárquico** `catalog.{padre}.{...}.{codigo}` en minúsculas. Ej.: `PUT /api/categories/catalog.ropa.camisetas` |
| **Adapter** | `src\codeunits\adapters\Cod80116.B2BCategoryAdapter.al` |
| **Origen BC** | Tabla `Item Category` con `Sync to B2B = true` (misma tabla que Familias; cada categoría se envía a los dos endpoints) |
| **Disparo** | Report 80102 (dataitem Categories, tras Families) |

El id se construye en `GetCategoryId()`: se toma el código de la categoría y se antepone recursivamente el de cada `Parent Category`, todo en minúsculas, unido por `.` y con prefijo fijo `catalog.`. Una categoría raíz `ROPA` → `catalog.ropa`; su hija `CAMISETAS` → `catalog.ropa.camisetas`.

### Payload

```json
{
  "name": {
    "es_ES": "Camisetas",
    "en_EN": "T-Shirts",
    "fr_FR": "T-shirts",
    "it_IT": "T-shirt"
  },
  "description": {
    "es_ES": "Camisetas",
    "en_EN": "T-Shirts",
    "fr_FR": "T-shirts",
    "it_IT": "T-shirt"
  },
  "models": [],
  "search": {
    "familyIds": ["camisetas"]
  },
  "active": true
}
```

| Campo JSON | Tipo | Origen BC | Obligatorio | Notas |
|---|---|---|---|---|
| `name` | objeto multiidioma | `Item Category.Description` **con traducciones** (`B2B Translation Entry`, tabla 5722, campo Description) | Sí | |
| `description` | objeto multiidioma | Igual que `name` (mismo campo origen) | Sí | |
| `models` | array | Fijo `[]` (el código para poblarlo está comentado) | Sí | El contenido de la categoría se resuelve por `search.familyIds` |
| `search.familyIds` | array de string | `[ LowerCase(Item Category.Code) ]` — siempre 1 elemento: la propia categoría como familia | Sí | El portal enlaza la categoría con los modelos cuyo `familyId` coincida |
| `active` | boolean | Fijo `true` | Sí | |
| `search.attributes/pricePVD/pricePVP/brandId/order/eans`, `slug`, `autoGenerated`, `marketIds` | — | **No se envían** | No | Admitidos por el esquema del portal |

---

## 9. Resumen de rutas y orden de sincronización recomendado

| Entidad | Verbo | URL plantilla (setup) | Id en URL | Adapter |
|---|---|---|---|---|
| Atributo | PUT | `Sync Attributes URL` | `B2B Code` | Cod80114 |
| Familia | PUT | `Sync Families URL` | código en minúsculas | Cod80124 |
| Categoría | PUT | `Sync Categories URL` | `catalog.x.y` | Cod80116 |
| Modelo | PUT | `Sync Models URL` | SystemId Item | Cod80112 |
| Imágenes | PUT | `Sync Model Images URL` | SystemId Item | Cod80115 |
| Producto | PUT | `Sync Products URL` | SystemId Item Variant | Cod80117 |
| Case Pack | PUT | `Sync Products URL` | SystemId Item (case pack) | Cod80123 |

Orden de dependencia que el backend debe tolerar (y que BC ejecuta): atributos/familias/categorías (report 80102, maestros) y por item: modelo → imágenes → productos → case packs (report 80101). El backend no debe rechazar referencias adelantadas de forma fatal (p. ej. `familyId` aún no dado de alta) o debe documentar el orden requerido.

## 10. Hallazgos importantes para el equipo .NET

1. **Typos que forman parte del contrato**: `configuragleComponennts` (modelo), `isModelAttributte` (atributo), `atributes` (familia). El backend debe deserializar esos nombres exactos.
2. **Solo 4 idiomas**: BC envía siempre `es_ES/en_EN/fr_FR/it_IT`; nunca `pt_PT/de_DE`. El producto (variante) envía **solo `es_ES`**.
3. **Upsert idempotente por PUT**: no existen POST ni DELETE en catálogo. Respuesta: 2xx con body JSON (u vacío); body no-JSON en un 2xx se trata como error en BC.
4. **Ids heterogéneos**: GUIDs de BC (mayúsculas, sin llaves) para modelo/imágenes/producto/case pack; códigos funcionales para atributo (`B2B Code`), familia (código lowercase) y categoría (`catalog.` + ruta jerárquica lowercase).
5. **`taxId` hardcoded** a `iva-normal` en productos y case packs: debe existir en el backend.
6. **Validación de `tallas`**: BC no envía variantes sin el atributo con B2B Code `tallas`; el backend puede asumir su presencia en `attributes`.
7. **Vías legacy** (`Cod80103`, `Cod80108`) con payloads ligeramente distintos (misma ruta): validar de forma laxa para no romperlas.
