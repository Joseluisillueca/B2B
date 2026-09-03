// Vista 5 — /{market}/{lang}/checkout (16-checkout.png).
//
// Cabecera con el cliente y sus tres botones, ficha de siete campos, líneas del
// pedido agrupadas por artículo y panel derecho con los cuatro totales, el aviso
// de bloqueo, el checkbox de condiciones y TERMINAR PEDIDO.
//
// El envío real a Business Central es la Fase BC del plan: TERMINAR PEDIDO deja el
// pedido registrado y pendiente de envío, y vacía el carrito.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur, date } from '../format.js';
import { state, lineKey } from '../state.js';
import { href, go } from '../router.js';
import { icons } from '../ui/icons.js';
import { pageHead } from '../ui/chrome.js';
import { groupLines } from '../ui/cart.js';
import { confirmDialog, promptDialog } from '../ui/dialog.js';
import { fetchRelated, relatedSectionHtml, bindRelatedRail } from '../ui/related.js';

const IVA = 0.21;   // Tipo general: el desglose real por línea llega con el pedido de BC

// M5: MÉTODO DE PAGO enseñaba el código interno (`transf30`). Business Central manda
// los métodos del cliente en client.payMethods, y según la versión del conector cada
// entrada es un código suelto o una ficha con su nombre legible ("Transferencia 30
// días"), que es lo que la referencia imprime y lo que ya muestra /invoices. Se
// normalizan las dos formas: si viene el nombre se enseña el nombre; si no, el código.
const payOption = method => {
  if (method && typeof method === 'object') {
    const value = method.id ?? method.code ?? method.key ?? method.value ?? '';
    return { value: String(value), label: String(method.name ?? method.label ?? method.description ?? value) };
  }
  return { value: String(method ?? ''), label: String(method ?? '') };
};

// Además de las formas de pago del cliente (BC), el portal ofrece pago con TARJETA
// (Stripe): al elegirla, TERMINAR PEDIDO pasa por la pasarela antes de cerrar.
const CARD = '__card__';
const payOptions = client => [
  ...(client.payMethods || []).map(payOption),
  { value: CARD, label: t('checkout.payCard') }
];

const payLabel = (client, value) =>
  payOptions(client).find(option => option.value === value)?.label || value || '';

export default function checkout(host) {
  const me = state.me || {};
  const client = me.client || {};
  const credential = state.credential || {};

  let editing = false;
  // Aceptar las condiciones sobrevive a la navegación DENTRO de la sesión del
  // carrito (ir a una ficha sugerida y volver no obliga a re-marcar). Se limpia
  // al terminar el pedido o al vaciar el carrito (sessionStorage: muere con la
  // pestaña, nunca se arrastra a un pedido de mañana).
  const ACCEPTED_KEY = 'ck_accepted';
  const readAccepted = () => { try { return sessionStorage.getItem(ACCEPTED_KEY) === '1'; } catch { return false; } };
  let accepted = readAccepted();
  const setAccepted = value => {
    accepted = value;
    try { value ? sessionStorage.setItem(ACCEPTED_KEY, '1') : sessionStorage.removeItem(ACCEPTED_KEY); } catch { /* modo privado */ }
  };
  let sent = null;
  let error = '';   // fallo de la última acción, en línea junto a los totales
  let transport = 0;      // porte calculado por las reglas para el carrito actual (0 = gratis)
  let previewKey = '';    // evita re-pedir la preview si no cambian los datos relevantes

  // "Añade también": relacionados (cross/up de BC) de los modelos del carrito. El
  // servidor ya excluye los modelos de origen, así que nada de lo que ya está en el
  // pedido se sugiere. Se cachea por juego de modelos para no re-pedir en cada
  // repintado, y sin sugerencias el bloque no existe.
  let suggested = [];
  let suggestKey = null;      // modelos de la última petición lanzada
  let suggestShown = false;   // la aparición suave solo la primera vez
  let windowIdPromise = null; // id real de la ventana activa (para la tarifa correcta)

  const serviceWindowId = () => windowIdPromise ??= api
    .get(`/api/shop/catalog?take=1&locale=${lang()}`)
    .then(data => {
      const type = state.prefs.window === 'scheduled' ? 'SCHEDULED' : 'REPLENISHMENT';
      const windows = data.windows || [];
      return (windows.find(w => w.orderType === type) || windows[0])?.id || '';
    })
    .catch(() => '');

  async function loadSuggestions() {
    const models = [...new Set(state.cartLines().map(line => line.modelId))].sort();
    const key = models.join(',');
    if (key === suggestKey) return;
    suggestKey = key;

    // Centinela de DESMONTAJE: `host` es el #view permanente del router (siempre
    // conectado), así que el guard real es un nodo PROPIO del checkout — si el usuario
    // navegó a otra vista mientras el fetch volaba, ese nodo ya no está conectado y NO
    // se debe repintar (repintaría el checkout encima de la otra vista).
    const sentinel = host.querySelector('.page.checkout');
    let items = [];
    if (models.length) {
      try { items = await fetchRelated(models, await serviceWindowId()); } catch { items = []; }
    }
    if (suggestKey !== key) return;                        // llegó tarde: ya manda otro carrito
    if (!sentinel || !sentinel.isConnected) return;        // el usuario ya no está en el checkout
    const changed = items.length !== suggested.length
      || items.some((it, i) => it.card.modelId !== suggested[i]?.card.modelId);
    suggested = items;
    if (changed) render();
  }
  const form = {
    reference: '',
    payMethod: payOptions(client)[0]?.value || '',
    shippingAddressId: (client.shippingAddresses || [])[0]?.id || '',
    notes: ''
  };

  const clientName = credential.name || client.name || '';
  const clientNumber = credential.clientNumber || client.number || '';

  function render() {
    // El checkout se repinta entero a menudo (condiciones, porte, líneas): el raíl
    // de sugerencias no debe perder su posición de scroll en cada repintado.
    const railScroll = host.querySelector('.ck-grid > .related .related-rail')?.scrollLeft || 0;
    const lines = state.cartLines();
    const units = state.cartUnits();
    const subtotal = state.cartTotal();
    const tax = Math.round(subtotal * IVA * 100) / 100;

    // TERMINAR PEDIDO se deshabilita por dos motivos distintos y el aviso tiene que
    // decir cuál: sin líneas, o con líneas y las condiciones sin aceptar. Antes el
    // segundo caso dejaba el botón apagado y sin explicación.
    const blocked = sent ? '' : !units ? t('checkout.blockedEmpty')
      : !accepted ? t('checkout.blockedTerms') : '';

    host.innerHTML = `
      <div class="page checkout">
        ${pageHead(t('checkout.heading'), [t('nav.catalog'), t('nav.checkout')])}

        <div class="ck-grid">
          <div class="ck-main">
            <div class="ck-head">
              <h2 class="ck-client">${esc(t('checkout.client'))}
                <b>${esc(clientName)}${clientNumber ? ` (${esc(clientNumber)})` : ''}</b></h2>
              <!-- M6: en la referencia (16-checkout.png) el carrito vacío deja los
                   cinco botones activos —ELIMINAR CARRITO en rojo con su papelera—
                   y solo apaga TERMINAR PEDIDO. -->
              <div class="ck-actions">
                <button type="button" class="btn-danger" id="dropCart">
                  ${icons.trash(16)} ${esc(t('checkout.dropCart'))}</button>
                <button type="button" class="btn-ghost" id="edit" aria-pressed="${editing}">
                  ${icons.pencil(16)} ${esc(t('checkout.edit'))}</button>
                <button type="button" class="btn-ghost" id="excel">
                  ${icons.fileDown(16)} ${esc(t('checkout.excel'))}</button>
              </div>
            </div>

            ${card()}

            <div class="ck-lines-head">
              <h2>${esc(t('checkout.products', { n: units }))}</h2>
              <button type="button" class="btn-ghost" id="favorite">
                ${icons.heart(16)} ${esc(t('checkout.saveFavorite'))}</button>
            </div>

            ${lines.length ? groups(lines) : `<p class="ck-empty">${esc(t('checkout.noProducts'))}</p>`}
          </div>

          <aside class="ck-side">
            ${error ? `
              <div class="notice notice-error" role="alert">
                ${icons.alert(18)}<div><span>${esc(error)}</span></div>
              </div>` : ''}
            ${sent ? sentNotice() : blocked ? blockedNotice(blocked) : ''}

            <dl class="ck-totals">
              <div><dt>${esc(t('checkout.subtotal'))}</dt><dd>${esc(eur(subtotal))}</dd></div>
              <div><dt>${esc(t('checkout.totalNet'))}</dt><dd>${esc(eur(subtotal))}</dd></div>
              <div class="ck-ship"><dt>${esc(t('checkout.shipping'))} ${icons.truck(16)}</dt>
                <dd>${transport > 0 ? esc(eur(transport)) : esc(t('checkout.freeShipping'))}</dd></div>
              <div class="ck-grand"><dt>${esc(t('checkout.totalGross'))}</dt>
                <dd>${esc(eur(subtotal + tax + transport))}</dd></div>
            </dl>

            <label class="ck-terms">
              <input type="checkbox" id="terms" ${accepted ? 'checked' : ''}>
              <span>${esc(t('checkout.terms'))}</span>
            </label>

            <!-- El CTA cierra el panel: el usuario ve primero el desglose y acepta
                 las condiciones, y solo entonces confirma (flujo natural). -->
            <button type="button" class="btn-primary block" id="submit"
              ${blocked ? 'aria-describedby="ckBlocked"' : ''}
              ${units && accepted && !sent ? '' : 'disabled'}>${esc(t('checkout.submit'))}</button>
          </aside>

          <!-- "Añade también" es la ÚLTIMA hija de .ck-grid: en escritorio la rejilla
               la coloca bajo las líneas (columna 1) y en columna única queda DESPUÉS
               del resumen y de TERMINAR PEDIDO — las sugerencias jamás se interponen
               entre el pedido y su CTA (D-A1). CTA de card: "Elegir tallas". -->
          ${lines.length && suggested.length ? relatedSectionHtml(suggested, {
            title: t('checkout.suggestTitle'),
            sub: t('checkout.suggestSub'),
            compact: true,
            id: 'ck-suggest',
            cta: t('related.ctaCheckout')
          }) : ''}
        </div>
      </div>`;

    // Porte real del carrito: consulta la preview cuando cambian ventana/dirección/unidades/
    // importe y re-pinta. Guardado por clave para no re-pedir sin cambios ni entrar en bucle.
    const previewNow = `${state.window}|${form.shippingAddressId}|${units}|${subtotal}`;
    if (units > 0 && previewNow !== previewKey) {
      previewKey = previewNow;
      api.transportPreview({ windowId: state.window, shippingAddressId: form.shippingAddressId || null, units, amount: subtotal })
        .then(r => { const c = Number(r?.cost) || 0; if (c !== transport) { transport = c; render(); } })
        .catch(() => {});
    }

    // El raíl de sugerencias: flechas si desborda, y la aparición suave solo la
    // primera vez (los repintados posteriores lo dejan quieto, sin re-animar).
    const suggest = host.querySelector('.ck-grid > .related');
    if (suggest) {
      bindRelatedRail(suggest);
      if (railScroll) suggest.querySelector('.related-rail').scrollLeft = railScroll;
      if (!suggestShown) { suggestShown = true; void suggest.offsetWidth; }
      suggest.classList.add('on');
    }
    loadSuggestions();

    bind();
  }

  // La ficha se repinta entera al marcar las condiciones: sin role="status" el aviso
  // aparecía y desaparecía sin que un lector de pantalla dijera nada.
  const blockedNotice = reason => `
    <div class="notice notice-error" id="ckBlocked" role="status">
      ${icons.alert(18)}
      <div><b>${esc(t('checkout.blockedTitle'))}</b><span>${esc(reason)}</span></div>
    </div>`;

  const sentNotice = () => `
    <div class="notice notice-ok">
      ${icons.check(18)}
      <div><b>${esc(t('checkout.sentTitle', { n: sent.reference || sent.name }))}</b>
        <span>${esc(t(sent.sentToBc ? 'checkout.sentBodyBc' : 'checkout.sentBody'))}</span></div>
    </div>`;

  // Los 7 campos de la ficha (16-checkout.png). Con EDITAR los cuatro que el cliente
  // decide se vuelven campos de formulario; los otros tres son informativos.
  function card() {
    const address = (client.shippingAddresses || [])
      .find(a => a.id === form.shippingAddressId) || (client.shippingAddresses || [])[0];
    const fiscal = client.fiscalInfo || {};

    return `
      <div class="ck-card">
        <dl class="ck-row">
          ${field(t('checkout.date'), date(new Date()))}
          ${field(t('checkout.reference'), editing
            ? `<input type="text" id="reference" value="${esc(form.reference)}" maxlength="60"
                aria-label="${esc(t('checkout.reference'))}">`
            : esc(form.reference) || '—', true)}
          ${field(t('checkout.payMethod'), editing
            ? `<select id="payMethod" aria-label="${esc(t('checkout.payMethod'))}">${payOptions(client).map(option =>
                `<option value="${esc(option.value)}"${option.value === form.payMethod ? ' selected' : ''}>
                   ${esc(option.label)}</option>`).join('')}</select>`
            : esc(payLabel(client, form.payMethod)) || '—', true)}
          ${field(t('checkout.type'), t(`window.${state.prefs.window}`).toUpperCase())}
        </dl>

        <dl class="ck-row two">
          ${field(t('checkout.shipTo'), editing && (client.shippingAddresses || []).length
            ? `<select id="shipTo" aria-label="${esc(t('checkout.shipTo'))}">${(client.shippingAddresses || []).map(a =>
                `<option value="${esc(a.id)}"${a.id === form.shippingAddressId ? ' selected' : ''}>
                   ${esc(addressText(a))}</option>`).join('')}</select>`
            : esc(address ? addressText(address) : '—'), true)}
          ${field(t('checkout.billing'),
            `${fiscal.fiscalName || clientName}${fiscal.fiscalId?.document ? ` (${fiscal.fiscalId.document})` : ''}`)}
        </dl>

        <dl class="ck-row one">
          ${field(t('checkout.notes'), editing
            ? `<textarea id="notes" rows="3" maxlength="500"
                aria-label="${esc(t('checkout.notes'))}">${esc(form.notes)}</textarea>`
            : esc(form.notes), true)}
        </dl>
      </div>`;
  }

  const field = (label, value, raw = false) => `
    <div class="ck-field"><dt>${esc(label)}</dt><dd>${raw ? value : esc(value)}</dd></div>`;

  const addressText = address => {
    const a = address?.address || {};
    return [a.streetAddress && `${a.streetAddress}${a.num ? ` ${a.num}` : ''}`, a.city, a.province, a.zipCode,
      a.countryIsoId && `(${a.countryIsoId})`].filter(Boolean).join(', ');
  };

  function groups(lines) {
    return `<div class="ck-lines">${groupLines(lines).map(group => `
      <section class="ck-group">
        <header>
          <h3>${esc(group.name || '')}</h3>
          <p>${esc(t('catalog.reference'))} <b>${esc(group.reference || '')}</b></p>
          <span class="ck-group-total">${esc(t('checkout.units', { n: group.units }))} · ${esc(eur(group.total))}</span>
        </header>
        <table class="ck-table">
          <thead><tr>
            <th>${esc(t('checkout.size'))}</th><th>${esc(t('checkout.qty'))}</th>
            <th>${esc(t('checkout.price'))}</th><th>${esc(t('checkout.amount'))}</th><th></th>
          </tr></thead>
          <tbody>${group.lines.map(line => `
            <tr>
              <td><span class="ck-size">${esc(line.size ?? '')}</span></td>
              <td>${esc(String(line.qty))}</td>
              <td>${esc(eur(line.price))}</td>
              <td>${esc(eur((Number(line.qty) || 0) * (Number(line.price) || 0)))}</td>
              <td><button type="button" class="ck-drop" data-key="${esc(lineKey(line))}"
                aria-label="${esc(t('cart.remove'))}">${icons.close(14)}</button></td>
            </tr>`).join('')}</tbody>
        </table>
      </section>`).join('')}</div>`;
  }

  function bind() {
    const $ = id => host.querySelector(`#${id}`);

    // El repintado destruye el botón que se acaba de pulsar: se devuelve el foco
    $('edit').onclick = () => {
      editing = !editing;
      render();
      host.querySelector('#edit')?.focus({ preventScroll: true });
    };
    // El repintado destruye la casilla que se acaba de marcar: se devuelve el foco
    $('terms').onchange = event => {
      setAccepted(event.target.checked);
      render();
      host.querySelector('#terms')?.focus({ preventScroll: true });
    };

    if (editing) {
      $('reference')?.addEventListener('input', e => { form.reference = e.target.value; });
      $('payMethod')?.addEventListener('change', e => { form.payMethod = e.target.value; });
      $('shipTo')?.addEventListener('change', e => { form.shippingAddressId = e.target.value; render(); });
      $('notes')?.addEventListener('input', e => { form.notes = e.target.value; });
    }

    host.querySelectorAll('.ck-drop').forEach(button => {
      button.onclick = () => { state.removeCartLine(button.dataset.key); render(); };
    });

    // Vaciar el carrito no tiene vuelta atrás: se confirma en el diálogo del portal
    $('dropCart').onclick = async () => {
      const ok = await confirmDialog({
        title: t('checkout.dropCart'), message: t('checkout.dropConfirm'),
        confirmLabel: t('checkout.dropCart')
      });
      if (!ok) return;
      state.clearCart();
      setAccepted(false);   // carrito nuevo, condiciones nuevas
      sent = null;
      error = '';
      render();
    };

    // Los dos botones que CREAN un carrito en el servidor siguen activos con el
    // carrito vacío (M6), pero no dejan un registro en blanco: avisan y no llaman.
    const needsLines = () => {
      if (state.cartUnits()) return true;
      error = t('checkout.emptyAction');
      render();
      return false;
    };

    $('excel').onclick = async event => {
      if (!needsLines()) return;
      const button = event.currentTarget;
      button.disabled = true;
      try {
        // El CSV lo genera el backend: se guarda el carrito de paso y se descarga
        const cart = await api.post('/api/portal/carts', payload(t('checkout.excelName')));
        await api.download(`/api/portal/carts/${cart.id}/export.csv`, 'pedido.csv');
        await api.del(`/api/portal/carts/${cart.id}`);
        button.disabled = false;
        if (error) { error = ''; render(); }
      } catch {
        // Antes la descarga fallaba en silencio (promesa rechazada y nada en pantalla)
        error = t('checkout.excelError');
        render();
      }
    };

    $('favorite').onclick = async event => {
      if (!needsLines()) return;
      const button = event.currentTarget;
      const name = await promptDialog({
        title: t('checkout.saveFavorite'), label: t('checkout.favoriteName'), value: defaultName()
      });
      if (name === null) return;
      button.disabled = true;
      try {
        await api.post('/api/portal/carts', payload(name.trim() || defaultName()));
        go(href('shopping-carts'));
      } catch {
        button.disabled = false;
        error = t('checkout.favoriteError');
        render();
      }
    };

    $('submit').onclick = async event => {
      const button = event.currentTarget;
      button.disabled = true;

      // Pago con tarjeta: se crea el pedido y se redirige a la pasarela; al volver,
      // la página de resultado muestra si el pago cuajó.
      if (form.payMethod === CARD) {
        try {
          const order = await api.post('/api/portal/orders', payload(defaultName()));
          const pay = await api.payOrder(order.id, lang());
          state.clearCart();
          setAccepted(false);
          window.location.href = pay.url;
        } catch (err) {
          button.disabled = false;
          // El backend explica el 400 (p. ej. artículos fuera de la visibilidad
          // del actor): ese mensaje LLEGA al usuario; el genérico es el respaldo.
          error = err?.body?.error || t('checkout.payError');
          render();
        }
        return;
      }

      try {
        sent = await api.post('/api/portal/orders', payload(defaultName()));
        state.clearCart();
        setAccepted(false);
        error = '';
        render();
      } catch (err) {
        button.disabled = false;
        // Mismo criterio: el error del backend (ApiError.body.error) por delante
        // del genérico — "Estos artículos no están disponibles…" se ve, no se tapa.
        error = err?.body?.error || t('checkout.submitError');
        render();
      }
    };
  }

  const defaultName = () => `${t(`window.${state.prefs.window}`)} ${date(new Date())}`;

  const payload = name => ({
    name,
    windowId: state.prefs.window,
    reference: form.reference || null,
    lines: state.cartLines(),
    // Instantánea del pedido para el modo portal (sin ERP): forma de pago, dirección
    // y notas. En modo ERP el backend los ignora (el pedido lo cierra BC).
    payMethod: form.payMethod === CARD ? 'card' : (form.payMethod || null),
    shippingAddressId: form.shippingAddressId || null,
    notes: form.notes || null
  });

  render();
}
