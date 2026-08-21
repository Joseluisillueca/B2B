// Vista 11 — /{market}/{lang}/statistics (11-statistics.png).
//
// Los cuatro filtros de la referencia (Temporada, Catálogo, Fecha de inicio, Fecha
// de fin), el H2 "Ventas totales por meses (dd/mm/aaaa - dd/mm/aaaa)" y el gráfico
// de barras con lo facturado al cliente mes a mes.
//
// El gráfico es SVG propio: sin librerías ni CDN, a sangre y sin marco, como en la
// referencia. Cada barra lleva su mes y su importe en el <title>, y el aria-label
// del SVG resume el periodo y el total facturado: el dato sigue siendo legible sin
// ver la imagen aunque la referencia no lleve ni tabla mensual ni bloque de totales
// (M7 — ambos se han retirado).

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur, date as fmtDate } from '../format.js';
import { pageHead } from '../ui/chrome.js';
import { state } from '../state.js';

const LOCALES = { es: 'es-ES', en: 'en-GB', fr: 'fr-FR', it: 'it-IT' };
const locale = () => LOCALES[lang()] || 'es-ES';

// Lienzo del gráfico en unidades de viewBox (escala con el ancho de la página).
// Proporción ~3.49:1 como en la referencia (11-statistics.png): sin tarjeta, el
// gráfico va a sangre y no debe crecer en alto hasta empujar el eje bajo el pliegue.
const W = 1000, H = 287;
const PAD = { top: 24, right: 24, bottom: 52, left: 82 };

export default async function statistics(host) {
  let query = readQuery();
  let data = null;
  // Agente sin suplantar: estadísticas AGREGADAS de la cartera (mismo gráfico) +
  // ranking de clientes. El endpoint agrega la cartera; los filtros del cliente no
  // aplican, así que en este modo la toolbar no se pinta.
  const agentMode = state.isAgent && !state.acting;

  host.innerHTML = `
    <div class="page">
      ${pageHead(t('nav.statistics'), [t('nav.statistics')])}
      <div id="statTools"></div>
      <div id="statHost" aria-live="polite"><div class="skeleton"></div></div>
    </div>`;

  const tools = host.querySelector('#statTools');
  const container = host.querySelector('#statHost');

  function readQuery() {
    const params = new URLSearchParams(location.search);
    return {
      season: params.get('season') || '',
      catalog: params.get('catalog') || '',
      from: params.get('from') || '',
      to: params.get('to') || ''
    };
  }

  function writeQuery() {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) if (value) params.set(key, value);
    const search = params.toString();
    history.replaceState({}, '', location.pathname + (search ? `?${search}` : ''));
  }

  async function load() {
    writeQuery();
    container.setAttribute('aria-busy', 'true');
    const params = new URLSearchParams();
    if (query.from) params.set('from', query.from);
    if (query.to) params.set('to', query.to);
    if (query.season) params.set('season', query.season);
    if (query.catalog) params.set('catalog', query.catalog);
    params.set('locale', lang());

    try {
      data = agentMode
        ? await api.agentStatistics(lang())
        : await api.get(`/api/portal/statistics?${params}`);
    } catch {
      container.removeAttribute('aria-busy');
      container.innerHTML = `<div class="panel"><b>${esc(t('statistics.errorTitle'))}</b>${esc(t('statistics.errorBody'))}</div>`;
      return;
    }
    container.removeAttribute('aria-busy');
    // El backend decide el rango cuando la vista entra sin fechas: se copia aquí
    // para que los dos campos de la toolbar enseñen lo que se está viendo.
    query = { ...query, from: query.from || data.from, to: query.to || data.to };
    paintTools();
    paintChart();
  }

  // ── Filtros ────────────────────────────────────────────────────────────────
  function paintTools() {
    // En modo agente la vista agrega toda la cartera: no hay filtros de cliente
    if (agentMode) { tools.innerHTML = ''; return; }
    // "Temporada" y "Catálogo" no tienen origen en BC todavía (plan §5): sin datos
    // el select va vacío y deshabilitado en vez de ofrecer opciones que no filtran.
    const seasons = data.seasons || [];
    const catalogs = data.catalogs || [];

    tools.innerHTML = `
      <div class="doc-tools stat-tools">
        ${pick('season', t('statistics.season'), seasons, query.season, t('statistics.allSeasons'))}
        ${pick('catalog', t('statistics.catalog'), catalogs, query.catalog, t('statistics.allCatalogs'))}
        <label class="tb-field">
          <span class="tb-legend">${esc(t('statistics.from'))}</span>
          <span class="tb-select"><input type="date" id="from" value="${esc(query.from)}"
            aria-label="${esc(t('statistics.from'))}"></span>
        </label>
        <label class="tb-field">
          <span class="tb-legend">${esc(t('statistics.to'))}</span>
          <span class="tb-select"><input type="date" id="to" value="${esc(query.to)}"
            aria-label="${esc(t('statistics.to'))}"></span>
        </label>
      </div>`;

    for (const id of ['from', 'to']) {
      tools.querySelector(`#${id}`).onchange = event => {
        query = { ...query, [id]: event.target.value };
        load();
      };
    }
    // Los dos selects de vocabulario se enlazan igual; hoy llegan vacíos y
    // deshabilitados (el plan §5 lo documenta), pero el de Catálogo no estaba
    // conectado siquiera, así que habría quedado muerto en cuanto BC publique datos.
    for (const id of ['season', 'catalog']) {
      const select = tools.querySelector(`#${id}`);
      if (select && !select.disabled)
        select.onchange = () => { query = { ...query, [id]: select.value }; load(); };
    }
  }

  const pick = (id, label, values, active, allLabel) => `
    <label class="tb-field">
      <span class="tb-legend">${esc(label)}</span>
      <span class="tb-select">
        <select id="${id}" aria-label="${esc(label)}"${values.length ? '' : ' disabled'}
          ${values.length ? '' : `title="${esc(t('statistics.noData'))}"`}>
          <option value="">${esc(allLabel)}</option>
          ${values.map(value => `<option value="${esc(value)}"${value === active ? ' selected' : ''}>
            ${esc(value)}</option>`).join('')}
        </select>
      </span>
    </label>`;

  // ── Gráfico ────────────────────────────────────────────────────────────────
  function paintChart() {
    const months = data.months || [];
    const range = t('statistics.title', {
      from: fmtDate(`${data.from}T00:00:00`),
      to: fmtDate(`${data.to}T00:00:00`)
    });

    container.innerHTML = `
      <h2 class="stat-title">${esc(range)}</h2>
      <div class="stat-chart">${chart(months)}</div>
      ${agentMode ? topClients() : ''}`;
  }

  // Ranking de clientes de la cartera por facturación (solo modo agente)
  function topClients() {
    const rows = data.topClients || [];
    if (!rows.length) return '';
    return `
      <section class="stat-top">
        <h3>${esc(t('agentStats.topClients'))}</h3>
        <ol class="stat-top-list">
          ${rows.map(row => `
            <li>
              <span class="stat-top-name"><b>${esc(row.client || '')}</b>
                ${row.number ? `<span class="ag-cnum">${esc(row.number)}</span>` : ''}</span>
              <span class="stat-top-amount">${esc(eur(row.total))}</span>
            </li>`).join('')}
        </ol>
      </section>`;
  }

  function chart(months) {
    const values = months.map(month => Number(month.amount) || 0);
    const top = niceMax(Math.max(0, ...values));
    const bottom = Math.min(0, ...values) < 0 ? -niceMax(Math.abs(Math.min(...values))) : 0;
    const span = top - bottom || 1;

    const plotW = W - PAD.left - PAD.right;
    const plotH = H - PAD.top - PAD.bottom;
    const y = value => PAD.top + plotH * (1 - (value - bottom) / span);
    const slot = plotW / Math.max(1, months.length);
    const barW = Math.min(52, Math.max(6, slot * 0.55));

    const ticks = tickValues(bottom, top);
    const empty = !months.length || values.every(value => value === 0);

    return `
      <svg viewBox="0 0 ${W} ${H}" role="img" preserveAspectRatio="xMidYMid meet"
        aria-label="${esc(chartLabel(months))}">
        ${ticks.map(tick => `
          <g class="stat-tick">
            <line x1="${PAD.left}" x2="${W - PAD.right}" y1="${y(tick).toFixed(1)}" y2="${y(tick).toFixed(1)}"></line>
            <text x="${PAD.left - 12}" y="${(y(tick) + 4).toFixed(1)}" text-anchor="end">${esc(shortEur(tick))}</text>
          </g>`).join('')}

        <line class="stat-axis" x1="${PAD.left}" x2="${PAD.left}" y1="${PAD.top}" y2="${H - PAD.bottom}"></line>
        <line class="stat-axis" x1="${PAD.left}" x2="${W - PAD.right}"
          y1="${y(0).toFixed(1)}" y2="${y(0).toFixed(1)}"></line>

        ${empty ? `
          <text class="stat-na" x="${(PAD.left + (W - PAD.right)) / 2}" y="${H - PAD.bottom + 26}"
            text-anchor="middle">${esc(t('statistics.empty'))}</text>` : months.map((month, index) => {
              const value = Number(month.amount) || 0;
              const x = PAD.left + slot * index + (slot - barW) / 2;
              const zero = y(0);
              const height = Math.abs(y(value) - zero);
              return `
                <g class="stat-bar${value < 0 ? ' negative' : ''}">
                  <title>${esc(`${monthLabel(month.month, true)} · ${eur(value)}`)}</title>
                  <rect x="${x.toFixed(1)}" y="${(value < 0 ? zero : zero - height).toFixed(1)}"
                    width="${barW.toFixed(1)}" height="${Math.max(height, value === 0 ? 0 : 1).toFixed(1)}"></rect>
                  <text class="stat-month" x="${(x + barW / 2).toFixed(1)}" y="${H - PAD.bottom + 26}"
                    text-anchor="middle">${esc(monthLabel(month.month))}</text>
                </g>`;
            }).join('')}
      </svg>`;
  }

  /**
   * Nombre accesible del gráfico. Un `role="img"` colapsa todo su contenido en esta
   * cadena: los <title> de cada barra NO se exponen, así que el desglose mes a mes
   * —que antes vivía en la tabla que la referencia no tiene (M7)— se escribe aquí.
   * Es un atributo, no texto en pantalla: la vista sigue siendo la de la referencia.
   */
  function chartLabel(months) {
    const head = [
      t('statistics.chartLabel'),
      `${t('statistics.total')}: ${eur(data.total)}`,
      `${t('statistics.count')}: ${data.count ?? 0}`
    ];
    const detail = months
      .filter(month => Number(month.amount))
      .map(month => `${monthLabel(month.month, true)}: ${eur(month.amount)}`);
    return [...head, ...(detail.length ? detail : [t('statistics.empty')])].join(' · ');
  }

  /** "2026-01" → "ene" (o "enero 2026" en el nombre accesible del gráfico) */
  function monthLabel(key, long = false) {
    const [year, month] = String(key || '').split('-');
    const moment = new Date(Number(year), Number(month) - 1, 1);
    if (Number.isNaN(moment.getTime())) return key || '';
    const text = moment.toLocaleDateString(locale(), long
      ? { month: 'long', year: 'numeric' }
      : { month: 'short' });
    // En el eje solo cabe el mes; el año se marca cuando empieza uno nuevo
    return long || month !== '01' ? text : `${text} ${year}`;
  }

  const shortEur = value => Math.abs(value) >= 1000
    ? `${(value / 1000).toLocaleString(locale(), { maximumFractionDigits: 1 })} k€`
    : `${value.toLocaleString(locale(), { maximumFractionDigits: 0 })} €`;

  /** Techo "redondo" (1, 2, 2,5 o 5 × 10ⁿ) para que las guías caigan en cifras legibles */
  function niceMax(value) {
    if (!(value > 0)) return 0;
    const magnitude = 10 ** Math.floor(Math.log10(value));
    const scaled = value / magnitude;
    const step = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 2.5 ? 2.5 : scaled <= 5 ? 5 : 10;
    return step * magnitude;
  }

  function tickValues(bottom, top) {
    if (top === 0 && bottom === 0) return [0];
    const steps = 4;
    const size = (top - bottom) / steps;
    return Array.from({ length: steps + 1 }, (_, index) => bottom + size * index);
  }

  await load();
}
