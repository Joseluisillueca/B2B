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
// existiendo como faceta del rail, pero no como columna del artículo. Las claves
// las nombra Business Central, así que se comparan normalizadas (sin acentos ni
// signos) y en las cuatro lenguas del portal; lo que no reconoce, lo deja pasar.
const HIDDEN_ATTRS = new Set(['grupo-de-edad']);

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
 * Atributos de la ficha, ya traducidos. Con `attributeList` (servidor nuevo) se usan
 * los slug; con el objeto `attributes` de siempre el slug sale del propio nombre, que
 * para "Grupo de edad" da exactamente la misma clave.
 */
const cardAttrs = item => {
  const list = Array.isArray(item.attributeList) && item.attributeList.length
    ? item.attributeList.map(entry => ({
        slug: entry.keySlug || entry.key,
        label: vocab('attr', entry.keySlug, entry.label, entry.key),
        value: vocab('attrValue', entry.valueSlug, entry.valueLabel, entry.value)
      }))
    : Object.entries(item.attributes || {}).map(([key, value]) => ({
        slug: key,
        label: vocab('attr', key, '', key),
        value: vocab('attrValue', value, '', value)
      }));

  return list.filter(entry => !HIDDEN_ATTRS.has(slugKey(entry.slug)));
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

export default async function catalog(host) {
  let query = readQuery();
  let data = null;

  host.innerHTML = `
    <div class="page catalog-top">
      <div class="cat-bar">
        <div class="cat-title">
          ${pageHead(t('nav.catalog'), [t('nav.catalog')],
            '<span class="cat-count" id="count" role="status"></span>')}
        </div>
        <div id="tools"></div>
      </div>
      <div class="cat-filters" id="filters" aria-label="${esc(t('catalog.filters'))}"></div>
      <div class="cat-list" id="list">
        <div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div>
      </div>
      <div id="pager"></div>
    </div>`;

  const filters = host.querySelector('#filters');
  const list = host.querySelector('#list');
  const tools = host.querySelector('#tools');
  const count = host.querySelector('#count');
  const pagerHost = host.querySelector('#pager');

  const itemsById = {};
  bindMatrix(list, itemsById, { onChange: () => paintPrices() });

  // El H1 y las migas se ocultan con el ojo; el recuento se queda, como en 20-header-ojo.png
  const paintCount = () => {
    const total = data?.total ?? 0;
    count.textContent = t(total === 1 ? 'catalog.countOne' : 'catalog.count', { n: total });
  };

  const paintPrices = () => {
    // El total del artículo (unidades pedidas) se refresca sin repintar la matriz
    for (const article of list.querySelectorAll('.item')) {
      const units = [...article.querySelectorAll('.sz-qty')]
        .reduce((sum, input) => sum + (Number(input.value) || 0), 0);
      const badge = article.querySelector('.item-units');
      if (!badge) continue;
      badge.textContent = units ? t('catalog.units', { n: units }) : '';
      badge.hidden = !units;
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
      count.textContent = '';
      list.innerHTML = `<div class="panel"><b>${esc(t('catalog.errorTitle'))}</b>${esc(t('catalog.errorBody'))}</div>`;
      return;
    }

    // La primera llamada aún no sabe qué ventana corresponde al tipo activo (el
    // catálogo es quien las trae); si no coincide se repite una sola vez.
    const knew = windows.length > 0;
    windows = data.windows || [];
    if (!knew && windowId() && data.window !== windowId()) return load({ keepScroll: true });

    list.removeAttribute('aria-busy');
    list.classList.remove('is-loading');
    paintCount();
    renderFilters();
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
  const allLinesLabel = () => { const l = t('catalog.allLines'); return l === 'catalog.allLines' ? 'Todas' : l; };
  const cssSafe = s => String(s).replace(/"/g, '\\"');

  function renderFilters() {
    if (!filtersBuilt) { buildFilters(); filtersBuilt = true; }
    syncFilters();
  }

  const catCheck = (group, value, label, checked) => `
    <label class="cat-check">
      <input type="checkbox" data-group="${esc(group)}" value="${esc(value)}" ${checked ? 'checked' : ''}>
      <span>${esc(label)}</span>
    </label>`;

  function buildFilters() {
    const lines = data.facets?.families || [];
    const attributes = data.facets?.attributes || [];

    filters.innerHTML = `
      <div class="cat-search">${icons.search(16)}
        <input type="search" id="modelSearch" value="${esc(query.q)}"
          placeholder="${esc(t('catalog.searchPlaceholder'))}" aria-label="${esc(t('catalog.facet.model'))}">
      </div>
      <details class="cat-lookup" data-lk="family">
        <summary><span class="lk-name">${esc(t('catalog.facet.lines'))}</span><span class="lk-count" data-count="family"></span>${icons.chevron(14)}</summary>
        <div class="cat-lookup-panel">
          <button type="button" class="lk-line" data-family="">${esc(allLinesLabel())}</button>
          ${lines.map(line => `<button type="button" class="lk-line" data-family="${esc(line.id)}">${esc(vocab('family', line.id, line.label, line.id))}</button>`).join('')}
        </div>
      </details>
      <details class="cat-lookup" data-lk="availability">
        <summary><span class="lk-name">${esc(t('catalog.facet.availability'))}</span><span class="lk-count" data-count="availability"></span>${icons.chevron(14)}</summary>
        <div class="cat-lookup-panel">
          ${AVAILABILITY.map(id => catCheck('availability', id, t(`catalog.availability.${id}`), query.availability.includes(id))).join('')}
        </div>
      </details>
      ${attributes.map(attr => {
        const title = vocab('attr', attr.keySlug, attr.label, attr.key);
        const sel = query.attributes[attr.key] || [];
        return `
        <details class="cat-lookup" data-lk="a.${esc(attr.key)}">
          <summary><span class="lk-name">${esc(title)}</span><span class="lk-count" data-count="a.${esc(attr.key)}"></span>${icons.chevron(14)}</summary>
          <div class="cat-lookup-panel scroll">
            ${attr.values.map(v => catCheck(`a.${attr.key}`, v.value, vocab('attrValue', v.slug, v.label, v.value), sel.includes(v.value))).join('')}
          </div>
        </details>`;
      }).join('')}
      <div class="cat-active" id="catActive"></div>`;

    wireFilters();
  }

  function wireFilters() {
    const search = filters.querySelector('#modelSearch');
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
        const s = filters.querySelector('#modelSearch'); if (s) s.value = '';
        load();
      }
    });
  }

  function removeFilter(group, value) {
    if (group === 'family') query = { ...query, family: '', skip: 0 };
    else if (group === 'q') { query = { ...query, q: '', skip: 0 }; const s = filters.querySelector('#modelSearch'); if (s) s.value = ''; }
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

    const search = filters.querySelector('#modelSearch');
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
      try { await api.download(`/api/portal/catalog.pdf?${apiQuery(query)}`, 'catalogo-lejan.pdf'); }
      finally { button.disabled = false; }
    };
  }

  // ── Listado ────────────────────────────────────────────────────────────────
  function paintList() {
    const items = data.items || [];
    for (const key of Object.keys(itemsById)) delete itemsById[key];
    for (const item of items) itemsById[item.modelId] = item;

    if (!items.length) {
      list.innerHTML = `
        <div class="panel">
          <b>${esc(t('catalog.emptyTitle'))}</b>${esc(t('catalog.emptyBody'))}
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
          ${main(item) ? `
            <p class="item-price matrix-price"><span>${main(item).label}</span>
              <b>${esc(eur(main(item).value))}</b>
              <span class="item-units" hidden></span></p>` : '<p class="item-units" hidden></p>'}
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
