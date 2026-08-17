// Diálogo modal del portal: confirmar una acción destructiva o pedir un nombre.
//
// Sustituye a confirm()/prompt() del navegador, que ni se traducen ni se visten y
// además bloquean el hilo. El <dialog> nativo ya trae lo importante — velo, Esc,
// trampa de foco y devolución del foco al botón que lo abrió —, así que aquí solo
// van el texto, los botones de marca y la promesa con el resultado.

import { t } from '../i18n.js';
import { esc } from '../format.js';

function run({ title, message = '', field = '', value = '', confirmLabel, cancelLabel, danger = false }) {
  return new Promise(resolve => {
    const dialog = document.createElement('dialog');
    dialog.className = 'dlg';
    // El form method="dialog" cierra con el value del botón pulsado, así que Enter
    // dentro del campo equivale a confirmar sin listener adicional.
    dialog.innerHTML = `
      <form method="dialog" class="panel">
        <h2>${esc(title)}</h2>
        ${message ? `<p>${esc(message)}</p>` : ''}
        ${field ? `
          <label><span>${esc(field)}</span>
            <input type="text" name="value" value="${esc(value)}" maxlength="120">
          </label>` : ''}
        <div class="dlg-actions">
          <button type="submit" value="cancel" class="btn-ghost">${esc(cancelLabel)}</button>
          <button type="submit" value="ok" class="${danger ? 'btn-danger' : 'btn-primary'}">
            ${esc(confirmLabel)}</button>
        </div>
      </form>`;

    document.body.append(dialog);
    const input = dialog.querySelector('input');

    // Esc (y el botón Cancelar) dejan returnValue vacío o "cancel": ambos son "no"
    dialog.addEventListener('close', () => {
      const ok = dialog.returnValue === 'ok';
      dialog.remove();
      resolve(field ? (ok ? input.value : null) : ok);
    });

    dialog.showModal();
    // Con el campo delante se escribe directo; si lo que se pide es confirmar algo
    // destructivo el foco arranca en Cancelar, que es la salida segura.
    if (input) { input.focus(); input.select(); }
    else dialog.querySelector(danger ? '.btn-ghost' : '.btn-primary')?.focus();
  });
}

/** true si el usuario confirma, false si cancela o pulsa Esc */
export const confirmDialog = ({ title, message, danger = true,
  confirmLabel = t('common.confirm'), cancelLabel = t('common.cancel') } = {}) =>
  run({ title, message, danger, confirmLabel, cancelLabel });

/** El texto escrito, o null si cancela o pulsa Esc */
export const promptDialog = ({ title, label, value = '',
  confirmLabel = t('common.confirm'), cancelLabel = t('common.cancel') } = {}) =>
  run({ title, field: label, value, confirmLabel, cancelLabel });
