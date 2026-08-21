// Pedidos de selección — /agent/model-selection (Fase 3). En el portal real es el
// listado de pedidos de programación (temporada) de la cartera. Reutiliza la tabla
// agregada del agente filtrada a SCHEDULED, y ofrece "Crear pedido de selección":
// el agente elige un cliente en /clients y arranca su Pedido de programación.

import { t } from '../i18n.js';
import { esc, eur, date } from '../format.js';
import { agentDocList } from '../ui/agent-doc-list.js';
import { statusChip } from '../ui/status-rail.js';
import { icons } from '../ui/icons.js';
import { href } from '../router.js';

const STATUSES = [
  { id: 'invoiced', tone: 'blue' },
  { id: 'partially-shipped', tone: 'lime' },
  { id: 'shipped', tone: 'amber' },
  { id: 'canceled', tone: 'red' },
  { id: 'open', tone: 'green' }
];

export default async function modelSelection(host) {
  const intro = `
    <div class="ms-intro">
      <p>${esc(t('selection.lead'))}</p>
      <a class="btn-primary" href="${href('clients')}">${icons.plus(15)} ${esc(t('selection.create'))}</a>
    </div>`;

  return agentDocList(host, {
    type: 'order', key: 'orders', orderType: 'SCHEDULED', statuses: STATUSES,
    title: t('nav.model-selection'),
    crumbs: [t('clients.crumb'), t('selection.crumb')],
    intro,
    columns: [
      { label: t('orders.col.number') },
      { label: t('orders.col.date') },
      { label: t('orders.col.amount'), className: 'num' },
      { label: t('orders.col.status') }
    ],
    cells: order => [
      esc(order.number),
      esc(date(order.date)),
      esc(eur(order.total)),
      statusChip(t(`orders.status.${order.status}`), TONE[order.status] || 'none')
    ]
  });
}

const TONE = Object.fromEntries(STATUSES.map(s => [s.id, s.tone]));
