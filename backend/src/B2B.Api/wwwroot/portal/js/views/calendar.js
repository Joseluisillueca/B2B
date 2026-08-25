// Calendario de citas / partes de visita — /agent/calendar (Fase 3). Agenda propia
// del comercial (no viene de BC): planifica visitas a sus clientes. Lista sus citas
// y permite crear una nueva y marcarla como hecha o cancelada.

import { api, ApiError } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc } from '../format.js';
import { pageHead } from '../ui/chrome.js';
import { icons } from '../ui/icons.js';

const KINDS = ['cita', 'parte'];

const dtLocale = { es: 'es-ES', en: 'en-GB', fr: 'fr-FR', it: 'it-IT' };
const fmtWhen = iso => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString(dtLocale[lang()] || 'es-ES',
    { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
};

export default async function calendar(host) {
  let appointments = [];
  let clients = [];
  let notice = null;

  host.innerHTML = `
    <div class="page account calendar">
      ${pageHead(t('nav.calendar'), [t('clients.crumb'), t('calendar.crumb')])}
      <div class="cal-grid">
        <section class="cal-form-wrap" id="formWrap"></section>
        <section class="cal-list-wrap" id="listWrap"><div class="skeleton short"></div></section>
      </div>
    </div>`;

  const formWrap = host.querySelector('#formWrap');
  const listWrap = host.querySelector('#listWrap');

  async function loadClients() {
    try {
      const params = new URLSearchParams({ take: '500' });
      const data = await api.agentClients(params);
      clients = (data.items || []).map(c => ({ id: c.clientId, name: c.name, number: c.number }));
    } catch { clients = []; }
  }

  async function loadAppointments() {
    try {
      const data = await api.appointments();
      appointments = data.items || [];
    } catch {
      listWrap.innerHTML = `<div class="panel"><b>${esc(t('calendar.errorTitle'))}</b>${esc(t('calendar.errorBody'))}</div>`;
      appointments = null;
    }
  }

  function renderForm() {
    formWrap.innerHTML = `
      <h2>${esc(t('calendar.newTitle'))}</h2>
      ${notice ? `<div class="notice notice-${notice.tone}" ${notice.tone === 'error' ? 'role="alert"' : ''}>
        ${notice.tone === 'ok' ? icons.check(16) : icons.alert(16)}<div><span>${esc(notice.text)}</span></div></div>` : ''}
      <form class="cal-form" novalidate>
        <p class="acc-field"><label><span>${esc(t('calendar.field.title'))}</span>
          <input type="text" name="title" maxlength="200" required></label></p>
        <p class="acc-field"><label><span>${esc(t('calendar.field.client'))}</span>
          <select name="clientId">
            <option value="">${esc(t('calendar.noClient'))}</option>
            ${clients.map(c => `<option value="${esc(c.id)}">${esc(c.name || c.number || c.id)}</option>`).join('')}
          </select></label></p>
        <p class="acc-field"><span class="acc-label">${esc(t('calendar.field.kind'))}</span>
          <span class="tb-seg" role="group" aria-label="${esc(t('calendar.field.kind'))}">
            ${KINDS.map((k, i) => `<button type="button" class="tb-seg-opt${i === 0 ? ' on' : ''}"
              data-kind="${k}" aria-pressed="${i === 0 ? 'true' : 'false'}">${esc(t(`calendar.kind.${k}`))}</button>`).join('')}
          </span>
          <input type="hidden" name="kind" value="${KINDS[0]}"></p>
        <div class="cal-row2">
          <p class="acc-field"><label><span>${esc(t('calendar.field.when'))}</span>
            <input type="datetime-local" name="start" required></label></p>
          <p class="acc-field cal-dur"><label><span>${esc(t('calendar.field.duration'))}</span>
            <input type="number" name="durationMinutes" min="15" max="480" step="15" value="60"></label></p>
        </div>
        <p class="acc-field"><label><span>${esc(t('calendar.field.notes'))}</span>
          <textarea name="notes" rows="2" maxlength="2000"></textarea></label></p>
        <div class="acc-actions">
          <button type="submit" class="btn-primary">${esc(t('calendar.save'))}</button>
        </div>
      </form>`;

    const form = formWrap.querySelector('form');

    // TIPO: control segmentado (Cita | Parte de visita); el valor va en el input oculto
    form.querySelectorAll('.tb-seg-opt[data-kind]').forEach(button => {
      button.onclick = () => {
        form.querySelectorAll('.tb-seg-opt[data-kind]').forEach(other => {
          const on = other === button;
          other.classList.toggle('on', on);
          other.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        form.elements.kind.value = button.dataset.kind;
      };
    });

    form.onsubmit = async event => {
      event.preventDefault();
      notice = null;
      const title = form.elements.title.value.trim();
      const when = form.elements.start.value;
      if (!title) { notice = { tone: 'error', text: t('calendar.titleRequired') }; return renderForm(); }
      if (!when) { notice = { tone: 'error', text: t('calendar.whenRequired') }; return renderForm(); }

      const submit = form.querySelector('button[type=submit]');
      submit.disabled = true;
      try {
        await api.createAppointment({
          title,
          clientId: form.elements.clientId.value || null,
          kind: form.elements.kind.value,
          start: new Date(when).toISOString(),
          durationMinutes: Number(form.elements.durationMinutes.value) || 60,
          notes: form.elements.notes.value.trim() || null
        });
        notice = { tone: 'ok', text: t('calendar.created') };
        await loadAppointments();
        renderForm();
        renderList();
      } catch (failure) {
        submit.disabled = false;
        notice = { tone: 'error', text: failure instanceof ApiError && failure.body?.error ? failure.body.error : t('calendar.error') };
        renderForm();
      }
    };
  }

  function renderList() {
    if (appointments === null) return;   // ya se pintó el error
    if (!appointments.length) {
      listWrap.innerHTML = `<div class="cal-empty">${icons.calendar ? icons.calendar(28) : ''}<p>${esc(t('calendar.none'))}</p></div>`;
      return;
    }
    listWrap.innerHTML = `
      <ul class="cal-list">
        ${appointments.map(a => `
          <li class="cal-item cal-${esc(a.status)}" data-id="${esc(a.id)}">
            <div class="cal-when">
              <span class="cal-kind cal-kind-${esc(a.kind)}">${esc(t(`calendar.kind.${a.kind}`))}</span>
              <time>${esc(fmtWhen(a.start))}</time>
            </div>
            <div class="cal-body">
              <b>${esc(a.title)}</b>
              ${a.clientName ? `<span class="cal-client">${icons.user(13)} ${esc(a.clientName)}</span>` : ''}
              ${a.notes ? `<p class="cal-notes">${esc(a.notes)}</p>` : ''}
            </div>
            <div class="cal-actions">
              <span class="cal-status cal-status-${esc(a.status)}">${esc(t(`calendar.status.${a.status}`))}</span>
              ${a.status === 'pending' ? `
                <button type="button" class="btn-ghost" data-done>${esc(t('calendar.markDone'))}</button>
                <button type="button" class="btn-ghost" data-cancel>${esc(t('calendar.markCancel'))}</button>` : ''}
            </div>
          </li>`).join('')}
      </ul>`;

    listWrap.querySelectorAll('.cal-item').forEach(li => {
      const id = li.dataset.id;
      li.querySelector('[data-done]')?.addEventListener('click', () => setStatus(id, 'done'));
      li.querySelector('[data-cancel]')?.addEventListener('click', () => setStatus(id, 'canceled'));
    });
  }

  async function setStatus(id, status) {
    try {
      await api.setAppointmentStatus(id, status);
      await loadAppointments();
      renderList();
    } catch { /* si falla, se deja como estaba */ }
  }

  await Promise.all([loadClients(), loadAppointments()]);
  renderForm();
  renderList();
}
