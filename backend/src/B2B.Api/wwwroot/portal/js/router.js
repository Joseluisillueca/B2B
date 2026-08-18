// Enrutado por History API sobre las rutas reales del portal: /{market}/{lang}/{vista}
// (p.ej. /es/es/orders, /es/es/catalog/catalog). Cada vista es un módulo ES que se
// carga con import() diferido: nada de bundler ni paso de build.

import { LANGS, DEFAULT_LANG, setLang } from './i18n.js';
import { state } from './state.js';

export const DEFAULT_MARKET = 'es';

// Vista → módulo. El literal de la ruta es el del portal actual, no se traduce.
const VIEWS = {
  'login': () => import('./views/login.js'),
  'credentials': () => import('./views/credentials.js'),
  'dashboard': () => import('./views/dashboard.js'),
  'catalog/catalog': () => import('./views/catalog.js'),
  'product': () => import('./views/product.js'),
  'checkout': () => import('./views/checkout.js'),
  'shopping-carts': () => import('./views/carts.js'),
  'orders': () => import('./views/orders.js'),
  'delivery-notes': () => import('./views/delivery-notes.js'),
  'invoices': () => import('./views/invoices.js'),
  'sat': () => import('./views/sat.js'),
  'statistics': () => import('./views/statistics.js'),
  'business': () => import('./views/business.js'),
  'profile': () => import('./views/profile.js'),
  'contact': () => import('./views/contact.js')
};

// Pantallas de acceso: card sobre degradado, sin header ni footer
const BARE = { login: 'off', credentials: 'credentials' };

export const parse = (pathname = location.pathname) => {
  const parts = pathname.replace(/^\/+|\/+$/g, '').split('/').filter(Boolean);

  if (parts.length === 1 && parts[0] === 'login')
    return { market: DEFAULT_MARKET, lang: DEFAULT_LANG, view: 'login' };

  const [market, lang, ...rest] = parts;
  if (!market || !lang || market.length !== 2 || lang.length !== 2)
    return null;

  return {
    market,
    lang: LANGS.includes(lang) ? lang : DEFAULT_LANG,
    view: rest.join('/') || 'dashboard'
  };
};

export const href = (view, route = current) =>
  view === 'login' ? '/login' : `/${route.market}/${route.lang}/${view}`;

export let current = { market: DEFAULT_MARKET, lang: DEFAULT_LANG, view: 'dashboard' };

let render = () => {};
export const onRender = fn => { render = fn; };

export function go(pathOrView, { replace = false } = {}) {
  const path = pathOrView.startsWith('/') ? pathOrView : href(pathOrView);
  if (path === location.pathname) return resolve();
  history[replace ? 'replaceState' : 'pushState']({}, '', path);
  return resolve();
}

export async function resolve() {
  // m-11: una ruta inexistente cae en la portada, y la barra de direcciones tiene
  // que reflejarlo (antes se quedaba /es/es/loquesea con el dashboard pintado).
  const parsed = parse();
  const route = parsed || { market: DEFAULT_MARKET, lang: DEFAULT_LANG, view: 'dashboard' };

  // Rutas con parámetro (p. ej. "product/2038"): la vista completa no está en VIEWS,
  // pero su prefijo antes de la primera "/" sí. Se separa el resto como route.param y
  // la vista pasa a ser el prefijo. "catalog/catalog" SÍ está en VIEWS, así que la
  // condición `!VIEWS[route.view]` lo deja intacto.
  if (route.view && !VIEWS[route.view] && route.view.includes('/')) {
    const slash = route.view.indexOf('/');
    const prefix = route.view.slice(0, slash);
    if (VIEWS[prefix]) {
      route.param = route.view.slice(slash + 1);
      route.view = prefix;
    }
  }

  const unknown = !parsed || !VIEWS[route.view];
  if (unknown) route.view = 'dashboard';
  current = route;
  if (unknown) history.replaceState({}, '', href('dashboard', route));

  await setLang(route.lang);

  // Guardas de sesión: sin token solo se puede estar en el login; con token pero
  // sin credencial elegida se pasa por la pantalla de credenciales.
  if (!state.token && route.view !== 'login') return go('/login', { replace: true });
  if (state.token && route.view === 'login') return go('dashboard', { replace: true });
  if (state.token && !state.credential && route.view !== 'credentials' && state.me?.credentials?.length)
    return go('credentials', { replace: true });

  document.body.dataset.chrome = BARE[route.view] || 'on';
  const module = await VIEWS[route.view]();
  const host = document.getElementById('view');
  host.innerHTML = '';
  await module.default(host, route);
  render(route);
  window.scrollTo(0, 0);
  return route;
}

export function start() {
  addEventListener('popstate', () => resolve());

  // Un solo listener para toda la navegación interna del portal
  addEventListener('click', event => {
    const link = event.target.closest('a[href^="/"]');
    if (!link || link.target === '_blank' || event.metaKey || event.ctrlKey || event.shiftKey) return;
    const path = link.getAttribute('href');
    if (!parse(path)) return;
    event.preventDefault();
    go(path);
  });

  return resolve();
}
