// Vista 10 — /{market}/{lang}/sat (08-sat.png).
//
// "Devoluciones": rail de estados (Todos, Confirmado verde, Pendiente naranja,
// Rechazado rojo), toolbar con "Buscar..." y el botón azul ⊕ NUEVA DEVOLUCIÓN, y la
// tabla de 10 columnas IMG · CÓDIGO · FECHA · CLIENTE · TIPO · HORARIO · BULTOS ·
// ITEMS · ESTADO · RESOLUCIÓN.
//
// No son documentos de Business Central: es el flujo propio del portal sobre
// return_requests (plan §1), así que aquí sí se da de alta.

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc, date, num } from '../format.js';
import { docList } from '../ui/doc-list.js';
import { statusChip } from '../ui/status-rail.js';
import { openModal, modalFacts } from '../ui/modal.js';
import { icons } from '../ui/icons.js';

// Orden y colores del rail, tal cual la captura
const STATUSES = [
  { id: 'confirmed', tone: 'green' },
  { id: 'pending', tone: 'orange' },
  { id: 'rejected', tone: 'red' }
];

const TONE = Object.fromEntries(STATUSES.map(status => [status.id, status.tone]));

const TYPES = ['return', 'exchange', 'defect'];
const SLOTS = ['morning', 'afternoon'];

const typeLabel = type => t(`sat.type.${type}`) || type;
const slotLabel = slot => t(`sat.slot.${slot}`) || slot;
const statusLabel = status => t(`sat.status.${status}`) || status;

export default async function sat(host) {
  await docList(host, {
    key: 'sat',
    endpoint: '/api/portal/returns',
    crumb: t('nav.sat'),
    statuses: STATUSES,
    filters: false,                       // la toolbar de /sat solo busca
    action: {
      label: t('sat.new'),
      icon: icons.plus(17),
      onClick: ({ reload }) => openForm(reload)
    },

    columns: [
      { label: t('sat.col.img'), className: 'sat-img' },
      { label: t('sat.col.code') },
      { label: t('sat.col.date') },
      { label: t('sat.col.client') },
      { label: t('sat.col.type') },
      { label: t('sat.col.slot') },
      { label: t('sat.col.packages'), className: 'num' },
      { label: t('sat.col.items'), className: 'num' },
      { label: t('sat.col.status') },
      { label: t('sat.col.resolution') }
    ],

    cells: request => [
      photo(request),
      `<button type="button" class="grid-link" data-open="${esc(request.id)}">${esc(request.code)}</button>`,
      esc(date(request.createdAt)),
      esc(request.client || ''),
      esc(typeLabel(request.type)),
      esc(slotLabel(request.pickupSlot)),
      esc(num(request.packages)),
      esc(num(request.items)),
      statusChip(statusLabel(request.status), TONE[request.status] || 'none'),
      request.resolution
        ? esc(request.resolution)
        : `<span class="sat-pending">${esc(t('sat.noResolution'))}</span>`
    ],

    detail: request => ({
      title: t('sat.detail', { n: request.code }),
      subtitle: date(request.createdAt),
      body: `
        ${modalFacts([
          [t('sat.col.status'), statusChip(statusLabel(request.status), TONE[request.status] || 'none')],
          [t('sat.col.type'), esc(typeLabel(request.type))],
          [t('sat.col.slot'), esc(slotLabel(request.pickupSlot))],
          [t('sat.col.packages'), esc(num(request.packages))],
          [t('sat.col.items'), esc(num(request.items))],
          request.reference ? [t('sat.reference'), esc(request.reference)] : null,
          request.owner ? [t('sat.owner'), esc(request.owner)] : null
        ])}
        ${request.notes ? `
          <h3 class="sat-block">${esc(t('sat.notes'))}</h3>
          <p class="sat-text">${esc(request.notes)}</p>` : ''}
        <h3 class="sat-block">${esc(t('sat.col.resolution'))}</h3>
        <p class="sat-text">${request.resolution
          ? esc(request.resolution)
          : `<span class="sat-pending">${esc(t('sat.resolutionPending'))}</span>`}</p>`
    })
  });
}

// Miniatura de la columna IMG: sin foto queda el marcador neutro, no un hueco
const photo = request => request.photoUrl
  ? `<img class="sat-thumb" src="${esc(request.photoUrl)}" alt="" loading="lazy">`
  : `<span class="sat-thumb sat-thumb-empty" role="img"
       aria-label="${esc(t('sat.noPhoto'))}" title="${esc(t('sat.noPhoto'))}">${icons.image(18)}</span>`;

// ── NUEVA DEVOLUCIÓN ─────────────────────────────────────────────────────────
// El alta va en el modal ancho del portal (ui/modal.js): <dialog> nativo, así que
// Esc, el velo y la devolución del foco al botón vienen de serie.
function openForm(reload) {
  const dialog = openModal({
    title: t('sat.new'),
    subtitle: t('sat.formLead'),
    body: `
      <form class="sat-form" novalidate>
        <p class="sat-error" role="alert" hidden></p>
        <div class="sat-grid">
          <p class="acc-field"><label>
            <span>${esc(t('sat.form.type'))}</span>
            <select name="type">
              ${TYPES.map(type => `<option value="${type}">${esc(typeLabel(type))}</option>`).join('')}
            </select>
          </label></p>
          <p class="acc-field"><label>
            <span>${esc(t('sat.form.slot'))}</span>
            <select name="pickupSlot">
              ${SLOTS.map(slot => `<option value="${slot}">${esc(slotLabel(slot))}</option>`).join('')}
            </select>
          </label></p>
          <p class="acc-field"><label>
            <span>${esc(t('sat.form.packages'))}</span>
            <input type="number" name="packages" min="1" max="99" step="1" value="1" required>
          </label></p>
          <p class="acc-field"><label>
            <span>${esc(t('sat.form.items'))}</span>
            <input type="number" name="items" min="1" max="999" step="1" value="1" required>
          </label></p>
          <p class="acc-field sat-wide"><label>
            <span>${esc(t('sat.form.reference'))}</span>
            <input type="text" name="reference" maxlength="120"
              placeholder="${esc(t('sat.form.referenceHint'))}">
          </label></p>
          <p class="acc-field sat-wide"><label>
            <span>${esc(t('sat.form.notes'))}</span>
            <textarea name="notes" rows="3" maxlength="1000"></textarea>
          </label></p>
        </div>
        <div class="acc-actions">
          <button type="button" class="btn-ghost" data-cancel>${esc(t('sat.form.cancel'))}</button>
          <button type="submit" class="btn-primary">${esc(t('sat.form.submit'))}</button>
        </div>
      </form>`
  });

  const form = dialog.querySelector('.sat-form');
  const error = dialog.querySelector('.sat-error');
  dialog.querySelector('[data-cancel]').onclick = () => dialog.close();
  form.querySelector('select')?.focus();

  form.onsubmit = async event => {
    event.preventDefault();
    const values = Object.fromEntries(new FormData(form));
    const submit = form.querySelector('button[type=submit]');
    submit.disabled = true;
    error.hidden = true;

    try {
      const created = await api.post('/api/portal/returns', {
        type: values.type,
        pickupSlot: values.pickupSlot,
        packages: Number(values.packages) || 0,
        items: Number(values.items) || 0,
        reference: String(values.reference || '').trim() || null,
        notes: String(values.notes || '').trim() || null
      });
      dialog.close();
      await reload();
      openModal({
        title: t('sat.createdTitle'),
        body: `<p class="sat-text">${esc(t('sat.created', { code: created.code }))}</p>`
      });
    } catch (failure) {
      submit.disabled = false;
      error.textContent = failure.body?.error || t('sat.createError');
      error.hidden = false;
    }
  };
}
