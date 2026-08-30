// Armazón persistente del back-office: cabecera negra + barra lateral de maestros.
// Reutiliza el lenguaje visual del portal (brand, header negro, avatar).
import { NAV } from './schemas.js';
import { icons } from './icons.js';
import { auth } from './api.js';
import { esc } from './util.js';

let counts = {};
export function setCounts(map) { counts = map || {}; refreshBadges(); }

export function renderShell() {
  const app = document.getElementById('app');
  app.removeAttribute('aria-busy');
  app.innerHTML = `
    <header class="mng-header">
      <a class="brand" href="#/dashboard">MITO PROJECTS<sup>™</sup></a>
      <nav class="mng-switch" aria-label="Área del back-office">
        <a class="mng-seg on" href="#/dashboard" aria-current="page">Gestión</a>
        <a class="mng-seg" href="/admin.html">CMS</a>
      </nav>
      <span class="spacer"></span>
      <a class="mng-portal" href="/es/es/dashboard" target="_blank" rel="noopener">Ver el portal ↗</a>
      <div class="mng-who">
        <div class="who"><div class="l1">${esc(auth.who || 'Administración')}</div><div class="l2">Administrador</div></div>
        <div class="avatar">${esc((auth.who || 'A').trim()[0] || 'A').toUpperCase()}</div>
      </div>
      <button class="mng-out" id="mngOut">Salir</button>
    </header>
    <div class="mng">
      <aside class="mng-side" id="mngSide">${nav()}</aside>
      <main class="mng-main" id="main" tabindex="-1"></main>
    </div>`;

  document.getElementById('mngOut').onclick = () => { auth.clear(); location.hash = '#/login'; };
}

function nav() {
  return NAV.map(group => `
    <div class="mng-group">
      <h3>${esc(group.title)}</h3>
      ${group.items.map(([view, label, icon]) => `
        <a class="mng-link" href="#/${view}" data-view="${view}">
          ${icons[icon] ? icons[icon](17) : ''}<span>${esc(label)}</span>
          <span class="mng-badge" data-badge="${view}" hidden></span>
        </a>`).join('')}
    </div>`).join('');
}

// Slugs de menú → entityType para el badge de recuento
const BADGE_TYPE = {
  models: 'model', products: 'product', offers: 'offer', inventory: 'inventory',
  'service-windows': 'service-window', categories: 'category', families: 'family',
  attributes: 'attribute', warehouses: 'warehouse', 'payment-methods': 'payment-method',
  clients: 'client', 'client-groups': 'client-group', agents: 'agent', orders: 'order',
};

function refreshBadges() {
  document.querySelectorAll('[data-badge]').forEach(el => {
    const type = BADGE_TYPE[el.dataset.badge];
    const n = type && counts[type];
    if (n) { el.textContent = n; el.hidden = false; } else { el.hidden = true; }
  });
}

export function markActive(view) {
  document.querySelectorAll('.mng-link').forEach(a => {
    const on = a.dataset.view === view;
    a.classList.toggle('on', on);
    if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
  });
}
