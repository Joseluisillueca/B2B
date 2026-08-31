// Reglas de transporte (portes) — listado + simulador y editor. El coste resultante
// viaja en el pedido a Business Central (totalTransport + incotermId). Reutiliza el
// lenguaje de diseño del portal (.biz-*/.acc-*) y del back-office (.mng-*).
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash, fkOptions } from '../util.js';
import { go } from '../router.js';

// Países más habituales para portes (dirección de envío). Vacío = cualquiera.
const COUNTRIES = [
  ['ES', 'España'], ['PT', 'Portugal'], ['FR', 'Francia'], ['IT', 'Italia'],
  ['DE', 'Alemania'], ['BE', 'Bélgica'], ['NL', 'Países Bajos'], ['AD', 'Andorra'],
  ['GB', 'Reino Unido'],
];
const COUNTRY_ES = Object.fromEntries(COUNTRIES);
const ORDER_TYPES = [['REPLENISHMENT', 'Reposición'], ['SCHEDULED', 'Programación']];
const ORDER_ES = { REPLENISHMENT: 'Reposición', SCHEDULED: 'Programación' };

const money = v => Number(v || 0).toLocaleString('es-ES', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' €';
// Como `money` pero sin decimales cuando el importe es redondo ("300 €", no "300,00 €").
const moneyShort = v => {
  const n = Number(v || 0);
  return n.toLocaleString('es-ES', { minimumFractionDigits: Number.isInteger(n) ? 0 : 2, maximumFractionDigits: 2 }) + ' €';
};
const num = v => Number(v || 0).toLocaleString('es-ES');

// BC solo reconoce estos dos incoterms (se guardan en minúscula). "" = ninguno.
const INCOTERMS = [['', '(ninguno)'], ['fob', 'FOB'], ['usa', 'USA']];
// Sugerencias de país para el datalist (se permite teclear el código exacto de BC).
const countryDatalistOptions = COUNTRIES.map(([v, l]) => `<option value="${v}">${esc(l)}</option>`).join('');

// Coste legible: "Portes gratis" (0), "8,50 €" o "0,90 €/ud".
function costLabel(r) {
  if (!r.cost || Number(r.cost) === 0) return '<span class="grid-chip ok">Portes gratis</span>';
  return `<b style="font-weight:700">${money(r.cost)}</b>${r.perUnit ? '<span class="muted"> /ud</span>' : ''}`;
}

// Resumen de condiciones como fichas. Sin condiciones → "Cualquier pedido".
function conditionChips(r) {
  const chips = [];
  if (r.countryIsoId) chips.push(`<span class="grid-chip">${esc(COUNTRY_ES[r.countryIsoId] || r.countryIsoId)}</span>`);
  if (r.orderType) chips.push(`<span class="grid-chip">${esc(ORDER_ES[r.orderType] || r.orderType)}</span>`);
  if (r.clientExternalId) chips.push(`<span class="grid-chip">Cliente</span>`);
  if (r.minUnits) chips.push(`<span class="grid-chip">≥ ${num(r.minUnits)} uds</span>`);
  if (r.minAmount) chips.push(`<span class="grid-chip">≥ ${moneyShort(r.minAmount)}</span>`);
  return chips.length ? chips.join(' ') : '<span class="muted">Cualquier pedido</span>';
}

// ══════════ Listado + simulador ══════════
export async function transportView(main) {
  const { items = [] } = await api.transportRules();

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Ventas · Transporte</p>
        <h1 class="title">Transporte</h1>
        <p class="lead">Reglas de portes por país, cliente, tipo de pedido y mínimos. Se evalúan por prioridad: la primera que casa manda. El coste viaja al pedido de Business Central.</p>
      </div>
      <a class="btn-primary" href="#/transport/new">${icons.plus(16)} Nueva regla</a>
    </div>
    <div id="listHost"></div>
    <section class="biz-section" style="margin-top:2.6rem">
      <header class="acc-head biz-head"><h2>${icons.sparkles ? icons.sparkles(20) : icons.truck(20)}Simulador</h2></header>
      <div class="biz-card">
        <p class="lead" style="margin:0 0 1.1rem">Introduce un pedido de ejemplo y comprueba qué regla casa y qué transporte saldría.</p>
        <form id="simForm" class="mng-form" novalidate>
          <div class="biz-grid">
            <p class="acc-field"><label><span>País de envío</span>
              <input id="simCountry" list="tr-countries" value="" autocomplete="off" placeholder="ES" spellcheck="false"></label>
              <span class="acc-hint">Usa el mismo código de país que Business Central (para España, ES).</span></p>
            <p class="acc-field"><label><span>Tipo de pedido</span>
              <select id="simType"><option value="">Cualquiera</option>${ORDER_TYPES.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select></label></p>
            <p class="acc-field"><label><span>Cliente (opcional)</span>
              <select id="simClient"><option value="">— Cualquiera —</option></select></label></p>
            <p class="acc-field"><label><span>Unidades</span>
              <input type="number" id="simUnits" min="0" step="1" value="0" inputmode="numeric"></label></p>
            <p class="acc-field"><label><span>Importe (€, sin IVA)</span>
              <input type="number" id="simAmount" min="0" step="any" value="0" inputmode="decimal"></label></p>
          </div>
          ${countryDatalist('tr-countries')}
          <div style="display:flex;gap:.6rem;flex-wrap:wrap;align-items:center">
            <button type="submit" class="btn-primary tr-simbtn">${icons.spin(15)} <span class="tr-simbtn-t">Simular transporte</span></button>
            <span class="acc-hint" style="margin:0">No modifica nada; solo consulta las reglas actuales.</span>
          </div>
        </form>
        <div id="simResult" role="status" aria-live="polite" style="margin-top:1.2rem"></div>
      </div>
    </section>`;

  paintList(main.querySelector('#listHost'), items);

  // Cartera de clientes para el selector del simulador (opcional; si falla, se ignora).
  fkOptions('client').then(opts => {
    const sel = main.querySelector('#simClient');
    if (sel && opts.length) sel.insertAdjacentHTML('beforeend',
      opts.map(o => `<option value="${esc(o.value)}">${esc(o.label)}</option>`).join(''));
  }).catch(() => {});

  const $ = id => main.querySelector('#' + id);

  // B4: al tocar cualquier entrada, el resultado anterior queda obsoleto → se limpia
  // hasta volver a simular, para no mostrar un coste que ya no corresponde.
  ['simCountry', 'simType', 'simClient', 'simUnits', 'simAmount'].forEach(id => {
    const el = $(id);
    if (el) el.addEventListener('input', () => { $('simResult').innerHTML = ''; });
  });

  main.querySelector('#simForm').onsubmit = async e => {
    e.preventDefault();
    const btn = e.target.querySelector('button[type=submit]');
    const label = btn.querySelector('.tr-simbtn-t');
    btn.disabled = true;
    btn.classList.add('is-busy');
    if (label) label.textContent = 'Simulando…';
    try {
      const r = await api.previewTransport({
        clientExternalId: $('simClient').value || null,
        countryIsoId: $('simCountry').value.trim() || null,
        orderType: $('simType').value || null,
        units: Number($('simUnits').value) || 0,
        amount: Number($('simAmount').value) || 0,
      });
      $('simResult').innerHTML = simResultHtml(r);
    } catch (err) {
      $('simResult').innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(err.body?.error || err.message)}</span></div></div>`;
    } finally {
      btn.disabled = false;
      btn.classList.remove('is-busy');
      if (label) label.textContent = 'Simular transporte';
    }
  };
}

function paintList(host, items) {
  if (!items.length) {
    host.innerHTML = `<div class="mng-empty">${icons.truck(30)}
      <b>Todavía no hay reglas de portes</b>
      <p>Sin reglas, los pedidos salen sin coste de transporte. Crea la primera para cobrar portes o darlos gratis a partir de un mínimo.</p>
      <a class="btn-primary" href="#/transport/new">${icons.plus(16)} Nueva regla</a></div>`;
    return;
  }
  host.innerHTML = `
    <div class="grid-scroll"><table class="grid">
      <thead><tr>
        <th style="width:3rem">#</th><th>Regla</th><th>Condiciones</th>
        <th style="text-align:right">Coste</th><th style="width:6rem">Estado</th><th class="grid-actions"></th>
      </tr></thead>
      <tbody>${items.map(r => `
        <tr class="row-link" data-id="${esc(r.id)}">
          <td class="muted" style="font-variant-numeric:tabular-nums">${num(r.priority)}</td>
          <td class="grid-link">${esc(r.name)}</td>
          <td>${conditionChips(r)}</td>
          <td style="white-space:nowrap;text-align:right;font-variant-numeric:tabular-nums">${costLabel(r)}</td>
          <td>${r.active ? '<span class="grid-chip ok">Activa</span>' : '<span class="grid-chip off">Inactiva</span>'}</td>
          <td class="grid-actions">${icons.right(16)}</td>
        </tr>`).join('')}</tbody>
    </table></div>
    <p class="acc-hint" style="margin-top:.8rem">La columna <b>#</b> es la prioridad: se evalúan de menor a mayor y gana la primera que casa.</p>`;
  host.querySelectorAll('tr[data-id]').forEach(tr =>
    tr.onclick = () => go(`#/transport/edit/${encodeURIComponent(tr.dataset.id)}`));
}

function simResultHtml(r) {
  if (!r.matched) {
    return `<div class="tr-sim tr-sim-none">
      <div class="tr-sim-ic">${icons.truck(22)}</div>
      <div><b>Ninguna regla casa</b><span>El pedido saldría sin coste de transporte (portes por defecto: 0,00 €).</span></div>
    </div>`;
  }
  const free = !r.cost || Number(r.cost) === 0;
  return `<div class="tr-sim tr-sim-ok">
    <div class="tr-sim-cost">
      <span class="tr-sim-label">Transporte</span>
      <span class="tr-sim-value${free ? ' tr-sim-free' : ''}">${free ? 'Gratis' : money(r.cost)}</span>
    </div>
    <div class="tr-sim-meta">
      <p><span class="acc-hint" style="margin:0">Regla aplicada</span><b>${esc(r.ruleName || '—')}</b></p>
      ${r.incotermId ? `<p><span class="acc-hint" style="margin:0">Incoterm a BC</span><b>${esc(r.incotermId)}</b></p>` : ''}
    </div>
  </div>`;
}

// ══════════ Editor (crear / editar) ══════════
export async function transportEditView(main, id) {
  const editing = !!id;
  let r = { active: true, priority: 0, cost: 0, perUnit: false };
  if (editing) {
    const { items = [] } = await api.transportRules();
    const found = items.find(x => String(x.id) === String(id));
    if (!found) { main.innerHTML = `<div class="notice notice-error" role="alert">La regla no existe.</div>`; return; }
    r = found;
  } else {
    // Sugerencia de prioridad para la nueva regla: por debajo de todas (mayor número),
    // dejando hueco (max + 10) para intercalar reglas más tarde sin renumerar.
    try {
      const { items = [] } = await api.transportRules();
      r.priority = items.reduce((m, x) => Math.max(m, Number(x.priority) || 0), 0) + 10;
    } catch { /* sin catálogo: se queda en 0 */ }
  }

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs"><a href="#/transport">Transporte</a> · <span>${editing ? 'Editar' : 'Nueva'}</span></p>
        <h1 class="title">${editing ? 'Editar regla' : 'Nueva regla'}</h1>
      </div>
    </div>
    <form class="mng-form nc-form" id="f" novalidate>
      <div id="notice"></div>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.truck(20)}Regla</h2></header>
        <div class="biz-card">
          <div class="biz-grid">
            <p class="acc-field wide"><label><span>Nombre *</span>
              <input id="name" value="${esc(r.name || '')}" maxlength="120" placeholder="Ej.: Portes gratis a partir de 300 €"></label></p>
            <p class="acc-field"><label><span>Prioridad</span>
              <input type="number" id="priority" step="1" value="${esc(r.priority ?? 0)}" inputmode="numeric"></label>
              <span class="acc-hint">Menor número = se evalúa antes. Gana la primera que casa.</span></p>
          </div>
          <label class="mng-check" style="margin:.2rem 0 0"><input type="checkbox" id="active" ${r.active !== false ? 'checked' : ''}> <span>Regla activa</span></label>
        </div>
      </section>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.list ? icons.list(20) : icons.grid(20)}Condiciones</h2></header>
        <div class="biz-card">
          <p class="lead" style="margin:0 0 1.1rem">Deja en blanco lo que no quieras exigir. La regla casa cuando <b>todas</b> las condiciones marcadas se cumplen.</p>
          <div class="biz-grid">
            <p class="acc-field"><label><span>País de envío</span>
              <input id="countryIsoId" list="tr-countries" value="${esc(r.countryIsoId || '')}" autocomplete="off" placeholder="Cualquiera" spellcheck="false"></label>
              <span class="acc-hint">País de la dirección de envío. Usa el mismo código de país que Business Central (para España, ES).</span></p>
            <p class="acc-field"><label><span>Tipo de pedido</span>
              <select id="orderType">
                <option value="">Cualquiera</option>
                ${ORDER_TYPES.map(([v, l]) => `<option value="${v}"${r.orderType === v ? ' selected' : ''}>${l}</option>`).join('')}
              </select></label></p>
            <p class="acc-field"><label><span>Cliente</span>
              <select id="clientExternalId"><option value="">— Cualquiera —</option></select></label>
              <span class="acc-hint">Restringe la regla a un cliente concreto.</span></p>
            <p class="acc-field"><label><span>Mínimo de unidades</span>
              <input type="number" id="minUnits" min="0" step="1" value="${r.minUnits ?? ''}" placeholder="Sin mínimo" inputmode="numeric"></label></p>
            <p class="acc-field"><label><span>Mínimo de importe (€)</span>
              <input type="number" id="minAmount" min="0" step="any" value="${r.minAmount ?? ''}" placeholder="Sin mínimo" inputmode="decimal"></label>
              <span class="acc-hint">Subtotal del pedido sin IVA.</span></p>
          </div>
        </div>
      </section>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.coin(20)}Resultado</h2></header>
        <div class="biz-card">
          <div class="biz-grid">
            <p class="acc-field"><label><span>Coste de transporte (€)</span>
              <input type="number" id="cost" min="0" step="any" value="${r.cost ?? 0}" inputmode="decimal"></label>
              <span class="acc-hint">0 = portes gratis.</span></p>
            <p class="acc-field"><label><span>Incoterm (opcional)</span>
              <select id="incotermId">${INCOTERMS.map(([v, l]) => `<option value="${v}"${String(r.incotermId || '').toLowerCase() === v ? ' selected' : ''}>${l}</option>`).join('')}</select></label>
              <span class="acc-hint">Business Central solo reconoce FOB o USA; déjalo en blanco si no aplica.</span></p>
          </div>
          <label class="mng-check" style="margin:.2rem 0 0"><input type="checkbox" id="perUnit" ${r.perUnit ? 'checked' : ''}> <span>El coste es por unidad (coste × unidades del pedido)</span></label>
        </div>
      </section>
      ${countryDatalist('tr-countries')}

      <div class="acc-actions nc-actions">
        ${editing ? '<button type="button" class="btn-danger" id="del">Eliminar</button>' : ''}
        <a class="btn-ghost" href="#/transport">Cancelar</a>
        <button type="submit" class="btn-primary">${editing ? 'Guardar cambios' : 'Crear regla'}</button>
      </div>
    </form>`;

  const $ = id2 => main.querySelector('#' + id2);
  const notice = $('notice');

  // Cartera de clientes (selector opcional). Se marca el actual si lo hubiera.
  fkOptions('client').then(opts => {
    const sel = $('clientExternalId');
    if (!sel) return;
    sel.insertAdjacentHTML('beforeend', opts.map(o =>
      `<option value="${esc(o.value)}"${String(r.clientExternalId) === String(o.value) ? ' selected' : ''}>${esc(o.label)}</option>`).join(''));
    // Cliente guardado que ya no está en la cartera: se conserva como opción suelta.
    if (r.clientExternalId && !opts.some(o => String(o.value) === String(r.clientExternalId)))
      sel.insertAdjacentHTML('beforeend', `<option value="${esc(r.clientExternalId)}" selected>${esc(r.clientExternalId)} (no listado)</option>`);
  }).catch(() => {
    // Sin catálogo de clientes: al menos conserva el valor guardado.
    const sel = $('clientExternalId');
    if (sel && r.clientExternalId) sel.insertAdjacentHTML('beforeend', `<option value="${esc(r.clientExternalId)}" selected>${esc(r.clientExternalId)}</option>`);
  });

  const numOrNull = v => { const s = String(v).trim(); return s === '' ? null : Number(s); };

  $('f').onsubmit = async e => {
    e.preventDefault();
    notice.innerHTML = '';
    const name = $('name').value.trim();
    if (!name) return warn(notice, 'El nombre es obligatorio.');
    const cost = Number($('cost').value || 0);
    if (cost < 0) return warn(notice, 'El coste no puede ser negativo.');

    const body = {
      name, active: $('active').checked, priority: Number($('priority').value || 0),
      clientExternalId: $('clientExternalId').value || null,
      countryIsoId: $('countryIsoId').value.trim() || null,
      orderType: $('orderType').value || null,
      minUnits: numOrNull($('minUnits').value),
      minAmount: numOrNull($('minAmount').value),
      cost, perUnit: $('perUnit').checked,
      incotermId: $('incotermId').value.trim() || null,
    };

    const btn = e.target.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      if (editing) await api.saveTransportRule(id, body);
      else await api.createTransportRule(body);
      flash(editing ? 'Regla guardada.' : 'Regla creada.');
      go('#/transport');
    } catch (err) {
      btn.disabled = false;
      warn(notice, err.body?.error || err.message || 'No se pudo guardar.');
    }
  };

  if (editing) $('del').onclick = async () => {
    if (!confirm('¿Eliminar esta regla de transporte?')) return;
    try { await api.delTransportRule(id); flash('Regla eliminada.'); go('#/transport'); }
    catch (err) { warn(notice, err.body?.error || err.message); }
  };
}

// Datalist compartido de países: sugiere los habituales pero permite teclear el
// código exacto que emita Business Central (no restringe a la lista).
const countryDatalist = id => `<datalist id="${id}">${countryDatalistOptions}</datalist>`;

function warn(host, text) {
  host.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(text)}</span></div></div>`;
  host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}
