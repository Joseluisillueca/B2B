// Detalle de un pedido de selección — /agent/model-selection/{id}. Se abre al pulsar
// una fila del listado. Muestra nombre, estado, fechas y los modelos y clientes de la
// selección (marca en tachado los que ya no existen en el catálogo/cartera).

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc, date } from '../format.js';
import { href } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { statusChip } from '../ui/status-rail.js';
import { icons } from '../ui/icons.js';

export default async function modelSelectionDetail(host, route) {
  const id = route?.param;
  host.innerHTML = `<div class="page cl"><div class="skeleton short"></div><div class="skeleton"></div></div>`;

  let sel;
  try { sel = await api.modelSelection(id); }
  catch {
    host.innerHTML = `<div class="page cl">
      ${pageHead(t('selection.crumb'), [t('clients.crumb'), t('selection.crumb')])}
      <div class="panel"><b>${esc(t('selection.errorTitle'))}</b>${esc(t('selection.errorBody'))}</div></div>`;
    return;
  }

  const chip = sel.status === 'sent'
    ? statusChip(t('selection.status.sent'), 'green')
    : statusChip(t('selection.status.draft'), 'none');
  const back = `<a class="btn-ghost ms-back" href="${href('agent/model-selection')}">${icons.left ? icons.left(15) : ''} ${esc(t('selection.crumb'))}</a>`;
  const chips = arr => arr.length
    ? arr.map(x => `<span class="ms-chip${x.missing ? ' miss' : ''}">${esc(x.name)}</span>`).join('')
    : `<span class="cl-no">—</span>`;

  host.innerHTML = `
    <div class="page cl ms-detail">
      ${pageHead(sel.name || t('selection.crumb'), [t('clients.crumb'), t('selection.crumb')], back)}
      <div class="ms-facts">
        <div class="ms-fact"><span class="ms-fact-l">${esc(t('selection.col.status'))}</span><span>${chip}</span></div>
        <div class="ms-fact"><span class="ms-fact-l">${esc(t('selection.col.created'))}</span><span>${esc(date(sel.createdAt))}</span></div>
        <div class="ms-fact"><span class="ms-fact-l">${esc(t('selection.col.sentDate'))}</span><span>${sel.sentAt ? esc(date(sel.sentAt)) : '—'}</span></div>
      </div>
      <h2 class="ms-h">${esc(t('selection.col.models'))} · ${sel.models.length}</h2>
      <div class="ms-chips">${chips(sel.models)}</div>
      <h2 class="ms-h">${esc(t('selection.col.clients'))} · ${sel.clients.length}</h2>
      <div class="ms-chips">${chips(sel.clients)}</div>
    </div>`;
}
