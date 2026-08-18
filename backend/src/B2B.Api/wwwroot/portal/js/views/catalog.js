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
  return {
    q: params.get('q') || '',
    family: params.get('family') || '',
    availability: (params.get('availability') || '').split(',').filter(Boolean),
    attributes,
    sort: params.get('sort') || (state.prefs.focus ? 'relevance' : 'featured'),
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
    <div class="page catalog">
      <aside class="rail" id="rail" aria-label="${esc(t('catalog.filters'))}"></aside>
      <div class="cat-main">
        <div class="cat-bar">
          <div class="cat-title">
            ${pageHead(t('nav.catalog'), [t('nav.catalog')],
              '<span class="cat-count" id="count" role="status"></span>')}
          </div>
          <div id="tools"></div>
        </div>
        <div class="cat-list" id="list">
          <div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div>
        </div>
        <div id="pager"></div>
      </div>
    </div>`;

  const rail = host.querySelector('#rail');
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
    paintRail();
    paintTools();
    paintList();
    paintPager();
  }

  // ── Rail de facetas ────────────────────────────────────────────────────────
  function paintRail() {
    const focused = railFocusKey();
    const lines = data.facets?.families || [];
    const attributes = data.facets?.attributes || [];

    rail.innerHTML = `
      <section class="rail-lines">
        <h2>${esc(t('catalog.facet.lines'))}</h2>
        <button type="button" class="rail-line${query.family ? '' : ' on'}" data-family=""
          aria-pressed="${query.family ? 'false' : 'true'}">
          ${esc(t('nav.catalog'))}</button>
        ${lines.map(line => `
          <button type="button" class="rail-line${query.family === line.id ? ' on' : ''}"
            data-family="${esc(line.id)}" aria-pressed="${query.family === line.id}"
            >${esc(vocab('family', line.id, line.label, line.id))}</button>`).join('')}
      </section>

      <section class="rail-model">
        <h2 id="modelLabel">${esc(t('catalog.facet.model'))}</h2>
        <input type="search" id="modelSearch" value="${esc(query.q)}"
          placeholder="${esc(t('catalog.searchPlaceholder'))}" aria-labelledby="modelLabel">
      </section>

      <section>
        <h2>${esc(t('catalog.facet.availability'))}</h2>
        ${AVAILABILITY.map(id => checkbox({
          group: 'availability', value: id,
          label: t(`catalog.availability.${id}`),
          checked: query.availability.includes(id)
        })).join('')}
      </section>

      ${attributes.map(attribute => attributeSection(attribute)).join('')}`;

    bindRail();
    restoreRailFocus(focused);
  }

  // Repintar el rail destruye el control que el usuario acaba de usar y el foco cae
  // al principio del documento: marcar tres casillas seguidas con el teclado era
  // imposible. Se anota qué control tenía el foco y se recupera tras el repintado.
  function railFocusKey() {
    const active = document.activeElement;
    if (!active || !rail.contains(active)) return '';
    if (active.id === 'modelSearch') return '#modelSearch';
    if (active.dataset?.facet) return `.rail-more[data-facet="${CSS.escape(active.dataset.facet)}"]`;
    if (active.dataset?.family !== undefined)
      return `.rail-line[data-family="${CSS.escape(active.dataset.family)}"]`;
    if (active.dataset?.group)
      return `.rail-check input[data-group="${CSS.escape(active.dataset.group)}"][value="${CSS.escape(active.value)}"]`;
    return '';
  }

  function restoreRailFocus(key) {
    if (!key) return;
    const target = rail.querySelector(key);
    if (!target) return;
    target.focus({ preventScroll: true });
    if (key === '#modelSearch') target.setSelectionRange(target.value.length, target.value.length);
  }

  // m1: la referencia no lleva recuento por faceta; `count` llega y no se pinta
  const checkbox = ({ group, value, label, checked }) => `
    <label class="rail-check">
      <input type="checkbox" data-group="${esc(group)}" value="${esc(value)}" ${checked ? 'checked' : ''}>
      <span>${esc(label)}</span>
    </label>`;

  function attributeSection(attribute) {
    const selected = query.attributes[attribute.key] || [];
    const expanded = expandedFacets.has(attribute.key);
    const values = expanded ? attribute.values : attribute.values.slice(0, FACET_PREVIEW);
    const title = vocab('attr', attribute.keySlug, attribute.label, attribute.key);

    // El nombre del atributo y el de cada valor se traducen; lo que viaja en el
    // filtro sigue siendo `attribute.key` / `value.value`, tal cual llega de la API.
    // "Ver más" se repite en cada faceta: el aria-label dice de cuál es.
    return `
      <section>
        <h2>${esc(title)}</h2>
        ${values.map(value => checkbox({
          group: `a.${attribute.key}`, value: value.value,
          label: vocab('attrValue', value.slug, value.label, value.value),
          checked: selected.includes(value.value)
        })).join('')}
        ${attribute.values.length > FACET_PREVIEW ? `
          <button type="button" class="rail-more" data-facet="${esc(attribute.key)}"
            aria-expanded="${expanded}"
            aria-label="${esc(`${t(expanded ? 'catalog.less' : 'catalog.more')} · ${title}`)}">
            ${esc(t(expanded ? 'catalog.less' : 'catalog.more'))}</button>` : ''}
      </section>`;
  }

  const expandedFacets = new Set();

  function bindRail() {
    rail.querySelectorAll('.rail-line').forEach(button => {
      button.onclick = () => {
        query = { ...query, family: button.dataset.family, skip: 0 };
        load();
      };
    });

    rail.querySelectorAll('.rail-check input').forEach(input => {
      input.onchange = () => {
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
      };
    });

    rail.querySelectorAll('.rail-more').forEach(button => {
      button.onclick = () => {
        const key = button.dataset.facet;
        expandedFacets.has(key) ? expandedFacets.delete(key) : expandedFacets.add(key);
        paintRail();
      };
    });

    // La búsqueda por MODELO no recarga en cada tecla: espera a que pares de escribir
    const search = rail.querySelector('#modelSearch');
    let timer;
    search.oninput = () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        // El repintado ya devuelve el foco a quien lo tenía (restoreRailFocus); no
        // se fuerza aquí, que arrancaba el foco a quien hubiera tabulado a otro
        // control durante los 300 ms de espera.
        query = { ...query, q: search.value.trim(), skip: 0 };
        load({ keepScroll: true });
      }, 300);
    };
  }

  // ── Toolbar ────────────────────────────────────────────────────────────────
  function paintTools() {
    tools.innerHTML = toolbar({ sort: query.sort });

    tools.querySelector('#sortMode').onchange = event => {
      query = { ...query, sort: event.target.value, skip: 0 };
      load();
    };

    tools.querySelector('#exportStock').onclick = async event => {
      const button = event.currentTarget;
      button.disabled = true;
      try { await api.download(`/api/shop/stock-export.csv?${apiQuery(query)}`, 'stock.csv'); }
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

    list.innerHTML = items.map(item => article(item, stockWindow, lines)).join('');
    bindFavorites();
    paintPrices();
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

          ${attributes.length ? `<dl class="item-attrs">${attributes.map(attribute => `
            <div><dt>${esc(attribute.label)}</dt><dd>${esc(attribute.value)}</dd></div>`).join('')}</dl>` : ''}

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
