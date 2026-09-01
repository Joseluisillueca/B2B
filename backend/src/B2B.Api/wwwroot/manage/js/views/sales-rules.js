// Condiciones de venta / promos — reglas con CONDICIONES (AND) + ACCIONES.
// Listado + simulador (vista lista) y editor de dos columnas con sub-diálogos por
// tipo (vista editor). Reutiliza el lenguaje de diseño de /manage (.biz-*/.acc-*/
// .grid-chip/.tr-chip/.btn-*) y el contrato /api/admin/sales-rules.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash, fkOptions } from '../util.js';
import { go } from '../router.js';
import { COUNTRIES, resolveCountry, countryName } from './transport.js';

// ── Formato y etiquetas ─────────────────────────────────────────────────────
const money = v => Number(v || 0).toLocaleString('es-ES', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' €';
const moneyShort = v => {
  const n = Number(v || 0);
  return n.toLocaleString('es-ES', { minimumFractionDigits: Number.isInteger(n) ? 0 : 2, maximumFractionDigits: 2 }) + ' €';
};
const num = v => Number(v || 0).toLocaleString('es-ES');
const parseNum = v => { const n = parseFloat(String(v ?? '').replace(',', '.').trim()); return Number.isFinite(n) ? n : null; };

const ORDER_LABEL = { REPLENISHMENT: 'Reposición', SCHEDULED: 'Programación' };
const OP_LABEL = { lt: '<', lte: '≤', gt: '>', gte: '≥', eq: '=' };

let uid = 0;
const nextId = p => `${p}-${++uid}`;

// Cachés de etiquetas de catálogos (id → nombre) para mostrar nombres legibles en
// los resúmenes (chips) de condiciones multi. Se pueblan bajo demanda con fkOptions.
const CATALOG_OF = { client: 'client', client_group: 'client-group', models: 'model', families: 'family' };
const LABELS = {};
async function ensureLabels(condTypes) {
  await Promise.all([...new Set(condTypes)].filter(t => CATALOG_OF[t] && !LABELS[t]).map(async t => {
    try { const opts = await fkOptions(CATALOG_OF[t]); LABELS[t] = new Map(opts.map(o => [String(o.value), o.label])); }
    catch { LABELS[t] = new Map(); }
  }));
}
const labelOf = (type, v) => (LABELS[type] && LABELS[type].get(String(v))) || v;

// ── Multi-selección por chips (reutiliza .tr-chip*) ──────────────────────────
// Modos: country (resuelve nombre/código ISO), catalog (fkOptions: busca por nombre),
// free (ids/códigos libres). Devuelve un controlador con .values y .resolvePending().
function chipMulti(host, opt) {
  let values = (opt.values || []).map(String);
  const labels = new Map();        // value → label (para pintar el chip)
  let valSet = new Set();
  let nameToVal = new Map();
  const dlId = nextId('sr-dl');
  const norm = s => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').trim().toLowerCase();

  host.innerHTML = `<div class="tr-chips" role="list" aria-label="${esc(opt.aria || 'Valores')}"></div>
    <input class="tr-country-input" autocomplete="off" spellcheck="false" role="combobox"
           aria-expanded="false" list="${dlId}" placeholder="${esc(opt.placeholder || 'Escribe y pulsa Intro')}">
    <datalist id="${dlId}"></datalist>
    <span class="sr-chip-alert" role="status" aria-live="polite" hidden></span>`;
  const chipsEl = host.querySelector('.tr-chips');
  const input = host.querySelector('input');
  const datalist = host.querySelector('datalist');
  const alertEl = host.querySelector('.sr-chip-alert');

  function labelFor(v) {
    if (opt.mode === 'country') return countryName(v);
    return labels.get(String(v)) || v;
  }
  function render() {
    chipsEl.innerHTML = values.map(v => {
      const name = labelFor(v);
      const tail = String(name) !== String(v) ? `<code>${esc(v)}</code>` : '';
      return `<span class="tr-chip" role="listitem"><span class="tr-chip-name">${esc(name)}</span>${tail}` +
        `<button type="button" class="tr-chip-x" data-v="${esc(v)}" aria-label="Quitar ${esc(name)}">${icons.close(12)}</button></span>`;
    }).join('');
  }
  function add(v) {
    v = String(v).trim();
    if (!v) return false;
    if (!values.includes(v)) { values.push(v); render(); }
    input.value = '';
    return true;
  }
  let alertTimer = 0;
  function reject(raw) {
    input.classList.add('is-invalid');
    setTimeout(() => input.classList.remove('is-invalid'), 800);
    // Aviso accesible además del parpadeo del borde (M3).
    const what = String(raw || '').trim();
    alertEl.textContent = what ? `No reconocido: "${what}". Elige uno de la lista.` : 'No reconocido: elige uno de la lista.';
    alertEl.hidden = false;
    clearTimeout(alertTimer);
    alertTimer = setTimeout(() => { alertEl.hidden = true; alertEl.textContent = ''; }, 4000);
  }
  function clearAlert() { clearTimeout(alertTimer); alertEl.hidden = true; alertEl.textContent = ''; }
  function resolve(raw) {
    const t = String(raw || '').trim();
    if (!t) return null;
    if (opt.mode === 'country') return resolveCountry(t);
    if (opt.mode === 'catalog') {
      if (valSet.has(t)) return t;
      const byName = nameToVal.get(norm(t));
      return byName || null;               // solo del catálogo
    }
    return t;                              // free: cualquier código/id
  }

  input.addEventListener('keydown', e => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const v = resolve(input.value);
      if (v) { clearAlert(); add(v); } else if (input.value.trim()) reject(input.value);
    } else if (e.key === 'Backspace' && !input.value && values.length) { values.pop(); render(); }
  });
  input.addEventListener('change', () => { const v = resolve(input.value); if (v) { clearAlert(); add(v); } });
  // N2: refleja el estado del combobox de sugerencias.
  input.addEventListener('focus', () => input.setAttribute('aria-expanded', 'true'));
  input.addEventListener('blur', () => input.setAttribute('aria-expanded', 'false'));
  input.addEventListener('input', e => {
    if (alertEl.textContent) clearAlert();
    if (e.inputType === 'insertReplacementText' || e.inputType == null) { const v = resolve(input.value); if (v) add(v); }
  });
  chipsEl.addEventListener('click', e => {
    const b = e.target.closest('.tr-chip-x');
    if (!b) return;
    values = values.filter(x => String(x) !== String(b.dataset.v));
    render(); input.focus();
  });

  function fillDatalist(list) { datalist.innerHTML = list.map(o => `<option value="${esc(o.v)}">${esc(o.hint || '')}</option>`).join(''); }

  if (opt.mode === 'country') {
    COUNTRIES.forEach(([c, n]) => { labels.set(c, n); });
    fillDatalist(COUNTRIES.map(([c, n]) => ({ v: n, hint: c })));
    render();
  } else if (opt.mode === 'catalog') {
    render();
    fkOptions(opt.src).then(opts => {
      opts.forEach(o => { labels.set(String(o.value), o.label); valSet.add(String(o.value)); nameToVal.set(norm(o.label), String(o.value)); });
      fillDatalist(opts.map(o => ({ v: o.label })));
      render();
    }).catch(() => {});
  } else {
    if (opt.suggest) fillDatalist(opt.suggest.map(v => ({ v })));
    render();
  }

  return { get values() { return [...values]; }, resolvePending() { const v = resolve(input.value); if (v) add(v); } };
}

// ── Registro de tipos de CONDICIÓN ───────────────────────────────────────────
// Cada tipo: build(host,data) → controlador { read() } que devuelve los params
// (sin `type`) o lanza Error(mensaje) si es inválido; summary(obj) → texto de chip.
const F = (labelText, inner, hint) =>
  `<p class="acc-field"><label><span>${esc(labelText)}</span>${inner}</label>${hint ? `<span class="acc-hint">${esc(hint)}</span>` : ''}</p>`;
const numInput = (id, val, extra = '') => `<input type="number" id="${id}" value="${val ?? ''}" step="any" inputmode="decimal" ${extra}>`;

function selField(labelText, id, pairs, sel, hint) {
  const opts = pairs.map(([v, l]) => `<option value="${v}"${String(sel) === String(v) ? ' selected' : ''}>${esc(l)}</option>`).join('');
  return F(labelText, `<select id="${id}">${opts}</select>`, hint);
}
function chipField(host, labelText, ctrlRef, opt, hint) {
  const hid = nextId('sr-cf');
  host.insertAdjacentHTML('beforeend',
    `<div class="acc-field wide"><span class="sr-flabel">${esc(labelText)}</span><div id="${hid}" class="sr-chiphost"></div>${hint ? `<span class="acc-hint">${esc(hint)}</span>` : ''}</div>`);
  ctrlRef.w = chipMulti(host.querySelector('#' + hid), { ...opt, aria: labelText });
}
function readMulti(w, minMsg) {
  w.resolvePending();
  const values = w.values;
  if (!values.length) throw new Error(minMsg);
  return { values };
}
function multiSummary(o, type, label) {
  const vals = (o.values || []);
  if (!vals.length) return label;
  const named = vals.map(v => type === 'country' ? v : labelOf(type, v));
  const shown = named.slice(0, 3).map(String);
  const extra = named.length > 3 ? ` +${named.length - 3}` : '';
  return `${label}: ${shown.join(', ')}${extra}`;
}

const COND_TYPES = [
  { type: 'order_type', label: 'Tipo de pedido',
    build(host, d) { host.innerHTML = selField('Tipo de pedido', 'v', [['REPLENISHMENT', 'Reposición'], ['SCHEDULED', 'Programación']], d.value || 'REPLENISHMENT'); return { read() { return { value: host.querySelector('#v').value }; } }; },
    summary(o) { return `Tipo de pedido: ${ORDER_LABEL[o.value] || o.value || '—'}`; } },

  { type: 'units_lt', label: 'Menos de X unidades',
    build(host, d) { host.innerHTML = F('Unidades máximas (excl.)', numInput('v', d.value, 'min="0" step="1"'), 'Casa si el carrito tiene MENOS de este número de unidades.'); return { read() { const n = parseNum(host.querySelector('#v').value); if (n == null || n < 0) throw new Error('Indica un número de unidades válido.'); return { value: n }; } }; },
    summary(o) { return `Unidades carrito < ${num(o.value)}`; } },

  { type: 'min_units', label: 'Unidades mínimas elegibles',
    build(host, d) { host.innerHTML = F('Unidades mínimas', numInput('v', d.value, 'min="0" step="1"'), 'Casa si el carrito tiene AL MENOS este número de unidades.'); return { read() { const n = parseNum(host.querySelector('#v').value); if (n == null || n < 0) throw new Error('Indica un número de unidades válido.'); return { value: n }; } }; },
    summary(o) { return `Unidades carrito ≥ ${num(o.value)}`; } },

  { type: 'cart_total', label: 'Por total del carrito',
    build(host, d) {
      host.innerHTML = selField('Operador', 'op', [['gte', 'Mayor o igual (≥)'], ['gt', 'Mayor (>)'], ['lte', 'Menor o igual (≤)'], ['lt', 'Menor (<)'], ['eq', 'Igual (=)']], d.op || 'gte')
        + F('Importe (€, sin IVA)', numInput('v', d.value, 'min="0"'));
      return { read() { const n = parseNum(host.querySelector('#v').value); if (n == null || n < 0) throw new Error('Indica un importe válido.'); return { op: host.querySelector('#op').value, value: n }; } };
    },
    summary(o) { return `Total carrito ${OP_LABEL[o.op] || '≥'} ${moneyShort(o.value)}`; } },

  { type: 'country', label: 'País de envío',
    build(host, d) { const ref = {}; chipField(host, 'Países de envío', ref, { mode: 'country', values: d.values, placeholder: 'Escribe un país o su código (ES)…' }, 'Casa si el país de envío es uno de estos.'); return { read() { return readMulti(ref.w, 'Añade al menos un país.'); } }; },
    summary(o) { return multiSummary(o, 'country', 'País'); } },

  { type: 'client', label: 'Por cliente',
    build(host, d) { const ref = {}; chipField(host, 'Clientes', ref, { mode: 'catalog', src: 'client', values: d.values, placeholder: 'Busca un cliente…' }, 'Casa si el cliente del carrito es uno de estos.'); return { read() { return readMulti(ref.w, 'Añade al menos un cliente.'); } }; },
    summary(o) { return multiSummary(o, 'client', 'Cliente'); } },

  { type: 'client_group', label: 'Por grupo de cliente',
    build(host, d) { const ref = {}; chipField(host, 'Grupos de cliente', ref, { mode: 'catalog', src: 'client-group', values: d.values, placeholder: 'Busca un grupo…' }); return { read() { return readMulti(ref.w, 'Añade al menos un grupo.'); } }; },
    summary(o) { return multiSummary(o, 'client_group', 'Grupo'); } },

  { type: 'market', label: 'Por mercado',
    build(host, d) { const ref = {}; chipField(host, 'Mercados', ref, { mode: 'free', values: d.values, placeholder: 'es, fr, it…', suggest: ['es', 'fr', 'it', 'pt', 'en'] }, 'Códigos de mercado (p. ej. es).'); return { read() { return readMulti(ref.w, 'Añade al menos un mercado.'); } }; },
    summary(o) { return multiSummary(o, 'market', 'Mercado'); } },

  { type: 'rate', label: 'Por tarifa',
    build(host, d) { const ref = {}; chipField(host, 'Tarifas', ref, { mode: 'free', values: d.values, placeholder: 'Id de tarifa…' }, 'Identificadores de tarifa.'); return { read() { return readMulti(ref.w, 'Añade al menos una tarifa.'); } }; },
    summary(o) { return multiSummary(o, 'rate', 'Tarifa'); } },

  { type: 'models', label: 'Por modelos',
    build(host, d) { const ref = {}; chipField(host, 'Modelos', ref, { mode: 'catalog', src: 'model', values: d.values, placeholder: 'Busca un modelo…' }, 'Casa si el carrito contiene alguno de estos modelos.'); return { read() { return readMulti(ref.w, 'Añade al menos un modelo.'); } }; },
    summary(o) { return multiSummary(o, 'models', 'Modelos'); } },

  { type: 'products', label: 'Por productos (variantes)',
    build(host, d) { const ref = {}; chipField(host, 'Productos', ref, { mode: 'free', values: d.values, placeholder: 'Id de variante…' }, 'Ids de variante separados por Intro.'); return { read() { return readMulti(ref.w, 'Añade al menos un producto.'); } }; },
    summary(o) { return multiSummary(o, 'products', 'Productos'); } },

  { type: 'families', label: 'Por familias',
    build(host, d) { const ref = {}; chipField(host, 'Familias', ref, { mode: 'catalog', src: 'family', values: d.values, placeholder: 'Busca una familia…' }); return { read() { return readMulti(ref.w, 'Añade al menos una familia.'); } }; },
    summary(o) { return multiSummary(o, 'families', 'Familias'); } },

  { type: 'brands', label: 'Por marcas',
    build(host, d) { const ref = {}; chipField(host, 'Marcas', ref, { mode: 'free', values: d.values, placeholder: 'Id de marca…' }); return { read() { return readMulti(ref.w, 'Añade al menos una marca.'); } }; },
    summary(o) { return multiSummary(o, 'brands', 'Marcas'); } },

  { type: 'agent_cart', label: 'Carrito de agente comercial',
    build(host, d) { host.innerHTML = selField('¿Creado por un agente?', 'v', [['true', 'Sí, lo creó un agente'], ['false', 'No, cliente directo']], d.value === false ? 'false' : 'true', 'Distingue carritos montados por un comercial de los del propio cliente.'); return { read() { return { value: host.querySelector('#v').value === 'true' }; } }; },
    summary(o) { return `Carrito de agente: ${o.value === false ? 'No' : 'Sí'}`; } },

  { type: 'date_between', label: 'Entre fechas',
    build(host, d) {
      host.innerHTML = F('Desde', `<input type="date" id="from" value="${esc(d.from || '')}">`) + F('Hasta', `<input type="date" id="to" value="${esc(d.to || '')}">`);
      return { read() { const from = host.querySelector('#from').value, to = host.querySelector('#to').value; if (!from && !to) throw new Error('Indica al menos una fecha.'); if (from && to && from > to) throw new Error('La fecha "Desde" no puede ser posterior a "Hasta".'); const o = {}; if (from) o.from = from; if (to) o.to = to; return o; } };
    },
    summary(o) { if (o.from && o.to) return `Entre ${o.from} y ${o.to}`; if (o.from) return `Desde ${o.from}`; if (o.to) return `Hasta ${o.to}`; return 'Entre fechas'; } },
];

// ── Registro de tipos de ACCIÓN ──────────────────────────────────────────────
const ACTION_TYPES = [
  { type: 'deny', label: 'Denegar carrito',
    build(host, d) { host.innerHTML = F('Mensaje al cliente (opcional)', `<input type="text" id="m" maxlength="200" value="${esc(d.message || '')}" placeholder="Ej.: Pedido no permitido para este mercado.">`, 'Se mostrará como motivo del bloqueo.'); return { read() { const m = host.querySelector('#m').value.trim(); return m ? { message: m } : {}; } }; },
    summary(o) { return o.message ? `Denegar: ${o.message}` : 'Denegar carrito'; } },

  { type: 'free_shipping', label: 'Portes gratis',
    build(host) { host.innerHTML = `<p class="sr-noparams">${icons.check(16)} Sin parámetros: el carrito no pagará transporte.</p>`; return { read() { return {}; } }; },
    summary() { return 'Portes gratis'; } },

  { type: 'fixed_transport', label: 'Importe fijo de transporte',
    build(host, d) { host.innerHTML = F('Importe de transporte (€)', numInput('a', d.amount, 'min="0"'), 'Sustituye el coste de portes por este importe.'); return { read() { const n = parseNum(host.querySelector('#a').value); if (n == null || n < 0) throw new Error('Indica un importe válido.'); return { amount: n }; } }; },
    summary(o) { return `Transporte fijo: ${moneyShort(o.amount)}`; } },

  { type: 'line_discount_percent', label: 'Descuento % por línea',
    build(host, d) { host.innerHTML = F('Descuento (%)', numInput('p', d.percent, 'min="0" max="100"'), 'Porcentaje aplicado a cada línea del carrito.'); return { read() { const n = parseNum(host.querySelector('#p').value); if (n == null || n < 0 || n > 100) throw new Error('Indica un porcentaje entre 0 y 100.'); return { percent: n }; } }; },
    summary(o) { return `Dto. línea: ${num(o.percent)}%`; } },

  { type: 'line_discount_fixed', label: 'Descuento fijo por línea',
    build(host, d) { host.innerHTML = F('Descuento (€)', numInput('a', d.amount, 'min="0"'), 'Importe descontado en cada línea del carrito.'); return { read() { const n = parseNum(host.querySelector('#a').value); if (n == null || n < 0) throw new Error('Indica un importe válido.'); return { amount: n }; } }; },
    summary(o) { return `Dto. fijo línea: ${moneyShort(o.amount)}`; } },

  { type: 'set_incoterm', label: 'Incoterm / servicio (FOB/USA)',
    build(host, d) { const v = (d.value || '').toLowerCase(); host.innerHTML = F('Incoterm', `<select id="v"><option value="fob"${v === 'usa' ? '' : ' selected'}>FOB</option><option value="usa"${v === 'usa' ? ' selected' : ''}>USA</option></select>`, 'Viaja al pedido de Business Central (solo reconoce FOB o USA).'); return { read() { return { value: host.querySelector('#v').value }; } }; },
    summary(o) { return `Incoterm: ${(o.value || 'fob').toUpperCase()}`; } },
];

const REG = { condition: COND_TYPES, action: ACTION_TYPES };
const descOf = (kind, type) => REG[kind].find(t => t.type === type) || null;
function summaryOf(kind, obj) { const d = descOf(kind, obj.type); return d ? d.summary(obj) : (obj.type || 'Desconocido'); }

// ── Sub-diálogo (nueva/editar condición o acción) ────────────────────────────
function openItemDialog(kind, initial) {
  return new Promise(resolve => {
    ensureModalCss();
    const registry = REG[kind];
    const editing = !!initial;
    const titleId = nextId('sr-t');
    const isCond = kind === 'condition';
    const overlay = document.createElement('div');
    overlay.className = 'sr-modal';
    overlay.innerHTML = `<div class="sr-dialog" role="dialog" aria-modal="true" aria-labelledby="${titleId}">
      <header><h3 id="${titleId}">${editing ? (isCond ? 'Editar condición' : 'Editar acción') : (isCond ? 'Nueva condición' : 'Nueva acción')}</h3>
        <button type="button" class="json-close" data-act="cancel" aria-label="Cerrar">✕</button></header>
      <div class="sr-dialog-body">
        ${selField(isCond ? 'Tipo de condición' : 'Tipo de acción', 'srType', registry.map(t => [t.type, t.label]), (initial && initial.type) || registry[0].type)}
        <div id="srFields"></div>
        <div id="srErr" role="alert" aria-live="assertive"></div>
        <p class="acc-hint sr-dlg-note">${isCond ? 'Esta condición se evaluará junto al resto: deben cumplirse TODAS.' : 'La acción se aplicará cuando la regla case.'}</p>
      </div>
      <footer class="sr-dialog-foot">
        <button type="button" class="btn-ghost" data-act="cancel">Cancelar</button>
        <button type="button" class="btn-primary" data-act="ok">${editing ? 'Guardar' : 'Añadir'}</button>
      </footer></div>`;

    const typeSel = overlay.querySelector('#srType');
    const fieldsHost = overlay.querySelector('#srFields');
    const errBox = overlay.querySelector('#srErr');
    let ctrl = null;
    // autoFocus: al ABRIR el diálogo enfocamos el selector de Tipo (primera decisión),
    // no el primer parámetro. Al CAMBIAR de tipo sí saltamos al primer campo nuevo.
    function build(type, data, autoFocus = true) { errBox.innerHTML = ''; fieldsHost.innerHTML = ''; ctrl = descOf(kind, type).build(fieldsHost, data || {}); if (autoFocus) { const first = fieldsHost.querySelector('input,select'); if (first) setTimeout(() => first.focus(), 30); } }
    if (editing) typeSel.value = initial.type;
    build(typeSel.value, initial, false);
    typeSel.onchange = () => build(typeSel.value, null);

    const opener = document.activeElement;
    const close = result => {
      overlay.remove();
      document.removeEventListener('keydown', onKey, true);
      window.removeEventListener('hashchange', cancel);
      if (opener && typeof opener.focus === 'function') opener.focus();
      resolve(result);
    };
    const cancel = () => close(null);
    const commit = () => {
      try { const params = ctrl.read(); close({ type: typeSel.value, ...params }); }
      catch (e) { errBox.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(16)}<div><span>${esc(e.message)}</span></div></div>`; }
    };
    const onKey = e => {
      if (e.key === 'Escape') { e.stopPropagation(); e.preventDefault(); cancel(); return; }
      if (e.key === 'Tab') {                     // trampa de foco simple
        const f = [...overlay.querySelectorAll('button,select,input,textarea,[href]')].filter(el => !el.disabled && el.offsetParent !== null);
        if (!f.length) return;
        const first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    };
    overlay.addEventListener('click', e => { if (e.target === overlay) cancel(); const b = e.target.closest('[data-act]'); if (!b) return; if (b.dataset.act === 'cancel') cancel(); else if (b.dataset.act === 'ok') commit(); });
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('hashchange', cancel);
    document.body.append(overlay);
    setTimeout(() => typeSel.focus(), 20);
  });
}

// ══════════ LISTADO + SIMULADOR ══════════
export async function salesRulesView(main) {
  const { items = [] } = await api.salesRules();

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Ventas · Condiciones de venta</p>
        <h1 class="title">Condiciones de venta / promos</h1>
        <p class="lead">Reglas que miran el carrito (tipo, unidades, importe, país, cliente…) y, si se cumplen <b>todas</b> sus condiciones, aplican acciones: denegar, portes gratis, transporte fijo o descuentos. Se evalúan por orden de aplicación.</p>
      </div>
      <a class="btn-primary" href="#/sales-rules/new">${icons.plus(16)} Nueva regla</a>
    </div>
    <div id="listHost"></div>
    <section class="biz-section" style="margin-top:2.6rem">
      <header class="acc-head biz-head"><h2>${icons.sparkles(20)}Simulador</h2></header>
      <div class="biz-card">
        <p class="lead" style="margin:0 0 1.1rem">Monta un carrito de ejemplo y comprueba qué reglas casan y qué resultado saldría. No modifica nada.</p>
        <form id="simForm" class="mng-form" novalidate>
          <div class="biz-grid cols-2">
            <p class="acc-field"><label><span>Tipo de pedido</span>
              <select id="simType"><option value="">Cualquiera</option><option value="REPLENISHMENT">Reposición</option><option value="SCHEDULED">Programación</option></select></label></p>
            <p class="acc-field"><label><span>Fecha</span><input type="date" id="simDate"></label></p>
            <p class="acc-field"><label><span>Unidades</span><input type="number" id="simUnits" min="0" step="1" value="0" inputmode="numeric"></label></p>
            <p class="acc-field"><label><span>Importe (€, sin IVA)</span><input type="number" id="simAmount" min="0" step="any" value="0" inputmode="decimal"></label></p>
            <p class="acc-field"><label><span>País de envío</span><input id="simCountry" list="sr-sim-countries" autocomplete="off" placeholder="España o ES" spellcheck="false"></label></p>
            <p class="acc-field"><label><span>Cliente</span><select id="simClient"><option value="">— Cualquiera —</option></select></label></p>
            <p class="acc-field"><label><span>Grupo de cliente</span><select id="simGroup"><option value="">— Cualquiera —</option></select></label></p>
            <p class="acc-field"><label><span>Mercado</span><input id="simMarket" placeholder="es" autocomplete="off" spellcheck="false"></label></p>
          </div>
          <datalist id="sr-sim-countries">${COUNTRIES.map(([v, l]) => `<option value="${esc(l)}">${v}</option>`).join('')}</datalist>
          <label class="mng-check" style="margin:.2rem 0 .9rem"><input type="checkbox" id="simAgent"> <span>El carrito lo creó un agente comercial</span></label>
          <div style="display:flex;gap:.6rem;flex-wrap:wrap;align-items:center">
            <button type="submit" class="btn-primary tr-simbtn">${icons.spin(15)} <span class="tr-simbtn-t">Simular</span></button>
            <span class="acc-hint" style="margin:0">Solo consulta las reglas activas actuales.</span>
          </div>
        </form>
        <div id="simResult" role="status" aria-live="polite" style="margin-top:1.2rem"></div>
      </div>
    </section>`;

  paintList(main.querySelector('#listHost'), items);
  // Nombres legibles en los chips de condiciones multi (cliente/grupo/modelo/familia).
  const usedTypes = items.flatMap(r => (r.conditions || []).map(c => c.type));
  ensureLabels(usedTypes).then(() => { if (main.querySelector('#listHost')) paintList(main.querySelector('#listHost'), items); });

  // Selectores de cliente/grupo del simulador (opcionales).
  fkOptions('client').then(opts => addOpts(main.querySelector('#simClient'), opts)).catch(() => {});
  fkOptions('client-group').then(opts => addOpts(main.querySelector('#simGroup'), opts)).catch(() => {});

  const $ = id => main.querySelector('#' + id);
  ['simType', 'simDate', 'simUnits', 'simAmount', 'simCountry', 'simClient', 'simGroup', 'simMarket', 'simAgent'].forEach(id => {
    const el = $(id); if (el) el.addEventListener('input', () => { $('simResult').innerHTML = ''; });
  });

  main.querySelector('#simForm').onsubmit = async e => {
    e.preventDefault();
    const btn = e.target.querySelector('button[type=submit]');
    const label = btn.querySelector('.tr-simbtn-t');
    btn.disabled = true; btn.classList.add('is-busy'); if (label) label.textContent = 'Simulando…';
    try {
      const r = await api.previewSalesRules({
        clientId: $('simClient').value || null,
        groupId: $('simGroup').value || null,
        market: $('simMarket').value.trim() || null,
        countryIsoId: resolveCountry($('simCountry').value) || ($('simCountry').value.trim().toUpperCase() || null),
        orderType: $('simType').value || null,
        units: Number($('simUnits').value) || 0,
        amount: Number($('simAmount').value) || 0,
        createdByAgent: $('simAgent').checked,
        date: $('simDate').value || null,
      });
      $('simResult').innerHTML = simResultHtml(r, items);
    } catch (err) {
      $('simResult').innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(err.body?.error || err.message)}</span></div></div>`;
    } finally {
      btn.disabled = false; btn.classList.remove('is-busy'); if (label) label.textContent = 'Simular';
    }
  };
}

const addOpts = (sel, opts) => { if (sel && opts.length) sel.insertAdjacentHTML('beforeend', opts.map(o => `<option value="${esc(o.value)}">${esc(o.label)}</option>`).join('')); };

const MAX_ROW_CHIPS = 4;
// Verde (ok) solo para acciones favorables; "Denegar" es restrictivo → danger (M1).
const actionChipVariant = o => (o && o.type === 'deny') ? ' danger' : ' ok';
function chipCells(kind, arr) {
  arr = arr || [];
  if (!arr.length) return kind === 'condition' ? '<span class="muted">—</span>' : '<span class="muted">—</span>';
  const shown = arr.slice(0, MAX_ROW_CHIPS).map(o => `<span class="grid-chip${kind === 'action' ? actionChipVariant(o) : ''}">${esc(summaryOf(kind, o))}</span>`);
  if (arr.length > MAX_ROW_CHIPS) {
    const rest = arr.slice(MAX_ROW_CHIPS).map(o => summaryOf(kind, o)).join(' · ');
    shown.push(`<span class="grid-chip off" title="${esc(rest)}">+${arr.length - MAX_ROW_CHIPS}</span>`);
  }
  return `<span class="sr-cellchips">${shown.join('')}</span>`;
}

function paintList(host, items) {
  if (!items.length) {
    host.innerHTML = `<div class="mng-empty">${icons.percent ? icons.percent(30) : icons.tag(30)}
      <b>Todavía no hay condiciones de venta</b>
      <p>Crea reglas para denegar carritos, dar portes gratis o aplicar descuentos según el tipo de pedido, las unidades, el importe, el país o el cliente.</p>
      <a class="btn-primary" href="#/sales-rules/new">${icons.plus(16)} Nueva regla</a></div>`;
    return;
  }
  host.innerHTML = `
    <div class="grid-scroll"><table class="grid sr-grid">
      <thead><tr>
        <th style="width:3rem">#</th><th>Nombre</th><th style="width:6rem">Estado</th>
        <th>Condiciones</th><th>Acciones</th><th class="grid-actions"></th>
      </tr></thead>
      <tbody>${items.map(r => `
        <tr class="row-link" data-id="${esc(r.id)}">
          <td class="muted" style="font-variant-numeric:tabular-nums">${num(r.priority)}</td>
          <td class="grid-link">${esc(r.name)}</td>
          <td>${r.active ? '<span class="grid-chip ok">Activa</span>' : '<span class="grid-chip off">Inactiva</span>'}</td>
          <td>${chipCells('condition', r.conditions)}</td>
          <td>${chipCells('action', r.actions)}</td>
          <td class="grid-actions">${icons.right(16)}</td>
        </tr>`).join('')}</tbody>
    </table></div>
    <p class="acc-hint" style="margin-top:.8rem">La columna <b>#</b> es el orden de aplicación: se evalúan de menor a mayor. Todas las reglas activas que casan aplican sus acciones.</p>`;
  host.querySelectorAll('tr[data-id]').forEach(tr => tr.onclick = () => go(`#/sales-rules/edit/${encodeURIComponent(tr.dataset.id)}`));
}

// Icono neutro (info) para estados sin resultado, coherente con el trazo del set
// (24×24, currentColor, stroke 2). Evita el check verde de éxito en un estado neutro.
const infoIcon = (size = 22) => `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 11.5v4.5"/><path d="M12 8h.01"/></svg>`;

function simResultHtml(r, items) {
  const nameById = new Map(items.map(i => [String(i.id), i.name]));
  const matched = (r.matched || []).map(id => nameById.get(String(id)) || id);
  if (r.denied) {
    return `<div class="sr-sim sr-sim-deny">
      <div class="sr-sim-ic">${icons.alert(22)}</div>
      <div><b>Carrito denegado</b><span>${esc(r.deniedReason || 'Una regla bloquea este carrito.')}</span>
      ${matched.length ? `<div class="sr-sim-rules">${matched.map(n => `<span class="grid-chip">${esc(n)}</span>`).join('')}</div>` : ''}</div>
    </div>`;
  }
  if (!matched.length) {
    return `<div class="sr-sim sr-sim-none">
      <div class="sr-sim-ic">${infoIcon(22)}</div>
      <div><b>Ninguna regla casa</b><span>El carrito seguiría con sus condiciones por defecto.</span></div>
    </div>`;
  }
  const transport = r.freeShipping ? 'Gratis' : (r.fixedTransport != null ? money(r.fixedTransport) : 'Sin cambios');
  const tiles = [];
  tiles.push(tile('Transporte', transport, r.freeShipping ? 'free' : ''));
  if (Number(r.lineDiscountPercent) > 0) tiles.push(tile('Dto. % línea', num(r.lineDiscountPercent) + ' %'));
  if (Number(r.lineDiscountFixed) > 0) tiles.push(tile('Dto. fijo línea', money(r.lineDiscountFixed)));
  return `<div class="sr-sim sr-sim-ok">
    <div class="sr-sim-tiles">${tiles.join('')}</div>
    <div class="sr-sim-meta"><span class="acc-hint" style="margin:0">Reglas que casan</span>
      <div class="sr-sim-rules">${matched.map(n => `<span class="grid-chip ok">${esc(n)}</span>`).join('')}</div></div>
  </div>`;
}
const tile = (label, value, mod) => `<div class="sr-tile"><span class="sr-tile-l">${esc(label)}</span><span class="sr-tile-v${mod === 'free' ? ' is-free' : ''}">${esc(value)}</span></div>`;

// ══════════ EDITOR (crear / editar) ══════════
export async function salesRuleEditView(main, id) {
  const editing = !!id;
  let rule = { name: '', active: true, priority: 0, conditions: [], actions: [] };
  const { items = [] } = await api.salesRules().catch(() => ({ items: [] }));
  if (editing) {
    const found = items.find(x => String(x.id) === String(id));
    if (!found) { main.innerHTML = `<div class="notice notice-error" role="alert">La regla no existe.</div>`; return; }
    rule = found;
  } else {
    rule.priority = items.reduce((m, x) => Math.max(m, Number(x.priority) || 0), 0) + 10;
  }

  // Estado de trabajo (copias mutables).
  let conditions = (rule.conditions || []).map(c => ({ ...c }));
  let actions = (rule.actions || []).map(a => ({ ...a }));

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs"><a href="#/sales-rules">Condiciones de venta</a> · <span>${editing ? 'Editar' : 'Nueva'}</span></p>
        <h1 class="title">${editing ? 'Editar regla' : 'Nueva regla'}</h1>
      </div>
    </div>
    <form class="mng-form nc-form" id="f" novalidate>
      <div id="notice"></div>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.percent ? icons.percent(20) : icons.tag(20)}Regla</h2></header>
        <div class="biz-card">
          <div class="biz-grid">
            <p class="acc-field wide"><label><span>Nombre *</span>
              <input id="name" value="${esc(rule.name || '')}" maxlength="160" placeholder="Ej.: Portes gratis a clientes VIP"></label></p>
            <p class="acc-field"><label><span>Orden de aplicación</span>
              <input type="number" id="priority" step="1" value="${esc(rule.priority ?? 0)}" inputmode="numeric"></label>
              <span class="acc-hint">Menor número = se evalúa antes.</span></p>
          </div>
          <label class="mng-check" style="margin:.2rem 0 0"><input type="checkbox" id="active" ${rule.active !== false ? 'checked' : ''}> <span>Regla activa</span></label>
        </div>
      </section>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.list(20)}Condiciones y acciones</h2></header>
        <div class="biz-card">
          <p class="lead" style="margin:0 0 1.3rem">La regla se aplica cuando se cumplen <b>TODAS</b> las condiciones (Y lógico). Entonces se ejecutan sus acciones.</p>
          <div class="sr-cols">
            <div class="sr-col">
              <div class="sr-col-head"><h3>Condiciones (reglas)</h3>
                <button type="button" class="sr-add" id="addCond" aria-label="Añadir condición">${icons.plus(18)}</button></div>
              <p class="acc-hint sr-col-hint">Se deben cumplir TODAS para aplicar la regla.</p>
              <div class="sr-box" id="condBox" role="list" aria-label="Condiciones de la regla"></div>
            </div>
            <div class="sr-col">
              <div class="sr-col-head"><h3>Acciones</h3>
                <button type="button" class="sr-add" id="addAct" aria-label="Añadir acción">${icons.plus(18)}</button></div>
              <p class="acc-hint sr-col-hint">Qué sucede cuando la regla case.</p>
              <div class="sr-box" id="actBox" role="list" aria-label="Acciones de la regla"></div>
            </div>
          </div>
        </div>
      </section>

      <div class="acc-actions nc-actions">
        ${editing ? '<button type="button" class="btn-danger" id="del">Eliminar</button>' : ''}
        <a class="btn-ghost" href="#/sales-rules">Cancelar</a>
        <button type="submit" class="btn-primary">${editing ? 'Guardar cambios' : 'Crear regla'}</button>
      </div>
    </form>`;

  const $ = i => main.querySelector('#' + i);
  const notice = $('notice');
  const condBox = $('condBox'), actBox = $('actBox');

  function renderCol(box, kind, arr, emptyText) {
    if (!arr.length) { box.classList.add('is-empty'); box.innerHTML = `<p class="sr-box-empty">${esc(emptyText)}</p>`; return; }
    box.classList.remove('is-empty');
    box.innerHTML = arr.map((o, i) => `
      <div class="sr-item" role="listitem">
        <button type="button" class="sr-item-main" data-edit="${i}">
          <span class="sr-item-type">${esc(descOf(kind, o.type)?.label || o.type)}</span>
          <span class="sr-item-sum">${esc(summaryOf(kind, o))}</span>
        </button>
        <button type="button" class="sr-item-x" data-del="${i}" aria-label="Quitar ${esc(summaryOf(kind, o))}">${icons.close(14)}</button>
      </div>`).join('');
  }
  function paintAll() {
    renderCol(condBox, 'condition', conditions, 'Sin condiciones. Añade al menos una con el botón +.');
    renderCol(actBox, 'action', actions, 'Sin acciones. Añade al menos una con el botón +.');
  }
  paintAll();

  // Nombres legibles en los resúmenes al hidratar (cliente/grupo/modelo/familia).
  ensureLabels(conditions.map(c => c.type)).then(paintAll);

  async function addItem(kind) {
    const obj = await openItemDialog(kind, null);
    if (!obj) return;
    if (kind === 'condition') { conditions.push(obj); await ensureLabels([obj.type]); } else actions.push(obj);
    notice.innerHTML = '';
    paintAll();
  }
  async function editItem(kind, i) {
    const arr = kind === 'condition' ? conditions : actions;
    const obj = await openItemDialog(kind, arr[i]);
    if (!obj) return;
    arr[i] = obj;
    if (kind === 'condition') await ensureLabels([obj.type]);
    paintAll();
  }
  $('addCond').onclick = () => addItem('condition');
  $('addAct').onclick = () => addItem('action');
  condBox.addEventListener('click', e => { const ed = e.target.closest('[data-edit]'); const del = e.target.closest('[data-del]'); if (ed) editItem('condition', +ed.dataset.edit); else if (del) { conditions.splice(+del.dataset.del, 1); paintAll(); } });
  actBox.addEventListener('click', e => { const ed = e.target.closest('[data-edit]'); const del = e.target.closest('[data-del]'); if (ed) editItem('action', +ed.dataset.edit); else if (del) { actions.splice(+del.dataset.del, 1); paintAll(); } });

  $('f').onsubmit = async e => {
    e.preventDefault();
    notice.innerHTML = '';
    const name = $('name').value.trim();
    if (!name) return warn(notice, 'El nombre es obligatorio.');
    if (!conditions.length) { condBox.classList.add('sr-box-err'); setTimeout(() => condBox.classList.remove('sr-box-err'), 1200); return warn(notice, 'Debe haber al menos una condición.'); }
    if (!actions.length) { actBox.classList.add('sr-box-err'); setTimeout(() => actBox.classList.remove('sr-box-err'), 1200); return warn(notice, 'Debe haber al menos una acción.'); }

    const body = { name, active: $('active').checked, priority: Number($('priority').value || 0), conditions, actions };
    const btn = e.target.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      if (editing) await api.saveSalesRule(id, body); else await api.createSalesRule(body);
      flash(editing ? 'Regla guardada.' : 'Regla creada.');
      go('#/sales-rules');
    } catch (err) { btn.disabled = false; warn(notice, err.body?.error || err.message || 'No se pudo guardar.'); }
  };

  if (editing) $('del').onclick = async () => {
    if (!confirm('¿Eliminar esta condición de venta?')) return;
    try { await api.delSalesRule(id); flash('Regla eliminada.'); go('#/sales-rules'); }
    catch (err) { warn(notice, err.body?.error || err.message); }
  };
}

function warn(host, text) {
  host.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(text)}</span></div></div>`;
  host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

// CSS del sub-diálogo inyectado una sola vez (el resto vive en manage.css).
function ensureModalCss() {
  if (document.getElementById('sr-modal-css')) return;
  const s = document.createElement('style');
  s.id = 'sr-modal-css';
  s.textContent = `
  .sr-modal{position:fixed;inset:0;z-index:80;background:rgba(20,18,17,.55);display:flex;align-items:flex-start;justify-content:center;padding:6vh 1.2rem 2rem;overflow:auto}
  .sr-dialog{background:var(--card,#faf7f7);border:1px solid var(--line,#d8d3d3);box-shadow:0 24px 60px -22px rgba(0,0,0,.5);width:min(40rem,100%);display:flex;flex-direction:column;border-radius:var(--r-sm,4px)}
  .sr-dialog header{display:flex;align-items:center;gap:.8rem;padding:1rem 1.2rem;border-bottom:2px solid var(--ink,#201e1d)}
  .sr-dialog header h3{margin:0;font-size:1.05rem;font-weight:800;letter-spacing:-.01em;color:var(--ink,#201e1d)}
  .sr-dialog header .json-close{margin-left:auto}
  .sr-dialog-body{padding:1.2rem}
  .sr-dialog-body .acc-field{margin:0 0 1rem}
  .sr-noparams{display:flex;align-items:center;gap:.5rem;margin:.2rem 0 0;color:var(--ok,#2f855a);font-size:.9rem}
  .sr-dlg-note{margin-top:.4rem}
  .sr-flabel{display:block;font-size:.74rem;font-weight:700;letter-spacing:.04em;text-transform:uppercase;color:var(--ink-2,#4a4746);margin:0 0 .4rem}
  .sr-dialog-foot{display:flex;justify-content:flex-end;gap:.6rem;padding:1rem 1.2rem;border-top:1px solid var(--line,#e5e0e0)}
  #srErr:not(:empty){margin:.2rem 0 .6rem}`;
  document.head.append(s);
}
