// Comunicación BC (nativa de GESTIÓN): todo lo que Business Central ha enviado al portal.
// Solo lectura; pulsar una fila abre el JSON recibido. Sustituye al explorador del CMS.
import { api } from '../api.js';
import { esc, showJson } from '../util.js';
import { icons } from '../icons.js';

const PAGE = 30;

export default async function received(main) {
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs">Integración</p>
      <h1 class="title">Comunicación BC</h1>
      <p class="lead">Todo lo que Business Central ha enviado al portal. Pulsa una fila para ver el JSON recibido.</p>
    </div></div>
    <div id="body"></div>`;

  const body = main.querySelector('#body');
  let rows = [];
  try {
    // El endpoint capa `take` a 200; se pagina con `skip` hasta agotar `total` para no
    // truncar la trazabilidad (un despliegue real supera fácilmente los 200 documentos).
    const acc = [];
    let skip = 0, total = Infinity;
    while (acc.length < total) {
      const data = await api.allDocs(skip, 200);
      total = Number.isFinite(data.total) ? data.total : acc.length;
      const items = data.items || [];
      acc.push(...items);
      if (!items.length) break;
      skip += items.length;
      if (skip > 50000) break;   // salvaguarda anti-bucle
    }
    rows = acc.map(d => ({
      type: d.entityType, id: d.externalId, parentId: d.parentId,
      first: d.firstReceivedAt, last: d.lastReceivedAt, payload: d.payload,
    }));
    rows.sort((a, b) => String(b.last).localeCompare(String(a.last)));
  } catch (e) {
    body.innerHTML = `<div class="notice notice-error" role="alert">No se pudo cargar la comunicación: ${esc(e.message || 'error')}</div>`;
    return;
  }

  if (!rows.length) {
    body.innerHTML = `<div class="mng-empty">${icons.layers ? icons.layers(30) : ''}
      <b>Todavía no ha llegado nada de Business Central</b>
      <p>Cuando el conector sincronice, aquí verás cada documento recibido y su JSON.</p></div>`;
    return;
  }

  body.innerHTML = `
    <div class="mng-tools">
      <div class="mng-search">${icons.search(16)}<input type="search" id="q" placeholder="Buscar por tipo o id…" aria-label="Buscar"></div>
      <span class="spacer"></span><span class="mng-count" id="count"></span>
    </div>
    <div class="grid-scroll"><table class="grid">
      <thead><tr><th>Tipo</th><th>Id externo</th><th>Recibido</th><th class="grid-actions"></th></tr></thead>
      <tbody id="rows"></tbody>
    </table></div>
    <div id="pagerHost"></div>`;

  const q = body.querySelector('#q');
  const tbody = body.querySelector('#rows');
  const count = body.querySelector('#count');
  const pagerHost = body.querySelector('#pagerHost');
  let page = 0, term = '';

  const fmtDate = v => { try { return new Date(v).toLocaleString('es-ES'); } catch { return String(v || '—'); } };
  const shortId = v => v && v.length > 20
    ? `<code class="grid-id" title="${esc(v)}">${esc(v.slice(0, 10))}…</code>` : esc(v || '—');

  const paint = () => {
    const shown = rows.filter(r => !term
      || r.type.toLowerCase().includes(term) || String(r.id).toLowerCase().includes(term));
    const pages = Math.max(1, Math.ceil(shown.length / PAGE));
    if (page >= pages) page = pages - 1;
    const slice = shown.slice(page * PAGE, page * PAGE + PAGE);
    count.textContent = `${shown.length} ${shown.length === 1 ? 'documento' : 'documentos'}`;
    tbody.innerHTML = slice.length ? slice.map((r, i) => `
      <tr class="row-link" data-i="${page * PAGE + i}">
        <td class="grid-link"><span class="grid-chip">${esc(r.type)}</span></td>
        <td>${shortId(r.id)}</td>
        <td>${esc(fmtDate(r.last))}</td>
        <td class="grid-actions">${icons.right(16)}</td></tr>`).join('')
      : `<tr class="grid-empty"><td colspan="4">Sin resultados con «${esc(term)}».</td></tr>`;
    pagerHost.innerHTML = pages > 1 ? pager(page, pages) : '';
    tbody.querySelectorAll('tr[data-i]').forEach(tr => tr.onclick = () => {
      const r = rows[Number(tr.dataset.i)];
      showJson(`${r.type} · ${r.id}`, r.payload, `recibido ${fmtDate(r.last)}`);
    });
    pagerHost.querySelectorAll('[data-pg]').forEach(b => b.onclick = () => {
      page = Number(b.dataset.pg); paint(); window.scrollTo({ top: 0, behavior: 'smooth' });
    });
  };
  q.oninput = () => { term = q.value.toLowerCase().trim(); page = 0; paint(); };
  paint();
}

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
