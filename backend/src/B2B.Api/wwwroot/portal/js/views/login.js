// Vista 1 — /login (00-inicio.png): tarjeta blanca sobre degradado azul.

import { t } from '../i18n.js';
import { esc } from '../format.js';
import { api } from '../api.js';
import { state } from '../state.js';
import { go } from '../router.js';

export default function login(host) {
  host.innerHTML = `
    <div class="sheet">
      <form class="card login-card" novalidate>
        <span class="brand">lejan<sup>™</sup></span>

        <input class="field" type="email" name="email" autocomplete="username" required
          placeholder="${esc(t('login.email'))}" aria-label="${esc(t('login.email'))}">
        <input class="field" type="password" name="password" autocomplete="current-password" required
          placeholder="${esc(t('login.password'))}" aria-label="${esc(t('login.password'))}">
        <a class="forgot" href="#">${esc(t('login.forgot'))}</a>

        <button class="submit" type="submit">${esc(t('login.submit'))}</button>
        <p class="err" role="alert"></p>

        <h2>${esc(t('login.noAccountTitle'))}</h2>
        <p>${esc(t('login.help'))}</p>
        <a class="create" href="#">${esc(t('login.create'))}</a>
        <p class="legal">${esc(t('login.legal'))}</p>
      </form>
    </div>`;

  const form = host.querySelector('form');
  const error = host.querySelector('.err');
  const submit = host.querySelector('.submit');

  form.onsubmit = async event => {
    event.preventDefault();
    const data = new FormData(form);
    const email = String(data.get('email') || '').trim();
    const password = String(data.get('password') || '');
    error.textContent = '';

    if (!email || !password) {
      error.textContent = t('login.required');
      return;
    }

    submit.disabled = true;
    try {
      const { token } = await api.login(email, password);
      state.token = token;
      state.credential = null;
      state.me = await api.me();
      // Tras entrar siempre se pasa por la selección de credenciales (01-tras-login.png)
      await go('credentials', { replace: true });
    } catch (failure) {
      state.clear();
      error.textContent = failure.status === 401 ? t('login.badCredentials') : t('login.serverError');
      submit.disabled = false;
    }
  };

  host.querySelector('input[name=email]').focus();
}
