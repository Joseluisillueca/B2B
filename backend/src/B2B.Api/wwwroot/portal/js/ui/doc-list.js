// Patrón de listado de documentos del portal actual, compartido por Pedidos,
// Albaranes y Facturas (03/04/05-*.png):
//
//   migas → H1 → rail de estados → toolbar (Buscar + selects + botón azul de
//   exportar) → tabla de cabecera negra → "No se han encontrado resultados" →
//   paginación «‹ 1 ›» + "Mostrar 12"
//
// Las tres vistas son la misma máquina con distintas columnas, así que vive aquí
// una sola vez y cada vista la configura. El filtro viaja en la URL: compartir el
// enlace comparte el listado que estás viendo.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur, num } from '../format.js';
import { pageHead } from './chrome.js';
import { statusRail, bindStatusRail } from './status-rail.js';
import { gridTable } from './table.js';
import { pager, bindPager } from './pager.js';
import { openModal } from './modal.js';
import { icons } from './icons.js';

const PAGE = 12;   // "Mostrar 12" de la referencia

// El select "Fechas" de la toolbar no es un calendario en la referencia: es una
// lista corta de rangos. Cada uno se traduce a from/to para la API.
export const DATE_RANGES = ['', 'm1', 'm3', 'y0', 'y1'];

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
 * Tabla de líneas del modal de detalle: talla, cantidad y precio salen del
 * payload del conector (items[] en pedidos, lines[] en albaranes y facturas).
 * `delivered` solo tiene sentido en el pedido, donde la línea puede ir a medias.
 */
export function linesTable(lines, { delivered = false } = {}) {
  if (!lines?.length) return `<p class="doc-error">${esc(t('docs.noLines'))}</p>`;

  return `
    <div class="grid-scroll">
      <table class="ck-table doc-lines">
        <thead>
          <tr>
            <th>${esc(t('docs.line.product'))}</th>
            <th>${esc(t('docs.line.size'))}</th>
            <th class="num">${esc(t('docs.line.qty'))}</th>
            ${delivered ? `<th class="num">${esc(t('docs.line.delivered'))}</th>` : ''}
            <th class="num">${esc(t('docs.line.price'))}</th>
            <th class="num">${esc(t('docs.line.discount'))}</th>
            <th class="num">${esc(t('docs.line.amount'))}</th>
          </tr>
        </thead>
        <tbody>
          ${lines.map(line => `
            <tr>
              <td>
                <b>${esc(line.name || '')}</b>
                ${line.reference ? `<span class="doc-line-ref">${esc(line.reference)}</span>` : ''}
              </td>
              <td>${line.size ? `<span class="ck-size">${esc(line.size)}</span>` : ''}</td>
              <td class="num">${esc(num(line.quantity))}</td>
              ${delivered ? `<td class="num">${esc(num(line.quantityDelivered))}</td>` : ''}
              <td class="num">${esc(eur(line.price))}</td>
              <td class="num">${line.discount ? `${esc(num(line.discount))} %` : ''}</td>
              <td class="num">${esc(eur(line.amount))}</td>
            </tr>`).join('')}
        </tbody>
      </table>
    </div>`;
}

/**
 * config:
 *   key        prefijo de i18n y nombre del CSV ('orders' | 'delivery-notes' | 'invoices')
 *   endpoint   '/api/portal/orders'
 *   crumb      literal de la miga ("Mis pedidos")
 *   statuses   [{ id, tone }] en el orden del rail; "Todos" se añade solo
 *   sorts      ids de "Ordenar por" (solo facturas). Sin esto la toolbar enseña "Fechas".
 *   columns    [{ label, className }]
 *   cells      item => [htmlDeCadaCelda]
 *   detail     (data, item) => { subtitle, body } para el modal de la fila
 *   filters    false quita los selects de la toolbar (devoluciones solo busca)
 *   action     { label, icon, onClick } sustituye al botón azul de exportar
 *              (en /sat es "⊕ NUEVA DEVOLUCIÓN", que no exporta: crea)
 *
 * Devuelve { reload } para que la vista repinte tras crear algo.
 */
export async function docList(host, config) {
  const { key, endpoint, crumb, statuses, sorts, columns, cells, detail,
    filters = true, action = null, onRendered = null } = config;

  let query = readQuery();
  let data = null;

  host.innerHTML = `
    <div class="page docs">
      <div class="docs-head">${pageHead(t(`nav.${key}`), [crumb])}</div>
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

  // ── Estado en la URL ────────────────────────────────────────────────────────
  function readQuery() {
    const params = new URLSearchParams(location.search);
    return {
      search: params.get('search') || '',
      status: params.get('status') || '',
      season: params.get('season') || '',
      dates: DATE_RANGES.includes(params.get('dates')) ? params.get('dates') : '',
      sort: params.get('sort') || (sorts ? sorts[0] : ''),
      skip: Number(params.get('skip')) || 0,
      take: Number(params.get('take')) || PAGE
    };
  }

  function writeQuery() {
    const params = new URLSearchParams();
    if (query.search) params.set('search', query.search);
    if (query.status) params.set('status', query.status);
    if (query.season) params.set('season', query.season);
    if (query.dates) params.set('dates', query.dates);
    if (sorts && query.sort && query.sort !== sorts[0]) params.set('sort', query.sort);
    if (query.skip) params.set('skip', String(query.skip));
    if (query.take !== PAGE) params.set('take', String(query.take));
    const search = params.toString();
    history.replaceState({}, '', location.pathname + (search ? `?${search}` : ''));
  }

  /** Lo que entiende la API: los rangos ya convertidos a from/to */
  function apiParams({ paged = true } = {}) {
    const params = new URLSearchParams();
    if (query.search) params.set('search', query.search);
    if (query.status) params.set('status', query.status);
    if (query.season) params.set('season', query.season);
    const range = rangeOf(query.dates);
    if (range.from) params.set('from', range.from);
    if (range.to) params.set('to', range.to);
    if (sorts && query.sort) params.set('sort', query.sort);
    params.set('locale', lang());
    if (paged) {
      params.set('skip', String(query.skip));
      params.set('take', String(query.take));
    }
    return params;
  }

  // ── Carga ───────────────────────────────────────────────────────────────────
  async function load() {
    writeQuery();
    list.setAttribute('aria-busy', 'true');
    try {
      data = await api.get(`${endpoint}?${apiParams()}`);
    } catch {
      list.removeAttribute('aria-busy');
      list.innerHTML = `<div class="panel"><b>${esc(t('docs.errorTitle'))}</b>${esc(t('docs.errorBody'))}</div>`;
      rail.innerHTML = '';
      pagerHost.innerHTML = '';
      return;
    }
    list.removeAttribute('aria-busy');
    paintRail();
    paintTools();
    paintList();
    paintPager();
  }

  // ── Rail ────────────────────────────────────────────────────────────────────
  function paintRail() {
    const counts = data.counts || {};
    const items = [
      { id: '', tone: 'none', label: t(`${key}.status.all`), count: counts.all ?? 0 },
      ...statuses.map(status => ({
        id: status.id,
        tone: status.tone,
        label: t(`${key}.status.${status.id}`),
        count: counts[status.id] ?? 0
      }))
    ];

    rail.innerHTML = statusRail({ label: t('docs.filters'), items, active: query.status });
    bindStatusRail(rail, status => {
      query = { ...query, status: status === query.status ? '' : status, skip: 0 };
      load();
    });
  }

  // ── Toolbar ─────────────────────────────────────────────────────────────────
  function paintTools() {
    // "Temporada" no tiene origen en BC todavía (seasonId llega vacío, plan §5):
    // sin datos el select va vacío y deshabilitado en vez de mentir con opciones.
    const seasons = data.seasons || [];

    tools.innerHTML = `
      <div class="doc-tools">
        <form class="doc-search" role="search">
          <input type="search" name="search" value="${esc(query.search)}"
            placeholder="${esc(t('docs.search'))}" aria-label="${esc(t('docs.searchLabel'))}">
          <button type="submit" aria-label="${esc(t('docs.searchLabel'))}">${icons.search(17)}</button>
        </form>

        ${filters ? `
        <label class="tb-field">
          <span class="tb-legend">${esc(t('docs.season'))}</span>
          <span class="tb-select">
            <select id="season" aria-label="${esc(t('docs.season'))}"${seasons.length ? '' : ' disabled'}
              ${seasons.length ? '' : `title="${esc(t('docs.noSeason'))}"`}>
              <option value="">${esc(t('docs.seasonAll'))}</option>
              ${seasons.map(season => `<option value="${esc(season)}"${season === query.season ? ' selected' : ''}>
                ${esc(season)}</option>`).join('')}
            </select>
          </span>
        </label>` : ''}

        ${!filters ? '' : sorts ? `
          <label class="tb-field">
            <span class="tb-legend">${esc(t('docs.sortBy'))}</span>
            <span class="tb-select">
              <select id="sort" aria-label="${esc(t('docs.sortBy'))}">
                ${sorts.map(sort => `<option value="${esc(sort)}"${sort === query.sort ? ' selected' : ''}>
                  ${esc(t(`docs.sort.${sort}`))}</option>`).join('')}
              </select>
            </span>
          </label>` : `
          <label class="tb-field">
            <span class="tb-legend">${esc(t('docs.dates'))}</span>
            <span class="tb-select">
              <select id="dates" aria-label="${esc(t('docs.dates'))}">
                ${DATE_RANGES.map(range => `<option value="${esc(range)}"${range === query.dates ? ' selected' : ''}>
                  ${esc(t(`docs.date.${range || 'all'}`))}</option>`).join('')}
              </select>
            </span>
          </label>`}

        <button type="button" class="btn-primary" id="${action ? 'actionBtn' : 'exportBtn'}">
          ${action?.icon ?? ''}${esc(action ? action.label : t(`${key}.export`))}</button>
      </div>`;

    const form = tools.querySelector('.doc-search');
    form.onsubmit = event => {
      event.preventDefault();
      query = { ...query, search: form.elements.search.value.trim(), skip: 0 };
      load().then(() => tools.querySelector('.doc-search input')?.focus());
    };

    const season = tools.querySelector('#season');
    if (season) season.onchange = () => { query = { ...query, season: season.value, skip: 0 }; load(); };

    const dates = tools.querySelector('#dates');
    if (dates) dates.onchange = () => { query = { ...query, dates: dates.value, skip: 0 }; load(); };

    const sort = tools.querySelector('#sort');
    if (sort) sort.onchange = () => { query = { ...query, sort: sort.value, skip: 0 }; load(); };

    const exportBtn = tools.querySelector('#exportBtn');
    if (exportBtn) exportBtn.onclick = async event => {
      const button = event.currentTarget;
      button.disabled = true;
      try {
        await api.download(`${endpoint}/export.csv?${apiParams({ paged: false })}`, `${key}.csv`);
      } finally {
        button.disabled = false;
      }
    };

    const actionBtn = tools.querySelector('#actionBtn');
    if (actionBtn) actionBtn.onclick = () => action.onClick({ reload: load });
  }

  // ── Tabla ───────────────────────────────────────────────────────────────────
  function paintList() {
    const items = data.items || [];
    list.innerHTML = gridTable({
      columns,
      rows: items.map(item => ({ id: item.id, cells: cells(item) })),
      empty: t('docs.empty')
    });

    // El número de documento abre su detalle; el resto de enlaces de la fila
    // (seguimiento del transportista) son anclas normales que van a su destino.
    list.querySelectorAll('[data-open]').forEach(button => {
      button.onclick = () => open(button.dataset.open);
    });

    // Gancho para acciones propias de la vista (p. ej. el botón de pago en facturas)
    onRendered?.(list, data);
  }

  async function open(id) {
    let document_;
    try {
      document_ = await api.get(`${endpoint}/${encodeURIComponent(id)}?locale=${lang()}`);
    } catch {
      openModal({ title: t('docs.detailErrorTitle'), body: `<p class="doc-error">${esc(t('docs.detailError'))}</p>` });
      return;
    }
    const { title, subtitle, body } = detail(document_);
    openModal({ title, subtitle, body });
  }

  // ── Paginación ──────────────────────────────────────────────────────────────
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
