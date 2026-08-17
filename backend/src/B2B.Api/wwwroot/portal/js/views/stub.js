// Andamio de las vistas que aún no tienen contenido. La Fase 0 solo entrega el
// cascarón navegable: cada vista ya tiene su ruta, sus migas, su H1 traducido y
// el aviso de la fase en la que se implementa.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { pageHead } from '../ui/chrome.js';

export const stub = (view, phase) => function render(host) {
  const title = t(`nav.${view}`);
  host.innerHTML = `
    <div class="page">
      ${pageHead(title, [title])}
      <div class="panel">
        <b>${esc(t('stub.title'))}</b>
        ${esc(t('stub.body'))}
        <span class="tag">${esc(t('stub.phase', { n: phase }))}</span>
      </div>
    </div>`;
};
