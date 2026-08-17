// Carrito lateral del portal. Es la misma lista que se ve luego en el checkout:
// una línea por talla, agrupadas por artículo, con el total de la ventana activa.

import { t } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state, lineKey } from '../state.js';
import { href, go } from '../router.js';
import { icons } from './icons.js';

/** Agrupa las líneas por modelo — así se lee igual en el drawer y en el checkout */
export function groupLines(lines) {
  const groups = new Map();
  for (const line of lines) {
    const group = groups.get(line.modelId) ?? {
      modelId: line.modelId, name: line.name, reference: line.reference, lines: [], units: 0, total: 0
    };
    group.lines.push(line);
    group.units += Number(line.qty) || 0;
    group.total += (Number(line.qty) || 0) * (Number(line.price) || 0);
    groups.set(line.modelId, group);
  }
  for (const group of groups.values())
    group.lines.sort((a, b) => sizeOrder(a.size) - sizeOrder(b.size));
  return [...groups.values()];
}

const sizeOrder = size => {
  const number = Number(size);
  return Number.isFinite(number) ? number : Number.MAX_SAFE_INTEGER;
};

export function cartBody() {
  const lines = state.cartLines();
  if (!lines.length)
    return `<div class="cart-empty">${esc(t('cart.empty'))}</div>`;

  return `<div class="cart-groups">${groupLines(lines).map(group => `
    <section class="cart-group">
      <h3>${esc(group.name || '')}</h3>
      <p class="cart-ref">${esc(t('catalog.reference'))} ${esc(group.reference || '')}</p>
      ${group.lines.map(line => `
        <div class="cart-line">
          <span class="cart-size">${esc(line.size ?? '')}</span>
          <span class="cart-qty">${esc(String(line.qty))} × ${esc(eur(line.price))}</span>
          <span class="cart-amount">${esc(eur((Number(line.qty) || 0) * (Number(line.price) || 0)))}</span>
          <button type="button" class="cart-drop" data-key="${esc(lineKey(line))}"
            aria-label="${esc(t('cart.remove'))}">${icons.close(13)}</button>
        </div>`).join('')}
    </section>`).join('')}</div>`;
}

/** Pinta el cuerpo y el pie del drawer que ya tiene el cascarón */
export function paintCartBody(drawer, { onClose } = {}) {
  const body = drawer.querySelector('.body');
  const foot = drawer.querySelector('.foot');
  if (!body || !foot) return;

  const units = state.cartUnits();
  body.innerHTML = cartBody();
  body.classList.toggle('is-empty', units === 0);

  foot.innerHTML = `
    <div class="cart-total">
      <span>${esc(t('cart.total'))}</span><b>${esc(eur(state.cartTotal()))}</b>
    </div>
    <button type="button" class="btn-primary block" id="cartGo" ${units ? '' : 'disabled'}>
      ${esc(t('cart.checkout'))}
    </button>
    <p class="cart-note">${esc(t('cart.units', { n: units }))}</p>`;

  body.querySelectorAll('.cart-drop').forEach(button => {
    button.onclick = () => state.removeCartLine(button.dataset.key);
  });
  foot.querySelector('#cartGo').onclick = () => {
    onClose?.();
    go(href('checkout'));
  };
}
