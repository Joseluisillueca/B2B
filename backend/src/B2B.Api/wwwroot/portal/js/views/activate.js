// Activación de cuenta / recuperación de contraseña (Fase 2). Pública: se llega
// desde el enlace del correo (…/activate?token=…), cuando el usuario aún no tiene
// sesión. Valida el token, deja fijar la contraseña, y si el enlace caducó ofrece
// pedir uno nuevo. Reutiliza la tarjeta del login (.sheet/.login-card).

import { api, ApiError } from '../api.js';
import { t } from '../i18n.js';
import { esc } from '../format.js';
import { go } from '../router.js';
import { brandMark } from '../branding.js';

const MIN = 8;

export default async function activate(host) {
  const token = new URLSearchParams(location.search).get('token') || '';

  host.innerHTML = `
    <div class="sheet">
      <div class="card login-card activate-card">
        <span class="brand">${brandMark()}</span>
        <div id="actBody"><div class="skeleton short"></div></div>
      </div>
    </div>`;
  const body = host.querySelector('#actBody');

  if (!token) return paintResend(body, '');

  let info;
  try {
    info = await api.get(`/api/activation/${encodeURIComponent(token)}`);
  } catch {
    return paintResend(body, '');
  }

  if (info?.valid) return paintSetPassword(body, token, info);
  return paintResend(body, info?.email || '');
}

// ── Fijar contraseña ───────────────────────────────────────────────────────────
function paintSetPassword(body, token, info) {
  body.innerHTML = `
    <h1>${esc(t('activate.title'))}</h1>
    <p class="activate-lead">${esc(t('activate.leadFor'))} <b>${esc(info.email || '')}</b></p>
    <form class="activate-form" novalidate>
      <input class="field" type="password" name="password" autocomplete="new-password" required
        placeholder="${esc(t('activate.password'))}" aria-label="${esc(t('activate.password'))}">
      <input class="field" type="password" name="repeat" autocomplete="new-password" required
        placeholder="${esc(t('activate.repeat'))}" aria-label="${esc(t('activate.repeat'))}">
      <p class="activate-hint">${esc(t('activate.rule', { min: MIN }))}</p>
      <button class="submit" type="submit">${esc(t('activate.submit'))}</button>
      <p class="err" role="alert"></p>
    </form>`;

  const form = body.querySelector('form');
  const error = body.querySelector('.err');
  const submit = body.querySelector('.submit');

  form.onsubmit = async event => {
    event.preventDefault();
    error.textContent = '';
    const password = String(form.elements.password.value || '');
    const repeat = String(form.elements.repeat.value || '');

    if (password.length < MIN) { error.textContent = t('activate.tooShort', { min: MIN }); return; }
    if (password !== repeat) { error.textContent = t('activate.mismatch'); return; }

    submit.disabled = true;
    try {
      await api.post(`/api/activation/${encodeURIComponent(token)}`, { password, repeat });
      paintDone(body);
    } catch (failure) {
      submit.disabled = false;
      error.textContent = failure instanceof ApiError && failure.body?.error
        ? failure.body.error : t('activate.error');
    }
  };
  form.querySelector('input')?.focus();
}

// ── Enlace caducado / inválido → pedir uno nuevo ─────────────────────────────────
function paintResend(body, email) {
  body.innerHTML = `
    <h1>${esc(t('activate.expiredTitle'))}</h1>
    <p class="activate-lead">${esc(t('activate.expiredBody'))}</p>
    <form class="activate-form" novalidate>
      <input class="field" type="email" name="email" autocomplete="username" required
        value="${esc(email)}" placeholder="${esc(t('activate.email'))}" aria-label="${esc(t('activate.email'))}">
      <button class="submit" type="submit">${esc(t('activate.resend'))}</button>
      <p class="err" role="alert"></p>
      <p class="activate-ok" hidden></p>
    </form>
    <a class="forgot" href="/login">${esc(t('activate.backToLogin'))}</a>`;

  const form = body.querySelector('form');
  const error = body.querySelector('.err');
  const ok = body.querySelector('.activate-ok');
  const submit = body.querySelector('.submit');

  form.onsubmit = async event => {
    event.preventDefault();
    error.textContent = '';
    ok.hidden = true;
    const value = String(form.elements.email.value || '').trim();
    if (!value) { error.textContent = t('activate.emailRequired'); return; }

    submit.disabled = true;
    try {
      await api.post('/api/activation/resend', { email: value });
      // Respuesta neutra a propósito: no se confirma si el correo existe
      ok.textContent = t('activate.resent');
      ok.hidden = false;
    } catch {
      error.textContent = t('activate.error');
    } finally {
      submit.disabled = false;
    }
  };
}

// ── Contraseña fijada ────────────────────────────────────────────────────────────
function paintDone(body) {
  body.innerHTML = `
    <h1>${esc(t('activate.doneTitle'))}</h1>
    <p class="activate-lead">${esc(t('activate.doneBody'))}</p>
    <button class="submit" type="button" id="toLogin">${esc(t('activate.goLogin'))}</button>`;
  body.querySelector('#toLogin').onclick = () => go('/login', { replace: true });
}
