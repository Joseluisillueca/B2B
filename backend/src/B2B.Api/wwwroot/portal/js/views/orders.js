// Vista 7 — /{market}/{lang}/orders (03-orders.png).
//
// Migas "Inicio / Mis pedidos", H1 "Pedidos", rail de estados con sus círculos de
// color (Facturado azul, Envío Parcial lima, Enviado amarillo, Cancelado rojo,
// Abierto verde), toolbar Buscar/Temporada/Fechas/EXPORTAR PEDIDOS y la tabla con
// las 7 columnas literales. El nº de pedido abre el detalle con sus líneas.

import { t } from '../i18n.js';
import { esc, eur, num, date } from '../format.js';
import { docList, linesTable } from '../ui/doc-list.js';
import { statusChip } from '../ui/status-rail.js';
import { modalFacts } from '../ui/modal.js';

// Orden y colores del rail, tal cual la captura
const STATUSES = [
  { id: 'invoiced', tone: 'blue' },
  { id: 'partially-shipped', tone: 'lime' },
  { id: 'shipped', tone: 'amber' },
  { id: 'canceled', tone: 'red' },
  { id: 'open', tone: 'green' }
];

const TONE = Object.fromEntries(STATUSES.map(status => [status.id, status.tone]));

const statusLabel = status => t(`orders.status.${status}`);

// BC manda siempre el tipo del pedido en su vocabulario (SCHEDULED); el cliente
// lo conoce por el nombre de la ventana de servicio con la que compra.
const typeLabel = type => ({
  SCHEDULED: t('window.scheduled'),
  REPLENISHMENT: t('window.replenishment')
}[type] || type || '');

export default async function orders(host) {
  await docList(host, {
    key: 'orders',
    endpoint: '/api/portal/orders',
    crumb: t('orders.crumb'),
    statuses: STATUSES,

    columns: [
      { label: t('orders.col.number') },
      { label: t('orders.col.date') },
      { label: t('orders.col.reference') },
      { label: t('orders.col.type') },
      { label: t('orders.col.units'), className: 'num' },
      { label: t('orders.col.amount'), className: 'num' },
      { label: t('orders.col.status') }
    ],

    cells: order => [
      `<button type="button" class="grid-link" data-open="${esc(order.id)}">${esc(order.number)}</button>`,
      esc(date(order.date)),
      esc(order.reference || ''),
      esc(typeLabel(order.type)),
      esc(num(order.units)),
      esc(eur(order.total)),
      statusChip(statusLabel(order.status), TONE[order.status] || 'none')
    ],

    detail: order => ({
      title: t('orders.detail', { n: order.number }),
      subtitle: date(order.date),
      body: `
        ${modalFacts([
          [t('orders.col.reference'), esc(order.reference || '—')],
          [t('orders.col.type'), esc(typeLabel(order.type))],
          [t('orders.col.status'), statusChip(statusLabel(order.status), TONE[order.status] || 'none')],
          [t('orders.col.units'), esc(num(order.units))],
          order.shippingAddress ? [t('checkout.shipTo'), esc(order.shippingAddress)] : null,
          order.observations ? [t('checkout.notes'), esc(order.observations)] : null
        ])}
        ${linesTable(order.lines, { delivered: true })}
        ${totals(order.totals, order.total)}`
    })
  });
}

const totals = (partial, total) => `
  <dl class="doc-totals">
    <div><dt>${esc(t('checkout.subtotal'))}</dt><dd>${esc(eur(partial?.amount))}</dd></div>
    <div><dt>${esc(t('docs.tax'))}</dt><dd>${esc(eur(partial?.tax))}</dd></div>
    <div class="grand"><dt>${esc(t('checkout.totalGross'))}</dt><dd>${esc(eur(total))}</dd></div>
  </dl>`;
