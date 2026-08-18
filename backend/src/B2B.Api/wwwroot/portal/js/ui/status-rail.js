// Rail de estados de los listados de documentos (03-orders.png, 04-delivery-notes.png,
// 05-invoices.png): una columna de círculos de color con su etiqueta, el activo
// enmarcado en azul. El punto es el mismo color que el chip de la columna ESTADO,
// así que la leyenda y la tabla se leen juntas.
//
// El color nunca va solo: cada fila lleva su etiqueta escrita, así que el rail
// sigue siendo legible sin distinguir colores. La referencia NO muestra el
// recuento de documentos por estado (m1), de modo que `count` se recibe pero no
// se pinta: la etiqueta ocupa toda la fila y ya no se recorta ("Pendiente De…"
// completo en facturas, m14).

import { esc } from '../format.js';

/**
 * items: [{ id, label, tone, count }] — el primero suele ser "Todos" (tone 'none')
 * active: id del estado elegido ('' = todos)
 */
export function statusRail({ label, items, active = '' }) {
  return `
    <nav class="srail" aria-label="${esc(label)}">
      ${items.map(item => `
        <button type="button" class="srail-item${item.id === active ? ' on' : ''}"
          data-status="${esc(item.id)}"${item.id === active ? ' aria-current="true"' : ''}>
          <i class="srail-dot tone-${esc(item.tone)}" aria-hidden="true"></i>
          <span class="srail-label">${esc(item.label)}</span>
        </button>`).join('')}
    </nav>`;
}

export function bindStatusRail(root, onPick) {
  root.querySelectorAll('[data-status]').forEach(button => {
    button.onclick = () => onPick(button.dataset.status);
  });
}

/** Chip de la columna ESTADO, del mismo tono que el punto del rail */
export const statusChip = (label, tone) =>
  `<span class="chip tone-${esc(tone)}">${esc(label)}</span>`;
