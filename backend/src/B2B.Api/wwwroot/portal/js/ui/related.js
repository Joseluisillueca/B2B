// Productos relacionados (cross-selling / up-selling que fija Business Central).
// Raíl horizontal compartido por la ficha de producto ("Completa la gama") y el
// checkout ("Añade también"). La API /api/shop/related resuelve los ids con el
// mismo pipeline del catálogo (tarifa, stock, visibilidad), así que aquí solo se
// pinta: si no hay relacionados, la sección NO existe (ni deja hueco).

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state } from '../state.js';
import { href } from '../router.js';
import { icons } from './icons.js';

// El precio de la card sigue la preferencia MOSTRAR PRECIOS del perfil (PVD por
// defecto), el mismo criterio que el catálogo: nunca una card sin precio si hay otro.
const preferred = () => (state.me?.prefs?.showPrices === 'pvp' ? 'pvp' : 'pvd');
const priceOf = (item, kind) =>
  item?.[kind] == null ? null : { label: t(`catalog.price.${kind}`), value: item[kind] };
const mainPrice = item =>
  priceOf(item, preferred()) ?? priceOf(item, preferred() === 'pvd' ? 'pvp' : 'pvd');

const productHref = card =>
  `${href('product')}/${encodeURIComponent(card.reference || card.modelId)}`;

/**
 * Pide los relacionados de uno o varios modelos (la ficha manda uno; el checkout,
 * todos los del carrito). Devuelve [{ relation:'cross'|'up', card }] en el orden
 * comercial de BC. Los modelos de origen nunca vuelven (los excluye el servidor).
 */
export async function fetchRelated(models, windowKey) {
  const ids = (models || []).filter(Boolean);
  if (!ids.length) return [];
  const params = new URLSearchParams();
  params.set('models', ids.join(','));
  if (windowKey) params.set('window', windowKey);
  params.set('locale', lang());
  const data = await api.get(`/api/shop/related?${params}`);
  return data.items || [];
}

// Card del raíl: reutiliza el lenguaje .pcard del catálogo (foto 4:5, nombre,
// referencia, precio y CTA), toda la tarjeta navega con el enlace estirado.
// Sin corazón de favorito: en un raíl secundario compite con el CTA y con el
// gesto de scroll; el favorito vive en la ficha y en el catálogo.
const railCard = ({ relation, card }, cta) => {
  const price = mainPrice(card);
  return `
    <article class="pcard rel-pcard" data-model="${esc(card.modelId)}">
      <div class="pcard-media">
        ${card.imageUri
          ? `<img src="${esc(card.imageUri)}" alt="" loading="lazy" decoding="async">`
          : `<span class="item-art" aria-hidden="true">${icons.shoe(52)}</span>`}
        ${relation === 'up' ? `<span class="rel-badge">${esc(t('related.up'))}</span>` : ''}
      </div>
      <div class="pcard-body">
        <h3 class="pcard-name"><a class="pcard-link" href="${esc(productHref(card))}"
          title="${esc(card.name || '')}">${esc(card.name || '')}</a></h3>
        <p class="pcard-ref">${esc(t('catalog.reference'))} <b>${esc(card.reference || '')}</b></p>
        ${price ? `<p class="pcard-price"><span>${esc(price.label)}</span> <b>${esc(eur(price.value))}</b></p>` : ''}
        <span class="pcard-cta" aria-hidden="true">${esc(cta || t('catalog.viewProduct'))} ${icons.right(15)}</span>
      </div>
    </article>`;
};

/**
 * HTML de la sección completa: cabecera (título + subtítulo + flechas) y raíl con
 * snap. `compact` es la variante del checkout (cards menores, título discreto).
 * `cta` cambia el microcopy de la card ("Ver ficha" por defecto; el checkout manda
 * "Elegir tallas": ahí la promesa del clic es abrir la matriz de tallas, no leer).
 */
export function relatedSectionHtml(items, { title, sub = '', compact = false, id = 'related', cta = '' } = {}) {
  if (!items?.length) return '';
  return `
    <section class="related${compact ? ' related-compact' : ''}" aria-labelledby="${esc(id)}-h">
      <header class="related-head">
        <div>
          <h2 class="related-title" id="${esc(id)}-h">${esc(title)}</h2>
          ${sub ? `<p class="related-sub">${esc(sub)}</p>` : ''}
        </div>
        <div class="related-nav">
          <button type="button" class="related-arrow rel-prev" aria-label="${esc(t('related.prev'))}">${icons.left(18)}</button>
          <button type="button" class="related-arrow rel-next" aria-label="${esc(t('related.next'))}">${icons.right(18)}</button>
        </div>
      </header>
      <div class="related-rail" role="list" aria-labelledby="${esc(id)}-h">
        ${items.map(item => `<div role="listitem" class="related-cell">${railCard(item, cta)}</div>`).join('')}
      </div>
    </section>`;
}

/**
 * Patrón de raíl con flechas, compartido: lo usa la sección de relacionados (abajo)
 * y el lookbook para sus raíles "Compra el look". Flechas solo si el raíl desborda
 * (clase `has-nav` en `host` y, si se pasa `nav`, su atributo hidden — para quien no
 * tiene la regla CSS de .related), apagadas en los extremos, paso configurable
 * (`step(rail)` → px; por defecto ~un ancho de vista) y scroll suave salvo "reducir
 * movimiento". En táctil (≤48rem) las flechas sobran: el snap ya pagina con el dedo.
 */
export function bindRail({ host, rail, prev, next, nav = null, step = null }) {
  if (!host || !rail || !prev || !next) return;

  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const stepOf = () => Math.max((step ? step(rail) : 0) || rail.clientWidth * 0.9, 160);

  const update = () => {
    const max = rail.scrollWidth - rail.clientWidth;
    const overflow = max > 4;
    host.classList.toggle('has-nav', overflow);
    if (nav) nav.hidden = !overflow || matchMedia('(max-width:48rem)').matches;
    prev.disabled = rail.scrollLeft <= 1;
    next.disabled = rail.scrollLeft >= max - 1;
  };

  prev.onclick = () => rail.scrollBy({ left: -stepOf(), behavior: reduced ? 'auto' : 'smooth' });
  next.onclick = () => rail.scrollBy({ left: stepOf(), behavior: reduced ? 'auto' : 'smooth' });
  rail.addEventListener('scroll', update, { passive: true });

  // El listener de resize se vigila a sí mismo con un temporizador barato además del
  // propio evento: si la sección se desconecta (repintados frecuentes del checkout), se
  // retira aunque el usuario nunca redimensione — sin acumulación de listeners.
  const onResize = () => (host.isConnected ? update() : dispose());
  const watchdog = setInterval(() => { if (!host.isConnected) dispose(); }, 15_000);
  function dispose() { removeEventListener('resize', onResize); clearInterval(watchdog); }
  addEventListener('resize', onResize);
  // Las imágenes lazy cambian el scrollWidth al llegar: se re-mide al cargar cada una
  rail.querySelectorAll('img').forEach(img => img.addEventListener('load', update, { once: true }));
  update();
}

/** Da vida a la sección de relacionados: el patrón de arriba sobre .related-rail */
export function bindRelatedRail(section) {
  bindRail({
    host: section,
    rail: section?.querySelector('.related-rail'),
    prev: section?.querySelector('.rel-prev'),
    next: section?.querySelector('.rel-next')
  });
}
