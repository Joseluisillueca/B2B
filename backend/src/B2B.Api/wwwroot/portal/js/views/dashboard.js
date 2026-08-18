// Vista 3 — /{market}/{lang}/dashboard (01-dashboard.png).
// Carrusel a ancho completo + H1 "Haz tu pedido" + las dos tarjetas-imagen
// Reposición y Programación, que fijan la ventana de servicio y llevan al catálogo.
//
// Todo el contenido sale del CMS (portal_content: dashboard.hero y dashboard.tiles)
// filtrado por ventana de publicación. Si el CMS aún no ha publicado nada, la
// portada se sostiene sola: sin carrusel y con las dos tarjetas en color de marca.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc } from '../format.js';
import { href } from '../router.js';
import { state } from '../state.js';
import { icons } from '../ui/icons.js';
import { carousel } from '../ui/carousel.js';

// Las dos tarjetas existen aunque el CMS no las haya configurado: son navegación,
// no contenido. Sin imagen se pintan con el degradado de marca.
const DEFAULT_TILES = [{ window: 'replenishment' }, { window: 'scheduled' }];

const cache = new Map();

async function content(key) {
  const cacheKey = `${key}:${lang()}`;
  if (!cache.has(cacheKey)) {
    cache.set(cacheKey, api
      .get(`/api/portal/content/${key}?locale=${encodeURIComponent(lang())}`)
      .then(body => body?.items ?? [])
      .catch(() => []));
  }
  return cache.get(cacheKey);
}

const tileHtml = tile => {
  const window = tile.window === 'scheduled' ? 'scheduled' : 'replenishment';
  const label = tile.title || t(`window.${window}`);
  // Si el medio del CMS ha desaparecido, la tarjeta cae en el degradado de marca
  // en vez de enseñar el icono de imagen rota
  const art = tile.imageUrl
    ? `<img src="${esc(tile.imageUrl)}" alt="${esc(tile.alt || '')}" loading="lazy" decoding="async"
        onerror="this.remove()">`
    : `<span class="t-art" aria-hidden="true"></span>`;

  return `
    <a class="tile" data-window="${window}" href="${esc(tile.ctaHref || href('catalog/catalog'))}">
      ${art}
      <span class="t-cart" aria-hidden="true">${icons.cart(26)}</span>
      <span class="t-label">${esc(label)}</span>
      ${tile.subtitle ? `<span class="t-sub">${esc(tile.subtitle)}</span>` : ''}
    </a>`;
};

export default function dashboard(host) {
  host.innerHTML = `
    <section class="hero" id="hero" aria-busy="true">
      <div class="hero-skeleton"></div>
    </section>
    <div class="page">
      <h1 class="title">${esc(t('dashboard.title'))}</h1>
      <div class="tiles" id="tiles" aria-busy="true">
        <span class="tile-skeleton"></span><span class="tile-skeleton"></span>
      </div>
    </div>`;

  const hero = host.querySelector('#hero');
  const tiles = host.querySelector('#tiles');

  // La ventana de servicio activa se elige aquí: es lo que cuenta el botón azul
  // del header y lo que filtra precios y stock en el catálogo.
  tiles.addEventListener('click', event => {
    const tile = event.target.closest('.tile[data-window]');
    if (tile) state.prefs = { ...state.prefs, window: tile.dataset.window };
  });

  // Sin await: el cascarón y el H1 se pintan ya; el contenido del CMS entra al vuelo
  content('dashboard.hero').then(items => {
    if (!hero.isConnected) return;
    hero.removeAttribute('aria-busy');
    hero.innerHTML = '';
    if (items.length) carousel(hero, items, { label: t('dashboard.heroLabel') });
    else hero.hidden = true;
  });

  content('dashboard.tiles').then(items => {
    if (!tiles.isConnected) return;
    tiles.removeAttribute('aria-busy');
    const list = items.length ? items : DEFAULT_TILES;
    tiles.innerHTML = list.map(tileHtml).join('');
    tiles.dataset.count = String(list.length);
  });
}
