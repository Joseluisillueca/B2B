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

      <!-- m8: la referencia no rotula este selector; el aria-label sigue nombrándolo -->
      <label class="tb-field">
        <span class="tb-select">
          ${viewIcon}
          <select id="viewMode" aria-label="${esc(t('catalog.view'))}">
            <option value="list"${view === 'list' ? ' selected' : ''}>${esc(t('catalog.viewList'))}</option>
            <option value="grid"${view === 'grid' ? ' selected' : ''}>${esc(t('catalog.viewGrid'))}</option>
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
