// Toolbar del catálogo (17-catalog-catalog.png): botón "Desc. Stock", selector de
// vista Listado/Grid y "Ordenar por". El Grid queda declarado pero deshabilitado:
// la Fase 2 del plan entrega solo el modo Listado.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { icons } from './icons.js';

export const SORTS = ['featured', 'relevance', 'price-asc', 'price-desc', 'name'];

export function toolbar({ sort, view = 'list' } = {}) {
  return `
    <div class="toolbar">
      <button type="button" class="tb-export" id="exportStock">
        ${icons.download(17)} ${esc(t('catalog.stockExport'))}
      </button>

      <!-- Sin Grid el selector no tiene nada que elegir: se deshabilita entero (y
           el title dice por qué) en vez de ofrecer una lista con una sola opción
           viva. Cuando llegue el Grid basta con quitar disabled y el title. -->
      <label class="tb-field" title="${esc(t('catalog.viewOnlyList'))}">
        <span class="tb-legend">${esc(t('catalog.view'))}</span>
        <span class="tb-select">
          ${icons.list(16)}
          <select id="viewMode" aria-label="${esc(t('catalog.view'))}" disabled
            title="${esc(t('catalog.viewOnlyList'))}">
            <option value="list"${view === 'list' ? ' selected' : ''}>${esc(t('catalog.viewList'))}</option>
            <option value="grid">${esc(t('catalog.viewGrid'))}</option>
          </select>
        </span>
      </label>

      <label class="tb-field">
        <span class="tb-legend">${esc(t('catalog.sortBy'))}</span>
        <span class="tb-select">
          <select id="sortMode" aria-label="${esc(t('catalog.sortBy'))}">
            ${SORTS.map(value => `<option value="${value}"${value === sort ? ' selected' : ''}>
              ${esc(t(`catalog.sort.${value}`))}</option>`).join('')}
          </select>
        </span>
      </label>
    </div>`;
}
