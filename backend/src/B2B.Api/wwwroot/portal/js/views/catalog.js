// Vista 4 — /{market}/{lang}/catalog/catalog (17-catalog-catalog.png).
// El catálogo comprable con la matriz de tallas sigue vivo y probado en /shop
// hasta que esta vista supere sus criterios: la decisión de arquitectura del plan
// (§2) es no duplicarlo a mano, sino extraerlo a ui/size-matrix.js en la Fase 2.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { pageHead } from '../ui/chrome.js';

export default function catalog(host) {
  host.innerHTML = `
    <div class="page">
      ${pageHead(t('nav.catalog'), [t('nav.catalog')])}
      <div class="panel">
        <b>${esc(t('catalog.soonTitle'))}</b>
        ${esc(t('catalog.soonBody'))}
        <span class="tag">${esc(t('stub.phase', { n: 2 }))}</span>
        <br><a class="cta" href="/shop.html">${esc(t('catalog.openShop'))}</a>
      </div>
    </div>`;
}
