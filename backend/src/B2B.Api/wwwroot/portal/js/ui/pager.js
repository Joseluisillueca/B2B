// Paginación del portal actual: «‹ 1 ›» centrado y el selector "Mostrar 12".
// Lo comparten el catálogo y los listados de las fases siguientes.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { icons } from './icons.js';

export const PAGE_SIZES = [12, 24, 48, 96];

/**
 * `size: false` deja solo «‹ 1 ›» centrado, sin el selector "Mostrar": es lo que
 * pide la vista de carritos favoritos (M9), la única del inventario que va sin
 * buscador y sin "Mostrar".
 */
export function pager({ total, skip, take, size = true }) {
  const pages = Math.max(1, Math.ceil(total / take));
  const current = Math.floor(skip / take) + 1;
  const numbers = pageWindow(current, pages);

  return `
    <nav class="pager" aria-label="${esc(t('pager.label'))}">
      <button type="button" class="pg-arrow" data-page="${current - 1}" ${current <= 1 ? 'disabled' : ''}
        aria-label="${esc(t('pager.prev'))}">${icons.left(16)}</button>
      ${numbers.map(page => page === '…'
        ? '<span class="pg-gap">…</span>'
        : `<button type="button" class="pg-num${page === current ? ' on' : ''}" data-page="${page}"
             ${page === current ? 'aria-current="page"' : ''}>${page}</button>`).join('')}
      <button type="button" class="pg-arrow" data-page="${current + 1}" ${current >= pages ? 'disabled' : ''}
        aria-label="${esc(t('pager.next'))}">${icons.right(16)}</button>

      ${size ? `
        <label class="pg-size">
          <span class="sr-only">${esc(t('pager.show'))}</span>
          <select id="pageSize" aria-label="${esc(t('pager.show'))}">
            ${PAGE_SIZES.map(option => `<option value="${option}"${option === take ? ' selected' : ''}>
              ${esc(t('pager.showN', { n: option }))}</option>`).join('')}
          </select>
        </label>` : ''}
    </nav>`;
}

/** Ventana de números alrededor del actual: 1 … 4 5 6 … 20 */
function pageWindow(current, pages) {
  if (pages <= 7) return Array.from({ length: pages }, (_, i) => i + 1);

  const around = [current - 1, current, current + 1].filter(p => p > 1 && p < pages);
  const list = [1, ...around, pages];
  const out = [];
  for (const page of list) {
    if (out.length && page - out.at(-1) > 1) out.push('…');
    out.push(page);
  }
  return out;
}

/** Conecta los botones y el selector con el repintado de la vista */
export function bindPager(root, { take, onPage, onSize }) {
  root.querySelectorAll('[data-page]').forEach(button => {
    button.onclick = () => onPage((Number(button.dataset.page) - 1) * take);
  });
  const size = root.querySelector('#pageSize');
  if (size) size.onchange = () => onSize(Number(size.value));
}
