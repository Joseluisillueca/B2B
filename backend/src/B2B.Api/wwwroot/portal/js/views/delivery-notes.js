// Vista 8 — /{market}/{lang}/delivery-notes (04-delivery-notes.png).
//
// Rail de dos estados (Facturados verde, No Facturados naranja) y la tabla con
// REFERENCIA · FECHA · FACTURADO · IMPORTE · DIRECCIÓN · ENLACE TRASNPORTE (la
// errata del rótulo es la del portal actual y se respeta). El albarán que trae
// transportUrlTrack abre el seguimiento del transportista en una pestaña nueva;
// sin él la celda queda vacía.

import { t } from '../i18n.js';
import { esc, eur, date } from '../format.js';
import { docList, linesTable } from '../ui/doc-list.js';
import { agentDocList } from '../ui/agent-doc-list.js';
import { statusChip } from '../ui/status-rail.js';
import { modalFacts } from '../ui/modal.js';
import { icons } from '../ui/icons.js';
import { state } from '../state.js';

const STATUSES = [
  { id: 'invoiced', tone: 'green' },
  { id: 'not-invoiced', tone: 'orange' }
];

const invoicedChip = note => statusChip(
  t(note.isInvoiced ? 'docs.yes' : 'docs.no'),
  note.isInvoiced ? 'green' : 'orange');

// El enlace sale del Shipping Agent de BC, así que va a un dominio de tercero:
// rel="noopener" y pestaña nueva para no perder el listado.
const trackLink = note => note.transportUrlTrack
  ? `<a class="doc-track" href="${esc(note.transportUrlTrack)}" target="_blank" rel="noopener noreferrer">
       ${icons.truck(16)}<span>${esc(note.transportId || t('docs.track'))}</span>
     </a>`
  : '';

export default async function deliveryNotes(host) {
  if (state.isAgent && !state.acting) {
    return agentDocList(host, {
      type: 'delivery-note', key: 'delivery-notes', statuses: STATUSES,
      columns: [
        { label: t('delivery-notes.col.number') },
        { label: t('delivery-notes.col.date') },
        { label: t('delivery-notes.col.invoiced') },
        { label: t('delivery-notes.col.amount'), className: 'num' }
      ],
      cells: note => [
        esc(note.number),
        esc(date(note.date)),
        invoicedChip(note),
        esc(eur(note.total))
      ]
    });
  }

  await docList(host, {
    key: 'delivery-notes',
    endpoint: '/api/portal/delivery-notes',
    crumb: t('nav.delivery-notes'),
    statuses: STATUSES,

    columns: [
      { label: t('delivery-notes.col.number') },
      { label: t('delivery-notes.col.date') },
      { label: t('delivery-notes.col.invoiced') },
      { label: t('delivery-notes.col.amount'), className: 'num' },
      { label: t('delivery-notes.col.address'), className: 'grid-hide-sm' },
      { label: t('delivery-notes.col.track') }
    ],

    cells: note => [
      `<button type="button" class="grid-link" data-open="${esc(note.id)}">${esc(note.number)}</button>`,
      esc(date(note.date)),
      invoicedChip(note),
      esc(eur(note.total)),
      esc(note.address || ''),
      trackLink(note)
    ],

    detail: note => ({
      title: t('delivery-notes.detail', { n: note.number }),
      subtitle: date(note.date),
      body: `
        ${modalFacts([
          [t('delivery-notes.col.invoiced'), invoicedChip(note)],
          note.transportId ? [t('delivery-notes.carrier'), esc(note.transportId)] : null,
          note.transportUrlTrack ? [t('delivery-notes.col.track'), trackLink(note)] : null,
          note.address ? [t('delivery-notes.col.address'), esc(note.address)] : null,
          note.observations ? [t('checkout.notes'), esc(note.observations)] : null
        ])}
        ${linesTable(note.lines)}
        <dl class="doc-totals">
          <div><dt>${esc(t('checkout.subtotal'))}</dt><dd>${esc(eur(note.totals?.amount))}</dd></div>
          <div><dt>${esc(t('docs.tax'))}</dt><dd>${esc(eur(note.totals?.tax))}</dd></div>
          <div class="grand"><dt>${esc(t('checkout.totalGross'))}</dt><dd>${esc(eur(note.total))}</dd></div>
        </dl>`
    })
  });
}
