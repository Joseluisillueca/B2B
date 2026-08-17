// Vista 3 — /{market}/{lang}/dashboard (01-dashboard.png).
// Fase 0: el H1 traducido (Haz tu pedido / Place your order / Passez votre
// commande / Effettua il tuo ordine). El carrusel y las dos tarjetas-imagen
// Reposición y Programación llegan con el CMS en la Fase 1.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { href } from '../router.js';

export default function dashboard(host) {
  host.innerHTML = `
    <div class="page">
      <h1 class="title">${esc(t('dashboard.title'))}</h1>
      <div class="panel">
        <b>${esc(t('dashboard.soonTitle'))}</b>
        ${esc(t('dashboard.soonBody'))}
        <span class="tag">${esc(t('stub.phase', { n: 1 }))}</span>
        <br><a class="cta" href="${href('catalog/catalog')}">${esc(t('nav.catalog'))}</a>
      </div>
    </div>`;
}
