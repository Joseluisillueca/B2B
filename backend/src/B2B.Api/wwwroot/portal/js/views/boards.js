// Cuadros de mando — /{market}/{lang}/boards.
// Varios paneles sobre las compras del cliente (o del cliente suplantado por el
// agente): KPIs, facturación por mes, top modelos, curva de tallas, reparto por
// familia y embudo de pedidos. Datos de /api/portal/dashboard (aislado por cliente).

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { pageHead } from '../ui/chrome.js';
import { bars, hbars, donut, chartFormat } from '../ui/chart.js';

const LOC = { es: 'es-ES', en: 'en-GB', fr: 'fr-FR', it: 'it-IT' };

export default async function boards(host) {
  host.innerHTML = `
    <div class="page">
      ${pageHead(t('nav.boards'), [t('nav.boards')])}
      <div id="bd" aria-live="polite"><div class="skeleton"></div></div>
    </div>`;
  const bd = host.querySelector('#bd');

  let data;
  try {
    data = await api.get(`/api/portal/dashboard?locale=${encodeURIComponent(lang())}`);
  } catch {
    bd.innerHTML = `<div class="panel"><b>${esc(t('boards.errorTitle'))}</b>${esc(t('boards.errorBody'))}</div>`;
    return;
  }

  const k = data.kpis || {};
  const monthData = (data.months || []).map(m => ({ label: monthLabel(m.month), value: m.amount }));
  const topModels = (data.topModels || []).map(m => ({ label: m.name || m.reference, sub: m.reference, value: m.amount }));
  const sizeCurve = (data.sizeCurve || []).map(s => ({ label: s.size, value: s.units }));
  const family = (data.byFamily || []).map(f => ({ label: cap(f.family), value: f.amount }));
  const funnel = (data.funnel || []).map(f => ({ label: statusLabel(f.status), value: f.amount, sub: String(f.count) }));

  const empty = t('boards.empty');
  bd.innerHTML = `
    <div class="bd-kpis">
      ${kpi(t('boards.kpiInvoiced'), eur(k.invoiced || 0))}
      ${kpi(t('boards.kpiUnits'), chartFormat.int(k.units || 0))}
      ${kpi(t('boards.kpiInvoices'), chartFormat.int(k.invoices || 0))}
      ${kpi(t('boards.kpiOrders'), chartFormat.int(k.orders || 0))}
      ${kpi(t('boards.kpiAvg'), eur(k.avgTicket || 0))}
    </div>
    <div class="bd-grid">
      ${panel(t('boards.salesByMonth'), bars(monthData, { format: chartFormat.money, empty, label: t('boards.salesByMonth') }), 'bd-wide')}
      ${panel(t('boards.topModels'), hbars(topModels, { format: chartFormat.money, empty }))}
      ${panel(t('boards.sizeCurve'), bars(sizeCurve, { format: chartFormat.int, empty, label: t('boards.sizeCurve') }))}
      ${panel(t('boards.byFamily'), donut(family, { format: chartFormat.money, caption: t('boards.byFamilyCap'), empty, label: t('boards.byFamily') }))}
      ${panel(t('boards.funnel'), hbars(funnel, { format: chartFormat.money, empty }))}
    </div>`;

  function monthLabel(key) {
    const [y, m] = String(key || '').split('-');
    const d = new Date(Number(y), Number(m) - 1, 1);
    if (Number.isNaN(d.getTime())) return key || '';
    const text = d.toLocaleDateString(LOC[lang()] || 'es-ES', { month: 'short' });
    return m === '01' ? `${text} ${y}` : text;
  }
  function statusLabel(status) {
    const key = `orders.status.${status}`;
    const translated = t(key);
    return translated === key ? status : translated;
  }
}

const kpi = (label, value) => `
  <div class="bd-kpi"><span class="bd-kpi-label">${esc(label)}</span><span class="bd-kpi-value">${esc(value)}</span></div>`;
const panel = (title, body, cls = '') => `
  <section class="bd-panel ${cls}"><h2>${esc(title)}</h2><div class="bd-panel-body">${body}</div></section>`;
const cap = s => (s && s.length ? s.charAt(0).toUpperCase() + s.slice(1) : s);
