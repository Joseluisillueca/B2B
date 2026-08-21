// Vista 2 — post-login (01-tras-login.png): "SELECCIONA AHORA TUS CREDENCIALES:".
// Una fila por credencial; el agente multi-cliente tendrá varias.

import { t } from '../i18n.js';
import { esc, initial, roleLabel, typeLabel } from '../format.js';
import { state } from '../state.js';
import { go } from '../router.js';
import { icons } from '../ui/icons.js';

export default function credentials(host) {
  const me = state.me || {};
  const list = me.credentials || [];

  host.innerHTML = `
    <div class="sheet">
      <div class="card cred-card">
        <span class="brand">lejan<sup>™</sup></span>
        <h1>${esc(t('credentials.title'))}</h1>
        ${list.length ? list.map(row).join('') : empty()}
      </div>
    </div>`;

  for (const button of host.querySelectorAll('[data-pick]')) {
    button.onclick = () => {
      const credential = list[Number(button.dataset.pick)];
      state.credential = credential;
      // El agente no aterriza en un carrito propio: su punto de partida es su
      // cartera de clientes, desde donde suplanta. El cliente sigue al dashboard.
      const isAgent = credential?.agent || credential?.type === 'agent';
      go(isAgent ? 'clients' : 'dashboard', { replace: true });
    };
  }

  host.querySelector('[data-logout]')?.addEventListener('click', () => {
    state.clear();
    go('/login', { replace: true });
  });
}

const row = (credential, index) => `
  <div class="cred-row">
    <span class="avatar">${esc(initial(credential.name))}</span>
    <span class="id">
      <b>${esc(credential.name)}</b>
      <span class="type">${esc(typeLabel(credential))}</span><br>
      <span class="role">${esc(roleLabel(credential))}</span>
    </span>
    <button type="button" class="pick" data-pick="${index}">
      ${icons.login(16)} ${esc(t('credentials.select'))}
    </button>
  </div>`;

// El usuario técnico del conector no tiene cliente: no hay nada que seleccionar
const empty = () => `
  <p class="cred-empty">${esc(t('credentials.none'))}
    <br><a href="#" data-logout>${esc(t('chrome.logout'))}</a></p>`;
