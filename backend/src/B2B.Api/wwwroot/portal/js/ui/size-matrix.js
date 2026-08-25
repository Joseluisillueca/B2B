// Matriz de tallas del listado (17-catalog-catalog.png): el pedido se hace EN el
// catálogo, no en una ficha aparte. Extraída de shop.html y completada con lo que
// pide la Fase 2: precio por talla, tope (+99), ∞ de ventana programada y celda
// bloqueada cuando no hay stock.

import { esc, eur } from '../format.js';
import { t } from '../i18n.js';
import { href } from '../router.js';
import { state, lineKey } from '../state.js';

/** Sentinela del conector (contrato 03 §7.7): ventana programada abierta */
export const INFINITE = 10000;
/** Tope de la referencia: por encima la celda dice (+99) */
export const CAP = 99;
const LOW = 10;

export const stockOf = (product, windowKey) =>
  Number(product?.stock?.[windowKey] ?? product?.stock?.[String(windowKey).toLowerCase()] ?? 0);

/** ok | low | out | inf — decide el punto del semáforo y si la celda admite cantidad */
export function stockState(stock) {
  if (stock >= INFINITE) return 'inf';
  if (stock <= 0) return 'out';
  if (stock < LOW) return 'low';
  return 'ok';
}

export const stockText = stock =>
  stock >= INFINITE ? '∞' : stock > CAP ? `+${CAP}` : String(stock);

/** Estado del artículo entero, para el orden y el filtro de disponibilidad */
export const rowState = (item, windowKey) => {
  const levels = (item.products || []).map(p => stockOf(p, windowKey));
  if (levels.some(s => s >= LOW)) return 'ok';
  if (levels.some(s => s > 0)) return 'low';
  return 'out';
};

/**
 * Matriz de un artículo. Las cantidades salen del carrito de la ventana activa, así
 * que al volver del checkout las celdas siguen mostrando lo que ya se pidió.
 */
export function sizeMatrix(item, { windowKey, lines = {} } = {}) {
  // Tabulador rodante: la matriz entera es UNA parada de teclado (la primera celda
  // con stock) y dentro se navega con las flechas. Con 15 tallas por artículo y 24
  // artículos por página, lo contrario son cientos de tabuladores hasta el pie.
  let stop = false;
  // F-04: en los artículos con precio por talla (kids 21–35) la cabecera lleva el
  // precio EFECTIVO de cada talla —el propio si lo tiene, si no el del artículo—,
  // no solo el de las tallas con oferta propia: si tres tallas de quince mostraban
  // importe, la columna parecía un error de datos.
  const perSize = !!item.pricePerSize;

  const cells = (item.products || []).map(product => {
    const stock = stockOf(product, windowKey);
    const status = stockState(stock);
    const line = lines[lineKey({ modelId: item.modelId, size: product.size })];
    const qty = Number(line?.qty) || 0;
    const price = product.pvd ?? item.pvd;
    const roving = status !== 'out' && !stop;
    if (roving) stop = true;

    return `
      <div class="sz-cell">
        ${perSize && price != null ? `<span class="sz-price-top" title="${esc(eur(price))}">${esc(eur(price).replace(/\s*€/, ''))}</span>` : ''}
        <div class="sz ${status}${qty > 0 ? ' has' : ''}">
          <div class="sz-head">
            <span class="sz-size">${esc(product.size ?? '—')}</span>
          </div>
          <div class="sz-body">
            <input type="number" min="0" max="9999" step="1" inputmode="numeric"
              class="sz-qty" value="${qty}" ${status === 'out' ? 'disabled' : ''}
              tabindex="${roving ? '0' : '-1'}"
              data-model="${esc(item.modelId)}" data-product="${esc(product.productId)}"
              data-size="${esc(product.size ?? '')}" data-price="${price ?? 0}"
              aria-label="${esc(t('catalog.sizeField', {
                size: product.size ?? '', name: item.name || '', stock: stockText(stock)
              }))}">
          </div>
        </div>
        <span class="sz-stock">${status === 'low' || status === 'out' ? '<i></i>' : ''}${esc(stockText(stock))}</span>
      </div>`;
  }).join('');

  // F-02: un artículo sin stock en ninguna talla no se pide, se CONSULTA. La
  // referencia ofrece la palabra en la fila; sin ella el usuario solo veía campos
  // apagados a 0 y ninguna indicación de qué hacer. El enlace lleva al formulario
  // de contacto con la referencia del artículo ya puesta en el asunto.
  const consult = rowState(item, windowKey) === 'out'
    ? `<p class="matrix-consult">
         <a href="${esc(href('contact'))}?ref=${encodeURIComponent(item.reference || '')}">
           ${esc(t('catalog.availability.consult'))}</a>
       </p>`
    : '';

  return `<div class="matrix${perSize ? ' wide' : ''}">${cells}</div>${consult}`;
}

/**
 * Un solo listener para toda la lista: cada cambio de cantidad actualiza el carrito
 * de la ventana activa y repinta la celda sin volver a pedir el catálogo.
 */
export function bindMatrix(root, itemsById, { onChange } = {}) {
  const apply = input => {
    const item = itemsById[input.dataset.model];
    if (!item) return;

    const qty = Math.max(0, Math.min(9999, Math.trunc(Number(input.value) || 0)));
    input.value = String(qty);
    input.closest('.sz')?.classList.toggle('has', qty > 0);

    state.setCartLine({
      modelId: item.modelId,
      productId: input.dataset.product,
      size: input.dataset.size,
      name: item.name,
      reference: item.reference,
      qty,
      price: Number(input.dataset.price) || 0
    });   // el carrito se guarda por ventana ACTIVA (reposición/programación), no por id de ventana

    onChange?.();
  };

  root.addEventListener('change', event => {
    if (event.target.classList.contains('sz-qty')) apply(event.target);
  });

  // Celdas navegables de una matriz, en orden de documento (las agotadas están
  // deshabilitadas y no reciben foco)
  const cellsOf = input =>
    [...(input.closest('.matrix')?.querySelectorAll('.sz-qty:not([disabled])') || [])];

  /** Columnas reales que ha resuelto el auto-fill del grid (cambian con el ancho) */
  const columnsOf = matrix =>
    Math.max(1, getComputedStyle(matrix).gridTemplateColumns.split(/\s+/).filter(Boolean).length);

  const MOVES = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: 'up', ArrowDown: 'down' };

  root.addEventListener('keydown', event => {
    const input = event.target;
    if (!input.classList?.contains('sz-qty')) return;

    // Enter no dispara change en algunos navegadores móviles
    if (event.key === 'Enter') {
      event.preventDefault();
      apply(input);
      input.blur();
      return;
    }

    const move = MOVES[event.key];
    if (move === undefined || event.altKey || event.ctrlKey || event.metaKey) return;

    const cells = cellsOf(input);
    const index = cells.indexOf(input);
    if (index < 0) return;

    // Arriba/abajo saltan una fila entera: el salto es el nº de columnas que el
    // grid tenga AHORA MISMO, no las 8 de la referencia a ancho completo.
    const step = typeof move === 'number' ? move
      : columnsOf(input.closest('.matrix')) * (move === 'down' ? 1 : -1);
    const next = cells[index + step];
    if (!next) return;

    // Con la cantidad puesta antes de irnos, el carrito no espera al blur
    event.preventDefault();
    apply(input);
    next.focus();
    next.select?.();
  });

  // El tabulador rodante sigue al foco: la matriz conserva UNA parada, y es la
  // última celda visitada (patrón de rejilla de la WAI-ARIA).
  root.addEventListener('focusin', event => {
    const input = event.target;
    if (!input.classList?.contains('sz-qty')) return;
    for (const cell of cellsOf(input)) cell.tabIndex = cell === input ? 0 : -1;
  });
}
