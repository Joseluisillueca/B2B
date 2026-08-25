// Toolbar del catálogo (17-catalog-catalog.png): botón "Desc. Stock", selector de
// vista Listado/Cuadrícula y "Ordenar por". Ambos modos pintan el mismo catálogo:
// Listado deja la matriz de tallas en línea, Cuadrícula muestra tarjetas con foto
// grande que enlazan a la ficha del producto.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { icons } from './icons.js';

export const SORTS = ['featured', 'relevance', 'price-asc', 'price-desc', 'name'];

export function toolbar({ sort, view = 'list' } = {}) {
  // El icono acompaña al modo activo: filas para Listado, cuadrícula para Grid
  const viewIcon = view === 'grid' ? icons.grid(16) : icons.list(16);
  return `
    <div class="toolbar">
      <button type="button" class="tb-export" id="exportStock">
        ${icons.download(17)} ${esc(t('catalog.stockExport'))}
      </button>
      <button type="button" class="tb-export" id="exportPdf">
        ${icons.fileDown(17)} ${esc(t('catalog.pdfExport'))}
      </button>

      <!-- Conmutador de vista segmentado (Modernist): activo en rojo -->
      <div class="tb-seg" role="group" aria-label="${esc(t('catalog.view'))}">
        <button type="button" class="tb-seg-opt${view === 'grid' ? '' : ' on'}" data-view="list"
          aria-pressed="${view === 'grid' ? 'false' : 'true'}">${icons.list(15)} ${esc(t('catalog.viewList'))}</button>
        <button type="button" class="tb-seg-opt${view === 'grid' ? ' on' : ''}" data-view="grid"
          aria-pressed="${view === 'grid' ? 'true' : 'false'}">${icons.grid(15)} ${esc(t('catalog.viewGrid'))}</button>
      </div>

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
