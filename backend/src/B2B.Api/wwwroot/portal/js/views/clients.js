// Vista "Clientes" del agente (10_clients_full.png) — la pantalla central del modo
// agente. Lista su cartera con GET /api/agent/clients, filtra y pagina, y desde el
// botón SELECCIONAR de cada fila abre el modal de suplantación: "Cliente
// seleccionado", con Pedido de reposición / Pedido de programación / Editar cliente.
//
// Reposición y Programación fijan la ventana de servicio, llaman a
// POST /api/agent/impersonate y entran en el catálogo del cliente (state.actAs).

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur, date } from '../format.js';
import { state } from '../state.js';
import { go, href } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { gridTable } from '../ui/table.js';
import { pager, bindPager } from '../ui/pager.js';
import { icons } from '../ui/icons.js';

const PAGE = 12;

// El país llega como ISO (ES); el modal de suplantación lo muestra con nombre.
const COUNTRY = { ES: 'España', PT: 'Portugal', FR: 'Francia', IT: 'Italia',
  DE: 'Alemania', GB: 'Reino Unido', US: 'Estados Unidos', AD: 'Andorra' };
const countryName = code => COUNTRY[String(code || '').toUpperCase()] || code || '';

const columns = () => [
  { label: t('clients.col.code') },
  { label: t('clients.col.name') },
  { label: t('clients.col.segment') },
  { label: t('clients.col.country') },
  { label: t('clients.col.province') },
  { label: t('clients.col.city') },
  { label: t('clients.col.active') },
  { label: t('clients.col.purchase') },
  { label: t('clients.col.valid') },
  { label: t('clients.col.lastOrder') },
  { label: t('clients.col.total'), className: 'num' },
  { label: t('clients.col.select'), className: 'cl-pick-col' }
];

// El color nunca va solo: el check verde lleva su título, y el "no" es un guion
// apagado, así que la columna se lee igual sin distinguir colores.
const yes = () => `<span class="cl-yes" title="${esc(t('clients.filter.yes'))}">${icons.check(16)}</span>`;
const no = () => `<span class="cl-no" title="${esc(t('clients.filter.no'))}" aria-hidden="true">—</span>`;
const flag = value => value ? yes() : no();

export default async function clients(host) {
  let query = readQuery();
  let data = null;

  host.innerHTML = `
    <div class="page cl">
      ${pageHead(t('clients.title'), [t('clients.crumb')])}
      <p class="cl-requests"><a href="${href('clients/requests')}">${esc(t('clients.requests'))}</a></p>
      <div id="tools"></div>
      <div id="list" aria-live="polite"><div class="skeleton short"></div></div>
      <div id="pagerHost"></div>
    </div>`;

  const tools = host.querySelector('#tools');
  const list = host.querySelector('#list');
  const pagerHost = host.querySelector('#pagerHost');

  function readQuery() {
    const params = new URLSearchParams(location.search);
    const tri = key => (['true', 'false'].includes(params.get(key)) ? params.get(key) : '');
    return {
      search: params.get('search') || '',
      city: params.get('city') || '',
      segment: params.get('segment') || '',
      active: tri('active'),
      canShop: tri('canShop'),
      skip: Number(params.get('skip')) || 0,
      take: Number(params.get('take')) || PAGE
    };
  }

  function writeQuery() {
    const params = new URLSearchParams();
    for (const key of ['search', 'city', 'segment', 'active', 'canShop'])
      if (query[key]) params.set(key, query[key]);
    if (query.skip) params.set('skip', String(query.skip));
    if (query.take !== PAGE) params.set('take', String(query.take));
    const search = params.toString();
    history.replaceState({}, '', location.pathname + (search ? `?${search}` : ''));
  }

  function apiParams() {
    const params = new URLSearchParams();
    for (const key of ['search', 'city', 'segment', 'active', 'canShop'])
      if (query[key]) params.set(key, query[key]);
    params.set('skip', String(query.skip));
    params.set('take', String(query.take));
    params.set('locale', lang());
    return params;
  }

  async function load() {
    writeQuery();
    list.setAttribute('aria-busy', 'true');
    try {
      data = await api.agentClients(apiParams());
    } catch {
      list.removeAttribute('aria-busy');
      // El endpoint de agente puede no estar todavía: la vista degrada con un
      // estado de error legible, no con una pantalla en blanco.
      paintTools();
      list.innerHTML = `<div class="panel"><b>${esc(t('clients.errorTitle'))}</b>${esc(t('clients.errorBody'))}</div>`;
      pagerHost.innerHTML = '';
      return;
    }
    list.removeAttribute('aria-busy');
    paintTools();
    paintList();
    paintPager();
  }

  function paintTools() {
    const triSelect = (id, label, value) => `
      <label class="tb-field">
        <span class="tb-legend">${esc(label)}</span>
        <span class="tb-select">
          <select id="${id}" aria-label="${esc(label)}">
            <option value=""${value === '' ? ' selected' : ''}>${esc(t('clients.filter.all'))}</option>
            <option value="true"${value === 'true' ? ' selected' : ''}>${esc(t('clients.filter.yes'))}</option>
            <option value="false"${value === 'false' ? ' selected' : ''}>${esc(t('clients.filter.no'))}</option>
          </select>
        </span>
      </label>`;

    const textField = (id, label, value) => `
      <label class="tb-field">
        <span class="tb-legend">${esc(label)}</span>
        <span class="tb-select cl-text">
          <input type="text" id="${id}" value="${esc(value)}" placeholder="${esc(t('clients.filter.all'))}"
            aria-label="${esc(label)}" autocomplete="off">
        </span>
      </label>`;

    tools.innerHTML = `
      <div class="doc-tools cl-tools">
        <form class="doc-search" role="search">
          <input type="search" name="search" value="${esc(query.search)}"
            placeholder="${esc(t('clients.search'))}" aria-label="${esc(t('clients.searchLabel'))}">
          <button type="submit" aria-label="${esc(t('clients.searchLabel'))}">${icons.search(17)}</button>
        </form>
        ${textField('city', t('clients.filter.city'), query.city)}
        ${triSelect('active', t('clients.filter.active'), query.active)}
        ${triSelect('canShop', t('clients.filter.purchase'), query.canShop)}
        ${textField('segment', t('clients.filter.segment'), query.segment)}
        <a class="btn-primary cl-new" href="${href('agent/clients/new')}">
          ${icons.user(16)} ${esc(t('clients.new'))}</a>
      </div>`;

    const form = tools.querySelector('.doc-search');
    form.onsubmit = event => {
      event.preventDefault();
      query = { ...query, search: form.elements.search.value.trim(), skip: 0 };
      load().then(() => tools.querySelector('.doc-search input')?.focus());
    };

    const bindText = id => {
      const input = tools.querySelector(`#${id}`);
      input.onchange = () => { query = { ...query, [id]: input.value.trim(), skip: 0 }; load(); };
    };
    bindText('city');
    bindText('segment');

    const bindSelect = id => {
      const select = tools.querySelector(`#${id}`);
      select.onchange = () => { query = { ...query, [id]: select.value, skip: 0 }; load(); };
    };
    bindSelect('active');
    bindSelect('canShop');
  }

  function paintList() {
    const items = data.items || [];
    const actingId = state.actingClient?.id;

    list.innerHTML = gridTable({
      columns: columns(),
      rows: items.map(client => {
        const selected = actingId != null && String(actingId) === String(client.clientId);
        return {
          id: client.clientId,
          cells: [
            esc(client.number || ''),
            `<b>${esc(client.name || '')}</b>`,
            esc(client.segment || ''),
            esc(client.country || ''),
            esc(client.province || ''),
            esc(client.city || ''),
            flag(client.active),
            flag(client.canShop),
            flag(client.valid),
            client.lastOrderDate ? esc(date(client.lastOrderDate)) : `<span class="cl-no" aria-hidden="true">—</span>`,
            esc(eur(client.total)),
            selected
              ? `<span class="cl-selected">${esc(t('clients.selected'))}
                   <button type="button" class="cl-release" data-release aria-label="${esc(t('clients.deselect'))}"
                     title="${esc(t('clients.deselect'))}">${icons.close(15)}</button></span>`
              : `<button type="button" class="cl-pick" data-pick="${esc(client.clientId)}"
                   aria-label="${esc(t('clients.select'))}" title="${esc(t('clients.select'))}">${icons.login(16)}</button>`
          ]
        };
      }),
      empty: t('clients.none')
    });

    const byId = new Map(items.map(client => [String(client.clientId), client]));
    list.querySelectorAll('[data-pick]').forEach(button => {
      button.onclick = () => openSelectModal(byId.get(button.dataset.pick));
    });
    list.querySelector('[data-release]')?.addEventListener('click', () => {
      state.stopActing();
      // Igual que el botón del header: re-resolver la ruta re-pinta el chrome
      // (carrito y "Deseleccionar" desaparecen) además de la lista. Con solo
      // load() el header seguía mostrando la suplantación hasta recargar (P2-2).
      go('clients');
    });
  }

  function paintPager() {
    pagerHost.innerHTML = pager({ total: data.total ?? 0, skip: query.skip, take: query.take });
    bindPager(pagerHost, {
      take: query.take,
      onPage: skip => { query = { ...query, skip: Math.max(0, skip) }; load(); },
      onSize: take => { query = { ...query, take, skip: 0 }; load(); }
    });
  }

  await load();
}

// ── Modal de suplantación ─────────────────────────────────────────────────────
// "Cliente seleccionado: {nombre}", país de origen y las tres acciones. Reposición
// y Programación fijan la ventana y suplantan; Editar cliente va al placeholder.
function openSelectModal(client) {
  if (!client) return;

  const dialog = document.createElement('dialog');
  dialog.className = 'dlg dlg-select';
  dialog.innerHTML = `
    <div class="panel sel-panel">
      <div class="sel-head">
        <p class="sel-kicker">${esc(t('clients.modal.title'))}</p>
        <h2>${esc(client.name || '')}</h2>
        <p class="sel-country">${esc(t('clients.modal.country'))}:
          <b>${esc(countryName(client.country) || '—')}</b></p>
      </div>
      <p class="sel-actions-title">${esc(t('clients.modal.actions'))}</p>
      <div class="sel-actions">
        <button type="button" class="sel-card" data-window="replenishment">
          <span class="sel-card-title">${esc(t('clients.modal.replenishment'))}</span>
          <span class="sel-card-desc">${esc(t('clients.modal.replenishmentDesc'))}</span>
        </button>
        <button type="button" class="sel-card" data-window="scheduled">
          <span class="sel-card-title">${esc(t('clients.modal.scheduled'))}</span>
          <span class="sel-card-desc">${esc(t('clients.modal.scheduledDesc'))}</span>
        </button>
        <a class="sel-card" href="${href('agent/clients/edit')}?id=${encodeURIComponent(client.clientId)}">
          <span class="sel-card-title">${esc(t('clients.modal.edit'))}</span>
          <span class="sel-card-desc">${esc(t('clients.modal.editDesc', { name: client.name || '' }))}</span>
        </a>
      </div>
      <p class="sel-error" id="selError" hidden></p>
      <div class="dlg-actions">
        <button type="button" class="btn-ghost" data-close>${esc(t('clients.modal.close'))}</button>
      </div>
    </div>`;

  document.body.append(dialog);
  dialog.addEventListener('close', () => dialog.remove());
  dialog.querySelector('[data-close]').onclick = () => dialog.close();

  const error = dialog.querySelector('#selError');

  dialog.querySelectorAll('[data-window]').forEach(card => {
    card.onclick = async () => {
      const window_ = card.dataset.window;
      dialog.querySelectorAll('.sel-card').forEach(button => { button.disabled = true; });
      error.hidden = true;
      try {
        const result = await api.impersonate(client.clientId);
        // El token de suplantación pasa a mandar en api.js; el dashboard/catálogo
        // ya se pintan como el cliente.
        state.actAs({ token: result.token, client: result.client || client, window: window_ });
        dialog.close();
        go('dashboard');
      } catch {
        dialog.querySelectorAll('.sel-card').forEach(button => { button.disabled = false; });
        error.textContent = t('clients.modal.error');
        error.hidden = false;
      }
    };
  });

  dialog.showModal();
  dialog.querySelector('.sel-card')?.focus();
}
