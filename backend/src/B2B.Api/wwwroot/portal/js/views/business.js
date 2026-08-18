// Vista 12 — /{market}/{lang}/business (10-business.png).
//
// El nombre del cliente como H1 y sus dos bloques: "Datos generales" (EMAIL,
// TELÉFONO, TELÉFONO SECUNDARIO, WEB, NOMBRE COMERCIAL, EMAIL FACTURACIÓN) y
// "Datos fiscales de la empresa" (RAZÓN SOCIAL, CIF, PAÍS, CÓDIGO POSTAL,
// PROVINCIA, CIUDAD, DIRECCIÓN + recargo de equivalencia), cada uno con EDITAR.
//
// Cierra la página el bloque "Direcciones de envío" con su botón AÑADIR, que es el
// segundo botón que registra `estructura.json` para esta vista (M8).
//
// Los datos maestros son de Business Central: ni EDITAR ni AÑADIR los escriben,
// registran una SOLICITUD de cambio (plan, Fase 4). La ficha sigue diciendo lo que
// dice BC hasta que el cambio vuelva por el sync.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, date } from '../format.js';
import { icons } from '../ui/icons.js';
import { pageHead } from '../ui/chrome.js';

const SECTIONS = {
  general: ['email', 'phone', 'secondaryPhone', 'web', 'tradeName', 'billingEmail'],
  fiscal: ['fiscalName', 'fiscalId', 'countryIsoId', 'zipCode', 'province', 'city',
    'streetAddress', 'recargoEquivalencia']
};

// Campos de una dirección de envío nueva, en el orden en que se piden
const ADDRESS_FIELDS = ['alias', 'streetAddress', 'num', 'zipCode', 'city', 'province', 'countryIsoId'];

const LABELS = {
  email: 'business.email', phone: 'business.phone', secondaryPhone: 'business.secondaryPhone',
  web: 'business.web', tradeName: 'business.tradeName', billingEmail: 'business.billingEmail',
  fiscalName: 'business.fiscalName', fiscalId: 'business.fiscalId', countryIsoId: 'business.country',
  zipCode: 'business.zipCode', province: 'business.province', city: 'business.city',
  streetAddress: 'business.street', recargoEquivalencia: 'business.recargo',
  alias: 'business.alias', num: 'business.addressNum'
};

/** "ES" → "España (ES)" en el idioma de la ruta, con Intl del navegador */
function countryName(iso) {
  if (!iso) return '';
  try {
    const name = new Intl.DisplayNames([lang()], { type: 'region' }).of(iso);
    return name && name !== iso ? `${name} (${iso})` : iso;
  } catch { return iso; }
}

export default async function business(host) {
  let data = null;
  let editing = '';      // '' | 'general' | 'fiscal'
  let notice = null;

  host.innerHTML = `
    <div class="page account">
      <div id="bizHead">${pageHead('', [t('business.crumb')])}</div>
      <div id="bizHost"><div class="skeleton short"></div></div>
    </div>`;

  const head = host.querySelector('#bizHead');
  const container = host.querySelector('#bizHost');

  try {
    data = await api.get('/api/portal/business');
  } catch {
    container.innerHTML = `<div class="panel"><b>${esc(t('business.errorTitle'))}</b>${esc(t('business.errorBody'))}</div>`;
    return;
  }

  head.innerHTML = pageHead(data.name || '', [t('business.crumb')]);

  function render() {
    container.innerHTML = `
      ${notice ? `
        <div class="notice notice-${notice.tone}"${notice.tone === 'error' ? ' role="alert"' : ''}>
          ${notice.tone === 'ok' ? icons.check(18) : icons.alert(18)}
          <div><span>${esc(notice.text)}</span></div>
        </div>` : ''}

      ${section('general', icons.building(20), t('business.general'))}
      ${section('fiscal', icons.coin(20), t('business.fiscal'))}
      ${addressesSection()}
      ${pending()}`;
    bind();
  }

  // ── Direcciones de envío (M8) ───────────────────────────────────────────────
  // Las manda BC (shipping-address del sync). AÑADIR no crea la dirección: abre el
  // formulario y registra la solicitud, igual que hacen los EDITAR de arriba.
  const addressesSection = () => `
    <section class="biz-section">
      <header class="acc-head biz-head">
        <h2>${icons.truck(20)}${esc(t('business.addresses'))}</h2>
        ${editing === 'addresses' ? '' : `
          <button type="button" class="btn-primary acc-edit" data-edit="addresses">
            ${icons.plus(15)}${esc(t('business.add'))}</button>`}
      </header>
      <div class="biz-card">
        ${editing === 'addresses' ? addressForm() : addressList()}
      </div>
    </section>`;

  const addressList = () => {
    const list = data.addresses || [];
    if (!list.length) return `<p class="biz-none">${esc(t('business.noAddresses'))}</p>`;
    return `<ul class="biz-addresses">${list.map(address => `
      <li>
        <b>${esc(address.alias || t('business.addressNoAlias'))}</b>
        <span>${esc(address.label || '')}</span>
      </li>`).join('')}</ul>`;
  };

  const addressForm = () => `
    <form class="biz-form" data-form="addresses">
      <p class="biz-hint">${icons.alert(16)}<span>${esc(t('business.requestHint'))}</span></p>
      <div class="biz-grid">
        ${ADDRESS_FIELDS.map(key => `
          <p class="acc-field"><label>
            <span>${esc(t(LABELS[key]))}</span>
            <input type="text" name="${key}" maxlength="200"${key === 'alias' ? ' required' : ''}>
          </label></p>`).join('')}
      </div>
      <div class="acc-actions">
        <button type="button" class="btn-ghost" data-cancel>${esc(t('business.cancel'))}</button>
        <button type="submit" class="btn-primary">${esc(t('business.request'))}</button>
      </div>
    </form>`;

  const section = (id, icon, title) => `
    <section class="biz-section">
      <header class="acc-head biz-head">
        <h2>${icon}${esc(title)}</h2>
        ${editing === id ? '' : `
          <button type="button" class="btn-primary acc-edit" data-edit="${id}">
            ${icons.pencil(15)}${esc(t('business.edit'))}</button>`}
      </header>
      <div class="biz-card">
        ${editing === id ? form(id) : (id === 'general' ? generalFacts() : fiscalFacts())}
      </div>
    </section>`;

  // ── Lectura ─────────────────────────────────────────────────────────────────
  const generalFacts = () => `
    <dl class="biz-row three">
      ${fact('email', data.general.email)}
      ${fact('phone', data.general.phone)}
      ${fact('secondaryPhone', data.general.secondaryPhone)}
    </dl>
    <dl class="biz-row three last">
      ${fact('web', data.general.web)}
      ${fact('tradeName', data.general.tradeName)}
      ${fact('billingEmail', data.general.billingEmail)}
    </dl>`;

  const fiscalFacts = () => `
    <dl class="biz-row one">${fact('fiscalName', data.fiscal.fiscalName)}</dl>
    <dl class="biz-row one">${fact('fiscalId', data.fiscal.fiscalId)}</dl>
    <dl class="biz-row four">
      ${fact('countryIsoId', countryName(data.fiscal.countryIsoId))}
      ${fact('zipCode', data.fiscal.zipCode)}
      ${fact('province', data.fiscal.province)}
      ${fact('city', data.fiscal.city)}
    </dl>
    <dl class="biz-row one">${fact('streetAddress', street())}</dl>
    <p class="biz-check">
      <label>
        <input type="checkbox" disabled ${data.fiscal.recargoEquivalencia ? 'checked' : ''}>
        <span>${esc(t('business.recargo'))}</span>
      </label>
    </p>`;

  const street = () => [data.fiscal.streetAddress, data.fiscal.addressDetail].filter(Boolean).join(', ');

  const fact = (key, value) => `
    <div class="biz-field">
      <dt>${esc(t(LABELS[key]))}</dt>
      <dd>${value ? esc(value) : '<span class="acc-none">—</span>'}</dd>
    </div>`;

  // ── Solicitud de cambio ─────────────────────────────────────────────────────
  function form(id) {
    const source = id === 'general' ? data.general : data.fiscal;
    const fields = SECTIONS[id].filter(key => key !== 'recargoEquivalencia');

    return `
      <form class="biz-form" data-form="${id}">
        <p class="biz-hint">${icons.alert(16)}<span>${esc(t('business.requestHint'))}</span></p>
        <div class="biz-grid">
          ${fields.map(key => `
            <p class="acc-field"><label>
              <span>${esc(t(LABELS[key]))}</span>
              <input type="${key.toLowerCase().includes('email') ? 'email' : 'text'}" name="${key}"
                maxlength="200" value="${esc(source[key] ?? '')}">
            </label></p>`).join('')}
        </div>

        ${id === 'fiscal' ? `
          <p class="biz-check">
            <label>
              <input type="checkbox" name="recargoEquivalencia"
                ${data.fiscal.recargoEquivalencia ? 'checked' : ''}>
              <span>${esc(t('business.recargo'))}</span>
            </label>
          </p>` : ''}

        <div class="acc-actions">
          <button type="button" class="btn-ghost" data-cancel>${esc(t('business.cancel'))}</button>
          <button type="submit" class="btn-primary">${esc(t('business.request'))}</button>
        </div>
      </form>`;
  }

  // Lo que ya está pedido y aún no ha vuelto de BC
  const pending = () => !data.pending?.length ? '' : `
    <section class="biz-pending">
      <h2>${esc(t('business.pending'))}</h2>
      <ul>
        ${data.pending.map(request => `
          <li>
            <span class="biz-pending-when">${esc(date(request.createdAt))}</span>
            <span class="biz-pending-what">${esc(t(`business.${request.section}`))}:
              ${esc(Object.keys(request.changes || {}).map(key => t(LABELS[key] || key)).join(', '))}</span>
          </li>`).join('')}
      </ul>
    </section>`;

  function bind() {
    container.querySelectorAll('[data-edit]').forEach(button => {
      button.onclick = () => { editing = button.dataset.edit; notice = null; render(); };
    });
    container.querySelectorAll('[data-cancel]').forEach(button => {
      button.onclick = () => { editing = ''; render(); };
    });

    const form = container.querySelector('.biz-form');
    if (!form) return;
    form.querySelector('input')?.focus();

    form.onsubmit = async event => {
      event.preventDefault();
      const id = form.dataset.form;
      const values = new FormData(form);

      if (id === 'addresses') return submitAddress(form, values);

      const source = id === 'general' ? data.general : data.fiscal;

      // Solo viaja lo que ha cambiado: la solicitud dice exactamente qué revisar
      const changes = {};
      for (const key of SECTIONS[id]) {
        if (key === 'recargoEquivalencia') {
          const checked = form.elements.recargoEquivalencia?.checked ?? false;
          if (checked !== Boolean(source.recargoEquivalencia)) changes[key] = checked;
          continue;
        }
        const value = String(values.get(key) ?? '').trim();
        if (value !== String(source[key] ?? '')) changes[key] = value;
      }

      if (!Object.keys(changes).length) {
        notice = { tone: 'error', text: t('business.noChanges') };
        render();
        return;
      }

      const submit = form.querySelector('button[type=submit]');
      submit.disabled = true;
      try {
        const created = await api.post('/api/portal/business/change-request', { section: id, changes });
        data = { ...data, pending: [created, ...(data.pending || [])] };
        editing = '';
        notice = { tone: 'ok', text: t('business.requested') };
      } catch (failure) {
        submit.disabled = false;
        notice = { tone: 'error', text: failure.body?.error || t('business.requestError') };
      }
      render();
    };
  }

  // Alta de dirección de envío: mismo canal que los EDITAR
  // (POST /api/portal/business/change-request, sección "addresses").
  async function submitAddress(form, values) {
    const changes = {};
    for (const key of ADDRESS_FIELDS) {
      const value = String(values.get(key) ?? '').trim();
      if (value) changes[key] = value;
    }

    if (!changes.alias || Object.keys(changes).length < 2) {
      notice = { tone: 'error', text: t('business.addressRequired') };
      render();
      return;
    }

    const submit = form.querySelector('button[type=submit]');
    submit.disabled = true;
    try {
      const created = await api.post('/api/portal/business/change-request',
        { section: 'addresses', changes });
      data = { ...data, pending: [created, ...(data.pending || [])] };
      editing = '';
      notice = { tone: 'ok', text: t('business.requested') };
    } catch (failure) {
      submit.disabled = false;
      notice = { tone: 'error', text: failure.body?.error || t('business.requestError') };
    }
    render();
  }

  // Al final del módulo: las plantillas de arriba son const y no se pueden usar
  // antes de su declaración (zona muerta temporal).
  render();
}
