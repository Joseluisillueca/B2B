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
import { salesRulesView, salesRuleEditView } from './views/sales-rules.js';

let shellReady = false;

export function go(hash) { location.hash = hash.startsWith('#') ? hash : '#/' + hash; }

// Guardia de salida de una vista con cambios sin guardar (la Cinta del catálogo). Si
// devuelve false, la navegación se anula y el hash vuelve al de la vista SIN repintarla.
// La guardia se retira sola en la primera navegación que prospera.
let leaveGuard = null, lastHash = location.hash, restoring = false;
export function setLeaveGuard(fn) { leaveGuard = fn; }

async function loadCounts() {
  try {
    const data = await api.summary();
    setCounts(Object.fromEntries((data.items || []).map(i => [i.entityType, i.count])));
  } catch { /* los badges son opcionales */ }
}

export async function resolve() {
  if (restoring) { restoring = false; return; }   // hashchange del propio retorno: nada que pintar
  if (leaveGuard && location.hash !== lastHash && !leaveGuard()) {
    restoring = true;
    location.hash = lastHash;
    return;
  }
  leaveGuard = null;
  lastHash = location.hash;
  const parts = (location.hash.replace(/^#\/?/, '') || 'dashboard').split('/').filter(Boolean);
  const view = parts[0] || 'dashboard';

  if (!auth.token || view === 'login') {
    shellReady = false;
    return loginView(document.getElementById('app'));
  }

  if (!shellReady) { renderShell(); shellReady = true; loadCounts(); }
  const main = document.getElementById('main');
  markActive(view);
  // Las vistas de TABLA ancha usan más ancho de ventana (no el max-width de lectura de los
  // formularios), para que no salga barra horizontal habiendo hueco. Se limpia al navegar.
  const wideTable = view === 'notifications-log' || view === 'received' || (view === 'orders' && !parts[1]) || (view === 'sales-rules' && !parts[1]);
  main.classList.toggle('mng-wide', wideTable);
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
    if (view === 'sales-rules') {
      if (parts[1] === 'new') return await salesRuleEditView(main, null);
      if (parts[1] === 'edit') return await salesRuleEditView(main, parts[2]);
      return await salesRulesView(main);
    }
    // Contenido web + comunicación (vistas nativas portadas del antiguo CMS). Import
    // dinámico: si un módulo aún no existe, solo falla su ruta, no todo el back-office.
    if (view === 'received') return await (await import('./views/received.js')).default(main);
    if (view === 'ribbon') return await (await import('./views/ribbon.js')).default(main);
    if (view === 'content') return await (await import('./views/content.js')).default(main);
    if (view === 'lookbook') return await (await import('./views/lookbook.js')).default(main);
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
