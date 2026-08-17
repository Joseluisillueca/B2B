// Modal de detalle de documento (líneas de un pedido, de un albarán o de una
// factura). Es el hermano ancho de ui/dialog.js: aquel pregunta, este enseña.
//
// <dialog> nativo, así que el velo, Esc, la trampa de foco y la devolución del
// foco al botón que lo abrió vienen de serie.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { icons } from './icons.js';

/**
 * body llega como HTML ya escapado por quien lo construye.
 * Devuelve el <dialog> por si la vista quiere cerrarlo desde fuera.
 */
export function openModal({ title, subtitle = '', body }) {
  const dialog = document.createElement('dialog');
  dialog.className = 'dlg dlg-wide';
  dialog.innerHTML = `
    <div class="panel doc-modal">
      <header class="doc-modal-head">
        <div>
          <h2>${esc(title)}</h2>
          ${subtitle ? `<p>${esc(subtitle)}</p>` : ''}
        </div>
        <button type="button" class="doc-modal-close" aria-label="${esc(t('docs.close'))}"
          title="${esc(t('docs.close'))}">${icons.close(18)}</button>
      </header>
      <div class="doc-modal-body">${body}</div>
    </div>`;

  document.body.append(dialog);
  dialog.addEventListener('close', () => dialog.remove());
  dialog.querySelector('.doc-modal-close').onclick = () => dialog.close();
  dialog.showModal();
  return dialog;
}

/** Ficha de datos del documento: pares etiqueta/valor sobre la tabla de líneas */
export const modalFacts = facts => `
  <dl class="doc-facts">
    ${facts.filter(fact => fact).map(([label, value]) => `
      <div><dt>${esc(label)}</dt><dd>${value}</dd></div>`).join('')}
  </dl>`;
