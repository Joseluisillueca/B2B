// Resultado del pago con tarjeta — /{market}/{lang}/pay?id=..&r=ok|cancel.
// La pasarela (Stripe/mock) devuelve aquí tras el pago. Consulta el estado real del
// pago en el backend (el redirect NO es prueba de pago) y muestra el resultado.

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc, eur } from '../format.js';
import { go, href } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { icons } from '../ui/icons.js';

export default async function pay(host) {
  const params = new URLSearchParams(location.search);
  const id = params.get('id') || '';
  const returned = params.get('r') || '';

  host.innerHTML = `
    <div class="page pay-page">
      ${pageHead(t('pay.title'), [t('nav.invoices'), t('pay.crumb')])}
      <div id="payHost" class="pay-panel panel"><div class="skeleton short"></div></div>
    </div>`;
  const panel = host.querySelector('#payHost');

  if (!id) return paint(panel, 'error', null);

  // El pago cuaja de forma asíncrona (webhook de Stripe / confirmación del mock): si
  // vuelve como "ok" pero aún consta pendiente, se reintenta unas veces antes de rendirse.
  let payment = null;
  const tries = returned === 'cancel' ? 1 : 6;
  for (let i = 0; i < tries; i++) {
    try { payment = await api.paymentStatus(id); } catch { return paint(panel, 'error', null); }
    if (payment.status !== 'pending' || returned === 'cancel') break;
    await new Promise(r => setTimeout(r, 1000));
  }

  const outcome = payment.status === 'paid' ? 'paid'
    : payment.status === 'failed' ? 'failed'
    : returned === 'cancel' || payment.status === 'canceled' ? 'canceled'
    : 'pending';
  paint(panel, outcome, payment);
}

function paint(panel, outcome, payment) {
  const back = payment?.kind === 'order' ? 'orders' : 'invoices';
  const icon = { paid: icons.check(40), canceled: icons.close(38), failed: icons.alert(38), pending: icons.alert(38), error: icons.alert(38) }[outcome];

  panel.className = `pay-panel panel pay-${outcome}`;
  panel.innerHTML = `
    <div class="pay-icon">${icon}</div>
    <h2>${esc(t(`pay.${outcome}.title`))}</h2>
    ${payment ? `<p class="pay-amount">${esc(payment.description || '')} · <b>${esc(eur(payment.amount))}</b></p>` : ''}
    <p class="pay-body">${esc(t(`pay.${outcome}.body`))}</p>
    <div class="pay-actions">
      <button type="button" class="btn-primary" id="payBack">${esc(t(back === 'orders' ? 'pay.toOrders' : 'pay.toInvoices'))}</button>
      <a class="btn-ghost" href="${href('dashboard')}">${esc(t('pay.toDashboard'))}</a>
    </div>`;
  panel.querySelector('#payBack').onclick = () => go(back);
}
