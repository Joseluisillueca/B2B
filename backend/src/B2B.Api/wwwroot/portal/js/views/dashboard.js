// Vista 3 — /{market}/{lang}/dashboard (01-dashboard.png).
// Carrusel a ancho completo + H1 "Haz tu pedido" + las dos tarjetas-imagen
// Reposición y Programación, que fijan la ventana de servicio y llevan al catálogo.
//
// Todo el contenido sale del CMS (portal_content: dashboard.hero y dashboard.tiles)
// filtrado por ventana de publicación. Si el CMS aún no ha publicado nada, la
// portada se sostiene sola: sin carrusel y con las dos tarjetas en color de marca.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
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

// Saludo según la hora local; el nombre sale del cliente del token
const greeting = () => {
  const h = new Date().getHours();
  const key = h < 12 ? 'greetMorning' : h < 20 ? 'greetAfternoon' : 'greetEvening';
  const name = state.credential?.name || state.me?.client?.name || '';
  return name ? `${t(`dashboard.${key}`)}, ${name}` : t(`dashboard.${key}`);
};

// Bento de KPIs de la cuenta: convierte la portada en un cuadro de mando. Cada tarjeta
// lleva la cifra grande en serif (Fraunces) y enlaza a la sección correspondiente.
const kpiHtml = (to, label, value, sub, accent = false) => `
  <a class="kpi${accent ? ' kpi-accent' : ''}" href="${href(to)}">
    <span class="kpi-label">${esc(label)}</span>
    <span class="kpi-value">${esc(value)}</span>
    <span class="kpi-sub">${esc(sub)}</span>
  </a>`;

async function paintKpis(node) {
  try {
    const [orders, stats, invoices] = await Promise.all([
      api.get('/api/portal/orders?take=1'),
      api.get('/api/portal/statistics'),
      api.get('/api/portal/invoices?take=500'),
    ]);
    const open = orders?.counts?.open ?? 0;
    const debt = (invoices?.items ?? []).reduce((s, i) => s + Number(i.debt || 0), 0);
    const overdue = invoices?.counts?.overdue ?? 0;
    node.innerHTML =
      kpiHtml('orders', t('dashboard.kpiOrders'), String(orders?.total ?? 0),
        t('dashboard.kpiOrdersSub', { n: open })) +
      kpiHtml('statistics', t('dashboard.kpiBilled'), eur(stats?.total ?? 0),
        t('dashboard.kpiBilledSub', { n: stats?.count ?? 0 })) +
      kpiHtml('invoices', t('dashboard.kpiDue'), eur(debt),
        overdue ? t('dashboard.kpiDueSub', { n: overdue }) : t('dashboard.kpiDueNone'),
        overdue > 0);
    node.hidden = false;
  } catch {
    node.hidden = true;   // si algo falla, la portada sigue funcionando sin el bento
  }
}

export default function dashboard(host) {
  host.innerHTML = `
    <section class="hero-full">
      <div class="hero-media" id="hero" aria-busy="true">
        <div class="hero-skeleton"></div>
      </div>
      <div class="hero-overlay">
        <span class="hero-kicker">${esc(greeting())}</span>
        <h1 class="hero-title">${esc(t('dashboard.title'))}</h1>
        <a class="hero-cta" href="${href('catalog/catalog')}">
          ${esc(t('dashboard.explore'))} <span aria-hidden="true">→</span>
        </a>
      </div>
    </section>
    <div class="page dash">
      <div class="kpis" id="kpis" hidden></div>
      <div class="tiles" id="tiles" aria-busy="true">
        <span class="tile-skeleton"></span><span class="tile-skeleton"></span>
      </div>
    </div>`;

  const hero = host.querySelector('#hero');
  const tiles = host.querySelector('#tiles');
  paintKpis(host.querySelector('#kpis'));

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
