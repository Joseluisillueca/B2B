// Vista 2 — post-login: selección de acceso. Rediseño "split de acceso" (hero rojo +
// panel de tarjetas), coherente con el login. El agente elige su credencial y luego, en
// su cartera, con qué cliente opera; el cliente entra a su dashboard.
//
// Reparto de texto (decisión de diseño): el hero rojo queda solo con display grande
// (marca + saludo + nombre), igual que el login. El texto pequeño en blanco sobre el
// rojo de marca no llega a contraste AA, así que TODA la guía de lectura vive en el
// panel claro (tinta sobre papel), que además es donde se decide. La etiqueta de acción
// de cada tarjeta ("Entrar" / "Elegir cliente") comunica el siguiente paso en el propio
// punto de acción.

import { t } from '../i18n.js';
import { esc, initial, roleLabel, typeLabel } from '../format.js';
import { state } from '../state.js';
import { go } from '../router.js';
import { icons } from '../ui/icons.js';
import { brandMark } from '../branding.js';

const isAgent = credential => !!(credential && (credential.agent || credential.type === 'agent'));

export default function credentials(host) {
  const me = state.me || {};
  const list = me.credentials || [];
  // Nombre grande del hero: la cuenta/credencial (no el email técnico del usuario).
  const displayName = (list[0] && list[0].name) || (me.name && !me.name.includes('@') ? me.name : '') || me.name || '';
  const single = list.length === 1;
  const agentOnly = list.length > 0 && list.every(isAgent);
  const lead = agentOnly ? t('credentials.agentHint') : t('credentials.clientHint');

  host.innerHTML = `
    <div class="cred-split">
      <aside class="cred-hero">
        <span class="brand">${brandMark()}</span>
        <div class="cred-hero-mid">
          <p class="hero-kicker">${esc(t('credentials.greeting'))}</p>
          <h1 class="cred-hero-name">${esc(displayName || t('credentials.choose'))}</h1>
        </div>
      </aside>
      <div class="cred-panel">
        <div class="cred-wrap">
          <h2 class="cred-h">${esc(t('credentials.choose'))}</h2>
          ${list.length ? `<p class="cred-lead">${esc(lead)}</p>` : ''}
          <div class="cred-cards${single ? ' single' : ''}">
            ${list.length ? list.map(row).join('') : empty()}
          </div>
          ${list.length ? exitLine() : ''}
        </div>
      </div>
    </div>`;

  for (const button of host.querySelectorAll('[data-pick]')) {
    button.onclick = () => {
      const credential = list[Number(button.dataset.pick)];
      state.credential = credential;
      // El agente no aterriza en un carrito propio: su punto de partida es su cartera de
      // clientes, desde donde suplanta. El cliente sigue al dashboard.
      go(isAgent(credential) ? 'clients' : 'dashboard', { replace: true });
    };
  }

  for (const link of host.querySelectorAll('[data-logout]')) {
    link.addEventListener('click', event => {
      event.preventDefault();
      state.clear();
      go('/login', { replace: true });
    });
  }
}

const row = (credential, index) => {
  const agent = isAgent(credential);
  const type = typeLabel(credential);
  const role = roleLabel(credential);
  // Evita el ruido "AGENTE · Agente": solo se añade el rol si aporta algo sobre el tipo.
  const sub = role && role.toLowerCase() !== type.toLowerCase()
    ? `<span class="cred-type">${esc(type)}</span> · ${esc(role)}`
    : `<span class="cred-type">${esc(type)}</span>`;
  // La etiqueta de acción dice qué pasará: el cliente entra; el agente pasa a elegir cliente.
  const action = agent ? t('credentials.pickClient') : t('credentials.enter');
  return `
  <button type="button" class="cred-card2" data-pick="${index}"
    aria-label="${esc(action)}: ${esc(credential.name)}">
    <span class="cred-av" aria-hidden="true">${esc(initial(credential.name))}</span>
    <span class="cred-meta">
      <b>${esc(credential.name)}</b>
      <span class="cred-sub">${sub}</span>
    </span>
    <span class="cred-action">
      <span class="cred-action-label">${esc(action)}</span>
      <span class="cred-go" aria-hidden="true">${icons.login(18)}</span>
    </span>
  </button>`;
};

// Salida discreta y accesible: si no eres tú, cierra sesión y vuelve al login.
const exitLine = () => `
  <p class="cred-exit">${esc(t('credentials.notYou'))}
    <button type="button" class="cred-exit-link" data-logout>${esc(t('chrome.logout'))}</button></p>`;

// El usuario técnico del conector no tiene cliente: no hay nada que seleccionar
const empty = () => `
  <p class="cred-empty">${esc(t('credentials.none'))}
    <br><a href="#" data-logout>${esc(t('chrome.logout'))}</a></p>`;
