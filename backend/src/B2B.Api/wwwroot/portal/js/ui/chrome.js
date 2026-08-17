// Chrome común a todas las vistas post-login (01-dashboard.png):
// header negro + barra blanca con "Catálogo" + footer gris.
// Del menú de usuario cuelgan todas las secciones salvo Catálogo, igual que en
// el portal actual.

import { t, LANGS, lang } from '../i18n.js';
import { state, onCartChange } from '../state.js';
import { esc, initial } from '../format.js';
import { href, go, current } from '../router.js';
import { icons } from './icons.js';
import { paintCartBody } from './cart.js';

const header = () => document.getElementById('chrome-header');
const nav = () => document.getElementById('chrome-nav');
const footer = () => document.getElementById('chrome-footer');
const drawer = () => document.getElementById('cart-drawer');
const veil = () => document.getElementById('veil');

// Secciones del menú de usuario, en el orden del portal actual
const MENU = [
  'orders', 'delivery-notes', 'invoices', 'shopping-carts',
  'sat', 'statistics', 'business', 'profile', 'contact'
];

const SOCIAL = [
  ['facebook', 'https://www.facebook.com/lejanbrand'],
  ['instagram', 'https://www.instagram.com/lejanbrand'],
  ['linkedin', 'https://www.linkedin.com/company/lejanbrand'],
  ['youtube', 'https://www.youtube.com/@lejanbrand'],
  ['tiktok', 'https://www.tiktok.com/@lejanbrand']
];

const windowLabel = () => t(`window.${state.prefs.window}`).toUpperCase();

export function renderChrome(route) {
  const bare = document.body.dataset.chrome !== 'on';
  for (const el of [header(), nav(), footer()]) el.hidden = bare;
  drawer().hidden = bare;
  if (bare) return;

  paintHeader();
  paintNav(route);
  paintFooter();
  paintDrawer();
  document.body.dataset.focus = state.prefs.focus ? 'on' : 'off';
}

function paintHeader() {
  const me = state.me || {};
  const credential = state.credential || {};
  const line1 = `${me.email || ''}${me.rol ? ` - ${me.rol}` : ''}`;
  const line2 = credential.name || me.client?.name || '';

  header().innerHTML = `
    <a class="brand" href="${href('dashboard')}" aria-label="lejan">lejan<sup>™</sup></a>

    <form class="h-search" role="search">
      <input type="search" name="q" placeholder="${esc(t('chrome.search'))}" aria-label="${esc(t('chrome.search'))}">
      <button type="submit" aria-label="${esc(t('chrome.search'))}">${icons.search(16)}</button>
    </form>

    <span class="spacer"></span>

    <div class="h-user">
      <button type="button" id="userBtn" aria-haspopup="menu" aria-expanded="false">
        <span class="avatar">${esc(initial(me.email))}</span>
        <span class="who"><span class="l1">${esc(line1)}</span><br><span class="l2">${esc(line2)}</span></span>
      </button>
    </div>

    <button type="button" class="h-icon" id="focusBtn" aria-pressed="${state.prefs.focus}"
      title="${esc(t('chrome.focus'))}" aria-label="${esc(t('chrome.focus'))}">
      ${state.prefs.focus ? icons.eyeOff(19) : icons.eye(19)}
    </button>

    <span class="sep"></span>

    <button type="button" class="h-lang" id="langBtn" aria-haspopup="menu" aria-expanded="false">
      ${esc(lang())} ${icons.chevron(14)}
    </button>

    <button type="button" class="h-cart" id="cartBtn">${cartButtonInner()}</button>`;

  header().querySelector('.h-search').onsubmit = event => {
    event.preventDefault();
    const term = new FormData(event.target).get('q');
    go(`${href('catalog/catalog')}${term ? `?q=${encodeURIComponent(term)}` : ''}`);
  };

  header().querySelector('#focusBtn').onclick = toggleFocus;

  header().querySelector('#userBtn').onclick = event => togglePopup(event.currentTarget, userMenu);
  header().querySelector('#langBtn').onclick = event => togglePopup(event.currentTarget, langMenu);
  header().querySelector('#cartBtn').onclick = openCart;
}

// En móvil el header solo deja sitio a marca, usuario y carrito: idioma y vista
// sin distracciones se recogen aquí (.m-only, que el CSS solo muestra ≤48rem).
const userMenu = () => `
  <div class="h-menu" role="menu">
    ${MENU.map(view => `<a role="menuitem" href="${href(view)}">${esc(t(`nav.${view}`))}</a>`).join('')}
    <hr class="m-only">
    <div class="m-only" role="group" aria-label="${esc(t('chrome.language'))}">
      <span class="m-title" aria-hidden="true">${esc(t('chrome.language'))}</span>
      ${LANGS.map(code => `<a role="menuitem" href="/${current.market}/${code}/${current.view}"
        ${code === lang() ? 'aria-current="true"' : ''}>${esc(t(`lang.${code}`))}</a>`).join('')}
    </div>
    <button type="button" role="menuitem" class="m-only" data-focus-toggle
      aria-pressed="${state.prefs.focus}">${esc(t('chrome.focus'))}</button>
    <hr>
    <button type="button" role="menuitem" class="out" data-logout>${esc(t('chrome.logout'))}</button>
  </div>`;

function toggleFocus() {
  state.prefs = { ...state.prefs, focus: !state.prefs.focus };
  paintHeader();
  document.body.dataset.focus = state.prefs.focus ? 'on' : 'off';
  // El catálogo reacciona cambiando el orden a "Relevancia" (plan §5)
  dispatchEvent(new CustomEvent('portal:focus'));
}

const langMenu = () => `
  <div class="h-menu" role="menu">
    ${LANGS.map(code => `<a role="menuitem" href="/${current.market}/${code}/${current.view}">
      ${esc(t(`lang.${code}`))}</a>`).join('')}
  </div>`;

function togglePopup(button, template) {
  const host = button.closest('.h-user') || button.parentElement;
  const open = host.querySelector('.h-menu');
  closePopups();
  if (open) return;

  button.setAttribute('aria-expanded', 'true');
  if (getComputedStyle(host).position === 'static') host.style.position = 'relative';
  host.insertAdjacentHTML('beforeend', template());
  host.querySelector('[data-logout]')?.addEventListener('click', () => {
    state.clear();
    go('/login', { replace: true });
  });
  host.querySelector('[data-focus-toggle]')?.addEventListener('click', () => {
    closePopups();
    toggleFocus();
  });
  setTimeout(() => addEventListener('click', onOutside, { once: true }));
}

const onOutside = () => closePopups();

function closePopups() {
  for (const menu of document.querySelectorAll('.h-menu')) menu.remove();
  for (const button of document.querySelectorAll('[aria-haspopup="menu"]'))
    button.setAttribute('aria-expanded', 'false');
}

// Una sola entrada, igual que el portal actual: Catálogo
function paintNav(route) {
  const active = route.view === 'catalog/catalog' ? ' aria-current="page"' : '';
  nav().innerHTML = `<a href="${href('catalog/catalog')}"${active}>${esc(t('nav.catalog'))}</a>`;
}

function paintFooter() {
  footer().innerHTML = `
    <span>${esc(t('footer.copyright'))}</span>
    <span class="social">${SOCIAL.map(([name, url]) =>
      `<a href="${url}" target="_blank" rel="noopener" aria-label="${name}">${icons[name](18)}</a>`).join('')}</span>`;
}

function paintDrawer() {
  drawer().innerHTML = `
    <div class="top">
      <h2>${esc(t('cart.title'))}</h2>
      <span class="win">${esc(t(`window.${state.prefs.window}`))}</span>
      <button type="button" id="cartClose" aria-label="${esc(t('cart.close'))}">${icons.close(16)}</button>
    </div>
    <div class="body"></div>
    <div class="foot"></div>`;
  drawer().querySelector('#cartClose').onclick = closeCart;
  veil().onclick = closeCart;
  paintCartBody(drawer(), { onClose: closeCart });
}

// El contador del botón azul y el contenido del drawer siguen al carrito: cualquier
// celda de la matriz de tallas los actualiza sin que la vista tenga que avisar.
onCartChange(() => {
  if (document.body.dataset.chrome !== 'on') return;
  const button = header().querySelector('#cartBtn');
  if (button) button.innerHTML = cartButtonInner();
  if (!drawer().hidden) paintCartBody(drawer(), { onClose: closeCart });
  const win = drawer().querySelector('.win');
  if (win) win.textContent = t(`window.${state.prefs.window}`);
});

const cartButtonInner = () =>
  `${icons.cart(16)}<span class="label">${esc(windowLabel())} (${state.cartUnits()})</span>`;

function openCart() {
  drawer().hidden = false;
  drawer().classList.add('on');
  veil().hidden = false;
  veil().classList.add('on');
}

function closeCart() {
  drawer().classList.remove('on');
  veil().classList.remove('on');
}

/**
 * Cabecera de vista: migas + H1. Con el ojo activo solo sobrevive "Inicio"
 * (20-header-ojo.png). `aside` es HTML que acompaña al título en su misma línea —
 * el catálogo cuelga ahí el recuento de artículos, que el ojo NO oculta.
 */
export const pageHead = (title, crumbs = [], aside = '') => `
  <p class="crumbs">
    <a href="${href('dashboard')}">${esc(t('nav.home'))}</a>
    ${crumbs.map(c => `<span class="crumb"> / <span>${esc(c)}</span></span>`).join('')}
  </p>
  <div class="cat-headline"><h1 class="title">${esc(title)}</h1>${aside}</div>`;
