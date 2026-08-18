// Vista 6 — /{market}/{lang}/shopping-carts (06-shopping-carts.png).
//
// Tabla de cabecera negra con NOMBRE · FECHA · PROPIETARIO/A · UNIDADES, el vacío
// literal "No hay carritos guardados" y la paginación «‹ 1 ›». El nombre carga el
// carrito en la ventana activa; la papelera lo borra.

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc, date } from '../format.js';
import { state } from '../state.js';
import { href, go } from '../router.js';
import { icons } from '../ui/icons.js';
import { pageHead } from '../ui/chrome.js';
import { pager, bindPager } from '../ui/pager.js';
import { confirmDialog } from '../ui/dialog.js';

const PAGE = 12;

export default async function carts(host) {
  let all = [];
  let skip = 0;
  let take = PAGE;
  let error = '';   // fallo de la última acción, en línea sobre la tabla

  host.innerHTML = `
    <div class="page">
      ${pageHead(t('nav.shopping-carts'), [t('nav.shopping-carts')])}
      <div id="cartsHost"><div class="skeleton short"></div></div>
    </div>`;

  const container = host.querySelector('#cartsHost');

  async function load() {
    try {
      all = (await api.get('/api/portal/carts')).items || [];
    } catch {
      container.innerHTML = `<div class="panel"><b>${esc(t('carts.errorTitle'))}</b>${esc(t('carts.errorBody'))}</div>`;
      return;
    }
    render();
  }

  function render() {
    const page = all.slice(skip, skip + take);

    // La tabla se desplaza dentro de .grid-scroll: en móvil no estira el documento
    container.innerHTML = `
      ${error ? `
        <div class="notice notice-error" role="alert">
          ${icons.alert(18)}<div><span>${esc(error)}</span></div>
        </div>` : ''}
      <div class="grid-scroll">
        <table class="grid">
          <thead>
            <tr>
              <th>${esc(t('carts.name'))}</th>
              <th>${esc(t('carts.date'))}</th>
              <th class="grid-owner">${esc(t('carts.owner'))}</th>
              <th>${esc(t('carts.units'))}</th>
              <th class="grid-actions"><span class="sr-only">${esc(t('carts.actions'))}</span></th>
            </tr>
          </thead>
          <tbody>
            ${page.length ? page.map(row).join('')
              : `<tr class="grid-empty"><td colspan="5">${esc(t('carts.empty'))}</td></tr>`}
          </tbody>
        </table>
      </div>
      <div id="cartsPager"></div>`;

    // M9: esta vista va sin buscador y sin "Mostrar" (plan §1, vista 6): solo «‹ 1 ›»
    const pagerHost = container.querySelector('#cartsPager');
    pagerHost.innerHTML = pager({ total: all.length, skip, take, size: false });
    bindPager(pagerHost, {
      take,
      onPage: value => { skip = Math.max(0, value); render(); },
      onSize: value => { take = value; skip = 0; render(); }
    });

    bind();
  }

  const row = cart => `
    <tr data-id="${esc(cart.id)}">
      <td><button type="button" class="grid-link" data-load="${esc(cart.id)}">${esc(cart.name)}</button></td>
      <td>${esc(date(cart.updatedAt || cart.createdAt))}</td>
      <td class="grid-owner">${esc(cart.owner || '')}</td>
      <td>${esc(String(cart.units ?? 0))}</td>
      <td class="grid-actions">
        <button type="button" class="grid-drop" data-drop="${esc(cart.id)}"
          aria-label="${esc(t('carts.delete'))}" title="${esc(t('carts.delete'))}">${icons.trash(16)}</button>
      </td>
    </tr>`;

  function bind() {
    container.querySelectorAll('[data-load]').forEach(button => {
      button.onclick = async () => {
        button.disabled = true;
        try {
          const cart = await api.get(`/api/portal/carts/${button.dataset.load}`);
          // El carrito guardado sustituye al de la ventana activa; el checkout ya
          // enseña el resultado, así que se navega allí directamente.
          state.loadCart(cart.lines || []);
          go(href('checkout'));
        } catch {
          button.disabled = false;
          error = t('carts.loadError');
          render();
        }
      };
    });

    container.querySelectorAll('[data-drop]').forEach(button => {
      // Borrar un carrito guardado no tiene vuelta atrás: se confirma en el diálogo
      button.onclick = async () => {
        const ok = await confirmDialog({
          title: t('carts.delete'), message: t('carts.deleteConfirm'),
          confirmLabel: t('carts.delete')
        });
        if (!ok) return;
        button.disabled = true;
        try {
          await api.del(`/api/portal/carts/${button.dataset.drop}`);
          all = all.filter(cart => cart.id !== button.dataset.drop);
          if (skip >= all.length) skip = Math.max(0, skip - take);
          error = '';
          render();
        } catch {
          button.disabled = false;
          error = t('carts.deleteError');
          render();
        }
      };
    });
  }

  await load();
}
