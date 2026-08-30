// Pedidos de selección — /agent/model-selection (Fase 3, réplica del portal real).
// LISTADO de las selecciones de modelos del agente; el botón "Nueva Selección" abre
// el builder (/agent/model-selection/add).

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc, date } from '../format.js';
import { href, go } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { gridTable } from '../ui/table.js';
import { statusChip } from '../ui/status-rail.js';
import { icons } from '../ui/icons.js';

export default async function modelSelection(host, route) {
  // Con parámetro (id) → detalle de una selección; sin él → listado.
  if (route?.param) return (await import('./model-selection-detail.js')).default(host, route);
  host.innerHTML = `
    <div class="page cl ms-list">
      ${pageHead(t('nav.model-selection'), [t('clients.crumb'), t('selection.crumb')])}
      <div class="cl-tools doc-tools ms-tools">
        <a class="btn-primary cl-new" href="${href('agent/model-selection/add')}">
          ${icons.plus(15)} ${esc(t('selection.new'))}</a>
      </div>
      <div id="list" aria-live="polite"><div class="skeleton short"></div></div>
    </div>`;

  const list = host.querySelector('#list');

  let data;
  try {
    data = await api.modelSelections();
  } catch {
    list.innerHTML = `<div class="panel"><b>${esc(t('selection.errorTitle'))}</b>${esc(t('selection.errorBody'))}</div>`;
    return;
  }

  const items = data.items || [];
  list.innerHTML = gridTable({
    columns: [
      { label: t('selection.col.name') },
      { label: t('selection.col.created') },
      { label: t('selection.col.models'), className: 'num' },
      { label: t('selection.col.clients'), className: 'num' },
      { label: t('selection.col.sentDate') },
      { label: t('selection.col.status') }
    ],
    rows: items.map(s => ({
      id: s.id,
      cells: [
        `<b>${esc(s.name || '')}</b>`,
        esc(date(s.createdAt)),
        esc(String(s.models ?? 0)),
        esc(String(s.clients ?? 0)),
        s.sentAt ? esc(date(s.sentAt)) : `<span class="cl-no" aria-hidden="true">—</span>`,
        s.status === 'sent'
          ? statusChip(t('selection.status.sent'), 'green')
          : statusChip(t('selection.status.draft'), 'none')
      ]
    })),
    empty: t('selection.none')
  });

  // Filas clicables → detalle de la selección.
  list.querySelectorAll('tr[data-id]').forEach(tr => {
    tr.style.cursor = 'pointer';
    tr.addEventListener('click', () => go('agent/model-selection/' + tr.dataset.id));
  });
}
