// Vista 9 — /{market}/{lang}/invoices (05-invoices.png).
//
// Rail de cinco estados (Vencida rojo, Cobradas verde, Parcial amarillo, A Crédito
// azul, Pendiente De… naranja), toolbar con "Ordenar por" en lugar de "Fechas" y
// la tabla Nº DE FACTURA · FECHA · FORMA DE PAGO · IMPORTE · DEUDA PENDIENTE · ESTADO.
//
// Parcial y A Crédito salen del rail de la referencia pero el contrato de BC solo
// distingue Paid/Unpaid (contrato 05 §4): hoy siempre cuentan 0. Se dejan a la
// vista para no romper la paridad y porque el mapeo ya está listo para el día que
// BC mande cobros parciales.

import { t } from '../i18n.js';
import { esc, eur, date } from '../format.js';
import { docList, linesTable } from '../ui/doc-list.js';
import { statusChip } from '../ui/status-rail.js';
import { modalFacts } from '../ui/modal.js';

const STATUSES = [
  { id: 'overdue', tone: 'red' },
  { id: 'paid', tone: 'green' },
  { id: 'partial', tone: 'amber' },
  { id: 'credit', tone: 'blue' },
  { id: 'pending', tone: 'orange' }
];

const TONE = Object.fromEntries(STATUSES.map(status => [status.id, status.tone]));

const SORTS = ['date-desc', 'date-asc', 'amount-desc', 'amount-asc', 'debt-desc', 'number-asc'];

const chip = invoice => statusChip(t(`invoices.status.${invoice.status}`), TONE[invoice.status] || 'none');

export default async function invoices(host) {
  await docList(host, {
    key: 'invoices',
    endpoint: '/api/portal/invoices',
    crumb: t('nav.invoices'),
    statuses: STATUSES,
    sorts: SORTS,

    columns: [
      { label: t('invoices.col.number') },
      { label: t('invoices.col.date') },
      { label: t('invoices.col.payMethod') },
      { label: t('invoices.col.amount'), className: 'num' },
      { label: t('invoices.col.debt'), className: 'num' },
      { label: t('invoices.col.status') }
    ],

    cells: invoice => [
      `<button type="button" class="grid-link" data-open="${esc(invoice.id)}">${esc(invoice.number)}</button>`,
      esc(date(invoice.date)),
      esc(invoice.payMethod || ''),
      esc(eur(invoice.total)),
      // La deuda de una factura cobrada es 0: se deja en blanco para que salte a
      // la vista la fila que sí debe algo
      invoice.debt ? `<b>${esc(eur(invoice.debt))}</b>` : '',
      chip(invoice)
    ],

    detail: invoice => ({
      title: t('invoices.detail', { n: invoice.number }),
      subtitle: date(invoice.date),
      body: `
        ${modalFacts([
          [t('invoices.col.payMethod'), esc(invoice.payMethod || '—')],
          [t('invoices.col.status'), chip(invoice)],
          invoice.dueDate ? [t('invoices.dueDate'), esc(date(invoice.dueDate))] : null,
          [t('invoices.col.debt'), esc(eur(invoice.debt))],
          invoice.fiscalName ? [t('invoices.fiscalName'), esc(invoice.fiscalName)] : null
        ])}
        ${linesTable(invoice.lines)}
        <dl class="doc-totals">
          <div><dt>${esc(t('checkout.subtotal'))}</dt><dd>${esc(eur(invoice.totals?.amount))}</dd></div>
          <div><dt>${esc(t('docs.tax'))}</dt><dd>${esc(eur(invoice.totals?.tax))}</dd></div>
          <div class="grand"><dt>${esc(t('checkout.totalGross'))}</dt><dd>${esc(eur(invoice.total))}</dd></div>
        </dl>`
    })
  });
}
