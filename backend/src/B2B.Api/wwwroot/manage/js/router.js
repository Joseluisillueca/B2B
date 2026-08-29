// Router por hash del back-office (#/models, #/clients/edit/<id>, …). No necesita
// fallback de servidor: /manage es una carpeta estática.
import { renderShell, markActive, setCounts } from './shell.js';
import { auth, api } from './api.js';
import { ROUTE_TYPE } from './schemas.js';
import loginView from './views/login.js';
import dashboardView from './views/dashboard.js';
import listView from './views/list.js';
import formView from './views/form.js';
import clientView from './views/client.js';
import usersView from './views/users.js';
import imagesView from './views/images.js';
import { ordersView, orderView } from './views/orders.js';
import { configView, connectionsView, docSourcesView, logsView } from './views/integration.js';

let shellReady = false;

export function go(hash) { location.hash = hash.startsWith('#') ? hash : '#/' + hash; }

async function loadCounts() {
  try {
    const data = await api.summary();
    setCounts(Object.fromEntries((data.items || []).map(i => [i.entityType, i.count])));
  } catch { /* los badges son opcionales */ }
}

export async function resolve() {
  const parts = (location.hash.replace(/^#\/?/, '') || 'dashboard').split('/').filter(Boolean);
  const view = parts[0] || 'dashboard';

  if (!auth.token || view === 'login') {
    shellReady = false;
    return loginView(document.getElementById('app'));
  }

  if (!shellReady) { renderShell(); shellReady = true; loadCounts(); }
  const main = document.getElementById('main');
  markActive(view);
  main.focus({ preventScroll: true });
  main.innerHTML = '<div class="skeleton"></div><div class="skeleton short"></div>';

  try {
    if (view === 'dashboard') return await dashboardView(main);
    if (view === 'images') return await imagesView(main);
    if (view === 'users') return await usersView(main);
    if (view === 'notifications-config') return await configView(main);
    if (view === 'notifications-log') return await logsView(main);
    if (view === 'connections') return await connectionsView(main);
    if (view === 'doc-sources') return await docSourcesView(main);
    if (view === 'orders') return parts[1] ? await orderView(main, parts[1]) : await ordersView(main);
    if (view === 'clients') {
      if (parts[1] === 'new') return await clientView(main, null);
      if (parts[1] === 'edit') return await clientView(main, parts[2]);
      return await listView(main, 'client', 'clients');
    }
    if (ROUTE_TYPE[view]) {
      const type = ROUTE_TYPE[view];
      if (parts[1] === 'new') return await formView(main, type, null);
      if (parts[1] === 'edit') return await formView(main, type, parts[2]);
      return await listView(main, type, view);
    }
    go('#/dashboard');
  } catch (e) {
    main.innerHTML = `<div class="notice notice-error" role="alert">${e.message || 'Error inesperado.'}</div>`;
  }
}

export function refreshCounts() { loadCounts(); }
