// Definición declarativa de los maestros: cada entidad describe su listado y su
// formulario (por secciones). El render usa las clases de formulario del portal
// (.biz-section/.biz-card/.biz-grid/.acc-field) para verse idéntico al portal B2B.
//
// Tipos de campo: text · area · num · money · bool · date · i18n · i18narea ·
// select (opts|src) · multi (src). `src` = entityType del que salen las opciones.

export const OPTS = {
  priceType:  [['PVD', 'PVD · precio de venta'], ['PVP', 'PVP · informativo']],
  orderType:  [['', 'Todos los tipos'], ['SCHEDULED', 'Programación'], ['REPLENISHMENT', 'Reposición'], ['NOT_DEFINED', 'No definido']],
  windowType: [['REPLENISHMENT', 'Reposición'], ['SCHEDULED', 'Programación'], ['NOT_DEFINED', 'No definido']],
  attrType:   [['ListString', 'Lista de textos'], ['String', 'Texto'], ['Int', 'Número']],
  attrFormat: [['List', 'Lista'], ['Box', 'Caja']],
};

// Etiqueta legible de cada entidad al usarse como opción (FK)
export const FK_LABEL = {
  model: 'name.es_ES|externalReference', product: 'sku|name.es_ES',
  family: 'name.es_ES|code', category: 'name.es_ES', 'service-window': 'name.es_ES|id',
  'payment-method': 'name.es_ES|externalReference', 'client-group': 'name.es_ES|externalReference',
  client: 'name.es_ES|name|externalReference',
};

export const SCHEMAS = {
  model: {
    type: 'model', singular: 'modelo', plural: 'Modelos', icon: 'box',
    lead: 'La ficha comercial de cada modelo. Sus variantes (tallas), precios e imagen se cuelgan de aquí.',
    id: { mode: 'guid' }, defaults: { productSegments: [], attributes: {} },
    list: [['Modelo', 'name.es_ES|name'], ['Referencia', 'externalReference'], ['Familia', 'familyId'], ['Activo', 'active', 'bool']],
    sections: [{ title: 'Datos del modelo', icon: 'box', fields: [
      { k: 'name', l: 'Nombre', t: 'i18n', req: true, wide: true },
      { k: 'externalReference', l: 'Referencia comercial', t: 'text', req: true },
      { k: 'familyId', l: 'Familia', t: 'select', src: 'family' },
      { k: 'active', l: 'Visible en el catálogo', t: 'bool', def: true },
      { k: 'description', l: 'Descripción', t: 'i18narea', wide: true },
    ] }],
  },

  product: {
    type: 'product', singular: 'variante', plural: 'Variantes', icon: 'tag', fem: true,
    lead: 'Cada talla de un modelo. La talla y el modelo son lo que enlaza el stock y el precio.',
    id: { mode: 'guid' }, defaults: {},
    list: [['SKU', 'sku'], ['Nombre', 'name.es_ES'], ['Modelo', 'modelId', 'fk:model'], ['Talla', 'attributes.tallas'], ['Activo', 'active', 'bool']],
    sections: [{ title: 'Datos de la variante', icon: 'tag', fields: [
      { k: 'modelId', l: 'Modelo', t: 'select', src: 'model', req: true, wide: true },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true, wide: true },
      { k: 'sku', l: 'SKU / referencia', t: 'text', req: true },
      { k: 'attributes.tallas', l: 'Talla', t: 'text', help: 'Ej.: 38, M, Única' },
      { k: 'ean', l: 'EAN / código de barras', t: 'text' },
      { k: 'taxId', l: 'Impuesto (taxId)', t: 'text' },
      { k: 'active', l: 'Activa', t: 'bool', def: true },
    ] }],
  },

  offer: {
    type: 'offer', singular: 'precio', plural: 'Precios', icon: 'coin',
    lead: 'La tarifa de un modelo (o de una talla concreta), opcionalmente por grupo o cliente y ventana.',
    id: { mode: 'guid' }, defaults: { pricesPerUnit: [] },
    list: [['Modelo', 'offerData.modelId|modelId', 'fk:model'], ['Talla', 'offerData.productId|productId', 'fk:product'], ['Tipo', 'offerData.priceType|priceType', 'chip'], ['Precio', 'offerData.basePrice.value|basePrice.value', 'money'], ['Desde', 'offerData.stock|stock']],
    sections: [
      { title: 'Tarifa', icon: 'coin', fields: [
        { k: 'modelId', l: 'Modelo', t: 'select', src: 'model', req: true, wide: true },
        { k: 'productId', l: 'Solo una talla (opcional)', t: 'select', src: 'product', help: 'Vacío = todas las tallas del modelo.' },
        { k: 'priceType', l: 'Tipo de precio', t: 'select', opts: 'priceType', def: 'PVD' },
        { k: 'basePrice.value', l: 'Precio (€)', t: 'money', req: true },
        { k: 'stock', l: 'Desde (uds mínimas)', t: 'num', help: '0 = sin mínimo.' },
        { k: 'discounts.0.percent', l: 'Descuento %', t: 'num' },
      ] },
      { title: 'Aplicación (opcional)', icon: 'users', fields: [
        { k: 'clientGroupId', l: 'Solo para el grupo', t: 'select', src: 'client-group' },
        { k: 'clientId', l: 'Solo para el cliente', t: 'select', src: 'client' },
        { k: 'orderType', l: 'Tipo de pedido', t: 'select', opts: 'orderType' },
        { k: 'fromDate', l: 'Válido desde', t: 'date' },
        { k: 'toDate', l: 'Válido hasta', t: 'date' },
        { k: 'priority', l: 'Prioridad', t: 'num', help: 'Menor número = más prioridad.' },
      ] },
    ],
  },

  inventory: {
    type: 'inventory', singular: 'stock', plural: 'Stock', icon: 'layers',
    lead: 'Unidades disponibles de una variante en una ventana de servicio.',
    id: { mode: 'field', from: '__productId' }, defaults: { type: 'Inventory' },
    list: [['Variante', '__externalId', 'fk:product'], ['Unidades', 'stock'], ['Ventana', 'stockServiceId', 'fk:service-window'], ['Tipo', 'orderType', 'chip']],
    sections: [{ title: 'Stock', icon: 'layers', fields: [
      { k: '__productId', l: 'Variante', t: 'select', src: 'product', req: true, wide: true, help: 'El stock se cuelga de la talla.' },
      { k: 'stock', l: 'Unidades disponibles', t: 'num', req: true },
      { k: 'stockServiceId', l: 'Ventana de servicio', t: 'select', src: 'service-window', req: true },
      { k: 'orderType', l: 'Tipo de pedido', t: 'select', opts: 'orderType' },
      { k: 'entryDate', l: 'Fecha de entrada', t: 'date' },
    ] }],
  },

  'service-window': {
    type: 'service-window', singular: 'ventana de servicio', plural: 'Ventanas de servicio', icon: 'calendar', fem: true,
    lead: 'Reposición (stock inmediato) o programación (campaña con fechas). El stock y los precios se agrupan por ella.',
    id: { mode: 'slug', from: 'id' }, defaults: {},
    list: [['Código', 'id|__externalId'], ['Nombre', 'name.es_ES'], ['Tipo', 'orderType', 'chip'], ['Desde', 'from', 'date'], ['Hasta', 'to', 'date']],
    sections: [{ title: 'Ventana', icon: 'calendar', fields: [
      { k: 'id', l: 'Código', t: 'text', req: true, help: 'Identificador corto, p. ej. "reposic".' },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true },
      { k: 'orderType', l: 'Tipo', t: 'select', opts: 'windowType', def: 'REPLENISHMENT' },
      { k: 'from', l: 'Desde', t: 'date' },
      { k: 'to', l: 'Hasta', t: 'date' },
      { k: 'limit', l: 'Fecha límite de pedido', t: 'date' },
    ] }],
  },

  warehouse: {
    type: 'warehouse', singular: 'almacén', plural: 'Almacenes', icon: 'building',
    lead: 'Desde dónde se sirve el género.',
    id: { mode: 'field', from: 'code' }, defaults: { transportIds: [], markets: ['es'] },
    list: [['Código', 'code|__externalId'], ['Nombre', 'description.es_ES'], ['Ciudad', 'address.city'], ['Activo', 'active', 'bool']],
    sections: [{ title: 'Almacén', icon: 'building', fields: [
      { k: 'code', l: 'Código', t: 'text', req: true },
      { k: 'description', l: 'Nombre', t: 'i18n', req: true, wide: true },
      { k: 'active', l: 'Activo', t: 'bool', def: true },
      { k: 'address.streetAddress', l: 'Dirección', t: 'text', wide: true },
      { k: 'address.city', l: 'Ciudad', t: 'text' },
      { k: 'address.province', l: 'Provincia', t: 'text' },
      { k: 'address.zipCode', l: 'Código postal', t: 'text' },
      { k: 'address.countryIsoId', l: 'País (ISO)', t: 'text', def: 'ES' },
    ] }],
  },

  'payment-method': {
    type: 'payment-method', singular: 'forma de pago', plural: 'Formas de pago', icon: 'coin', fem: true,
    lead: 'Las opciones de pago que verá el cliente en el checkout.',
    id: { mode: 'slug', from: 'externalReference' }, defaults: {},
    list: [['Código', 'externalReference|__externalId'], ['Nombre', 'name.es_ES'], ['Orden', 'order'], ['Crédito', 'allowCredit', 'bool']],
    sections: [{ title: 'Forma de pago', icon: 'coin', fields: [
      { k: 'externalReference', l: 'Código', t: 'text', req: true },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true, wide: true },
      { k: 'order', l: 'Orden en el checkout', t: 'num' },
      { k: 'allowCredit', l: 'Permite crédito', t: 'bool' },
      { k: 'requiredForConfirm', l: 'Obligatoria para confirmar', t: 'bool' },
    ] }],
  },

  category: {
    type: 'category', singular: 'categoría', plural: 'Categorías', icon: 'folder', fem: true,
    lead: 'Agrupa modelos por familias para navegar el catálogo.',
    id: { mode: 'field', from: '__id' }, defaults: { models: [] },
    list: [['Id', '__externalId'], ['Nombre', 'name.es_ES'], ['Familias', 'search.familyIds', 'arr'], ['Activa', 'active', 'bool']],
    sections: [{ title: 'Categoría', icon: 'folder', fields: [
      { k: '__id', l: 'Id de categoría', t: 'text', req: true, wide: true, help: 'Jerárquico: catalog.familia.subfamilia' },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true, wide: true },
      { k: 'search.familyIds', l: 'Familias que agrupa', t: 'multi', src: 'family' },
      { k: 'active', l: 'Activa', t: 'bool', def: true },
    ] }],
  },

  family: {
    type: 'family', singular: 'familia', plural: 'Familias', icon: 'folder', fem: true,
    lead: 'La agrupación base de los modelos.',
    id: { mode: 'slug', from: 'code' }, defaults: { atributes: [] },
    list: [['Código', 'code|__externalId'], ['Nombre', 'name.es_ES']],
    sections: [{ title: 'Familia', icon: 'folder', fields: [
      { k: 'code', l: 'Código', t: 'text', req: true },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true, wide: true },
    ] }],
  },

  attribute: {
    type: 'attribute', singular: 'atributo', plural: 'Atributos', icon: 'tag',
    lead: 'Características de los productos (p. ej. tallas).',
    id: { mode: 'field', from: 'code' }, defaults: {},
    list: [['Código', 'code|__externalId'], ['Nombre', 'name.es_ES'], ['Tipo', 'type'], ['Web', 'visibleWeb', 'bool']],
    sections: [{ title: 'Atributo', icon: 'tag', fields: [
      { k: 'code', l: 'Código', t: 'text', req: true, help: 'Ej.: tallas' },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true },
      { k: 'type', l: 'Tipo', t: 'select', opts: 'attrType', def: 'ListString' },
      { k: 'visibleFormat', l: 'Formato', t: 'select', opts: 'attrFormat', def: 'List' },
      { k: 'visibleWeb', l: 'Visible en web', t: 'bool', def: true },
      { k: 'values', l: 'Valores', t: 'valuelist', wide: true, help: 'Un valor por línea.' },
    ] }],
  },

  'client-group': {
    type: 'client-group', singular: 'grupo de clientes', plural: 'Grupos de clientes', icon: 'users',
    lead: 'Agrupa clientes para aplicarles tarifas comunes.',
    id: { mode: 'slug', from: 'externalReference' }, defaults: {},
    list: [['Código', 'externalReference|__externalId'], ['Nombre', 'name.es_ES'], ['Formas de pago', 'paymentMethods', 'arr']],
    sections: [{ title: 'Grupo de clientes', icon: 'users', fields: [
      { k: 'externalReference', l: 'Código', t: 'text', req: true },
      { k: 'name', l: 'Nombre', t: 'i18n', req: true, wide: true },
      { k: 'paymentMethods', l: 'Formas de pago', t: 'multi', src: 'payment-method' },
    ] }],
  },

  // El CLIENTE se edita con un formulario a medida (views/client.js); aquí solo va
  // lo que necesita el LISTADO (columnas, textos).
  client: {
    type: 'client', singular: 'cliente', plural: 'Clientes', icon: 'building',
    lead: 'Empresas que compran en el portal. Cada una con su ficha fiscal, sus direcciones de envío y sus accesos.',
    list: [['Cliente', 'name.es_ES|name'], ['Código', 'externalReference'], ['Email', 'email'], ['Puede comprar', 'canShop', 'bool']],
  },

  agent: {
    type: 'agent', singular: 'agente comercial', plural: 'Agentes comerciales', icon: 'user',
    lead: 'Comerciales que entran al portal y llevan una cartera de clientes.',
    id: { mode: 'guid' }, defaults: { markets: ['es'] },
    list: [['Nombre', 'name'], ['Email', 'email'], ['Cartera', 'clientIds', 'fkarr:client']],
    sections: [{ title: 'Agente', icon: 'user', fields: [
      { k: 'email', l: 'Email', t: 'text', req: true, help: 'Con este email entra al portal.' },
      { k: 'name', l: 'Nombre', t: 'text', req: true },
      { k: 'culture', l: 'Idioma', t: 'select', opts: 'culture', def: 'es_ES' },
      { k: 'clientIds', l: 'Cartera de clientes', t: 'multi', src: 'client' },
    ] }],
  },
};

OPTS.culture = [['es_ES', 'Español'], ['en_EN', 'English'], ['fr_FR', 'Français'], ['it_IT', 'Italiano']];

// Estructura del menú lateral
export const NAV = [
  { title: 'General', items: [['dashboard', 'Resumen', 'home']] },
  { title: 'Catálogo', items: [
    ['models', 'Modelos', 'box'], ['products', 'Variantes', 'tag'], ['offers', 'Precios', 'coin'],
    ['inventory', 'Stock', 'layers'], ['service-windows', 'Ventanas', 'calendar'],
    ['categories', 'Categorías', 'folder'], ['families', 'Familias', 'folder'],
    ['attributes', 'Atributos', 'tag'], ['warehouses', 'Almacenes', 'building'],
    ['payment-methods', 'Formas de pago', 'coin'], ['images', 'Imágenes', 'image'],
    ['ribbon', 'Cinta del catálogo', 'ribbon'],
  ] },
  { title: 'Comercial', items: [
    ['clients', 'Clientes', 'building'], ['client-groups', 'Grupos', 'users'],
    ['agents', 'Agentes', 'user'], ['users', 'Accesos', 'key'],
  ] },
  { title: 'Ventas', items: [
    ['orders', 'Pedidos', 'cart'],
    ['sales-rules', 'Condiciones de venta', 'percent'],
  ] },
  { title: 'Contenido', items: [
    ['content', 'Portada', 'layout'], ['lookbook', 'Lookbook', 'book'],
  ] },
  { title: 'Integración', items: [
    ['notifications-config', 'Notificaciones', 'send'], ['notifications-log', 'Realizadas', 'list'],
    ['connections', 'Conexiones', 'layers'], ['doc-sources', 'Origen de documentos', 'fileDown'],
    ['received', 'Comunicación BC', 'activity'],
  ] },
];

// Ruta (slug del menú) → entityType del esquema, para las vistas genéricas
export const ROUTE_TYPE = {
  models: 'model', products: 'product', offers: 'offer', inventory: 'inventory',
  'service-windows': 'service-window', categories: 'category', families: 'family',
  attributes: 'attribute', warehouses: 'warehouse', 'payment-methods': 'payment-method',
  'client-groups': 'client-group', agents: 'agent',
};
