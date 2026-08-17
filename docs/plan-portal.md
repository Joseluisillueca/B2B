# Plan de trabajo — Portal B2B lejan (paridad con el portal actual)

Encargo del cliente: **idéntico al portal actual en puntos de menú, apariencia y
funcionamiento**, con las imágenes de la portada configurables desde el CMS y el catálogo
como el del ejemplo; más moderno en acabado, idéntico en estructura y funciones.

Referencias: `docs/front-referencia/*.png` + `estructura.json`, `docs/cms-referencia/`,
`docs/contrato-api/`, `PRODUCT.md`. Rutas reales: `/{market}/{lang}/{vista}` (`/es/es/orders`).

---

## 1. Inventario de vistas del portal real y gap análisis

### Chrome común (todas las vistas post-login)
- **Header negro**: logo `lejan™` · buscador global (placeholder `Búsqueda`) · avatar +
  `{email} - {rol}` / `{nombre cliente}` (menú de usuario, desde donde cuelgan todas las
  secciones salvo Catálogo) · icono **ojo** (toggle: oculta título, migas y la faceta
  LÍNEAS, y cambia el orden a "Relevancia") · selector de idioma `ES ▾` · botón azul de
  carrito `REPOSICIÓN (0)` con el tipo de ventana activa y el contador.
- **Barra blanca de navegación**: un solo ítem, `Catálogo`.
- **Patrón de listado**: migas `Inicio / Sección` → H1 → sidebar de estados (círculo de
  color + etiqueta, activo enmarcado en azul) → toolbar (buscar + selects + botón azul de
  exportar) → tabla de **cabecera negra, texto blanco en mayúsculas** → `No se han
  encontrado resultados` → paginación `‹ 1 ›` + selector `Mostrar 12`.
- **Footer gris**: `© 2026 | LEJAN BRAND` + Facebook, Instagram, LinkedIn, YouTube, TikTok.

### Vistas

| # | Ruta | H1 / migas | Contenido literal | Estado hoy |
|---|---|---|---|---|
| 1 | `/login` | — | Tarjeta sobre degradado azul: logo, Email, Password, "¿Olvidaste tu contraseña?", `INICIAR SESIÓN`, "¿NO TIENES CUENTA?" + texto de alta y `Crear una cuenta` | Parcial (login propio en `shop.html`) |
| 2 | post-login | `SELECCIONA AHORA TUS CREDENCIALES:` | Una fila por credencial: avatar, `TEST 5` / `CLIENTE` / `Administrador`, botón `SELECCIONAR` | **No existe** |
| 3 | `/dashboard` | `Haz tu pedido` | Carrusel hero a ancho completo (~3.4:1, sin texto superpuesto, dots) + grid de **2 tarjetas-imagen**: `Reposición` y `Programación` (icono carrito arriba-izq, rótulo grande abajo-dcha) | **No existe** |
| 4 | `/catalog/catalog` | `Catálogo · 99 artículos` | Facetas `LÍNEAS` (Catálogo/Accesorios/Calzado/Limpieza), `MODELO` (buscar), `DISPONIBILIDAD` (Disponible/Consultar/< 10u), `GRUPO DE EDAD` (Adulto/Kids), `SILUETA`, `COLECCIÓN` (+ `Ver más`). Toolbar: `Desc. Stock`, selector `Listado`/Grid, `Ordenar por: Destacados`. Fila: foto, nombre en mayúsculas + **corazón favorito**, `Referencia:`, `SILUETA`/`COLECCIÓN`, `PVD 52,00 €`, y matriz de tallas (8 por fila, cabecera negra con la talla — **y el precio por talla en kids**, tallas 36–46 adulto / 21–35 kids, celda = input cantidad + `(stock)` con punto semáforo, tope `(+99)`) | **Hecho al ~60 %** en `shop.html` |
| 5 | `/checkout` | migas `Inicio / Catálogo / Checkout` | `Cliente: Test 5 (C100057)` + `ELIMINAR CARRITO` (rojo outline), `EDITAR`, `DESCARGAR EXCEL`. Ficha: `FECHA`, `REF. PEDIDO CLIENTE`, `MÉTODO DE PAGO`, `TIPO`, `DIRECCIÓN DE ENVÍO`, `FACTURACIÓN`, `OBSERVACIONES`. `Productos en tu pedido (Total unidades N)` + `GUARDAR EN FAVORITOS`. Panel derecho: aviso de bloqueo, `TERMINAR PEDIDO`, `Subtotal` / `Total (sin impuestos)` / `Transporte → Portes gratis` / `Total (con impuestos)`, checkbox de condiciones | Parcial (drawer local) |
| 6 | `/shopping-carts` | `Carritos favoritos` | Tabla `NOMBRE · FECHA · PROPIETARIO/A · UNIDADES`; vacío: `No hay carritos guardados`; sin buscador ni `Mostrar` | **No existe** |
| 7 | `/orders` | `Pedidos` | Estados: Todos, `Facturado` (azul), `Envío Parcial` (lima), `Enviado` (amarillo), `Cancelado` (rojo), `Abierto` (verde). Toolbar: `Buscar...`, `Temporada`, `Fechas`, `EXPORTAR PEDIDOS`. Tabla `N. DE PEDIDO · FECHA · REF. DE CLIENTE · TIPO · TOTAL UNIDADES · IMPORTE · ESTADO` | **No existe** |
| 8 | `/delivery-notes` | `Albaranes` | Estados: Todos, `Facturados` (verde), `No Facturados` (naranja). Toolbar: `Buscar...`, `Temporada`, `Fechas`, `EXPORTAR LISTADO`. Tabla `REFERENCIA · FECHA · FACTURADO · IMPORTE · DIRECCIÓN · ENLACE TRASNPORTE` (errata literal del original) | **No existe** |
| 9 | `/invoices` | `Facturas` | Estados: Todas, `Vencida` (rojo), `Cobradas` (verde), `Parcial` (amarillo), `A Crédito` (azul), `Pendiente De…` (naranja). Toolbar: `Buscar...`, `Temporada`, `Ordenar por`, `EXPORTAR LISTADO`. Tabla `Nº DE FACTURA · FECHA · FORMA DE PAGO · IMPORTE · DEUDA PENDIENTE · ESTADO` | **No existe** |
| 10 | `/sat` | `Devoluciones` | Estados: Todos, `Confirmado` (verde), `Pendiente` (naranja), `Rechazado` (rojo). Toolbar: `Buscar...` + `⊕ NUEVA DEVOLUCIÓN`. Tabla `IMG · CÓDIGO · FECHA · CLIENTE · TIPO · HORARIO · BULTOS · ITEMS · ESTADO · RESOLUCIÓN` | **No existe** |
| 11 | `/statistics` | `Estadísticas` | Selects `Temporada`, `Catálogo`, `Fecha de inicio`, `Fecha de fin`; H2 `Ventas totales por meses (01/08/2025 - 17/08/2026)` + gráfico de barras | **No existe** |
| 12 | `/business` | `{nombre cliente}` | `Datos generales` + `EDITAR`: `EMAIL`, `TELÉFONO`, `TELÉFONO SECUNDARIO`, `WEB`, `NOMBRE COMERCIAL`, `EMAIL FACTURACIÓN`. `Datos fiscales de la empresa` + `EDITAR`: `RAZÓN SOCIAL`, `CIF`, `PAÍS`, `CÓDIGO POSTAL`, `PROVINCIA`, `CIUDAD`, `DIRECCIÓN`, checkbox recargo de equivalencia | **No existe** |
| 13 | `/profile` | `Bienvenido, {email}` | `Mis datos` (`NOMBRE`, `EMAIL`, `ROL`, `IDIOMA`) + `EDITAR`; `Preferencias` (`MOSTRAR PRECIOS`, `MODO LISTADO (ESCRITORIO)`, `MODO LISTADO (MOVIL)`, `DIRECCIÓN DE ENVÍO POR DEFECTO`) + `EDITAR`; banda `Cambiar contraseña` con 3 campos | **No existe** |
| 14 | `/contact` | `Contacto` | `Puedes enviarnos este formulario o escribirnos vía tiendas@lejanbrand.com`; `Asunto:*`, `Email de contacto:`, `Adjuntar archivo:` (`SELECCIONAR FICHERO`), `Mensaje:*`, botón `ENVIAR SOLICITUD` | **No existe** |

Idiomas: es / en / fr / it (títulos de portada: *Haz tu pedido · Place your order · Passez
votre commande · Effettua il tuo ordine*).

### Gap resumido
- **Front**: 1 de 14 vistas; falta cascarón, enrutado, i18n, chrome y 13 vistas.
- **Catálogo**: `shop.html` tiene matriz, semáforo, filtros de línea/disponibilidad, carrito
  y ventanas; **le faltan** facetas de atributo (edad, silueta, colección), `Ordenar por`,
  Listado/Grid, favorito, `Desc. Stock`, `Consultar`, tope `(+99)` y **precio por talla**.
- **Backend**: no hay endpoints de lectura para el cliente de pedidos/albaranes/facturas,
  aunque los datos ya están en `sync_documents` como payloads del conector.
- **Identidad (bloqueante nº 1)**: `AppUser` no tiene vínculo con `client`; sin él no hay
  filtrado de documentos, ni precios por cliente, ni selector de credenciales.
- **Devoluciones**: `/sat` **no** son los documentos de devolución de BC (bultos, horario de
  recogida, resolución, foto): es un flujo propio del portal → tablas propias.
- **CMS**: "Web / Contenido" está como *Próximamente*; no hay modelo de datos para banners.

---

## 2. Decisión de arquitectura del front

**Módulos ES nativos servidos desde `wwwroot`, no fichero único.** Catorce vistas en un solo
HTML superarían las 4.000 líneas e impedirían trabajar en paralelo; los navegadores soportan
`<script type="module">` e `import()` dinámico, así que hay separación por vista y carga
diferida **sin bundler ni paso de build**. El enrutado es por History API con las rutas
reales (`/es/es/orders`), no hash, porque el cliente pide paridad de URLs y basta un
`MapFallbackToFile` en el backend.

```
wwwroot/portal/
  index.html  cascarón: header, nav, <main>, footer, drawer de carrito
  app.css     tokens de marca (#0d0d0d, #fff, azul #2b2bff, Archivo Black) + layout
  js/  boot.js (arranque) · router.js (/{market}/{lang}/{view}, import() diferido) ·
       api.js (JWT, 401→login) · state.js (sesión, cliente, ventana, carrito, prefs) ·
       i18n.js + i18n/{es,en,fr,it}.json · format.js (moneda/fecha es-ES)
  js/ui/     table, pager, status-rail, toolbar, drawer, modal, empty, size-matrix,
             cart, carousel
  js/views/  login, credentials, dashboard, catalog, checkout, carts, orders,
             delivery-notes, invoices, sat, statistics, business, profile, contact
```

`shop.html` se mantiene intacto hasta que `views/catalog.js` supere sus criterios; entonces
`/shop` redirige al portal y el fichero se borra.

---

## 3. Portada configurable desde el CMS

**Tabla `portal_content`** (migración EF Core nueva): `Key` + `Locale` (PK compuesta),
`Json` (jsonb), `UpdatedAt`, `UpdatedBy`. Claves iniciales: `dashboard.hero` (carrusel),
`dashboard.tiles` (las dos tarjetas Reposición/Programación), `login.background`,
`footer.social`. `Locale` admite `*` para contenido común.

Forma de un elemento: `{ id, order, active, imageUrl, imageUrlMobile, alt, title, subtitle,
ctaText, ctaHref, publishFrom, publishTo }`. Sin `ctaHref` el banner no es clicable; las
fechas permiten programar campañas.

**Media**: `POST /api/admin/media` (multipart, valida tipo y tamaño) guarda en
`wwwroot/media/portal/` y devuelve `{url}`; `GET /api/admin/media`; `DELETE /api/admin/media/{file}`.

**Endpoints**
- `GET /api/portal/content/{key}?locale=es` — autenticado; solo elementos activos y en
  ventana de publicación, ordenados.
- `GET /api/admin/content` · `GET|PUT|DELETE /api/admin/content/{key}?locale=` — upsert con
  validación de esquema.

**Editor en `admin.html`**: entrada de nav **Web → Contenido de portada** (sustituye al
placeholder), con lista reordenable de banners, formulario por elemento (subida con
previsualización, textos, CTA, fechas, activo), pestañas de idioma, botón *Publicar* y
vista previa del grid tal como lo verá el cliente.

---

## 4. Fases

### Fase 0 — Cascarón, identidad de cliente y navegación
**Objetivo**: portal navegable con el chrome completo y el usuario vinculado a su cliente.

**Ficheros**: migración de `AppUser`, `Auth/AuthEndpoints.cs`, `Sync/SyncEndpoints.cs`,
`Portal/PortalEndpoints.cs` (nuevo), `Program.cs`; `wwwroot/portal/index.html`, `app.css`,
`js/{boot,router,api,state,i18n,format}.js`, `views/{login,credentials}.js` + stubs.

**Backend**: `AppUser.ClientExternalId`, `ClientNumber`, `Role`, `Culture`; provisión del
usuario desde `PUT /api/clients/{clientId}/users/admin` (hoy solo guarda el `sync_document`);
claims `clientId`/`clientNumber` en el JWT; `GET /api/portal/me` →
`{ email, rol, culture, credentials[], client:{ id, number, name, fiscalInfo, canShop,
productSegments, payMethods, creditInfo, shippingAddresses[] } }` proyectado de los
`sync_documents` `client`, `client-user` y `shipping-address`; `MapFallbackToFile`.

**Aceptación**
- `/es/es/orders` recargado en el navegador sirve el portal (no 404).
- Header, barra `Catálogo` y footer coinciden con `01-dashboard.png` y `03-orders.png`:
  buscador, bloque de usuario a dos líneas, ojo, `ES ▾`, botón azul `REPOSICIÓN (0)`,
  `© 2026 | LEJAN BRAND` + 5 iconos sociales.
- Login reproduce `00-inicio.png` (incluidos los textos de "¿NO TIENES CUENTA?") y tras
  entrar aparece la pantalla `SELECCIONA AHORA TUS CREDENCIALES:` de `01-tras-login.png`.
- Cambiar idioma en la URL cambia el título de portada al literal de cada idioma.
- Tests xUnit (TDD): provisión de usuario desde el sync, claims del token, `me` filtrado por
  cliente, fallback de rutas.

**No entra**: contenido real de las vistas, catálogo, banners, permisos por rol de agente.

---

### Fase 1 — Portada configurable + CMS Web/Contenido
**Objetivo**: `/dashboard` idéntico a `01-dashboard.png`, alimentado desde `/admin`.

**Ficheros**: `Data/PortalContent.cs` + migración, `Admin/ContentEndpoints.cs`,
`Admin/MediaEndpoints.cs`, `Portal/PortalEndpoints.cs`; `views/dashboard.js`,
`ui/carousel.js`; `wwwroot/admin.html`.

**Backend**: los endpoints de §3.

**Aceptación**
- La portada muestra carrusel a ancho completo con dots y, bajo el H1 `Haz tu pedido`, las
  dos tarjetas-imagen `Reposición` y `Programación` con icono de carrito, que fijan la
  ventana de servicio activa y llevan al catálogo.
- Sustituir imagen o texto en `/admin` y recargar `/es/es/dashboard` refleja el cambio sin
  desplegar.
- Un elemento con `publishTo` pasado desaparece de la portada y sigue visible en el CMS.
- El buscador del header lanza la búsqueda en el catálogo con el término aplicado.
- Tests: CRUD del bloque, filtro por ventana de publicación, rechazo de payload inválido,
  subida de media con tipo no permitido.

**No entra**: contenido de otras vistas, versionado/rollback, CDN.

---

### Fase 2 — Catálogo, checkout y carritos favoritos
**Objetivo**: la compra completa dentro del portal, con paridad con `17-catalog-catalog.png`,
`20-header-ojo.png` y `16-checkout.png`.

**Ficheros**: `Shop/ShopEndpoints.cs`, `Portal/CartEndpoints.cs`, `Data/Cart.cs` + migración;
`views/{catalog,checkout,carts}.js`, `ui/{size-matrix,cart,toolbar,pager}.js`.

**Backend**
- `GET /api/shop/catalog`: paginación, búsqueda, orden (`Destacados`/`Relevancia`), facetas
  por atributo (edad, silueta, colección) desde `AttributesJson`, **precio por talla** y
  **precios del cliente** (ofertas por `clientId`/`groupIds` con la prioridad del contrato 03).
- `GET /api/shop/stock-export.csv` para `Desc. Stock`.
- Favoritos de modelo: `GET|PUT|DELETE /api/portal/favorites/{modelId}`.
- Carritos: tabla `carts` (`Id, ClientId, UserId, Name, ServiceWindowId, LinesJson, IsFavorite,
  CreatedAt, UpdatedAt`); `GET|POST /api/portal/carts`, `GET|PUT|DELETE /api/portal/carts/{id}`,
  `GET /api/portal/carts/{id}/export.csv` (CSV con BOM, abre en Excel).

**Aceptación**
- La fila de catálogo reproduce la de la captura: foto, nombre en mayúsculas + corazón,
  `Referencia:`, `SILUETA`/`COLECCIÓN`, `PVD`, matriz de 8 celdas por fila con cabecera
  negra, punto semáforo, `(+99)` y precio por talla en artículos kids (21–35).
- Las 6 facetas de la sidebar filtran y el toggle del ojo oculta migas, H1 y `LÍNEAS`.
- El checkout muestra la ficha de 7 campos, los 5 botones literales, los 4 totales, el
  aviso rojo con carrito vacío y el checkbox de condiciones.
- `GUARDAR EN FAVORITOS` crea un carrito que aparece en `/shopping-carts` con `NOMBRE ·
  FECHA · PROPIETARIO/A · UNIDADES` y se puede recuperar; vacío dice `No hay carritos guardados`.
- `TERMINAR PEDIDO` deja el pedido en "pendiente de envío a BC" y vacía el carrito.
- Tests: precios y precio-por-talla del cliente, facetas, orden, CRUD de carritos, CSV.

**No entra**: envío real a BC, PDFs, modo Grid (solo Listado en esta fase).

---

### Fase 3 — Documentos del cliente: pedidos, albaranes y facturas
**Objetivo**: las 3 vistas de consulta leyendo los payloads que el conector ya deja en
`sync_documents`.

**Ficheros**: `Portal/DocumentEndpoints.cs`, `Portal/DocumentProjections.cs`;
`views/{orders,delivery-notes,invoices}.js`, `ui/{table,status-rail,modal}.js`.

**Backend** (todo filtra por el `clientId` del token; nunca se devuelve documento ajeno)
- `GET /api/portal/orders?search=&status=&season=&from=&to=&skip=&take=` sobre
  `EntityType='order'` con `payload->>'clientId'` del token, **excluyendo** devoluciones
  (`type='NOT_DEFINED'` o importes negativos). Proyección: `externalReference`, `orderedDate`,
  `purchaseOrderId`, `type`, suma de `items[].transactionInfo.info.quantity`,
  `totals.total`, `status` (`open|partially-shipped|shipped|invoiced|canceled` → *Abierto,
  Envío Parcial, Enviado, Facturado, Cancelado*).
- `GET /api/portal/orders/{id}`: cabecera + `items[]` (nombre `es_ES`, talla vía
  `productInfo.sku`, cantidad, `quantityDelivered`, precio, descuento, IVA, estado de línea).
- `GET /api/portal/delivery-notes[...]` y `/{id}`: `number`, `deliveryDate`, `isInvoiced`,
  `totals.total`, `shippingAddress` resumida y **`transportUrlTrack`** para `ENLACE TRASNPORTE`.
- `GET /api/portal/invoices[...]` y `/{id}`: `number`, `issueDate`, `payMethodName.es_ES`,
  `totals.total`, deuda pendiente (`total` si `status='Unpaid'`, 0 si `Paid`),
  `payments[0].dueDate` para calcular `Vencida`, `status`.
- `GET /api/portal/{orders|delivery-notes|invoices}/export.csv` para los botones EXPORTAR.

**Aceptación**
- Las 3 vistas reproducen `03-orders.png`, `04-delivery-notes.png` y `05-invoices.png`:
  mismas columnas y en el mismo orden, mismos filtros de estado con sus colores, misma
  toolbar, mismo vacío `No se han encontrado resultados`, paginación `‹ 1 ›` y `Mostrar 12`.
- El albarán con `transportUrlTrack` abre el seguimiento en pestaña nueva; sin él, celda vacía.
- Un usuario del cliente A no ve ningún documento del cliente B (test explícito).
- `EXPORTAR` descarga un CSV con las mismas columnas visibles y los filtros aplicados.
- Tests: filtrado por cliente, exclusión de devoluciones, mapeo de estados, paginación, CSV.

**No entra**: descarga de PDF (necesita OData de BC → Fase BC), `/sat`.

---

### Fase 4 — Cuenta, empresa, estadísticas, contacto y devoluciones
**Objetivo**: cerrar la paridad de menú.

**Ficheros**: `Portal/AccountEndpoints.cs`, `Portal/SatEndpoints.cs`, `Data/ReturnRequest.cs`
+ migración; `views/{profile,business,statistics,contact,sat}.js`.

**Backend**
- `GET|PUT /api/portal/profile` (nombre, idioma) y **preferencias** (`MOSTRAR PRECIOS`,
  modo listado escritorio/móvil, dirección de envío por defecto) en `portal_user_prefs`.
- `POST /api/portal/password` (verifica la actual).
- `GET /api/portal/business` (datos generales + fiscales + direcciones) y
  `POST /api/portal/business/change-request`: los cambios se registran como **solicitud**,
  no se escriben en BC en esta fase.
- `POST /api/portal/contact` (multipart con adjunto) → guarda y envía a `tiendas@lejanbrand.com`.
- `GET /api/portal/statistics?season=&from=&to=` → importe facturado por mes del cliente a
  partir de las facturas de `sync_documents`.
- Devoluciones propias del portal: tabla `return_requests` (`Code, ClientId, CreatedAt, Type,
  PickupSlot, Packages, Items, Status, Resolution, PhotoUrl`), `GET /api/portal/returns`,
  `POST /api/portal/returns`, `GET /api/portal/returns/{id}`; alta desde `NUEVA DEVOLUCIÓN`.

**Aceptación**
- `/profile` muestra `Bienvenido, {email}`, las tarjetas `Mis datos` y `Preferencias` con sus
  4 campos cada una, los botones `EDITAR` y la banda gris `Cambiar contraseña` (`09-profile.png`).
- `/business` reproduce `Datos generales` y `Datos fiscales de la empresa` con todos los
  campos y el checkbox de recargo de equivalencia (`10-business.png`).
- `/statistics` muestra los 4 filtros y el H2 `Ventas totales por meses (dd/mm/aaaa -
  dd/mm/aaaa)` con barras coherentes con las facturas del cliente (`11-statistics.png`).
- `/contact` envía el formulario con adjunto y confirma (`07-contact.png`).
- `/sat` lista con las 10 columnas y los 4 filtros de estado, y `NUEVA DEVOLUCIÓN` crea una
  solicitud visible en el listado (`08-sat.png`).
- Cambiar `MOSTRAR PRECIOS` a PVP se refleja en el catálogo.
- Tests: cambio de contraseña (casos de error), agregado mensual, alta de devolución,
  persistencia de preferencias.

**No entra**: escritura de datos maestros en BC, gestión de usuarios adicionales del cliente,
resolución/flujo interno de la devolución en el CMS.

---

### Fase BC — Integración de salida (no bloquea la paridad visual)
**Objetivo**: convertir el portal en transaccional contra Business Central.

- `POST /api/portal/orders` → construye y entrega el pedido a BC (contrato 04 §5 y
  `06-api-odata-bc.md`), con reintentos y estado de envío visible en `/orders`.
- `GET /api/portal/documents/{type}/{id}/pdf` → API OData `salesDocuments` por `systemId`
  (contrato 05 §7); devuelve y cachea la URL pública del blob. Botón PDF por fila en pedidos,
  albaranes y facturas (hasta entonces el botón se muestra deshabilitado, no oculto).
- Sincronización del estado del pedido enviado vía `/api/orders/search`.
- Traslado a BC de las devoluciones creadas en `/sat`.

**Aceptación**: un pedido creado en el portal aparece en BC y vuelve por el sync con su
`externalReference`; el PDF de una factura se descarga desde su fila.

---

## 5. Riesgos y decisiones abiertas
- **Toggle del ojo**: inferido (oculta migas, H1 y `LÍNEAS`, y cambia el orden a
  `Relevancia`); confirmar semántica con el cliente antes de la Fase 2.
- **Precio por talla**: la oferta por `productId` debe ganar a la de `modelId` (contrato 03).
- **Ninguna captura tiene filas de datos**: los botones de acción por fila son inferencia;
  validarlos con datos reales antes de cerrar la Fase 3.
- **`Temporada`/`Catálogo`** no tienen origen claro (`seasonId` llega siempre `""`): si no
  hay dato, select vacío y deshabilitado.
- **Imágenes de producto**: dependen de `model-image`; si faltan, placeholder neutro.
