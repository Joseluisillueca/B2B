// Listado genérico de un maestro: búsqueda, paginación, alta y edición al pulsar fila.
import { SCHEMAS } from '../schemas.js';
import { icons } from '../icons.js';
import { esc, dig, loadRows, fkOptions } from '../util.js';
import { go } from '../router.js';

const PAGE = 25;
const ENUM_ES = {
  SCHEDULED: 'Programada', REPLENISHMENT: 'Reposición', NOT_DEFINED: 'No definido',
  PVD: 'PVD', PVP: 'PVP', open: 'Abierto',
};
const enumEs = v => ENUM_ES[v] ?? v;

export default async function list(main, type, slug) {
  const sc = SCHEMAS[type];
  const article = sc.fem ? 'Nueva' : 'Nuevo';
  const rows = await loadRows(type);

  // Precarga de etiquetas para columnas FK (fk:tipo y fkarr:tipo)
  const fkCols = sc.list.filter(c => /^fka?rr?:|^fk:/.test(String(c[2])));
  const fkMaps = {};
  await Promise.all(fkCols.map(async c => {
    const t = c[2].split(':')[1];
    if (fkMaps[t]) return;
    const opts = await fkOptions(t);
    fkMaps[t] = Object.fromEntries(opts.map(o => [o.value, o.label]));
  }));

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Maestros</p>
        <h1 class="title">${esc(sc.plural)}</h1>
        ${sc.lead ? `<p class="lead">${esc(sc.lead)}</p>` : ''}
      </div>
      <a class="btn-primary" href="#/${slug}/new">${icons.plus(16)} ${article} ${esc(sc.singular)}</a>
    </div>
    <div id="body"></div>`;

  const body = main.querySelector('#body');

  // Etiqueta de búsqueda por fila (incluye nombres FK, no solo el JSON crudo con GUIDs)
  const haystack = row => {
    let s = JSON.stringify(row.payload).toLowerCase() + ' ' + row.id.toLowerCase();
    for (const c of fkCols) {
      const t = c[2].split(':')[1];
      const val = c[1] === '__externalId' ? row.id : dig(row.payload, c[1]);
      for (const id of Array.isArray(val) ? val : [val])
        if (id != null) s += ' ' + String(fkMaps[t]?.[normId(id)] || '').toLowerCase();
    }
    return s;
  };

  const cell = (row, col) => {
    const [, path, fmt] = col;
    const val = path === '__externalId' ? row.id : dig(row.payload, path);
    if (fmt === 'bool') return val ? '<span class="grid-chip ok">Sí</span>' : '<span class="grid-chip off">No</span>';
    if (fmt === 'money') return val == null ? '—' : Number(val).toLocaleString('es-ES', { minimumFractionDigits: 2 }) + ' €';
    if (fmt === 'date') return val ? String(val).slice(0, 10) : '—';
    if (fmt === 'arr') return Array.isArray(val) && val.length
      ? val.map(x => `<span class="grid-chip">${esc(typeof x === 'object' ? (x.id || x.name || '') : x)}</span>`).join(' ') : '—';
    if (String(fmt).startsWith('fkarr:')) { const t = fmt.slice(6); return Array.isArray(val) && val.length
      ? val.map(id => `<span class="grid-chip">${esc(fkMaps[t]?.[normId(id)] || id)}</span>`).join(' ') : '—'; }
    if (String(fmt).startsWith('fk:')) { const t = fmt.slice(3); return val ? esc(fkMaps[t]?.[normId(val)] || val) : '—'; }
    if (fmt === 'chip') return val ? `<span class="grid-chip">${esc(enumEs(val))}</span>` : '—';
    return val == null || val === '' ? '—' : esc(String(val));
  };

  // Maestro totalmente vacío → estado vacío con CTA (sin cabecera de tabla huérfana)
  if (!rows.length) {
    body.innerHTML = `<div class="mng-empty">${icons[sc.icon] ? icons[sc.icon](30) : ''}
      <b>Todavía no hay ${esc(sc.plural.toLowerCase())}</b>
      <p>${esc(sc.lead || '')}</p>
      <a class="btn-primary" href="#/${slug}/new">${icons.plus(16)} ${article} ${esc(sc.singular)}</a></div>`;
    return;
  }

  // Armazón estático (el buscador no se repinta → no pierde el foco al teclear)
  body.innerHTML = `
    <div class="mng-tools">
      <div class="mng-search">${icons.search(16)}<input type="search" id="q" placeholder="Buscar…" aria-label="Buscar"></div>
      <span class="spacer"></span><span class="mng-count" id="count"></span>
    </div>
    <div class="grid-scroll"><table class="grid">
      <thead><tr>${sc.list.map(c => `<th>${esc(c[0])}</th>`).join('')}<th class="grid-actions"></th></tr></thead>
      <tbody id="rows"></tbody>
    </table></div>
    <div id="pagerHost"></div>`;

  const q = body.querySelector('#q');
  const tbody = body.querySelector('#rows');
  const count = body.querySelector('#count');
  const pagerHost = body.querySelector('#pagerHost');
  let page = 0, term = '';

  const paintRows = () => {
    const shown = rows.filter(r => !term || haystack(r).includes(term));
    const pages = Math.max(1, Math.ceil(shown.length / PAGE));
    if (page >= pages) page = pages - 1;
    const slice = shown.slice(page * PAGE, page * PAGE + PAGE);
    count.textContent = `${shown.length} ${shown.length === 1 ? 'registro' : 'registros'}`;
    tbody.innerHTML = slice.length ? slice.map(r => `
      <tr class="row-link" data-id="${esc(r.id)}">
        ${sc.list.map((c, j) => `<td${j === 0 ? ' class="grid-link"' : ''}>${cell(r, c)}</td>`).join('')}
        <td class="grid-actions">${icons.right(16)}</td></tr>`).join('')
      : `<tr class="grid-empty"><td colspan="${sc.list.length + 1}">Sin resultados con «${esc(term)}».</td></tr>`;
    pagerHost.innerHTML = pages > 1 ? pager(page, pages) : '';
    tbody.querySelectorAll('tr[data-id]').forEach(tr => tr.onclick = () => go(`#/${slug}/edit/${encodeURIComponent(tr.dataset.id)}`));
    pagerHost.querySelectorAll('[data-pg]').forEach(b => b.onclick = () => { page = Number(b.dataset.pg); paintRows(); window.scrollTo({ top: 0, behavior: 'smooth' }); });
  };
  q.oninput = () => { term = q.value.toLowerCase().trim(); page = 0; paintRows(); };
  paintRows();
}

const normId = v => String(typeof v === 'object' ? (v.id || v.code || '') : v);

function pager(page, pages) {
  const btn = (p, label, on = false, dis = false) =>
    `<button class="pg-num${on ? ' on' : ''}" data-pg="${p}"${dis ? ' disabled' : ''}>${label}</button>`;
  const nums = [];
  for (let p = 0; p < pages; p++) {
    if (p === 0 || p === pages - 1 || Math.abs(p - page) <= 1) nums.push(btn(p, p + 1, p === page));
    else if (nums[nums.length - 1] !== '…') nums.push('…');
  }
  return `<div class="pager">
    ${btn(Math.max(0, page - 1), icons.left(16), false, page === 0)}
    ${nums.map(n => n === '…' ? '<span class="pg-gap">…</span>' : n).join('')}
    ${btn(Math.min(pages - 1, page + 1), icons.right(16), false, page === pages - 1)}</div>`;
}
