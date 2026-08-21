// Bandeja de solicitudes de registro (prealtas) — /clients/requests.
// En el portal real esta ruta da 404; aquí lista las prealtas creadas por el agente
// (GET /api/agent/clients/requests). Tras crear una, muestra el aviso de activación.

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc, date } from '../format.js';
import { go, href } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { gridTable } from '../ui/table.js';
import { icons } from '../ui/icons.js';

const STATUS = { pending: 'requests.status.pending', approved: 'requests.status.approved', rejected: 'requests.status.rejected' };

export default async function requests(host) {
  host.innerHTML = `
    <div class="page cl requests">
      ${pageHead(t('requests.title'), [t('clients.crumb'), t('requests.crumb')])}
      <div class="cl-tools doc-tools">
        <a class="btn-ghost" href="${href('clients')}">${icons.chevronLeft ? icons.chevronLeft(16) : ''}${esc(t('requests.back'))}</a>
        <a class="btn-primary cl-new" href="${href('agent/clients/new')}">${icons.user(16)} ${esc(t('clients.new'))}</a>
      </div>
      <div id="ntc"></div>
      <div id="list" aria-live="polite"><div class="skeleton short"></div></div>
    </div>`;

  const list = host.querySelector('#list');
  const ntc = host.querySelector('#ntc');

  // Aviso tras crear una prealta (lo dejó new-client.js en sessionStorage)
  const raw = sessionStorage.getItem('nc-created');
  if (raw) {
    sessionStorage.removeItem('nc-created');
    try {
      const info = JSON.parse(raw);
      const text = info.activationSent
        ? t('requests.createdSent', { name: info.name, email: info.email })
        : t('requests.createdNoMail', { name: info.name });
      ntc.innerHTML = `<div class="notice notice-ok">${icons.check(18)}<div><span>${esc(text)}</span></div></div>`;
    } catch { /* aviso opcional */ }
  }

  let data;
  try {
    data = await api.clientRequests();
  } catch {
    list.innerHTML = `<div class="panel"><b>${esc(t('requests.errorTitle'))}</b>${esc(t('requests.errorBody'))}</div>`;
    return;
  }

  const items = data.items || [];
  list.innerHTML = gridTable({
    columns: [
      { label: t('requests.col.name') },
      { label: t('requests.col.email') },
      { label: t('requests.col.preClient') },
      { label: t('requests.col.status') },
      { label: t('requests.col.date') }
    ],
    rows: items.map(r => ({
      id: r.id,
      cells: [
        `<b>${esc(r.name || '')}</b>`,
        esc(r.email || ''),
        r.preClient ? `<span class="cl-yes" title="${esc(t('clients.filter.yes'))}">${icons.check(16)}</span>`
                    : `<span class="cl-no" aria-hidden="true">—</span>`,
        `<span class="req-badge req-${esc(r.status)}">${esc(t(STATUS[r.status] || r.status))}</span>`,
        esc(date(r.createdAt))
      ]
    })),
    empty: t('requests.none')
  });
}
