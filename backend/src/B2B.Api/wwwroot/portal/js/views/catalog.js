// Vista 4 — /{market}/{lang}/catalog/catalog (17-catalog-catalog.png y 20-header-ojo.png).
//
// El catálogo comprable del portal: rail de facetas (LÍNEAS, MODELO, DISPONIBILIDAD y
// las que BC publique como atributos del modelo), toolbar con "Desc. Stock", orden y
// vista, y una fila por artículo con su matriz de tallas. El pedido se hace aquí:
// cada celda es una cantidad que entra directa en el carrito de la ventana activa.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state } from '../state.js';
import { href } from '../router.js';
import { icons } from '../ui/icons.js';
import { pageHead } from '../ui/chrome.js';
import { sizeMatrix, bindMatrix } from '../ui/size-matrix.js';
import { toolbar } from '../ui/toolbar.js';
import { pager, bindPager } from '../ui/pager.js';

const AVAILABILITY = ['available', 'consult', 'low'];   // orden de la referencia
const FACET_PREVIEW = 3;                                // valores antes de "Ver más"

// "MOSTRAR PRECIOS" de /profile (09-profile.png): la ficha muestra UNA sola línea
// de precio, la de la preferencia (PVD por defecto, PVP si así lo pide el usuario),
// como en la referencia (m7). Si el artículo no trae el precio elegido se enseña el
// que haya, nunca una fila sin precio.
const preferred = () => (state.me?.prefs?.showPrices === 'pvp' ? 'pvp' : 'pvd');

const priceOf = (item, kind) =>
  item?.[kind] == null ? null : { label: t(`catalog.price.${kind}`), value: item[kind] };

const main = item => priceOf(item, preferred()) ?? priceOf(item, preferred() === 'pvd' ? 'pvp' : 'pvd');

// Vista por defecto del catálogo: la preferencia MODO LISTADO del perfil (una para
// escritorio y otra para móvil), que el selector de la toolbar puede pisar en la
// sesión (la elección se guarda en la URL y en prefs). 'grid' | 'list'.
const defaultView = () => {
  const prefs = state.me?.prefs || {};
  const mobile = window.matchMedia('(max-width:48rem)').matches;
  return (mobile ? prefs.listMobile : prefs.listDesktop) === 'grid' ? 'grid' : 'list';
};

/** URL de la ficha del producto: /{market}/{lang}/product/{referencia} */
const productHref = item =>
  `${href('product')}/${encodeURIComponent(item.reference || item.modelId)}`;

// La ficha del listado solo lleva SILUETA y COLECCIÓN (m6). GRUPO DE EDAD sigue
// existiendo como faceta del rail, pero no como columna del artículo. Los CÓDIGOS
// (style code, color code) son identificadores internos del ERP, no descriptores:
// al comprador no le dicen nada en la fila (en la ficha el style code va en la línea
// de referencia). Las claves las nombra Business Central, así que se comparan
// normalizadas (sin acentos ni signos) y en las cuatro lenguas del portal; lo que
// no reconoce, lo deja pasar.
const HIDDEN_ATTRS = new Set(['grupo-de-edad', 'style-code', 'color-code']);

// \u2500\u2500 Vocabulario del cat\u00e1logo (M-1) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
//
// Business Central manda las familias y los atributos con el MISMO texto en los
// cuatro idiomas, as\u00ed que a\u00f1adir `locale` a la petici\u00f3n no basta: quien traduce el
// vocabulario del rail es el portal. El backend acompa\u00f1a cada t\u00e9rmino de una clave
// estable \u2014`families[].id`, `keySlug` del atributo y `slug` del valor\u2014 y de la
// etiqueta que \u00e9l ha resuelto; aqu\u00ed se busca la clave en el diccionario y, si no
// est\u00e1, se cae en esa etiqueta y por \u00faltimo en el texto crudo. Las claves de FILTRO
// (`key` y `value`) no se tocan nunca: son las que viajan en la URL y en la API.
//
// El slug se normaliza aqu\u00ed porque el del servidor conserva los acentos
// ("colecci\u00f3n"); el diccionario usa claves sin acentos y separadas por guiones.
const slugKey = value => String(value ?? '').normalize('NFD')
  .replace(/[\u0300-\u036f]/g, '')
  .toLowerCase()
  .replace(/[^a-z0-9]+/g, '-')
  .replace(/^-+|-+$/g, '');

const vocab = (prefix, slug, label, raw) => {
  const key = slugKey(slug || raw);
  if (key) {
    const translated = t(`catalog.${prefix}.${key}`);
    if (translated !== `catalog.${prefix}.${key}`) return translated;
  }
  return label || raw || '';
};

/**
 * ¿El valor del atributo ya está escrito en el nombre del artículo? Un ERP que nombra
 * "BUND RETRO Field Yellow" manda además LINE=BUND RETRO y COLOR NAME=Field Yellow:
 * pintarlos como chips es repetir el título. Se compara por palabras normalizadas
 * (sin acentos ni caja, guion como separador) y con límite de palabra, así "ELAN"
 * casa con "ELAN Aegean" pero una letra suelta no casa con cualquier nombre.
 */
const inName = (name, value) => {
  const needle = slugKey(value);
  return needle.length > 1 && `-${slugKey(name)}-`.includes(`-${needle}-`);
};

/**
 * Atributos de la ficha, ya traducidos. Con `attributeList` (servidor nuevo) se usan
 * los slug; con el objeto `attributes` de siempre el slug sale del propio nombre, que
 * para "Grupo de edad" da exactamente la misma clave. Fuera: los ocultos de siempre y
 * los valores que el nombre del artículo ya dice (solo actúa cuando se repiten).
 */
const cardAttrs = item => {
  const list = Array.isArray(item.attributeList) && item.attributeList.length
    ? item.attributeList.map(entry => ({
        slug: entry.keySlug || entry.key,
        raw: entry.value,
        label: vocab('attr', entry.keySlug, entry.label, entry.key),
        value: vocab('attrValue', entry.valueSlug, entry.valueLabel, entry.value)
      }))
    : Object.entries(item.attributes || {}).map(([key, value]) => ({
        slug: key,
        raw: value,
        label: vocab('attr', key, '', key),
        value: vocab('attrValue', value, '', value)
      }));

  return list.filter(entry => !HIDDEN_ATTRS.has(slugKey(entry.slug)) && !inName(item.name, entry.raw));
};

/** F-03: la etiqueta del corazón describe lo que hace el clic, no lo que ya es */
const favLabel = on => t(on ? 'catalog.favoriteOff' : 'catalog.favorite');

// El estado del rail vive en la URL: compartir el enlace comparte el filtro, y el
// buscador del header entra por ?q= sin que el catálogo tenga que saber de él.
function readQuery() {
  const params = new URLSearchParams(location.search);
  const attributes = {};
  for (const [key, value] of params) {
    if (key.startsWith('a.') && key.length > 2 && value) attributes[key.slice(2)] = value.split(',');
  }
  const urlView = params.get('view');
  return {
    q: params.get('q') || '',
    family: params.get('family') || '',
    availability: (params.get('availability') || '').split(',').filter(Boolean),
    attributes,
    sort: params.get('sort') || (state.prefs.focus ? 'relevance' : 'featured'),
    // El modo lo fija la URL si viene; si no, la última elección de la sesión y por
    // último la preferencia del perfil. El selector de la toolbar manda sobre todo.
    view: urlView === 'grid' || urlView === 'list' ? urlView : (state.prefs.catalogView || defaultView()),
    skip: Number(params.get('skip')) || 0,
    take: Number(params.get('take')) || 24
  };
}

function writeQuery(query, { keepScroll = false } = {}) {
  const params = new URLSearchParams();
  if (query.q) params.set('q', query.q);
  if (query.family) params.set('family', query.family);
  if (query.availability.length) params.set('availability', query.availability.join(','));
  for (const [key, values] of Object.entries(query.attributes))
    if (values.length) params.set(`a.${key}`, values.join(','));
  if (query.sort && query.sort !== 'featured') params.set('sort', query.sort);
  // Listado es el modo por defecto: solo la cuadrícula deja rastro en la URL
  if (query.view === 'grid') params.set('view', 'grid');
  if (query.skip) params.set('skip', String(query.skip));
  if (query.take !== 24) params.set('take', String(query.take));

  const search = params.toString();
  history.replaceState({}, '', location.pathname + (search ? `?${search}` : ''));
  if (!keepScroll) window.scrollTo({ top: 0, behavior: 'smooth' });
}

const apiQuery = query => {
  const params = new URLSearchParams();
  if (query.q) params.set('q', query.q);
  if (query.family) params.set('family', query.family);
  if (query.availability.length) params.set('availability', query.availability.join(','));
  for (const [key, values] of Object.entries(query.attributes))
    if (values.length) params.set(`a.${key}`, values.join(','));
  params.set('sort', query.sort);
  params.set('window', windowId());
  // M-1: el vocabulario del catálogo (familias, facetas y valores de atributo) lo
  // traduce el backend según la locale de la ruta. Mientras no esté implementado
  // seguirá llegando en español, que es lo que se veía hasta ahora.
  params.set('locale', lang());
  return params;
};

/** Ventana de servicio activa: el tipo lo elige la portada, el id lo trae el catálogo */
let windows = [];
const windowId = () => {
  const type = state.prefs.window === 'scheduled' ? 'SCHEDULED' : 'REPLENISHMENT';
  return (windows.find(w => w.orderType === type) || windows[0])?.id || '';
};

// Si la instancia no publica el tipo de ventana preferido, la preferencia se realinea
// al que existe con `state.alignWindow(windows)`: la MISMA regla que aplican el chrome
// (cabecera y carrito) y la ficha, así ninguna pantalla habla de una ventana que no hay.

export default async function catalog(host) {
  let query = readQuery();
  let data = null;

  // En móvil la foto es la que vende: siete controles antes del primer artículo lo
  // dejaban en la mitad inferior de la pantalla. Herramientas y filtros se recogen en
  // UN desplegable cerrado (mismo lenguaje que los lookups de faceta) y solo el buscador
  // queda a la vista. La decisión se toma al pintar, como la vista por defecto.
  const mobile = window.matchMedia('(max-width:48rem)').matches;
  const filtersHtml = `<div class="cat-filters" id="filters" aria-label="${esc(t('catalog.filters'))}"></div>`;

  host.innerHTML = `
    <div class="page catalog-top">
      <nav class="cat-ribbon is-pending" id="ribbon" aria-label="${esc(t('ribbon.label'))}"></nav>
      <div class="cat-bar">
        <div class="cat-title">
          ${pageHead(t('nav.catalog'), [t('nav.catalog')],
            '<span class="cat-count" id="count" role="status"></span><span class="cat-adapted" id="adapted" hidden></span>')}
        </div>
        ${mobile ? '' : '<div id="tools"></div>'}
      </div>
      ${mobile ? `
      <div class="cat-filters" id="searchHost"></div>
      <details class="cat-lookup cat-mobile-tools">
        <summary><span class="lk-name">${esc(t('catalog.filters'))}</span>${icons.chevron(14)}</summary>
        <div class="cat-lookup-panel">
          <div id="tools"></div>
          ${filtersHtml}
        </div>
      </details>` : filtersHtml}
      <div class="cat-list" id="list">
        <div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div>
      </div>
      <div id="pager"></div>
    </div>`;

  const ribbon = host.querySelector('#ribbon');
  const filters = host.querySelector('#filters');
  // Buscador: dentro de la barra de filtros en escritorio, fuera del desplegable en móvil
  const searchHost = host.querySelector('#searchHost');
  const searchInput = () => host.querySelector('#modelSearch');
  const list = host.querySelector('#list');
  const tools = host.querySelector('#tools');
  const count = host.querySelector('#count');
  // "Catalogo adaptado a tu cuenta": aparece en cuanto el servidor dice `restricted`
  const adapted = host.querySelector('#adapted');
  adapted.innerHTML = `${icons.eye(13)}<span>${esc(t('ribbon.adapted'))}</span>`;
  const pagerHost = host.querySelector('#pager');

  const itemsById = {};
  bindMatrix(list, itemsById, { onChange: () => paintPrices() });

  // ── Cinta de navegación (banda bajo CATÁLOGO|LOOKBOOK) ─────────────────────
  // Las entradas las COMPUTA EL SERVIDOR para el actor (familias visibles + los
  // valores de atributo que active /manage): aquí solo se pintan, con el mismo
  // diccionario del rail. La cinta NO es otra fuente de filtros: cada clic muta
  // el MISMO `query` que los desplegables y syncRibbon() lee ese `query` de
  // vuelta — estado único, cinta y desplegables siempre coherentes.
  // Viaja DENTRO de la respuesta del catálogo (`ribbon`, 14a-4/14a-8: estable, sin
  // filtros de query) y se pinta EN LA MISMA pasada que sustituye los esqueletos: sin
  // segunda petición y sin salto de layout (el hueco .is-pending ya reserva su altura).
  let ribbonEntries = null;                 // null = aún no llegó; [] = sin cinta
  let ribbonTotal = 0;                      // modelos del surtido completo (recuento de TODO)
  let ribbonBuilt = false;

  // El filtro de atributo viaja con clave y valor CRUDOS de BC ("Silueta",
  // "Melrose"); la cinta trae slugs de servidor ("silueta", "melrose"). Este mapa
  // slug→crudo se alimenta de las facetas de cada respuesta y ACUMULA: una faceta
  // recortada por otro filtro no borra lo ya aprendido.
  const ribbonVocab = new Map();
  const feedRibbonVocab = () => {
    for (const attr of data.facets?.attributes || []) {
      const rec = ribbonVocab.get(attr.keySlug) || { key: attr.key, values: new Map() };
      rec.key = attr.key;
      for (const v of attr.values || []) rec.values.set(v.slug, v.value);
      ribbonVocab.set(attr.keySlug, rec);
    }
  };

  // slug de servidor → { key, value } CRUDOS para query.attributes. Clave y valor
  // siguen la misma escalera: el vocabulario de facetas primero; si no lo conoce
  // (facetas recortadas por otro filtro, o un deep-link cuyo PRIMER catálogo ya viene
  // filtrado), lo que la propia cinta trae del servidor (`entry.rawKey`/`entry.raw`),
  // que es lo que el filtro compara tal cual; el slug queda de último recurso.
  // Sin `rawKey` una clave con espacios ("Grupo de edad" → "grupo-de-edad") no casaba
  // y el catálogo se quedaba vacío con la pestaña encendida.
  const resolveEntry = entry => {
    const rec = ribbonVocab.get(entry.attributeId);
    return {
      key: rec?.key || entry.rawKey || entry.attributeId,
      value: rec?.values.get(entry.value) ?? entry.raw ?? entry.value
    };
  };

  const familyOf = entry => entry.key.startsWith('family:') ? entry.key.slice(7) : '';

  const entryOn = entry => {
    if (entry.kind === 'family') return query.family === familyOf(entry);
    const { key, value } = resolveEntry(entry);
    return (query.attributes[key] || []).includes(value);
  };

  // La etiqueta del servidor ya viene resuelta por locale Y con los títulos que
  // configura /manage (Cinta del catálogo): si trae algo más elaborado que el dato
  // crudo (un título propio, el nombre real del maestro), manda ella. El diccionario
  // local solo traduce las entradas "sin vestir" (etiqueta == dato crudo, p. ej. la
  // familia cuyo nombre es su propio id capitalizado) — antes pisaba los títulos.
  const ribbonText = entry => {
    const raw = entry.kind === 'family' ? familyOf(entry) : (entry.raw ?? entry.value);
    if (entry.label && entry.label.trim().toLowerCase() !== String(raw ?? '').trim().toLowerCase())
      return entry.label;
    return entry.kind === 'family'
      ? vocab('family', familyOf(entry), entry.label, entry.label)
      : vocab('attrValue', entry.value, entry.label, entry.label);
  };

  // Con 0 o 1 entrada no hay nada que navegar: una cinta de una sola pestaña parece
  // un filtro roto. En su lugar, y en el MISMO hueco, una línea de contexto que dice
  // cuál es el surtido de la cuenta — "Tu surtido: Calzado · 38 artículos" (UX-M1).
  function buildRibbon() {
    if (ribbonBuilt) return;
    ribbonBuilt = true;
    ribbon.classList.remove('is-pending');
    const entries = ribbonEntries || [];
    if (entries.length <= 1) {
      if (!entries.length) { ribbon.hidden = true; return; }
      const [only] = entries;
      ribbon.classList.add('is-context');
      ribbon.removeAttribute('aria-label');
      ribbon.setAttribute('role', 'presentation');
      ribbon.innerHTML = `<p class="cat-context">${icons.list(14)}<span>${esc(t('ribbon.yourRange',
        { label: ribbonText(only), n: only.count ?? ribbonTotal }))}</span></p>`;
      return;
    }
    // TODO lleva el recuento del surtido completo, como cada pestaña lleva el suyo (D-B2)
    const chips = [`<button type="button" class="rib-chip" data-rib="all" aria-pressed="false">${esc(t('ribbon.all'))}${
      ribbonTotal ? `<span class="rib-count">${ribbonTotal}</span>` : ''}</button>`];
    // Separador en CADA cambio de plano: familias -> atributo A -> atributo B (D-M2).
    // Antes solo se separaba familia/atributo y dos atributos seguidos se leían como uno.
    let lastGroup = 'family';
    entries.forEach((entry, index) => {
      const group = entry.kind === 'family' ? 'family' : `attr:${entry.attributeId}`;
      if (group !== lastGroup) { chips.push('<span class="rib-sep" aria-hidden="true"></span>'); lastGroup = group; }
      chips.push(`<button type="button" class="rib-chip" data-rib="${index}" aria-pressed="false">${esc(ribbonText(entry))}${
        entry.count ? `<span class="rib-count">${entry.count}</span>` : ''}</button>`);
    });
    ribbon.innerHTML = `
      <button type="button" class="rib-arrow rib-prev" aria-label="${esc(t('ribbon.prev'))}">${icons.left(16)}</button>
      <div class="rib-rail">${chips.join('')}</div>
      <button type="button" class="rib-arrow rib-next" aria-label="${esc(t('ribbon.next'))}">${icons.right(16)}</button>`;
    wireRibbon();
  }

  function wireRibbon() {
    const rail = ribbon.querySelector('.rib-rail');
    rail.addEventListener('click', event => {
      const chipEl = event.target.closest('.rib-chip');
      if (!chipEl) return;
      if (chipEl.dataset.rib === 'all') {
        // TODO = sin filtro de lo que la cinta gobierna (familia + sus atributos);
        // buscador y disponibilidad no son suyos y se respetan.
        const attributes = { ...query.attributes };
        for (const entry of ribbonEntries) if (entry.kind === 'attr') delete attributes[resolveEntry(entry).key];
        query = { ...query, family: '', attributes, skip: 0 };
        return load({ keepScroll: true });
      }
      const entry = ribbonEntries[Number(chipEl.dataset.rib)];
      if (!entry) return;
      if (entry.kind === 'family') {
        // Mismo mecanismo que el desplegable LÍNEAS; re-clic = "Todas"
        query = { ...query, family: entryOn(entry) ? '' : familyOf(entry), skip: 0 };
      } else {
        // Mismo mecanismo que la casilla de su faceta: alterna el valor del grupo
        const { key, value } = resolveEntry(entry);
        const set = new Set(query.attributes[key] || []);
        set.has(value) ? set.delete(value) : set.add(value);
        const attributes = { ...query.attributes };
        if (set.size) attributes[key] = [...set]; else delete attributes[key];
        query = { ...query, attributes, skip: 0 };
      }
      load({ keepScroll: true });
    });

    // Flechas solo si desborda (patrón del raíl de relacionados); en táctil no
    // existen: el raíl se desplaza con el dedo y el snap lo deja en un chip.
    const prev = ribbon.querySelector('.rib-prev');
    const next = ribbon.querySelector('.rib-next');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const step = () => Math.max(rail.clientWidth * 0.8, 120);
    const update = () => {
      const max = rail.scrollWidth - rail.clientWidth;
      ribbon.classList.toggle('has-nav', max > 4);
      prev.disabled = rail.scrollLeft <= 1;
      next.disabled = rail.scrollLeft >= max - 1;
      // El desvanecido del raíl solo por el lado que todavía esconde pestañas
      ribbon.classList.toggle('at-start', prev.disabled);
      ribbon.classList.toggle('at-end', next.disabled);
    };
    prev.onclick = () => rail.scrollBy({ left: -step(), behavior: reduced ? 'auto' : 'smooth' });
    next.onclick = () => rail.scrollBy({ left: step(), behavior: reduced ? 'auto' : 'smooth' });
    rail.addEventListener('scroll', update, { passive: true });
    // El listener de resize se vigila con un temporizador barato además del propio
    // evento (patrón de related.js): si la cinta se desconecta al cambiar de vista,
    // se retira aunque el usuario nunca redimensione — sin acumulación de listeners.
    const onResize = () => (ribbon.isConnected ? update() : dispose());
    const watchdog = setInterval(() => { if (!ribbon.isConnected) dispose(); }, 15_000);
    // El ancho del raíl también cambia sin que se redimensione la ventana: al salir del
    // modo sin distracciones (el ojo del header oculta la cinta) el raíl pasa de 0 a su
    // ancho real, y sin recalcular las flechas se quedaban con el estado de cuando no
    // se veía. Un observador de tamaño lo cubre; si el navegador no lo trae, queda el
    // listener de resize de siempre.
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(() => update()) : null;
    observer?.observe(rail);
    function dispose() {
      removeEventListener('resize', onResize);
      clearInterval(watchdog);
      observer?.disconnect();
    }
    addEventListener('resize', onResize);

    // El estado activo puede llegar por deep-link: el chip encendido se centra
    requestAnimationFrame(() => {
      const on = rail.querySelector('.rib-chip.on');
      if (on) rail.scrollLeft = Math.max(0, on.offsetLeft - rail.clientWidth / 2 + on.offsetWidth / 2);
      update();
    });
  }

  // Relee `query` y enciende lo que toque (llamado en cada load, venga el cambio
  // de la cinta o de los desplegables — una sola fuente de verdad).
  function syncRibbon() {
    // Con la línea de contexto (<=1 entrada) no hay chips que sincronizar
    if (!ribbonBuilt || !ribbon.querySelector('[data-rib="all"]')) return;
    let any = false;
    ribbon.querySelectorAll('.rib-chip').forEach(chipEl => {
      if (chipEl.dataset.rib === 'all') return;
      const on = entryOn(ribbonEntries[Number(chipEl.dataset.rib)]);
      any = any || on;
      chipEl.classList.toggle('on', on);
      chipEl.setAttribute('aria-pressed', String(on));
    });
    const all = ribbon.querySelector('[data-rib="all"]');
    all.classList.toggle('on', !any);
    all.setAttribute('aria-pressed', String(!any));
  }

  // El H1 y las migas se ocultan con el ojo; el recuento se queda, como en 20-header-ojo.png
  const paintCount = () => {
    const total = data?.total ?? 0;
    count.textContent = t(total === 1 ? 'catalog.countOne' : 'catalog.count', { n: total });
  };

  const paintPrices = () => {
    // El TOTAL de línea (unidades × precio de cada talla) se refresca sin repintar la
    // matriz y solo existe con unidades: sin ellas la fila no lleva segundo importe.
    // El precio unitario ya va en la ficha; repetirlo bajo la matriz se leía como
    // tarifa duplicada en las 24 filas de la página.
    for (const article of list.querySelectorAll('.item')) {
      let units = 0, amount = 0;
      for (const input of article.querySelectorAll('.sz-qty')) {
        const qty = Number(input.value) || 0;
        units += qty;
        amount += qty * (Number(input.dataset.price) || 0);
      }
      const line = article.querySelector('.matrix-price');
      if (!line) continue;
      line.querySelector('.item-units').textContent = units ? t('catalog.units', { n: units }) : '';
      line.querySelector('.item-total').textContent = units ? eur(amount) : '';
      line.hidden = !units;
    }
  };

  async function load({ keepScroll = false } = {}) {
    writeQuery(query, { keepScroll });
    // Al filtrar sin salir de la página el listado se apaga y deja de aceptar
    // clics, y el recuento (role="status") anuncia "Cargando…": sin esto marcar
    // una casilla no daba señal ninguna hasta que llegaba la respuesta.
    list.setAttribute('aria-busy', 'true');
    list.classList.add('is-loading');
    count.textContent = t('catalog.loading');

    const params = apiQuery(query);
    params.set('skip', String(query.skip));
    params.set('take', String(query.take));

    try {
      data = await api.get(`/api/shop/catalog?${params}`);
    } catch {
      list.removeAttribute('aria-busy');
      list.classList.remove('is-loading');
      // Sin catálogo no habrá cinta: el hueco reservado se retira o deja una regla
      // suelta y un salto de 62px cuando el usuario reintenta.
      ribbon.classList.remove('is-pending');
      if (!ribbonBuilt) ribbon.hidden = true;
      count.textContent = '';
      list.innerHTML = `<div class="panel"><b>${esc(t('catalog.errorTitle'))}</b>${esc(t('catalog.errorBody'))}</div>`;
      return;
    }

    // La primera llamada aún no sabe qué ventana corresponde al tipo activo (el
    // catálogo es quien las trae); si no coincide se repite una sola vez.
    const knew = windows.length > 0;
    windows = data.windows || [];
    state.alignWindow(windows);
    if (!knew && windowId() && data.window !== windowId()) return load({ keepScroll: true });

    // Tras el await el usuario puede haber navegado: si la vista ya no está
    // conectada, no se pinta nada encima de la siguiente (patrón del checkout).
    if (!list.isConnected) return;
    // La cinta viene DENTRO de la primera respuesta (es estable: no cambia al filtrar)
    // y se pinta en esta misma pasada, con los esqueletos todavía en pantalla.
    if (ribbonEntries === null) {
      ribbonEntries = data.ribbon?.entries || [];
      ribbonTotal = Number(data.ribbon?.total) || 0;
    }
    feedRibbonVocab();
    buildRibbon();
    adapted.hidden = !data.restricted;

    list.removeAttribute('aria-busy');
    list.classList.remove('is-loading');
    paintCount();
    renderFilters();
    syncRibbon();
    paintTools();
    paintList();
    paintPager();
  }

  // ── Barra de filtros (lookups desplegables arriba) ─────────────────────────
  // Sustituye al rail lateral: búsqueda + un desplegable (<details>) por Familia,
  // Disponibilidad y cada atributo, más chips de filtros activos. Se construye UNA vez
  // y solo se sincronizan estados (no se reconstruye al filtrar → no se cierran los
  // desplegables ni se pierde el foco del buscador).
  let filtersBuilt = false;
  let filtersSig = null;   // firma de lo construido: se reconstruye solo si cambia
  const allLinesLabel = () => { const l = t('catalog.allLines'); return l === 'catalog.allLines' ? 'Todas' : l; };
  const cssSafe = s => String(s).replace(/"/g, '\\"');

  // El servidor RECORTA cada faceta con los demás filtros (con q=1040 todas quedan en
  // un valor), así que lo que una faceta "tiene" se aprende del conjunto más ancho
  // visto, acumulando entre respuestas como el vocabulario de la cinta. Por valor se
  // guarda su ficha (slug, etiqueta) y el MAYOR recuento visto.
  const familySeen = new Map();   // id → { id, label }
  const facetSeen = new Map();    // keySlug normalizado → { attr, values: Map(valor → ficha) }
  const learnFacets = () => {
    for (const line of data.facets?.families || []) familySeen.set(line.id, line);
    for (const attr of data.facets?.attributes || []) {
      const key = slugKey(attr.keySlug || attr.key);
      const rec = facetSeen.get(key) || { attr, values: new Map() };
      rec.attr = attr;
      for (const v of attr.values || []) {
        const prev = rec.values.get(v.value);
        // Sin recuento (servidor antiguo) no se puede llamar identificador a nada
        const count = v.count == null ? Infinity : Number(v.count) || 0;
        rec.values.set(v.value, { ...v, count: Math.max(prev?.count ?? 0, count) });
      }
      facetSeen.set(key, rec);
    }
  };

  // Una faceta merece desplegable solo si DISCRIMINA: con un único valor no filtra
  // nada (Género = Mujer en las 49); con un artículo por valor es un identificador
  // (style code, nombre de color), no un filtro; los códigos son del ERP; y lo que la
  // cinta ya navega (LINE) no se repite debajo. Regla de datos, no de marca: ALMA solo
  // pierde facetas que no filtran nada.
  const ribbonKeys = () => new Set((ribbonEntries || [])
    .filter(entry => entry.kind === 'attr').map(entry => slugKey(entry.attributeId)));
  const discriminates = key => {
    const values = facetSeen.get(key)?.values;
    if (!values || values.size < 2) return false;
    if ([...values.values()].every(v => v.count <= 1)) return false;
    return !/-code$/.test(key) && !ribbonKeys().has(key);
  };
  const visibleFacets = () => [...facetSeen.keys()].filter(discriminates).map(key => facetSeen.get(key));
  const visibleLines = () => (familySeen.size > 1 ? [...familySeen.values()] : []);

  // Se construye UNA vez y se reconstruye solo cuando llega conocimiento nuevo (una
  // faceta que empieza a discriminar, valores que no se habían visto): un deep-link
  // estrecho (?q=1040) no puede dejar la barra sin facetas para siempre.
  function renderFilters() {
    learnFacets();
    const sig = [visibleLines().length ? `family:${familySeen.size}` : '',
      ...visibleFacets().map(rec => `${rec.attr.key}:${rec.values.size}`)].join('|');
    if (!filtersBuilt || sig !== filtersSig) { buildFilters(); filtersBuilt = true; filtersSig = sig; }
    syncFilters();
  }

  const catCheck = (group, value, label, checked) => `
    <label class="cat-check">
      <input type="checkbox" data-group="${esc(group)}" value="${esc(value)}" ${checked ? 'checked' : ''}>
      <span>${esc(label)}</span>
    </label>`;

  function buildFilters() {
    // Familias y facetas del conocimiento ACUMULADO, no de la respuesta de turno (que
    // puede venir recortada por otro filtro). "Líneas" solo con 2+ familias: con una
    // sola el desplegable no elige nada.
    const lines = visibleLines();
    const facets = visibleFacets();
    const collator = new Intl.Collator(lang(), { sensitivity: 'base' });

    const searchHtml = `
      <div class="cat-search">${icons.search(16)}
        <input type="search" id="modelSearch" value="${esc(query.q)}"
          placeholder="${esc(t('catalog.searchPlaceholder'))}" aria-label="${esc(t('catalog.facet.model'))}">
      </div>`;
    // En móvil el buscador vive fuera del desplegable y se pinta UNA vez: una faceta
    // nueva reconstruye los lookups, no la caja donde el usuario está escribiendo.
    if (searchHost && !searchInput()) searchHost.innerHTML = searchHtml;

    filters.innerHTML = `
      ${searchHost ? '' : searchHtml}
      ${lines.length ? `
      <details class="cat-lookup" data-lk="family">
        <summary><span class="lk-name">${esc(t('catalog.facet.lines'))}</span><span class="lk-count" data-count="family"></span>${icons.chevron(14)}</summary>
        <div class="cat-lookup-panel">
          <button type="button" class="lk-line" data-family="">${esc(allLinesLabel())}</button>
          ${lines.map(line => `<button type="button" class="lk-line" data-family="${esc(line.id)}">${esc(vocab('family', line.id, line.label, line.id))}</button>`).join('')}
        </div>
      </details>` : ''}
      <details class="cat-lookup" data-lk="availability">
        <summary><span class="lk-name">${esc(t('catalog.facet.availability'))}</span><span class="lk-count" data-count="availability"></span>${icons.chevron(14)}</summary>
        <div class="cat-lookup-panel">
          ${AVAILABILITY.map(id => catCheck('availability', id, t(`catalog.availability.${id}`), query.availability.includes(id))).join('')}
        </div>
      </details>
      ${facets.map(({ attr, values }) => {
        const title = vocab('attr', attr.keySlug, attr.label, attr.key);
        const sel = query.attributes[attr.key] || [];
        const options = [...values.values()]
          .map(v => ({ ...v, text: vocab('attrValue', v.slug, v.label, v.value) }))
          .sort((a, b) => collator.compare(a.text, b.text));
        return `
        <details class="cat-lookup" data-lk="a.${esc(attr.key)}">
          <summary><span class="lk-name">${esc(title)}</span><span class="lk-count" data-count="a.${esc(attr.key)}"></span>${icons.chevron(14)}</summary>
          <div class="cat-lookup-panel scroll">
            ${options.map(v => catCheck(`a.${attr.key}`, v.value, v.text, sel.includes(v.value))).join('')}
          </div>
        </details>`;
      }).join('')}
      <div class="cat-active" id="catActive"></div>`;

    wireFilters();
  }

  function wireFilters() {
    const search = searchInput();
    let timer;
    search.oninput = () => {
      clearTimeout(timer);
      timer = setTimeout(() => { query = { ...query, q: search.value.trim(), skip: 0 }; load({ keepScroll: true }); }, 300);
    };

    // Familia (selección única) → cierra su desplegable al elegir
    filters.querySelectorAll('.lk-line').forEach(button => {
      button.onclick = () => {
        query = { ...query, family: button.dataset.family, skip: 0 };
        button.closest('details')?.removeAttribute('open');
        load();
      };
    });

    // Facetas multi (disponibilidad / atributos) — delegado
    filters.addEventListener('change', event => {
      const input = event.target.closest('.cat-check input');
      if (!input) return;
      const group = input.dataset.group;
      if (group === 'availability') {
        const set = new Set(query.availability);
        input.checked ? set.add(input.value) : set.delete(input.value);
        query = { ...query, availability: [...set], skip: 0 };
      } else {
        const key = group.slice(2);
        const set = new Set(query.attributes[key] || []);
        input.checked ? set.add(input.value) : set.delete(input.value);
        const attributes = { ...query.attributes };
        if (set.size) attributes[key] = [...set]; else delete attributes[key];
        query = { ...query, attributes, skip: 0 };
      }
      load({ keepScroll: true });
    });

    // Solo un desplegable abierto a la vez
    filters.querySelectorAll('details.cat-lookup').forEach(d =>
      d.addEventListener('toggle', () => {
        if (d.open) filters.querySelectorAll('details.cat-lookup[open]').forEach(o => { if (o !== d) o.removeAttribute('open'); });
      }));

    // Chips de filtros activos (delegado)
    filters.querySelector('#catActive').addEventListener('click', event => {
      const chipBtn = event.target.closest('[data-remove]');
      if (chipBtn) return removeFilter(chipBtn.dataset.remove, chipBtn.dataset.value);
      if (event.target.closest('#clearAll')) {
        query = { ...query, q: '', family: '', availability: [], attributes: {}, skip: 0 };
        const s = searchInput(); if (s) s.value = '';
        load();
      }
    });
  }

  function removeFilter(group, value) {
    if (group === 'family') query = { ...query, family: '', skip: 0 };
    else if (group === 'q') { query = { ...query, q: '', skip: 0 }; const s = searchInput(); if (s) s.value = ''; }
    else if (group === 'availability') query = { ...query, availability: query.availability.filter(v => v !== value), skip: 0 };
    else {
      const key = group.slice(2);
      const rest = (query.attributes[key] || []).filter(v => v !== value);
      const attributes = { ...query.attributes };
      if (rest.length) attributes[key] = rest; else delete attributes[key];
      query = { ...query, attributes, skip: 0 };
    }
    load({ keepScroll: true });
  }

  // Actualiza estados sin reconstruir la barra (mantiene desplegables abiertos y foco).
  function syncFilters() {
    const lines = data.facets?.families || [];
    const attributes = data.facets?.attributes || [];

    const search = searchInput();
    if (search && document.activeElement !== search) search.value = query.q;

    filters.querySelectorAll('.cat-check input').forEach(input => {
      const group = input.dataset.group;
      input.checked = group === 'availability'
        ? query.availability.includes(input.value)
        : (query.attributes[group.slice(2)] || []).includes(input.value);
    });

    filters.querySelectorAll('.lk-line').forEach(b => b.classList.toggle('on', (b.dataset.family || '') === query.family));
    const famCount = filters.querySelector('[data-count="family"]');
    if (famCount) {
      const label = query.family ? vocab('family', query.family, lines.find(l => l.id === query.family)?.label, query.family) : '';
      famCount.textContent = label ? `: ${label}` : '';
    }
    filters.querySelector('details[data-lk="family"]')?.classList.toggle('active', !!query.family);

    const setCount = (lk, n) => {
      const el = filters.querySelector(`[data-count="${cssSafe(lk)}"]`);
      if (el) el.textContent = n ? ` (${n})` : '';
      filters.querySelector(`details[data-lk="${cssSafe(lk)}"]`)?.classList.toggle('active', n > 0);
    };
    setCount('availability', query.availability.length);
    for (const attr of attributes) setCount(`a.${attr.key}`, (query.attributes[attr.key] || []).length);

    paintActiveChips(lines, attributes);
  }

  function paintActiveChips(lines, attributes) {
    const host2 = filters.querySelector('#catActive');
    const chips = [];
    if (query.q) chips.push(chip('q', '', `“${query.q}”`));
    if (query.family) chips.push(chip('family', '', vocab('family', query.family, lines.find(l => l.id === query.family)?.label, query.family)));
    for (const id of query.availability) chips.push(chip('availability', id, t(`catalog.availability.${id}`)));
    for (const attr of attributes)
      for (const v of (query.attributes[attr.key] || [])) {
        const val = attr.values.find(x => x.value === v);
        chips.push(chip(`a.${attr.key}`, v, vocab('attrValue', val?.slug, val?.label, v)));
      }
    host2.innerHTML = chips.length
      ? chips.join('') + `<button type="button" class="cat-clear" id="clearAll">${esc(t('catalog.clear'))}</button>`
      : '';
  }

  const chip = (group, value, label) => `
    <button type="button" class="cat-chip" data-remove="${esc(group)}" data-value="${esc(value)}">
      ${esc(label)} ${icons.close(13)}</button>`;

  // ── Toolbar ────────────────────────────────────────────────────────────────
  function paintTools() {
    tools.innerHTML = toolbar({ sort: query.sort, view: query.view });
    paintWindowSwitch();

    tools.querySelector('#sortMode').onchange = event => {
      query = { ...query, sort: event.target.value, skip: 0 };
      load();
    };

    // Cambiar de vista no vuelve a pedir el catálogo: los datos ya están, solo se
    // repinta. La elección se guarda en la URL (writeQuery) y en las preferencias
    // de sesión para que sobreviva a ir a una ficha y volver.
    tools.querySelectorAll('[data-view]').forEach(button => {
      button.onclick = () => {
        query = { ...query, view: button.dataset.view === 'grid' ? 'grid' : 'list' };
        state.prefs = { ...state.prefs, catalogView: query.view };
        writeQuery(query, { keepScroll: true });
        paintTools();
        paintList();
      };
    });

    tools.querySelector('#exportStock').onclick = async event => {
      const button = event.currentTarget;
      button.disabled = true;
      try { await api.download(`/api/shop/stock-export.csv?${apiQuery(query)}`, 'stock.csv'); }
      finally { button.disabled = false; }
    };

    // Catálogo (filtrado) en PDF con marca y la tarifa del cliente
    tools.querySelector('#exportPdf').onclick = async event => {
      const button = event.currentTarget;
      button.disabled = true;
      // El nombre real lo pone el servidor con la marca de la instancia (Content-Disposition);
      // este es solo el respaldo si esa cabecera no llegara.
      try { await api.download(`/api/portal/catalog.pdf?${apiQuery(query)}`, 'catalogo.pdf'); }
      finally { button.disabled = false; }
    };
  }

  // ── Ventana de pedido ──────────────────────────────────────────────────────
  // Reposición | Programación SS27: la ventana con la que se compra se elige AQUÍ,
  // donde se compra, y no solo en el tile de la portada (hasta ahora el único
  // conmutador). Escribe la misma preferencia que la portada y recarga: `windowId()`
  // resuelve el id y el carrito sigue siendo el de la ventana activa. Solo se pinta
  // con dos TIPOS de ventana (la preferencia es por tipo: con dos programadas no
  // habría nada que elegir); con una no hay nada que conmutar. El nombre lo trae
  // cada ventana ("Programación SS27"), no el diccionario.
  function paintWindowSwitch() {
    const typeOf = w => (w.orderType === 'SCHEDULED' ? 'scheduled' : 'replenishment');
    const byType = new Map();
    for (const w of windows) if (!byType.has(typeOf(w))) byType.set(typeOf(w), w);
    if (byType.size < 2) return;
    const active = state.prefs.window === 'scheduled' ? 'scheduled' : 'replenishment';
    tools.querySelector('.toolbar')?.insertAdjacentHTML('afterbegin', `
      <div class="tb-seg tb-window" role="group" aria-label="${esc(t('catalog.window'))}">
        ${[...byType].map(([type, w]) => `<button type="button" class="tb-seg-opt${type === active ? ' on' : ''}"
          data-window="${type}" aria-pressed="${type === active ? 'true' : 'false'}">${esc(w.name || t(`window.${type}`))}</button>`).join('')}
      </div>`);
    tools.querySelectorAll('.tb-window [data-window]').forEach(button => {
      button.onclick = () => {
        if (button.dataset.window === active) return;
        state.prefs = { ...state.prefs, window: button.dataset.window };
        query = { ...query, skip: 0 };
        load({ keepScroll: true });
      };
    });
  }

  // ── Listado ────────────────────────────────────────────────────────────────
  function paintList() {
    const items = data.items || [];
    for (const key of Object.keys(itemsById)) delete itemsById[key];
    for (const item of items) itemsById[item.modelId] = item;

    if (!items.length) {
      list.innerHTML = `
        <div class="panel">
          <b>${esc(t('catalog.emptyTitle'))}</b>${esc(t(data.restricted ? 'catalog.emptyBodyRestricted' : 'catalog.emptyBody'))}
          <div><button type="button" class="link" id="clearFilters">${esc(t('catalog.clear'))}</button></div>
        </div>`;
      list.querySelector('#clearFilters').onclick = () => {
        query = { ...query, q: '', family: '', availability: [], attributes: {}, skip: 0 };
        load();
      };
      return;
    }

    const stockWindow = data.window;
    const lines = Object.fromEntries(
      state.cartLines().map(line => [`${line.modelId}|${line.size}`, line]));

    // Cuadrícula: tarjetas con foto grande que enlazan a la ficha (el pedido se hace
    // allí). Listado: la fila de siempre con la matriz de tallas en línea, intacta.
    if (query.view === 'grid') {
      list.classList.add('is-grid');
      list.innerHTML = `<div class="cat-grid">${items.map(item => card(item)).join('')}</div>`;
    } else {
      list.classList.remove('is-grid');
      list.innerHTML = items.map(item => article(item, stockWindow, lines)).join('');
    }
    bindFavorites();
    paintPrices();
  }

  // Tarjeta de la cuadrícula (referencia thehoffbrand.com): foto 4:5 protagonista con
  // zoom al hover, nombre, referencia, precio y un CTA a la ficha. Toda la tarjeta
  // navega con un enlace estirado (::after); el corazón queda por encima, clicable.
  function card(item) {
    const price = main(item);
    const target = productHref(item);
    return `
      <article class="pcard" data-model="${esc(item.modelId)}">
        <div class="pcard-media">
          ${item.imageUri
            ? `<img src="${esc(item.imageUri)}" alt="" loading="lazy" decoding="async">`
            : `<span class="item-art" aria-hidden="true">${icons.shoe(52)}</span>`}
          <button type="button" class="item-fav pcard-fav" data-model="${esc(item.modelId)}"
            aria-pressed="${item.favorite ? 'true' : 'false'}"
            aria-label="${esc(favLabel(item.favorite))}" title="${esc(favLabel(item.favorite))}">
            ${item.favorite ? icons.heartOn(22) : icons.heart(22)}
          </button>
        </div>
        <div class="pcard-body">
          <h3 class="pcard-name"><a class="pcard-link" href="${esc(target)}">${esc(item.name)}</a></h3>
          <p class="pcard-ref">${esc(t('catalog.reference'))} <b>${esc(item.reference || '')}</b></p>
          ${price ? `<p class="pcard-price"><span>${price.label}</span> <b>${esc(eur(price.value))}</b></p>` : ''}
          <span class="pcard-cta" aria-hidden="true">${esc(t('catalog.viewProduct'))} ${icons.right(15)}</span>
        </div>
      </article>`;
  }

  function article(item, stockWindow, lines) {
    const attributes = cardAttrs(item);
    return `
      <article class="item" data-model="${esc(item.modelId)}">
        <div class="item-photo">
          ${item.imageUri
            ? `<img src="${esc(item.imageUri)}" alt="" loading="lazy" decoding="async">`
            : `<span class="item-art" aria-hidden="true">${icons.shoe(46)}</span>`}
        </div>

        <div class="item-info">
          <div class="item-title">
            <h2>${esc(item.name)}</h2>
            <button type="button" class="item-fav" data-model="${esc(item.modelId)}"
              aria-pressed="${item.favorite ? 'true' : 'false'}"
              aria-label="${esc(favLabel(item.favorite))}" title="${esc(favLabel(item.favorite))}">
              ${item.favorite ? icons.heartOn(22) : icons.heart(22)}
            </button>
          </div>

          <p class="item-ref">${esc(t('catalog.reference'))} <b>${esc(item.reference || '')}</b></p>

          ${attributes.length ? `<div class="item-attrs">${attributes.map(attribute => `
            <span class="tag" title="${esc(attribute.label)}">${esc(attribute.value)}</span>`).join('')}</div>` : ''}

          ${main(item) ? `
            <p class="item-price"><span>${main(item).label}</span>
              <b>${esc(eur(main(item).value))}</b></p>` : ''}
        </div>

        <div class="item-matrix">
          ${sizeMatrix(item, { windowKey: stockWindow, lines })}
          <!-- Total de LÍNEA (unidades × precio), rellenado por paintPrices() y solo
               visible con unidades: el precio unitario ya va arriba, en la ficha. -->
          <p class="item-price matrix-price" hidden><span class="item-units"></span><b class="item-total"></b></p>
        </div>
      </article>`;
  }

  // F-03: el corazón anuncia la ACCIÓN, no el estado. Marcado dice "Quitar de
  // favoritos" y sin marcar "Añadir a favoritos"; aria-pressed sigue llevando el
  // estado. Sin esto un lector de pantalla ofrecía "Añadir" sobre algo ya añadido.
  function paintFav(button, on) {
    button.setAttribute('aria-pressed', String(on));
    button.setAttribute('aria-label', favLabel(on));
    button.setAttribute('title', favLabel(on));
    button.innerHTML = on ? icons.heartOn(22) : icons.heart(22);
  }

  function bindFavorites() {
    list.querySelectorAll('.item-fav').forEach(button => {
      button.onclick = async () => {
        const modelId = button.dataset.model;
        const on = button.getAttribute('aria-pressed') !== 'true';
        paintFav(button, on);
        const item = itemsById[modelId];
        if (item) item.favorite = on;
        try {
          await (on ? api.put(`/api/portal/favorites/${encodeURIComponent(modelId)}`)
                    : api.del(`/api/portal/favorites/${encodeURIComponent(modelId)}`));
        } catch {
          // Sin red el corazón vuelve a su sitio en vez de mentir
          paintFav(button, !on);
          if (item) item.favorite = !on;
        }
      };
    });
  }

  // ── Paginación ─────────────────────────────────────────────────────────────
  function paintPager() {
    if ((data.total ?? 0) <= query.take) { pagerHost.innerHTML = ''; return; }
    pagerHost.innerHTML = pager({ total: data.total, skip: query.skip, take: query.take });
    bindPager(pagerHost, {
      take: query.take,
      onPage: skip => { query = { ...query, skip: Math.max(0, skip) }; load(); },
      onSize: take => { query = { ...query, take, skip: 0 }; load(); }
    });
  }

  // El ojo del header cambia el orden a "Relevancia" (plan §5) y esconde LÍNEAS:
  // el catálogo se entera por el evento del chrome, no por sondeo.
  const onFocus = () => {
    if (!list.isConnected) return removeEventListener('portal:focus', onFocus);
    query = { ...query, sort: state.prefs.focus ? 'relevance' : 'featured', skip: 0 };
    load({ keepScroll: true });
  };
  addEventListener('portal:focus', onFocus);

  await load({ keepScroll: true });
}
