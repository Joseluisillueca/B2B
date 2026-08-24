// Ficha de producto — /{market}/{lang}/product/{referencia} (estilo thehoffbrand.com:
// foto protagonista a la izquierda, ficha aireada a la derecha). El pedido se hace
// aquí igual que en el catálogo: la MISMA matriz de tallas mete cantidades en el
// carrito de la ventana de servicio activa. Se reutilizan sizeMatrix/bindMatrix.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state } from '../state.js';
import { href } from '../router.js';
import { icons } from '../ui/icons.js';
import { sizeMatrix, bindMatrix } from '../ui/size-matrix.js';
import { createViewer } from '../ui/viewer.js';

// Precio de la ficha: una sola línea, la de la preferencia MOSTRAR PRECIOS del perfil
// (PVD por defecto). Si el artículo no trae ese precio se enseña el otro (mismo criterio
// que el listado, m7).
const preferred = () => (state.me?.prefs?.showPrices === 'pvp' ? 'pvp' : 'pvd');
const priceOf = (item, kind) =>
  item?.[kind] == null ? null : { label: t(`catalog.price.${kind}`), value: item[kind] };
const mainPrice = item =>
  priceOf(item, preferred()) ?? priceOf(item, preferred() === 'pvd' ? 'pvp' : 'pvd');

// Vocabulario traducido de los atributos (mismo criterio que el catálogo): la clave
// estable se busca en el diccionario; si no está, se cae en la etiqueta del servidor.
const slugKey = value => String(value ?? '').normalize('NFD')
  .replace(/[̀-ͯ]/g, '').toLowerCase()
  .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

const vocab = (prefix, slug, label, raw) => {
  const key = slugKey(slug || raw);
  if (key) {
    const translated = t(`catalog.${prefix}.${key}`);
    if (translated !== `catalog.${prefix}.${key}`) return translated;
  }
  return label || raw || '';
};

// La ficha muestra TODOS los atributos (silueta, colección, temporada…); a diferencia
// del listado, aquí no se esconde ninguno.
const attrsOf = item => Array.isArray(item.attributeList) && item.attributeList.length
  ? item.attributeList.map(entry => ({
      label: vocab('attr', entry.keySlug, entry.label, entry.key),
      value: vocab('attrValue', entry.valueSlug, entry.valueLabel, entry.value)
    }))
  : Object.entries(item.attributes || {}).map(([key, value]) => ({
      label: vocab('attr', key, '', key),
      value: vocab('attrValue', value, '', value)
    }));

const favLabel = on => t(on ? 'catalog.favoriteOff' : 'catalog.favorite');

export default async function product(host, route) {
  const reference = decodeURIComponent(route?.param || '').trim();

  host.innerHTML = `
    <div class="page product">
      <div class="product-grid">
        <div class="skeleton product-skeleton"></div>
        <div><div class="skeleton short"></div><div class="skeleton short"></div></div>
      </div>
    </div>`;

  // Ventana de servicio activa: el tipo lo elige la portada, el id lo trae el catálogo.
  // Como el listado, la primera llamada aún no conoce el id, así que se repite una vez
  // si el servidor resolvió una ventana distinta a la activa.
  let windows = [];
  const windowId = () => {
    const type = state.prefs.window === 'scheduled' ? 'SCHEDULED' : 'REPLENISHMENT';
    return (windows.find(w => w.orderType === type) || windows[0])?.id || '';
  };

  async function loadData() {
    const params = new URLSearchParams();
    params.set('q', reference);
    params.set('take', '50');
    params.set('window', windowId());
    params.set('locale', lang());
    const data = await api.get(`/api/shop/catalog?${params}`);
    const knew = windows.length > 0;
    windows = data.windows || [];
    if (!knew && windowId() && data.window !== windowId()) return loadData();
    return data;
  }

  let data;
  try {
    data = await loadData();
  } catch {
    host.querySelector('.product-grid').outerHTML = `
      <div class="panel">
        <b>${esc(t('product.errorTitle'))}</b>${esc(t('product.errorBody'))}
        <div><a class="cta" href="${href('catalog/catalog')}">${esc(t('product.backToCatalog'))}</a></div>
      </div>`;
    return;
  }

  const items = data.items || [];
  const item = items.find(entry => (entry.reference || '') === reference) || items[0];

  if (!item) {
    host.innerHTML = `
      <div class="page product">
        <nav class="crumbs" aria-label="${esc(t('nav.catalog'))}">
          <a href="${href('dashboard')}">${esc(t('nav.home'))}</a>
          <span class="crumb"> / <a href="${href('catalog/catalog')}">${esc(t('nav.catalog'))}</a></span>
        </nav>
        <div class="panel">
          <b>${esc(t('product.notFoundTitle'))}</b>${esc(t('product.notFoundBody'))}
          <div><a class="cta" href="${href('catalog/catalog')}">${esc(t('product.backToCatalog'))}</a></div>
        </div>
      </div>`;
    return;
  }

  const attributes = attrsOf(item);
  const price = mainPrice(item);
  const lines = Object.fromEntries(
    state.cartLines().map(line => [`${line.modelId}|${line.size}`, line]));

  host.innerHTML = `
    <div class="page product">
      <nav class="crumbs" aria-label="${esc(t('nav.catalog'))}">
        <a href="${href('dashboard')}">${esc(t('nav.home'))}</a>
        <span class="crumb"> / <a href="${href('catalog/catalog')}">${esc(t('nav.catalog'))}</a></span>
        <span class="crumb"> / <span>${esc(item.name || reference)}</span></span>
      </nav>

      <div class="product-grid">
        <figure class="product-media" id="media"></figure>

        <div class="product-panel">
          <div class="product-head">
            <h1 class="product-name">${esc(item.name || reference)}</h1>
            <button type="button" class="item-fav product-fav" id="fav"
              aria-pressed="${item.favorite ? 'true' : 'false'}"
              aria-label="${esc(favLabel(item.favorite))}" title="${esc(favLabel(item.favorite))}">
              ${item.favorite ? icons.heartOn(24) : icons.heart(24)}
            </button>
          </div>

          <p class="product-ref">${esc(t('catalog.reference'))} <b>${esc(item.reference || '')}</b></p>

          ${attributes.length ? `<dl class="product-attrs">${attributes.map(attribute => `
            <div><dt>${esc(attribute.label)}</dt><dd>${esc(attribute.value)}</dd></div>`).join('')}</dl>` : ''}

          ${price ? `<p class="product-price"><span>${price.label}</span>
            <b>${esc(eur(price.value))}</b></p>` : ''}

          <div class="product-buy">
            <div id="buy">${sizeMatrix(item, { windowKey: data.window, lines })}</div>
            <div class="product-order">
              <span class="product-total" id="total"></span>
              <button type="button" class="btn-primary" id="add">${esc(t('product.add'))}</button>
            </div>
          </div>

          <div class="product-docs">
            <button type="button" class="product-doc" disabled title="${esc(t('product.downloadSoon'))}">
              ${icons.fileDown(17)} ${esc(t('product.downloadTech'))}
            </button>
            <button type="button" class="product-doc" disabled title="${esc(t('product.downloadSoon'))}">
              ${icons.image(17)} ${esc(t('product.downloadGallery'))}
            </button>
          </div>
        </div>
      </div>
    </div>`;

  // ── Visor multi-ángulo (giro 360 + zoom); degrada a la portada o a placeholder ──
  createViewer(host.querySelector('#media'),
    item.images?.length ? item.images : (item.imageUri ? [item.imageUri] : []),
    { name: item.name || reference });

  // ── Matriz de tallas: el mismo componente del catálogo mete en el carrito ──
  const buy = host.querySelector('#buy');
  const total = host.querySelector('#total');
  const itemsById = { [item.modelId]: item };

  const paintTotal = () => {
    let units = 0, amount = 0;
    for (const input of buy.querySelectorAll('.sz-qty')) {
      const qty = Number(input.value) || 0;
      units += qty;
      amount += qty * (Number(input.dataset.price) || 0);
    }
    total.innerHTML = units
      ? `${esc(t('catalog.units', { n: units }))} · <b>${esc(eur(amount))}</b>`
      : `<span class="product-hint">${esc(t('product.pickSizes'))}</span>`;
  };

  bindMatrix(buy, itemsById, { onChange: paintTotal });
  paintTotal();

  // "AÑADIR": el pedido ya se hace al teclear en la matriz. Si aún no hay unidades,
  // el botón lleva el foco a la primera talla disponible para empezar; si ya hay
  // unidades, abre el carrito para revisarlas (reutiliza el botón del header).
  host.querySelector('#add').onclick = () => {
    const units = [...buy.querySelectorAll('.sz-qty')].reduce((sum, i) => sum + (Number(i.value) || 0), 0);
    if (units > 0) {
      document.getElementById('cartBtn')?.click();
      return;
    }
    const first = buy.querySelector('.sz-qty:not([disabled])');
    first?.focus();
    first?.select?.();
  };

  // ── Favorito: mismo comportamiento y accesibilidad que el catálogo ──
  const setFav = (button, on) => {
    button.setAttribute('aria-pressed', String(on));
    button.setAttribute('aria-label', favLabel(on));
    button.setAttribute('title', favLabel(on));
    button.innerHTML = on ? icons.heartOn(24) : icons.heart(24);
  };

  host.querySelector('#fav').onclick = async event => {
    const button = event.currentTarget;
    const on = button.getAttribute('aria-pressed') !== 'true';
    setFav(button, on);
    item.favorite = on;
    try {
      await (on ? api.put(`/api/portal/favorites/${encodeURIComponent(item.modelId)}`)
                : api.del(`/api/portal/favorites/${encodeURIComponent(item.modelId)}`));
    } catch {
      setFav(button, !on);
      item.favorite = !on;
    }
  };
}
