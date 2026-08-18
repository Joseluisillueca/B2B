// Vista 13 — /{market}/{lang}/profile (09-profile.png).
//
// "Bienvenido, {email}", las dos tarjetas —Mis datos y Preferencias— con su botón
// EDITAR, y la banda gris "Cambiar contraseña" con sus tres campos.
//
// EMAIL y ROL son de Business Central y no se editan aquí; lo que el usuario sí
// decide es su nombre, su idioma y cómo quiere ver el catálogo.

import { api } from '../api.js';
import { t, lang, LANGS } from '../i18n.js';
import { esc, roleLabel } from '../format.js';
import { state } from '../state.js';
import { go, current } from '../router.js';
import { icons } from '../ui/icons.js';
import { pageHead } from '../ui/chrome.js';

const PRICES = ['pvd', 'pvp'];
const MODES = ['list', 'grid'];

// El portal enruta por código de 2 letras; el backend guarda la cultura del conector
const CULTURES = { es: 'es_ES', en: 'en_EN', fr: 'fr_FR', it: 'it_IT' };
const langOf = culture => String(culture || '').slice(0, 2).toLowerCase();

export default async function profile(host) {
  let data = null;
  let editing = '';        // '' | 'data' | 'prefs'
  let notice = null;       // { tone: 'ok' | 'error', text }
  let passwordNotice = null;

  host.innerHTML = `
    <div class="page account">
      ${pageHead(t('profile.welcome', { email: state.me?.email || '' }), [t('profile.crumb')])}
      <div id="accHost"><div class="skeleton short"></div></div>
    </div>
    <section class="acc-band" id="passwordBand" hidden></section>`;

  const container = host.querySelector('#accHost');
  const band = host.querySelector('#passwordBand');

  try {
    data = await api.get('/api/portal/profile');
  } catch {
    container.innerHTML = `<div class="panel"><b>${esc(t('profile.errorTitle'))}</b>${esc(t('profile.errorBody'))}</div>`;
    return;
  }

  // ── Tarjetas ────────────────────────────────────────────────────────────────
  function render() {
    container.innerHTML = `
      ${notice ? noticeBox(notice) : ''}
      <div class="acc-grid">
        ${card('data', t('profile.myData'), editing === 'data' ? dataForm() : dataFacts())}
        ${card('prefs', t('profile.prefs'), editing === 'prefs' ? prefsForm() : prefsFacts())}
      </div>`;
    bind();
  }

  const card = (id, title, body) => `
    <section class="acc-card">
      <header class="acc-head">
        <h2>${esc(title)}</h2>
        ${editing === id ? '' : `
          <button type="button" class="btn-primary acc-edit" data-edit="${id}">
            ${icons.pencil(15)}${esc(t('profile.edit'))}</button>`}
      </header>
      ${body}
    </section>`;

  const facts = rows => `
    <dl class="acc-facts">
      ${rows.filter(Boolean).map(([label, value]) => `
        <div><dt>${esc(label)}</dt><dd>${value || '<span class="acc-none">—</span>'}</dd></div>`).join('')}
    </dl>`;

  const dataFacts = () => facts([
    [t('profile.name'), esc(data.name || '')],
    [t('profile.email'), esc(data.email || '')],
    [t('profile.role'), esc(roleLabel(data))],
    [t('profile.language'), esc(t(`lang.${langOf(data.culture)}`))]
  ]);

  // m15: la referencia muestra los valores de Preferencias en mayúsculas (LISTADO)
  const prefsFacts = () => facts([
    [t('profile.showPrices'), esc(t(`profile.price.${data.prefs.showPrices}`).toUpperCase())],
    [t('profile.listDesktop'), esc(t(`profile.mode.${data.prefs.listDesktop}`).toUpperCase())],
    [t('profile.listMobile'), esc(t(`profile.mode.${data.prefs.listMobile}`).toUpperCase())],
    [t('profile.defaultAddress'), esc(addressLabel(data.prefs.shippingAddressId))]
  ]);

  const addressLabel = id => {
    const address = (data.shippingAddresses || []).find(a => a.id === id);
    return address ? [address.alias, address.label].filter(Boolean).join(' · ') : '';
  };

  // EMAIL y ROL siguen a la vista: los manda BC, no se escriben desde el portal
  const dataForm = () => `
    <form class="acc-form" data-form="data">
      ${field(t('profile.name'), `<input type="text" name="name" maxlength="200"
        value="${esc(data.name || '')}" required>`)}
      ${readonly(t('profile.email'), data.email)}
      ${readonly(t('profile.role'), roleLabel(data))}
      ${field(t('profile.language'), `<select name="culture">
        ${LANGS.map(code => `<option value="${CULTURES[code]}"
          ${CULTURES[code] === data.culture ? ' selected' : ''}>${esc(t(`lang.${code}`))}</option>`).join('')}
      </select>`)}
      ${formActions()}
    </form>`;

  const prefsForm = () => `
    <form class="acc-form" data-form="prefs">
      ${field(t('profile.showPrices'), select('showPrices', PRICES, data.prefs.showPrices, 'profile.price'))}
      ${field(t('profile.listDesktop'), select('listDesktop', MODES, data.prefs.listDesktop, 'profile.mode'))}
      ${field(t('profile.listMobile'), select('listMobile', MODES, data.prefs.listMobile, 'profile.mode'))}
      ${field(t('profile.defaultAddress'), `<select name="shippingAddressId">
        <option value="">${esc(t('profile.noAddress'))}</option>
        ${(data.shippingAddresses || []).map(address => `<option value="${esc(address.id)}"
          ${address.id === data.prefs.shippingAddressId ? ' selected' : ''}>
          ${esc([address.alias, address.label].filter(Boolean).join(' · '))}</option>`).join('')}
      </select>`)}
      ${formActions()}
    </form>`;

  const select = (name, values, active, prefix) => `
    <select name="${name}">
      ${values.map(value => `<option value="${value}"${value === active ? ' selected' : ''}>
        ${esc(t(`${prefix}.${value}`))}</option>`).join('')}
    </select>`;

  const field = (label, control) => `
    <p class="acc-field"><label><span>${esc(label)}</span>${control}</label></p>`;

  const readonly = (label, value) => `
    <p class="acc-field acc-field-fixed"><span>${esc(label)}</span><b>${esc(value || '')}</b></p>`;

  const formActions = () => `
    <div class="acc-actions">
      <button type="button" class="btn-ghost" data-cancel>${esc(t('profile.cancel'))}</button>
      <button type="submit" class="btn-primary">${esc(t('profile.save'))}</button>
    </div>`;

  const noticeBox = ({ tone, text }) => `
    <div class="notice notice-${tone}"${tone === 'error' ? ' role="alert"' : ''}>
      ${tone === 'ok' ? icons.check(18) : icons.alert(18)}<div><span>${esc(text)}</span></div>
    </div>`;

  function bind() {
    container.querySelectorAll('[data-edit]').forEach(button => {
      button.onclick = () => { editing = button.dataset.edit; notice = null; render(); };
    });
    container.querySelectorAll('[data-cancel]').forEach(button => {
      button.onclick = () => { editing = ''; render(); };
    });

    const form = container.querySelector('.acc-form');
    if (!form) return;
    form.querySelector('input, select')?.focus();
    form.onsubmit = async event => {
      event.preventDefault();
      const values = Object.fromEntries(new FormData(form));
      const submit = form.querySelector('button[type=submit]');
      submit.disabled = true;

      const body = form.dataset.form === 'data'
        ? { name: String(values.name || '').trim(), culture: values.culture }
        : {
            prefs: {
              showPrices: values.showPrices,
              listDesktop: values.listDesktop,
              listMobile: values.listMobile,
              shippingAddressId: values.shippingAddressId || ''
            }
          };

      try {
        data = await api.put('/api/portal/profile', body);
      } catch (failure) {
        submit.disabled = false;
        notice = { tone: 'error', text: failure.body?.error || t('profile.saveError') };
        render();
        return;
      }

      // El resto del portal lee las preferencias de la ficha de sesión: se refresca
      // aquí para que el catálogo cambie de PVD a PVP sin volver a entrar.
      if (state.me) state.me = { ...state.me, name: data.name, culture: data.culture, prefs: data.prefs };

      editing = '';
      notice = { tone: 'ok', text: t('profile.saved') };

      // El idioma vive en la ruta: cambiarlo aquí lleva a la misma vista traducida
      const next = langOf(data.culture);
      if (next !== lang() && LANGS.includes(next)) {
        go(`/${current.market}/${next}/profile`);
        return;
      }
      render();
    };
  }

  // ── Banda gris "Cambiar contraseña" ─────────────────────────────────────────
  function renderBand() {
    band.hidden = false;
    band.innerHTML = `
      <div class="acc-band-inner">
        <h2>${esc(t('profile.password'))}</h2>
        <p class="acc-band-lead">${esc(t('profile.passwordLead'))}</p>
        ${passwordNotice ? noticeBox(passwordNotice) : ''}
        <form class="acc-password" novalidate>
          <input type="password" name="current" autocomplete="current-password"
            placeholder="${esc(t('profile.currentPassword'))}" aria-label="${esc(t('profile.currentPassword'))}">
          <input type="password" name="next" autocomplete="new-password"
            placeholder="${esc(t('profile.newPassword'))}" aria-label="${esc(t('profile.newPassword'))}">
          <input type="password" name="repeat" autocomplete="new-password"
            placeholder="${esc(t('profile.repeatPassword'))}" aria-label="${esc(t('profile.repeatPassword'))}">
          <button type="submit" class="btn-primary">${icons.lock(15)}${esc(t('profile.changePassword'))}</button>
        </form>
      </div>`;

    const form = band.querySelector('form');
    form.onsubmit = async event => {
      event.preventDefault();
      const values = Object.fromEntries(new FormData(form));
      const submit = form.querySelector('button[type=submit]');
      submit.disabled = true;
      try {
        await api.post('/api/portal/password', {
          current: values.current, next: values.next, repeat: values.repeat
        });
        passwordNotice = { tone: 'ok', text: t('profile.passwordOk') };
      } catch (failure) {
        passwordNotice = { tone: 'error', text: failure.body?.error || t('profile.passwordError') };
      }
      renderBand();
    };
  }

  // Al final del módulo: las plantillas de arriba son const y no se pueden usar
  // antes de su declaración (zona muerta temporal).
  render();
  renderBand();
}
