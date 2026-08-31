// Reglas de transporte (portes) — listado + simulador y editor. El coste resultante
// viaja en el pedido a Business Central (totalTransport + incotermId). Reutiliza el
// lenguaje de diseño del portal (.biz-*/.acc-*) y del back-office (.mng-*).
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash, fkOptions } from '../util.js';
import { go } from '../router.js';

// Lista COMPLETA de países (ISO 3166-1 alpha-2) con su nombre en español. Se usa en
// el editor (multi-país por chips) y en el simulador (país único). Vacío = cualquiera.
const COUNTRIES = [
  ['AD', 'Andorra'], ['AE', 'Emiratos Árabes Unidos'], ['AF', 'Afganistán'], ['AG', 'Antigua y Barbuda'],
  ['AI', 'Anguila'], ['AL', 'Albania'], ['AM', 'Armenia'], ['AO', 'Angola'], ['AQ', 'Antártida'],
  ['AR', 'Argentina'], ['AS', 'Samoa Americana'], ['AT', 'Austria'], ['AU', 'Australia'], ['AW', 'Aruba'],
  ['AX', 'Islas Åland'], ['AZ', 'Azerbaiyán'], ['BA', 'Bosnia y Herzegovina'], ['BB', 'Barbados'],
  ['BD', 'Bangladés'], ['BE', 'Bélgica'], ['BF', 'Burkina Faso'], ['BG', 'Bulgaria'], ['BH', 'Baréin'],
  ['BI', 'Burundi'], ['BJ', 'Benín'], ['BL', 'San Bartolomé'], ['BM', 'Bermudas'], ['BN', 'Brunéi'],
  ['BO', 'Bolivia'], ['BQ', 'Bonaire, San Eustaquio y Saba'], ['BR', 'Brasil'], ['BS', 'Bahamas'],
  ['BT', 'Bután'], ['BV', 'Isla Bouvet'], ['BW', 'Botsuana'], ['BY', 'Bielorrusia'], ['BZ', 'Belice'],
  ['CA', 'Canadá'], ['CC', 'Islas Cocos'], ['CD', 'República Democrática del Congo'],
  ['CF', 'República Centroafricana'], ['CG', 'Congo'], ['CH', 'Suiza'], ['CI', 'Costa de Marfil'],
  ['CK', 'Islas Cook'], ['CL', 'Chile'], ['CM', 'Camerún'], ['CN', 'China'], ['CO', 'Colombia'],
  ['CR', 'Costa Rica'], ['CU', 'Cuba'], ['CV', 'Cabo Verde'], ['CW', 'Curazao'], ['CX', 'Isla de Navidad'],
  ['CY', 'Chipre'], ['CZ', 'Chequia'], ['DE', 'Alemania'], ['DJ', 'Yibuti'], ['DK', 'Dinamarca'],
  ['DM', 'Dominica'], ['DO', 'República Dominicana'], ['DZ', 'Argelia'], ['EC', 'Ecuador'], ['EE', 'Estonia'],
  ['EG', 'Egipto'], ['EH', 'Sáhara Occidental'], ['ER', 'Eritrea'], ['ES', 'España'], ['ET', 'Etiopía'],
  ['FI', 'Finlandia'], ['FJ', 'Fiyi'], ['FK', 'Islas Malvinas'], ['FM', 'Micronesia'], ['FO', 'Islas Feroe'],
  ['FR', 'Francia'], ['GA', 'Gabón'], ['GB', 'Reino Unido'], ['GD', 'Granada'], ['GE', 'Georgia'],
  ['GF', 'Guayana Francesa'], ['GG', 'Guernsey'], ['GH', 'Ghana'], ['GI', 'Gibraltar'], ['GL', 'Groenlandia'],
  ['GM', 'Gambia'], ['GN', 'Guinea'], ['GP', 'Guadalupe'], ['GQ', 'Guinea Ecuatorial'], ['GR', 'Grecia'],
  ['GS', 'Islas Georgias del Sur y Sandwich del Sur'], ['GT', 'Guatemala'], ['GU', 'Guam'],
  ['GW', 'Guinea-Bisáu'], ['GY', 'Guyana'], ['HK', 'Hong Kong'], ['HM', 'Islas Heard y McDonald'],
  ['HN', 'Honduras'], ['HR', 'Croacia'], ['HT', 'Haití'], ['HU', 'Hungría'], ['ID', 'Indonesia'],
  ['IE', 'Irlanda'], ['IL', 'Israel'], ['IM', 'Isla de Man'], ['IN', 'India'],
  ['IO', 'Territorio Británico del Océano Índico'], ['IQ', 'Irak'], ['IR', 'Irán'], ['IS', 'Islandia'],
  ['IT', 'Italia'], ['JE', 'Jersey'], ['JM', 'Jamaica'], ['JO', 'Jordania'], ['JP', 'Japón'], ['KE', 'Kenia'],
  ['KG', 'Kirguistán'], ['KH', 'Camboya'], ['KI', 'Kiribati'], ['KM', 'Comoras'], ['KN', 'San Cristóbal y Nieves'],
  ['KP', 'Corea del Norte'], ['KR', 'Corea del Sur'], ['KW', 'Kuwait'], ['KY', 'Islas Caimán'],
  ['KZ', 'Kazajistán'], ['LA', 'Laos'], ['LB', 'Líbano'], ['LC', 'Santa Lucía'], ['LI', 'Liechtenstein'],
  ['LK', 'Sri Lanka'], ['LR', 'Liberia'], ['LS', 'Lesoto'], ['LT', 'Lituania'], ['LU', 'Luxemburgo'],
  ['LV', 'Letonia'], ['LY', 'Libia'], ['MA', 'Marruecos'], ['MC', 'Mónaco'], ['MD', 'Moldavia'],
  ['ME', 'Montenegro'], ['MF', 'San Martín (parte francesa)'], ['MG', 'Madagascar'], ['MH', 'Islas Marshall'],
  ['MK', 'Macedonia del Norte'], ['ML', 'Malí'], ['MM', 'Myanmar (Birmania)'], ['MN', 'Mongolia'],
  ['MO', 'Macao'], ['MP', 'Islas Marianas del Norte'], ['MQ', 'Martinica'], ['MR', 'Mauritania'],
  ['MS', 'Montserrat'], ['MT', 'Malta'], ['MU', 'Mauricio'], ['MV', 'Maldivas'], ['MW', 'Malaui'],
  ['MX', 'México'], ['MY', 'Malasia'], ['MZ', 'Mozambique'], ['NA', 'Namibia'], ['NC', 'Nueva Caledonia'],
  ['NE', 'Níger'], ['NF', 'Isla Norfolk'], ['NG', 'Nigeria'], ['NI', 'Nicaragua'], ['NL', 'Países Bajos'],
  ['NO', 'Noruega'], ['NP', 'Nepal'], ['NR', 'Nauru'], ['NU', 'Niue'], ['NZ', 'Nueva Zelanda'], ['OM', 'Omán'],
  ['PA', 'Panamá'], ['PE', 'Perú'], ['PF', 'Polinesia Francesa'], ['PG', 'Papúa Nueva Guinea'],
  ['PH', 'Filipinas'], ['PK', 'Pakistán'], ['PL', 'Polonia'], ['PM', 'San Pedro y Miquelón'],
  ['PN', 'Islas Pitcairn'], ['PR', 'Puerto Rico'], ['PS', 'Palestina'], ['PT', 'Portugal'], ['PW', 'Palaos'],
  ['PY', 'Paraguay'], ['QA', 'Catar'], ['RE', 'Reunión'], ['RO', 'Rumanía'], ['RS', 'Serbia'], ['RU', 'Rusia'],
  ['RW', 'Ruanda'], ['SA', 'Arabia Saudí'], ['SB', 'Islas Salomón'], ['SC', 'Seychelles'], ['SD', 'Sudán'],
  ['SE', 'Suecia'], ['SG', 'Singapur'], ['SH', 'Santa Elena'], ['SI', 'Eslovenia'], ['SJ', 'Svalbard y Jan Mayen'],
  ['SK', 'Eslovaquia'], ['SL', 'Sierra Leona'], ['SM', 'San Marino'], ['SN', 'Senegal'], ['SO', 'Somalia'],
  ['SR', 'Surinam'], ['SS', 'Sudán del Sur'], ['ST', 'Santo Tomé y Príncipe'], ['SV', 'El Salvador'],
  ['SX', 'Sint Maarten (parte neerlandesa)'], ['SY', 'Siria'], ['SZ', 'Esuatini'], ['TC', 'Islas Turcas y Caicos'],
  ['TD', 'Chad'], ['TF', 'Territorios Australes Franceses'], ['TG', 'Togo'], ['TH', 'Tailandia'],
  ['TJ', 'Tayikistán'], ['TK', 'Tokelau'], ['TL', 'Timor Oriental'], ['TM', 'Turkmenistán'], ['TN', 'Túnez'],
  ['TO', 'Tonga'], ['TR', 'Turquía'], ['TT', 'Trinidad y Tobago'], ['TV', 'Tuvalu'], ['TW', 'Taiwán'],
  ['TZ', 'Tanzania'], ['UA', 'Ucrania'], ['UG', 'Uganda'], ['UM', 'Islas Ultramarinas Menores de EE. UU.'],
  ['US', 'Estados Unidos'], ['UY', 'Uruguay'], ['UZ', 'Uzbekistán'], ['VA', 'Ciudad del Vaticano'],
  ['VC', 'San Vicente y las Granadinas'], ['VE', 'Venezuela'], ['VG', 'Islas Vírgenes Británicas'],
  ['VI', 'Islas Vírgenes de EE. UU.'], ['VN', 'Vietnam'], ['VU', 'Vanuatu'], ['WF', 'Wallis y Futuna'],
  ['WS', 'Samoa'], ['YE', 'Yemen'], ['YT', 'Mayotte'], ['ZA', 'Sudáfrica'], ['ZM', 'Zambia'], ['ZW', 'Zimbabue'],
];
const COUNTRY_ES = Object.fromEntries(COUNTRIES);

// Resolución flexible de país: acepta el código exacto (ES), el nombre en español
// (con o sin acentos/mayúsculas) o un código de 2 letras que BC pueda emitir aunque
// no esté en la lista. Devuelve el código ISO en mayúsculas o null.
const stripAccents = s => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '');
const normName = s => stripAccents(s).trim().toLowerCase();
const NAME_TO_CODE = new Map(COUNTRIES.map(([c, n]) => [normName(n), c]));
const CODE_SET = new Set(COUNTRIES.map(([c]) => c));
function resolveCountry(raw) {
  const t = String(raw || '').trim();
  if (!t) return null;
  const up = t.toUpperCase();
  if (CODE_SET.has(up)) return up;
  const n = normName(t);
  if (NAME_TO_CODE.has(n)) return NAME_TO_CODE.get(n);
  if (/^[a-z]{2}$/i.test(t)) return up; // código de 2 letras no catalogado (BC a medida)
  return null;
}
// Parsea el string coma-separado del backend a una lista de códigos únicos en mayúsculas.
const parseCountries = csv => {
  const out = [];
  String(csv || '').split(',').forEach(p => {
    const c = p.trim().toUpperCase();
    if (c && !out.includes(c)) out.push(c);
  });
  return out;
};
// Nombre legible de un código para las fichas/chips (cae al propio código si no está).
const countryName = code => COUNTRY_ES[code] || code;
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
// Opciones del datalist: valor = nombre (para poder BUSCAR por nombre) y el código
// como etiqueta secundaria. `resolveCountry` acepta luego tanto el nombre como el código.
const countryDatalistOptions = COUNTRIES.map(([v, l]) => `<option value="${esc(l)}">${v}</option>`).join('');
const MAX_ROW_CHIPS = 4; // países mostrados en el listado antes de resumir con "+N".

// Países de una regla como chips (código) para el listado. Muchos → primeros + "+N".
function countryCells(csv) {
  const codes = parseCountries(csv);
  if (!codes.length) return '';
  const shown = codes.slice(0, MAX_ROW_CHIPS).map(c => `<span class="grid-chip">${esc(c)}</span>`);
  if (codes.length > MAX_ROW_CHIPS) {
    const rest = codes.slice(MAX_ROW_CHIPS);
    shown.push(`<span class="grid-chip off" title="${esc(rest.map(countryName).join(', '))}">+${rest.length}</span>`);
  }
  return `<span class="tr-country-cells">${shown.join('')}</span>`;
}

// Coste legible: "Portes gratis" (0), "8,50 €" o "0,90 €/ud".
function costLabel(r) {
  if (!r.cost || Number(r.cost) === 0) return '<span class="grid-chip ok">Portes gratis</span>';
  return `<b style="font-weight:700">${money(r.cost)}</b>${r.perUnit ? '<span class="muted"> /ud</span>' : ''}`;
}

// Resumen de condiciones como fichas. Sin condiciones → "Cualquier pedido".
function conditionChips(r) {
  const chips = [];
  const cc = countryCells(r.countryIsoId);
  if (cc) chips.push(cc);
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
              <input id="simCountry" list="tr-countries" value="" autocomplete="off" placeholder="España o ES" spellcheck="false"></label>
              <span class="acc-hint">Un solo país. Escribe el nombre o el código de Business Central (para España, ES).</span></p>
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
        countryIsoId: resolveCountry($('simCountry').value) || ($('simCountry').value.trim().toUpperCase() || null),
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
            <div class="acc-field wide tr-country-field">
              <label class="tr-country-lbl" for="countryInput"><span>País de envío</span></label>
              <div class="tr-chips" id="countryChips" role="list" aria-label="Países de la regla"></div>
              <input id="countryInput" list="tr-countries" class="tr-country-input" autocomplete="off"
                     placeholder="Escribe un país o su código (ES) y pulsa Intro" spellcheck="false"
                     role="combobox" aria-expanded="false" aria-controls="tr-countries" aria-describedby="countryHint">
              <span class="acc-hint" id="countryHint">Deja vacío para aplicar a cualquier país. Usa el mismo código que Business Central (para España, ES).</span>
            </div>
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

  // ── Multi-país por chips ──────────────────────────────────────────────
  // Estado = lista de códigos ISO. Se hidrata del string coma-separado guardado.
  const chipHost = $('countryChips');
  const cInput = $('countryInput');
  let countryCodes = parseCountries(r.countryIsoId);

  function renderCountryChips() {
    chipHost.innerHTML = countryCodes.map(code => {
      const name = countryName(code);
      const tail = name !== code ? `<code>${esc(code)}</code>` : '';
      return `<span class="tr-chip" role="listitem"><span class="tr-chip-name">${esc(name)}</span>${tail}` +
        `<button type="button" class="tr-chip-x" data-code="${esc(code)}" aria-label="Quitar ${esc(name)}">${icons.close(12)}</button></span>`;
    }).join('');
  }
  function addCountry(code) {
    if (!code) return false;
    if (!countryCodes.includes(code)) { countryCodes.push(code); renderCountryChips(); }
    cInput.value = '';
    return true;
  }
  function rejectCountry() {
    cInput.classList.add('is-invalid');
    setTimeout(() => cInput.classList.remove('is-invalid'), 900);
  }
  renderCountryChips();

  cInput.addEventListener('keydown', e => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const code = resolveCountry(cInput.value);
      if (code) addCountry(code);
      else if (cInput.value.trim()) rejectCountry();
    } else if (e.key === 'Backspace' && !cInput.value && countryCodes.length) {
      countryCodes.pop();
      renderCountryChips();
    }
  });
  // Selección de una opción del datalist con el ratón (Chromium: insertReplacementText).
  cInput.addEventListener('input', e => {
    if (e.inputType === 'insertReplacementText' || e.inputType == null) {
      const code = resolveCountry(cInput.value);
      if (code) addCountry(code);
    }
  });
  // Respaldo: confirmar al perder foco o seleccionar del datalist en otros navegadores.
  cInput.addEventListener('change', () => {
    const code = resolveCountry(cInput.value);
    if (code) addCountry(code);
  });
  chipHost.addEventListener('click', e => {
    const btn = e.target.closest('.tr-chip-x');
    if (!btn) return;
    countryCodes = countryCodes.filter(c => c !== btn.dataset.code);
    renderCountryChips();
    cInput.focus();
  });

  $('f').onsubmit = async e => {
    e.preventDefault();
    notice.innerHTML = '';
    // Rescata un país tecleado pero no confirmado como chip (sin pulsar Intro).
    const pending = resolveCountry(cInput.value);
    if (pending) addCountry(pending);
    const name = $('name').value.trim();
    if (!name) return warn(notice, 'El nombre es obligatorio.');
    const cost = Number($('cost').value || 0);
    if (cost < 0) return warn(notice, 'El coste no puede ser negativo.');

    const body = {
      name, active: $('active').checked, priority: Number($('priority').value || 0),
      clientExternalId: $('clientExternalId').value || null,
      countryIsoId: countryCodes.join(',') || null,
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
