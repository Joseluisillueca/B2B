// Listado agregado de documentos del AGENTE (Fase 3): pedidos / albaranes / facturas
// de TODA su cartera en una tabla con columna CLIENTE, filtro por cliente y rail de
// estados. Hermano de doc-list.js (el del cliente), pero contra /api/agent/documents.
//
// Al pulsar el nombre del cliente de una fila, el agente SUPLANTA a ese cliente y
// entra en su listado propio (donde puede abrir el detalle del documento).

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc } from '../format.js';
import { state } from '../state.js';
import { go } from '../router.js';
import { pageHead } from './chrome.js';
import { statusRail, bindStatusRail } from './status-rail.js';
import { gridTable } from './table.js';
import { pager, bindPager } from './pager.js';
import { icons } from './icons.js';
import { DATE_RANGES } from './doc-list.js';

const PAGE = 12;

function rangeOf(id) {
  const today = new Date();
  const iso = value => value.toISOString().slice(0, 10);
  const back = days => iso(new Date(today.getTime() - days * 864e5));
  const year = today.getFullYear();
  switch (id) {
    case 'm1': return { from: back(30) };
    case 'm3': return { from: back(90) };
    case 'y0': return { from: `${year}-01-01` };
    case 'y1': return { from: `${year - 1}-01-01`, to: `${year - 1}-12-31` };
    default: return {};
  }
}

/**
 * config:
 *   type      'order' | 'delivery-note' | 'invoice'
 *   key       prefijo i18n ('orders' | 'delivery-notes' | 'invoices')
 *   statuses  [{ id, tone }] del rail
 *   columns   columnas SIN la de cliente (la añade el helper delante)
 *   cells     item => [celdas SIN la de cliente]
 *   orderType (opcional) 'SCHEDULED' para la vista de pedidos de selección
 */
export async function agentDocList(host, config) {
  const { type, key, statuses, columns, cells, orderType = '',
    title = t(`nav.${key}`), crumbs = [t('clients.crumb'), t(`nav.${key}`)], intro = '' } = config;
  let query = readQuery();
  let data = null;

  host.innerHTML = `
    <div class="page docs agent-docs">
      <div class="docs-head">${pageHead(title, crumbs)}${intro}</div>
      <aside class="docs-rail" id="rail"></aside>
      <div class="docs-main">
        <div id="tools"></div>
        <div id="list" aria-live="polite"><div class="skeleton short"></div></div>
        <div id="pagerHost"></div>
      </div>
    </div>`;

  const rail = host.querySelector('#rail');
  const tools = host.querySelector('#tools');
  const list = host.querySelector('#list');
  const pagerHost = host.querySelector('#pagerHost');

  function readQuery() {
    const params = new URLSearchParams(location.search);
    return {
      search: params.get('search') || '',
      status: params.get('status') || '',
      client: params.get('client') || '',
      dates: DATE_RANGES.includes(params.get('dates')) ? params.get('dates') : '',
      skip: Number(params.get('skip')) || 0,
      take: Number(params.get('take')) || PAGE
    };
  }

  function writeQuery() {
    const params = new URLSearchParams();
    for (const k of ['search', 'status', 'client', 'dates'])
      if (query[k]) params.set(k, query[k]);
    if (query.skip) params.set('skip', String(query.skip));
    if (query.take !== PAGE) params.set('take', String(query.take));
    const search = params.toString();
    history.replaceState({}, '', location.pathname + (search ? `?${search}` : ''));
  }

  function apiParams() {
    const params = new URLSearchParams();
    params.set('type', type);
    if (orderType) params.set('orderType', orderType);
    for (const k of ['search', 'status', 'client'])
      if (query[k]) params.set(k, query[k]);
    const range = rangeOf(query.dates);
    if (range.from) params.set('from', range.from);
    if (range.to) params.set('to', range.to);
    params.set('locale', lang());
    params.set('skip', String(query.skip));
    params.set('take', String(query.take));
    return params;
  }

  async function load() {
    writeQuery();
    list.setAttribute('aria-busy', 'true');
    try {
      data = await api.agentDocuments(apiParams());
    } catch {
      list.removeAttribute('aria-busy');
      list.innerHTML = `<div class="panel"><b>${esc(t('docs.errorTitle'))}</b>${esc(t('docs.errorBody'))}</div>`;
      rail.innerHTML = ''; tools.innerHTML = ''; pagerHost.innerHTML = '';
      return;
    }
    list.removeAttribute('aria-busy');
    paintRail();
    paintTools();
    paintList();
    paintPager();
  }

  function paintRail() {
    const counts = data.counts || {};
    const items = [
      { id: '', tone: 'none', label: t(`${key}.status.all`), count: counts.all ?? 0 },
      ...statuses.map(s => ({ id: s.id, tone: s.tone, label: t(`${key}.status.${s.id}`), count: counts[s.id] ?? 0 }))
    ];
    rail.innerHTML = statusRail({ label: t('docs.filters'), items, active: query.status });
    bindStatusRail(rail, status => {
      query = { ...query, status: status === query.status ? '' : status, skip: 0 };
      load();
    });
  }

  function paintTools() {
    const clients = data.clients || [];
    tools.innerHTML = `
      <div class="doc-tools">
        <form class="doc-search" role="search">
          <input type="search" name="search" value="${esc(query.search)}"
            placeholder="${esc(t('agentDocs.search'))}" aria-label="${esc(t('docs.searchLabel'))}">
          <button type="submit" aria-label="${esc(t('docs.searchLabel'))}">${icons.search(17)}</button>
        </form>
        <label class="tb-field">
          <span class="tb-legend">${esc(t('agentDocs.client'))}</span>
          <span class="tb-select">
            <select id="client" aria-label="${esc(t('agentDocs.client'))}">
              <option value="">${esc(t('agentDocs.allClients'))}</option>
              ${clients.map(c => `<option value="${esc(c.id)}"${c.id === query.client ? ' selected' : ''}>
                ${esc(c.name || c.number || c.id)}</option>`).join('')}
            </select>
          </span>
        </label>
        <label class="tb-field">
          <span class="tb-legend">${esc(t('docs.dates'))}</span>
          <span class="tb-select">
            <select id="dates" aria-label="${esc(t('docs.dates'))}">
              ${DATE_RANGES.map(range => `<option value="${esc(range)}"${range === query.dates ? ' selected' : ''}>
                ${esc(t(`docs.date.${range || 'all'}`))}</option>`).join('')}
            </select>
          </span>
        </label>
      </div>`;

    const form = tools.querySelector('.doc-search');
    form.onsubmit = event => {
      event.preventDefault();
      query = { ...query, search: form.elements.search.value.trim(), skip: 0 };
      load().then(() => tools.querySelector('.doc-search input')?.focus());
    };
    const client = tools.querySelector('#client');
    client.onchange = () => { query = { ...query, client: client.value, skip: 0 }; load(); };
    const dates = tools.querySelector('#dates');
    dates.onchange = () => { query = { ...query, dates: dates.value, skip: 0 }; load(); };
  }

  function paintList() {
    const items = data.items || [];
    list.innerHTML = gridTable({
      columns: [{ label: t('agentDocs.client') }, ...columns],
      rows: items.map(item => ({
        id: item.id,
        cells: [
          `<button type="button" class="grid-link ag-client" data-drill="${esc(item.client?.id || '')}">
             <b>${esc(item.client?.name || '')}</b>
             ${item.client?.number ? `<span class="ag-cnum">${esc(item.client.number)}</span>` : ''}</button>`,
          ...cells(item)
        ]
      })),
      empty: t('agentDocs.empty')
    });

    // Drill: suplantar al cliente de la fila y entrar en su listado del mismo tipo
    list.querySelectorAll('[data-drill]').forEach(button => {
      button.onclick = () => drill(button.dataset.drill);
    });
  }

  async function drill(clientId) {
    if (!clientId) return;
    try {
      const result = await api.impersonate(clientId);
      state.actAs({ token: result.token, client: result.client });
      go(key);   // 'orders' | 'delivery-notes' | 'invoices' del cliente suplantado
    } catch { /* si falla la suplantación, la fila simplemente no navega */ }
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
  return { reload: load };
}
