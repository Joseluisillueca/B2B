// Pedidos del portal: listado y detalle con gestión de estado y eliminación.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, dig, loadRows, fkOptions, flash } from '../util.js';
import { go } from '../router.js';

const STATUSES = [['open', 'Abierto'], ['shipped', 'Enviado'], ['partially-shipped', 'Envío parcial'],
  ['invoiced', 'Facturado'], ['canceled', 'Cancelado']];
const statusLabel = s => (STATUSES.find(x => x[0] === s) || [s, s || '—'])[1];
const statusClass = s => ({ open: '', shipped: 'ok', 'partially-shipped': 'warn', invoiced: 'ok', canceled: 'danger' }[s] || '');
const money = v => v == null ? '—' : Number(v).toLocaleString('es-ES', { minimumFractionDigits: 2 }) + ' €';

export async function ordersView(main) {
  const [rows, clients] = await Promise.all([loadRows('order'), fkOptions('client')]);
  const clientName = Object.fromEntries(clients.map(o => [o.value, o.label]));
  // Solo pedidos de venta (los de devolución tienen total < 0)
  const orders = rows.filter(r => Number(dig(r.payload, 'totals.total.value') ?? 0) >= 0)
    .sort((a, b) => String(dig(b.payload, 'orderedDate') || '').localeCompare(String(dig(a.payload, 'orderedDate') || '')));

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Ventas</p>
        <h1 class="title">Pedidos</h1>
        <p class="lead">Los pedidos que hacen los clientes en el portal. Aquí gestionas su estado.</p>
      </div>
    </div>
    <div class="mng-tools">
      <div class="mng-search">${icons.search(16)}<input type="search" id="q" placeholder="Buscar por nº o cliente…"></div>
      <span class="spacer"></span><span class="mng-count" id="count"></span>
    </div>
    <div class="grid-scroll"><table class="grid">
      <thead><tr><th>Nº</th><th>Cliente</th><th>Fecha</th><th>Importe</th><th>Estado</th><th class="grid-actions"></th></tr></thead>
      <tbody id="rows"></tbody>
    </table></div>`;

  const q = main.querySelector('#q'), tbody = main.querySelector('#rows'), count = main.querySelector('#count');
  const paint = () => {
    const term = (q.value || '').toLowerCase().trim();
    const shown = orders.filter(r => {
      const num = String(dig(r.payload, 'externalReference') || r.id).toLowerCase();
      const cli = String(clientName[dig(r.payload, 'clientId')] || '').toLowerCase();
      return !term || num.includes(term) || cli.includes(term);
    });
    count.textContent = `${shown.length} ${shown.length === 1 ? 'pedido' : 'pedidos'}`;
    tbody.innerHTML = shown.length ? shown.map((r, i) => {
      const st = String(dig(r.payload, 'status') || 'open');
      return `<tr class="row-link" data-i="${i}">
        <td class="grid-link">${esc(dig(r.payload, 'externalReference') || r.id.slice(0, 8))}</td>
        <td>${esc(clientName[dig(r.payload, 'clientId')] || '—')}</td>
        <td>${esc(String(dig(r.payload, 'orderedDate') || '').slice(0, 10) || '—')}</td>
        <td>${money(dig(r.payload, 'totals.total.value'))}</td>
        <td><span class="grid-chip ${statusClass(st)}">${esc(statusLabel(st))}</span></td>
        <td class="grid-actions">${icons.right(16)}</td></tr>`;
    }).join('') : `<tr class="grid-empty"><td colspan="6">${term ? 'Sin resultados.' : 'Todavía no hay pedidos en el portal.'}</td></tr>`;
    tbody.querySelectorAll('tr[data-i]').forEach(tr => tr.onclick = () => go(`#/orders/${encodeURIComponent(shown[tr.dataset.i].id)}`));
  };
  q.oninput = paint; paint();
}

export async function orderView(main, id) {
  let p = {};
  try { p = JSON.parse((await api.doc('order', id)).payload); } catch { p = {}; }
  const clients = await fkOptions('client');
  const clientName = Object.fromEntries(clients.map(o => [o.value, o.label]));
  const items = Array.isArray(p.items) ? p.items : [];
  const cur = String(p.status || 'open');

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs"><a href="#/orders">Pedidos</a> · <span>${esc(p.externalReference || id.slice(0, 8))}</span></p>
        <h1 class="title">Pedido ${esc(p.externalReference || '')}</h1>
        <p class="lead">${esc(clientName[p.clientId] || 'Cliente')} · origen ${esc(p.source === 'portal' ? 'portal' : 'ERP')}</p>
      </div>
    </div>

    <dl class="mng-order-head">
      <div class="mng-fact"><dt>Fecha</dt><dd>${esc(String(p.orderedDate || '').slice(0, 10) || '—')}</dd></div>
      <div class="mng-fact"><dt>Ref. cliente</dt><dd>${esc(p.purchaseOrderId || p.reference || '—')}</dd></div>
      <div class="mng-fact"><dt>Forma de pago</dt><dd>${esc(p.payMethodId || '—')}</dd></div>
      <div class="mng-fact"><dt>Total</dt><dd>${money(dig(p, 'totals.total.value'))}</dd></div>
    </dl>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.truck(20)}Estado del pedido</h2></header>
      <div class="biz-card">
        <div class="mng-status-pills" id="pills">${STATUSES.map(([v, l]) =>
          `<button data-s="${v}" class="${v === cur ? 'on' : ''}">${esc(l)}</button>`).join('')}</div>
        ${p.observations ? `<p class="acc-hint" style="margin-top:1rem">Observaciones: ${esc(p.observations)}</p>` : ''}
        ${p.shippingAddress ? `<p class="acc-hint">Envío a: ${esc(addressLine(p.shippingAddress))}</p>` : ''}
      </div>
    </section>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.cart(20)}Líneas</h2></header>
      <div class="grid-scroll"><table class="grid">
        <thead><tr><th>Artículo</th><th>SKU</th><th>Uds</th><th>Precio</th><th>Importe</th></tr></thead>
        <tbody>${items.length ? items.map(it => {
          const info = it?.transactionInfo?.info || {};
          return `<tr><td>${esc(dig(it, 'productName.es_ES') || dig(it, 'productInfo.name.es_ES') || '—')}</td>
            <td>${esc(dig(it, 'productInfo.sku') || '—')}</td>
            <td>${esc(info.quantity ?? '')}</td>
            <td>${money(info.price?.value)}</td>
            <td>${money(info.amount?.value)}</td></tr>`;
        }).join('') : '<tr class="grid-empty"><td colspan="5">Sin líneas.</td></tr>'}</tbody>
      </table></div>
    </section>

    <div class="acc-actions nc-actions">
      <button type="button" class="btn-danger" id="del">Eliminar pedido</button>
      <a class="btn-ghost" href="#/orders">Volver</a>
    </div>`;

  main.querySelectorAll('#pills button').forEach(btn => btn.onclick = async () => {
    try {
      await api.orderStatus(id, btn.dataset.s);
      main.querySelectorAll('#pills button').forEach(b => b.classList.toggle('on', b === btn));
      flash(`Estado: ${statusLabel(btn.dataset.s)}.`);
    } catch (e) { flash(e.body?.error || e.message, 'err'); }
  });
  main.querySelector('#del').onclick = async () => {
    if (!confirm('¿Eliminar este pedido? Desaparecerá del portal.')) return;
    try { await api.delOrder(id); flash('Pedido eliminado.'); go('#/orders'); }
    catch (e) { flash(e.body?.error || e.message, 'err'); }
  };
}

function addressLine(a) {
  return [a.streetAddress && `${a.streetAddress}${a.num ? ' ' + a.num : ''}`, a.zipCode, a.city, a.province]
    .filter(Boolean).join(', ');
}
