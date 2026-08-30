// Alta / edición de CLIENTE — formulario rico por secciones (básico, fiscal,
// direcciones de envío múltiples, comercial y contacto). Guarda el documento
// `client` y una `shipping-address` por cada dirección. Modelado sobre el alta de
// cliente del portal (views/new-client.js), con el mismo lenguaje visual.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, dig, setPath, delPath, fkOptions, flash, invalidateOptions, showJson } from '../util.js';

const uuid = () => (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2));
import { go } from '../router.js';

const COUNTRIES = [['ES', 'España'], ['PT', 'Portugal'], ['FR', 'Francia'], ['IT', 'Italia'],
  ['DE', 'Alemania'], ['GB', 'Reino Unido'], ['NL', 'Países Bajos'], ['BE', 'Bélgica'], ['US', 'EE. UU.']];

const field = (name, label, value = '', { type = 'text', wide = false } = {}) => `
  <p class="acc-field${wide ? ' wide' : ''}"><label><span>${esc(label)}</span>
    <input type="${type}" data-key="${esc(name)}" value="${esc(value ?? '')}" maxlength="200"></label></p>`;

const countrySelect = (name, value = 'ES') => `
  <p class="acc-field"><label><span>País</span>
    <select data-key="${esc(name)}">${COUNTRIES.map(([v, l]) =>
      `<option value="${v}"${v === (value || 'ES') ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select></label></p>`;

const addressFields = (prefix, a = {}) => `
  <div class="biz-grid">
    ${field(`${prefix}.streetAddress`, 'Dirección', a.streetAddress, { wide: false })}
    ${field(`${prefix}.num`, 'Número', a.num)}
    ${countrySelect(`${prefix}.countryIsoId`, a.countryIsoId)}
    ${field(`${prefix}.province`, 'Provincia', a.province)}
    ${field(`${prefix}.city`, 'Ciudad', a.city)}
    ${field(`${prefix}.zipCode`, 'Código postal', a.zipCode)}
  </div>`;

export default async function client(main, id) {
  const editing = !!id;
  let c = {};
  let rawPayload = null;
  let addresses = [];   // {id, payload}
  if (editing) {
    try { const d = await api.doc('client', id); rawPayload = d.payload; c = JSON.parse(d.payload); } catch { c = {}; }
    if (Array.isArray(c)) c = c[0] ?? {};
    const all = await api.docs('shipping-address').catch(() => ({ items: [] }));
    addresses = (all.items || []).filter(d => d.parentId === id).map(d => {
      let p = {}; try { p = JSON.parse(d.payload); } catch {}
      return { id: d.externalId, payload: p };
    });
  }
  const [groups, methods] = await Promise.all([fkOptions('client-group'), fkOptions('payment-method')]);
  const chosenGroups = (Array.isArray(c.groupIds) ? c.groupIds : []).map(String);
  const chosenMethods = (Array.isArray(c.payMethods) ? c.payMethods : []).map(x => String(typeof x === 'object' ? (x.id || x.code || '') : x));
  const fiscal = c.fiscalInfo || {};

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs"><a href="#/clients">Clientes</a> · <span>${editing ? 'Editar' : 'Nuevo'}</span></p>
        <h1 class="title">${editing ? esc(c.name || 'Cliente') : 'Nuevo cliente'}</h1>
        <p class="lead">Todo lo que el portal necesita del cliente: identificación, fiscalidad, direcciones de envío y condiciones comerciales.</p>
      </div>
      ${editing ? `<button type="button" class="btn-ghost" id="viewJson">${icons.list ? icons.list(15) : ''} Ver JSON recibido</button>` : ''}
    </div>
    <form class="mng-form nc-form" id="f" novalidate>
      <div id="notice"></div>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.building(20)}Datos básicos</h2></header>
        <div class="biz-card">
          ${field('name', 'Nombre comercial *', c.name, { wide: true })}
          <div class="biz-grid">
            ${field('externalReference', 'Código de cliente *', c.externalReference)}
            ${field('email', 'Email de contacto', c.email, { type: 'email' })}
            ${field('web', 'Web', c.web, { type: 'url' })}
            ${field('taxId', 'NIF / CIF', c.taxId)}
            ${field('phone', 'Teléfono', typeof c.phone === 'string' ? c.phone : (c.phone?.number || ''))}
          </div>
          <p class="acc-field"><label class="mng-check">
            <input type="checkbox" data-key="canShop" ${c.canShop !== false ? 'checked' : ''}> <span>Puede comprar en el portal</span></label></p>
        </div>
      </section>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.coin(20)}Información fiscal</h2></header>
        <div class="biz-card">
          <div class="biz-grid">
            ${field('fiscalInfo.fiscalName', 'Razón social', fiscal.fiscalName)}
            ${field('fiscalInfo.alias', 'Alias', fiscal.alias)}
            ${field('fiscalInfo.fiscalId.document', 'NIF fiscal', fiscal.fiscalId?.document)}
          </div>
          <p class="mng-subhead">Dirección fiscal</p>
          ${addressFields('fiscalInfo.address', fiscal.address || {})}
        </div>
      </section>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.truck(20)}Direcciones de envío</h2></header>
        <div class="biz-card">
          <p class="biz-hint">${icons.alert(16)}<span>Añade tantas como necesites. El cliente elegirá una al hacer el pedido.</span></p>
          <div id="shipHost"></div>
          <button type="button" class="btn-ghost nc-add" id="addShip">${icons.plus(15)} Añadir dirección</button>
        </div>
      </section>

      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.users(20)}Condiciones comerciales</h2></header>
        <div class="biz-card">
          <p class="acc-field wide"><label><span>Grupos de tarifa</span></label>
            <div class="mng-multi" id="groups">${groups.length ? groups.map(o =>
              `<label><input type="checkbox" value="${esc(o.value)}"${chosenGroups.includes(String(o.value)) ? ' checked' : ''}> ${esc(o.label)}</label>`).join('')
              : '<span class="mng-multi-empty">No hay grupos. Créalos en «Grupos».</span>'}</div></p>
          <p class="acc-field wide" style="margin-top:1rem"><label><span>Formas de pago disponibles</span></label>
            <div class="mng-multi" id="methods">${methods.length ? methods.map(o =>
              `<label><input type="checkbox" value="${esc(o.value)}" data-name="${esc(o.label)}"${chosenMethods.includes(String(o.value)) ? ' checked' : ''}> ${esc(o.label)}</label>`).join('')
              : '<span class="mng-multi-empty">No hay formas de pago. Créalas en «Formas de pago».</span>'}</div></p>
        </div>
      </section>

      <div class="acc-actions nc-actions">
        ${editing ? '<button type="button" class="btn-danger" id="del">Eliminar cliente</button>' : ''}
        <a class="btn-ghost" href="#/clients">Cancelar</a>
        <button type="submit" class="btn-primary">${editing ? 'Guardar cambios' : 'Crear cliente'}</button>
      </div>
    </form>`;

  const formEl = main.querySelector('#f');
  const notice = main.querySelector('#notice');

  const jsonBtn = main.querySelector('#viewJson');
  if (jsonBtn) jsonBtn.onclick = () =>
    showJson(`Cliente · ${id}`, rawPayload ?? c,
      rawPayload ? 'JSON recibido de Business Central' : 'Datos actuales de la ficha (creada en el portal)');
  const shipHost = main.querySelector('#shipHost');
  const removedAddrIds = [];

  const addShip = (addr = null) => {
    const wrap = document.createElement('div');
    wrap.className = 'nc-ship';
    if (addr?.id) wrap.dataset.addrId = addr.id;
    const a = addr?.payload?.address || {};
    wrap.innerHTML = `
      <div class="nc-ship-head"><b>Dirección de envío</b>
        <button type="button" class="btn-ghost nc-remove">${icons.close(14)} Quitar</button></div>
      ${field('alias', 'Alias (p. ej. Tienda centro)', addr?.payload?.alias || '')}
      ${addressFields('address', a)}`;
    wrap.querySelector('.nc-remove').onclick = () => {
      if (wrap.dataset.addrId) removedAddrIds.push(wrap.dataset.addrId);
      wrap.remove();
    };
    shipHost.append(wrap);
  };
  main.querySelector('#addShip').onclick = () => addShip();
  if (addresses.length) addresses.forEach(addShip); else addShip();

  // ── Guardar ──
  formEl.onsubmit = async event => {
    event.preventDefault();
    notice.innerHTML = '';
    const body = collectClient();
    if (!body.name) return warn(notice, 'El nombre comercial es obligatorio.');
    if (!body.externalReference) return warn(notice, 'El código de cliente es obligatorio.');

    // Reúne y VALIDA las direcciones ANTES de guardar el cliente (así no hay guardado
    // parcial): un bloque en blanco se ignora; uno con datos exige alias.
    const toSave = [];
    for (const wrap of shipHost.querySelectorAll('.nc-ship')) {
      const addr = collectAddress(wrap);
      if (!addr.alias && isAddrEmpty(addr.address)) continue;     // bloque vacío → se ignora
      if (!addr.alias) return warn(notice, 'Cada dirección de envío necesita un alias (o déjala en blanco para descartarla).');
      toSave.push({ addrId: wrap.dataset.addrId || uuid(), addr });
    }

    const btn = formEl.querySelector('button[type=submit]');
    btn.disabled = true;
    const clientId = editing ? id : uuid();
    try {
      await api.saveEntity('client', clientId, body);
      for (const { addrId, addr } of toSave) await api.saveEntity('shipping-address', addrId, addr, clientId);
      for (const gone of removedAddrIds) await api.delEntity('shipping-address', gone).catch(() => {});
      invalidateOptions('client');
      flash(editing ? 'Cliente guardado.' : 'Cliente creado.');
      go('#/clients');
    } catch (failure) {
      btn.disabled = false;
      warn(notice, failure.body?.error || failure.message || 'No se pudo guardar el cliente.');
    }
  };

  if (editing) main.querySelector('#del').onclick = async () => {
    if (!confirm('¿Eliminar este cliente? Sus accesos y pedidos dejarán de estar asociados.')) return;
    try {
      for (const a of addresses) await api.delEntity('shipping-address', a.id).catch(() => {});
      await api.delEntity('client', id);
      invalidateOptions('client');
      flash('Cliente eliminado.');
      go('#/clients');
    } catch (e) { warn(notice, e.body?.error || e.message); }
  };

  function collectClient() {
    const body = { markets: ['es'], productSegments: [], ...structuredClone(c) };
    // Campos simples y anidados de las secciones (todo lo que no sea dirección de envío/multi)
    for (const el of formEl.querySelectorAll('[data-key]')) {
      if (el.closest('.nc-ship')) continue;         // las direcciones se tratan aparte
      const key = el.dataset.key;
      if (el.type === 'checkbox') { setPath(body, key, el.checked); continue; }
      const raw = (el.value ?? '').trim();
      raw ? setPath(body, key, raw) : delPath(body, key);   // vaciar un campo lo borra de verdad
    }
    body.groupIds = [...main.querySelectorAll('#groups input:checked')].map(i => i.value);
    body.payMethods = [...main.querySelectorAll('#methods input:checked')].map(i => ({ id: i.value, name: i.dataset.name }));
    return body;
  }

  function collectAddress(wrap) {
    const out = {};
    for (const el of wrap.querySelectorAll('[data-key]')) {
      const raw = (el.value ?? '').trim();
      if (raw) setPath(out, el.dataset.key, raw);
    }
    return out;
  }
}

// Vacía = sin datos reales. El país es un select que SIEMPRE vale "ES", así que no
// cuenta para decidir si el bloque está en blanco (si no, nunca se saltaría).
const isAddrEmpty = a => {
  if (!a) return true;
  const rest = { ...a }; delete rest.countryIsoId;
  return !Object.values(rest).some(v => String(v || '').trim());
};
function warn(host, text) {
  host.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(text)}</span></div></div>`;
  host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}
