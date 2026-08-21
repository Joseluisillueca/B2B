// "Próximamente" de las vistas de agente que aún no tienen contenido propio
// (pedidos de selección, calendario de citas, alta/edición de cliente). Comparten
// el chrome de agente y sus migas; el contenido llega en la Fase 2.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { pageHead } from '../ui/chrome.js';

// Cada ruta placeholder rotula su propia sección para que las migas y el H1 no
// mientan ("Calendario de citas" en /agent/calendar, etc.).
const TITLES = {
  'agent/model-selection': 'nav.model-selection',
  'agent/calendar': 'nav.calendar',
  'agent/clients/new': 'clients.new',
  'agent/clients/edit': 'clients.modal.edit',
  'clients/requests': 'clients.requests'
};

export default function agentSoon(host, route) {
  const title = t(TITLES[route.view] || 'soon.title');
  host.innerHTML = `
    <div class="page">
      ${pageHead(title, [title])}
      <div class="panel">
        <b>${esc(t('soon.title'))}</b>
        ${esc(t('soon.body'))}
        <span class="tag">${esc(t('soon.tag'))}</span>
      </div>
    </div>`;
}
