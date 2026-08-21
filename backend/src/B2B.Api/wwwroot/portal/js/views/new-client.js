// Alta de cliente por el agente — /agent/clients/new (spec real §2, "Registro").
// Formulario largo de una columna con 5 secciones: Información básica (+ checkbox
// "Crear precliente"), Información fiscal + dirección fiscal, Direcciones de envío
// (checkbox "= dirección fiscal"), Emails de contacto y Información de contacto.
//
// El maestro vive en BC (sync de una vía): esto NO crea el cliente en BC, registra
// una prealta (POST /api/agent/clients) y dispara el correo de activación al contacto.
//
// Los bloques dinámicos (direcciones de envío / emails) se añaden y quitan por DOM,
// NO re-renderizando el formulario: así no se pierde lo que el agente ya ha tecleado.

import { api, ApiError } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc } from '../format.js';
import { go, href } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { icons } from '../ui/icons.js';

const PHONE_CODES = ['+34', '+351', '+33', '+39', '+49', '+44', '+31', '+32', '+41', '+43', '+1'];
// Países habituales (el real trae 239; ES por defecto). Se localizan al idioma de la
// ruta con Intl.DisplayNames: en EN sale "Spain", en FR "Espagne", etc.
const COUNTRY_CODES = ['ES', 'PT', 'FR', 'IT', 'DE', 'GB', 'NL', 'BE', 'AD', 'US'];
const countryOptions = () => {
  let names;
  try { names = new Intl.DisplayNames([lang()], { type: 'region' }); } catch { names = null; }
  return COUNTRY_CODES.map(iso => [iso, names?.of(iso) || iso]);
};
const PROVINCES = ['Álava', 'Albacete', 'Alicante', 'Almería', 'Asturias', 'Ávila', 'Badajoz', 'Barcelona',
  'Burgos', 'Cáceres', 'Cádiz', 'Cantabria', 'Castellón', 'Ciudad Real', 'Córdoba', 'A Coruña', 'Cuenca',
  'Girona', 'Granada', 'Guadalajara', 'Gipuzkoa', 'Huelva', 'Huesca', 'Illes Balears', 'Jaén', 'León',
  'Lleida', 'Lugo', 'Madrid', 'Málaga', 'Murcia', 'Navarra', 'Ourense', 'Palencia', 'Las Palmas',
  'Pontevedra', 'La Rioja', 'Salamanca', 'Santa Cruz de Tenerife', 'Segovia', 'Sevilla', 'Soria',
  'Tarragona', 'Teruel', 'Toledo', 'Valencia', 'Valladolid', 'Bizkaia', 'Zamora', 'Zaragoza'];
const EMAIL_TYPES = ['Orders', 'Invoices', 'Main', 'Other'];

const field = (name, label, { type = 'text', wide = false } = {}) => `
  <p class="acc-field${wide ? ' wide' : ''}"><label>
    <span>${esc(label)}</span>
    <input type="${type}" name="${esc(name)}" maxlength="200">
  </label></p>`;

const select = (name, label, options, selected = '') => `
  <p class="acc-field"><label>
    <span>${esc(label)}</span>
    <span class="tb-select">
      <select name="${esc(name)}">
        ${options.map(o => {
          const [val, text] = Array.isArray(o) ? o : [o, o];
          return `<option value="${esc(val)}"${val === selected ? ' selected' : ''}>${esc(text)}</option>`;
        }).join('')}
      </select>
    </span>
  </label></p>`;

const addressBlock = prefix => `
  <div class="biz-grid">
    ${field(`${prefix}.streetAddress`, t('newClient.street'))}
    ${field(`${prefix}.num`, t('newClient.num'))}
    ${select(`${prefix}.countryIsoId`, t('newClient.country'), countryOptions(), 'ES')}
    ${select(`${prefix}.province`, t('newClient.province'), [['', '—'], ...PROVINCES])}
    ${field(`${prefix}.city`, t('newClient.city'))}
    ${field(`${prefix}.zipCode`, t('newClient.zip'))}
  </div>
  <p class="acc-field wide"><label>
    <span>${esc(t('newClient.description'))}</span>
    <textarea name="${esc(prefix)}.description" rows="2" maxlength="500"></textarea>
  </label></p>`;

export default async function newClient(host) {
  let seq = 0;   // índice creciente para bloques dinámicos

  host.innerHTML = `
    <div class="page account new-client">
      ${pageHead(t('newClient.title'), [t('clients.crumb'), t('newClient.crumb')])}
      <div id="ncNotice"></div>
      <form class="nc-form" novalidate>

        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.building(20)}${esc(t('newClient.basic'))}</h2></header>
          <div class="biz-card">
            ${field('name', t('newClient.name'), { wide: true })}
            <div class="biz-grid">
              ${field('email', t('newClient.email'), { type: 'email' })}
              ${field('web', t('newClient.web'), { type: 'url' })}
              ${select('phone.code', t('newClient.phoneCode'), PHONE_CODES, '+34')}
              ${field('phone.number', t('newClient.phoneNumber'))}
            </div>
            <p class="biz-check"><label>
              <input type="checkbox" name="createPreClient">
              <span>${esc(t('newClient.preClient'))}</span>
            </label></p>
            <p class="biz-hint">${icons.alert(16)}<span>${esc(t('newClient.preClientHint'))}</span></p>
          </div>
        </section>

        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.coin(20)}${esc(t('newClient.fiscal'))}</h2></header>
          <div class="biz-card">
            <div class="biz-grid">
              ${field('fiscalInfo.fiscalName', t('newClient.fiscalName'))}
              ${field('fiscalInfo.alias', t('newClient.alias'))}
            </div>
            ${field('fiscalInfo.fiscalId.document', t('newClient.fiscalId'), { wide: true })}
            <h3 class="nc-sub">${esc(t('newClient.fiscalAddress'))}</h3>
            ${addressBlock('fiscalInfo.address')}
          </div>
        </section>

        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.truck(20)}${esc(t('newClient.shipping'))}</h2></header>
          <div class="biz-card">
            <p class="biz-check"><label>
              <input type="checkbox" name="sameShippingAddressAsFiscal" id="sameShip" checked>
              <span>${esc(t('newClient.sameAsFiscal'))}</span>
            </label></p>
            <div id="shipHost"></div>
            <button type="button" class="btn-ghost nc-add" id="addShip">${icons.plus(15)}${esc(t('newClient.addShipping'))}</button>
          </div>
        </section>

        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.building(20)}${esc(t('newClient.emails'))}</h2></header>
          <div class="biz-card">
            <p class="biz-hint">${icons.alert(16)}<span>${esc(t('newClient.emailsHint'))}</span></p>
            <p class="biz-none" id="noEmails">${esc(t('newClient.noEmails'))}</p>
            <div id="emailHost"></div>
            <button type="button" class="btn-ghost nc-add" id="addEmail">${icons.plus(15)}${esc(t('newClient.addEmail'))}</button>
          </div>
        </section>

        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.user(20)}${esc(t('newClient.contact'))}</h2></header>
          <div class="biz-card">
            <div class="biz-grid">
              ${field('contacts[0].name', t('newClient.contactName'))}
              ${field('contacts[0].lastName', t('newClient.contactLastName'))}
              ${field('contacts[0].email', t('newClient.contactEmail'), { type: 'email' })}
              ${field('contacts[0].company', t('newClient.contactCompany'))}
              ${select('contacts[0].phones[0].code', t('newClient.phoneCode'), PHONE_CODES, '+34')}
              ${field('contacts[0].phones[0].number', t('newClient.phoneNumber'))}
            </div>
          </div>
        </section>

        <div class="acc-actions nc-actions">
          <a class="btn-ghost" href="${href('clients')}">${esc(t('newClient.cancel'))}</a>
          <button type="submit" class="btn-primary">${esc(t('newClient.submit'))}</button>
        </div>
      </form>`;

  const form = host.querySelector('.nc-form');
  const notice = host.querySelector('#ncNotice');
  const shipHost = form.querySelector('#shipHost');
  const emailHost = form.querySelector('#emailHost');
  const addShip = form.querySelector('#addShip');
  const same = form.querySelector('#sameShip');
  const noEmails = form.querySelector('#noEmails');

  // Checkbox "= dirección fiscal": oculta el bloque de envío y su botón
  const syncShip = () => { shipHost.hidden = same.checked; addShip.hidden = same.checked; };
  same.onchange = syncShip; syncShip();

  addShip.onclick = () => {
    const i = seq++;
    const wrap = document.createElement('div');
    wrap.className = 'nc-ship';
    wrap.innerHTML = `
      <div class="nc-ship-head"><b>${esc(t('newClient.shippingAddress'))}</b>
        <button type="button" class="btn-ghost nc-remove">${icons.close(14)}${esc(t('newClient.remove'))}</button></div>
      ${field(`shippingAddresses[${i}].alias`, t('newClient.alias'))}
      ${addressBlock(`shippingAddresses[${i}]`)}`;
    wrap.querySelector('.nc-remove').onclick = () => wrap.remove();
    shipHost.append(wrap);
  };

  const addEmailBtn = form.querySelector('#addEmail');
  addEmailBtn.onclick = () => {
    const i = seq++;
    noEmails.hidden = true;
    const wrap = document.createElement('div');
    wrap.className = 'nc-email biz-grid';
    wrap.innerHTML = `
      ${field(`secondaryEmails[${i}].emailName`, t('newClient.emailName'))}
      ${field(`secondaryEmails[${i}].email`, t('newClient.email'), { type: 'email' })}
      ${select(`secondaryEmails[${i}].type`, t('newClient.emailType'), EMAIL_TYPES, 'Orders')}
      <p class="acc-field nc-email-x"><button type="button" class="btn-ghost nc-remove">${icons.close(14)}${esc(t('newClient.remove'))}</button></p>`;
    wrap.querySelector('.nc-remove').onclick = () => {
      wrap.remove();
      if (!emailHost.children.length) noEmails.hidden = false;
    };
    emailHost.append(wrap);
  };

  form.onsubmit = async event => {
    event.preventDefault();
    notice.innerHTML = '';
    const body = collect(form);

    if (!body.name) return warn(notice, t('newClient.nameRequired'));
    if (!body.email || !body.email.includes('@')) return warn(notice, t('newClient.emailRequired'));

    const submitBtn = form.querySelector('button[type=submit]');
    submitBtn.disabled = true;
    try {
      const created = await api.createClient(body);
      sessionStorage.setItem('nc-created', JSON.stringify({
        name: created.name, email: created.email, activationSent: created.activationSent
      }));
      go('clients/requests');
    } catch (failure) {
      submitBtn.disabled = false;
      warn(notice, failure instanceof ApiError && failure.body?.error ? failure.body.error : t('newClient.error'));
    }
  };
}

function warn(host, text) {
  host.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(text)}</span></div></div>`;
  host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

// Construye el objeto anidado a partir de los name="a.b[0].c" del formulario
function collect(form) {
  const root = {};
  for (const el of form.elements) {
    if (!el.name) continue;
    if (el.type === 'checkbox') { setPath(root, el.name, el.checked); continue; }
    const value = String(el.value ?? '').trim();
    if (value) setPath(root, el.name, value);
  }
  if (root.sameShippingAddressAsFiscal) delete root.shippingAddresses;
  // Los índices de los bloques dinámicos no son contiguos (se comparten y se pueden
  // quitar): se compactan los arrays para no mandar huecos null al backend.
  for (const key of ['shippingAddresses', 'secondaryEmails', 'contacts'])
    if (Array.isArray(root[key])) root[key] = root[key].filter(Boolean);
  return root;
}

// setPath(obj, "a.b[0].c", v) → obj.a.b[0].c = v (crea objetos/arrays por el camino)
function setPath(root, path, value) {
  const parts = path.replace(/\[(\d+)\]/g, '.$1').split('.');
  let node = root;
  for (let i = 0; i < parts.length - 1; i++) {
    const key = parts[i];
    const nextIsIndex = /^\d+$/.test(parts[i + 1]);
    if (node[key] == null) node[key] = nextIsIndex ? [] : {};
    node = node[key];
  }
  node[parts[parts.length - 1]] = value;
}
