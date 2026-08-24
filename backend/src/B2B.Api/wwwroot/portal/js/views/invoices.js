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

import { t, lang } from '../i18n.js';
import { esc, eur, date } from '../format.js';
import { api } from '../api.js';
import { docList, linesTable } from '../ui/doc-list.js';
import { agentDocList } from '../ui/agent-doc-list.js';
import { statusChip } from '../ui/status-rail.js';
import { modalFacts } from '../ui/modal.js';
import { state } from '../state.js';
import { icons } from '../ui/icons.js';

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
  if (state.isAgent && !state.acting) {
    return agentDocList(host, {
      type: 'invoice', key: 'invoices', statuses: STATUSES,
      columns: [
        { label: t('invoices.col.number') },
        { label: t('invoices.col.date') },
        { label: t('invoices.col.amount'), className: 'num' },
        { label: t('invoices.col.debt'), className: 'num' },
        { label: t('invoices.col.status') }
      ],
      cells: invoice => [
        esc(invoice.number),
        esc(date(invoice.date)),
        esc(eur(invoice.total)),
        invoice.debt ? `<b>${esc(eur(invoice.debt))}</b>` : '',
        chip(invoice)
      ]
    });
  }

  // Facturas ya pagadas con tarjeta desde el portal (registro local, pendiente de
  // conciliación en BC): se marcan para no volver a ofrecer el pago.
  let paidLocal = new Set();
  try {
    const data = await api.payments();
    paidLocal = new Set((data.items || [])
      .filter(p => p.kind === 'invoice' && p.status === 'paid')
      .map(p => String(p.targetId)));
  } catch { /* si falla, simplemente no se marca nada */ }

  // Deuda + acción de pago: si ya se pagó por el portal, un check; si hay deuda, el
  // botón "Pagar con tarjeta"; si no debe nada, en blanco.
  const debtCell = invoice => {
    if (paidLocal.has(String(invoice.id)))
      return `<span class="inv-paid">${icons.check(15)} ${esc(t('invoices.paidCard'))}</span>`;
    if (!invoice.debt) return '';
    return `<span class="inv-debt"><b>${esc(eur(invoice.debt))}</b>
      <button type="button" class="inv-pay" data-pay="${esc(invoice.id)}">
        ${icons.card ? icons.card(14) : ''} ${esc(t('invoices.pay'))}</button></span>`;
  };

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
      debtCell(invoice),
      chip(invoice)
    ],

    // El botón de pago redirige a la pasarela (Stripe/mock) con la URL que da el backend
    onRendered: list => list.querySelectorAll('[data-pay]').forEach(button => {
      button.onclick = async () => {
        button.disabled = true;
        try {
          const r = await api.payInvoice(button.dataset.pay, lang());
          window.location.href = r.url;
        } catch {
          button.disabled = false;
        }
      };
    }),

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
