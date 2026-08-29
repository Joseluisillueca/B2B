// Integración BC / Notificaciones — 4 vistas: Configuración (eventos×canales+transformers),
// Conexiones, Origen de documentos y Notificaciones realizadas. Reutiliza el diseño /manage.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash } from '../util.js';

const SAMPLE = {
  salesOrders: '{"id":"<guid>","clientId":"<guid>","shippingAddressId":"<guid>","referenceOrder":"REF-1","payMethodId":"sepa30","incotermId":"","saleId":"","total":{"value":121},"totalTax":{"value":21},"totalDiscount":{"value":0},"totalCart":{"value":100},"totalTransport":{"value":0},"totalCartDiscount":{"value":0},"items":[{"id":"l1","productId":"<guid>","modelId":"<guid>","sku":"SKU1","quantity":5,"productName":{"es_ES":"Producto X"},"price":{"value":18},"priceOriginal":{"value":20},"amount":{"value":90},"totalDiscounts":{"value":10},"stockServiceId":"SS1"}],"stockServices":[{"stockServiceId":"SS1","from":"01/09/2026","to":"15/09/2026","baseFrom":"2026-09-01","baseTo":"2026-09-15"}]}',
  customers: '{"id":"<guid>","name":"Tienda Ejemplo","email":"tienda@ejemplo.com","web":"","phone":{"number":"600000000"},"fiscalInfo":{"fiscalName":"Ejemplo SL","alias":"Ejemplo","fiscalId":{"document":"B12345678"},"address":{"streetAddress":"Calle Mayor","num":"1","city":"Valencia","province":"Valencia","zipCode":"46001","countryIsoId":"ES"}},"shippingAddresses":[{"streetAddress":"Calle Colón","num":"10","city":"Valencia","province":"Valencia","zipCode":"46004","countryIsoId":"ES","alias":"Tienda"}]}',
  shipToAddresss: '{"clientID":"<guid>","shippingAddressId":"<guid>","shippingAddressAlias":"Tienda Centro","shippingAddress":{"streetAddress":"Calle Colón","num":"10","city":"Valencia","province":"Valencia","zipCode":"46004","countryIsoId":"ES"}}',
};
const sampleFor = ep => SAMPLE[ep] || '{}';

// ══════════ Notificaciones → Configuración ══════════
export async function configView(main) {
  const data = await api.intEvents();
  paintConfig(main, data.events);
}

function paintConfig(main, events) {
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs">Integración · Notificaciones</p>
      <h1 class="title">Configuración</h1>
      <p class="lead">Cada evento se envía por uno o varios canales. Email = destinatarios; Business Central = endpoint + transformer JSON (JUST.net).</p>
    </div></div>
    <div class="grid-scroll"><table class="grid">
      <thead><tr><th>Evento</th><th>Descripción</th><th>Canales</th></tr></thead>
      <tbody>${events.map(ev => `
        <tr>
          <td class="grid-link">${esc(ev.name)}${ev.fixed ? ' <span class="grid-chip">Fijo</span>' : ''}</td>
          <td>${esc(ev.description)}</td>
          <td>${ev.channels.length ? ev.channels.map(c => `
            <button class="grid-chip" data-ev="${esc(ev.key)}" data-ch="${c.id}" style="cursor:pointer;margin:.1rem .2rem">
              ${c.channelType === 'email' ? icons.send(13) : icons.layers(13)} ${c.channelType === 'email' ? 'Email' : 'Business Central'}${c.endpoint ? ' · ' + esc(c.endpoint) : ''}
            </button>`).join('') : '<span class="muted">Sin canales</span>'}</td>
        </tr>`).join('')}</tbody>
    </table></div>`;
  main.querySelectorAll('[data-ch]').forEach(b => b.onclick = () => {
    const ev = events.find(e => e.key === b.dataset.ev);
    const ch = ev.channels.find(c => String(c.id) === b.dataset.ch);
    editChannel(main, ev, ch);
  });
}

function editChannel(main, ev, ch) {
  const isBc = ch.channelType === 'business-central';
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs"><a href="#/notifications-config">Configuración</a> · <span>${esc(ev.name)}</span></p>
      <h1 class="title">Canal ${isBc ? 'Business Central' : 'Email'}</h1>
    </div></div>
    <form class="mng-form" id="chf">
      <div id="notice"></div>
      ${isBc ? `
        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.layers(20)}Endpoint y transformer</h2></header>
          <div class="biz-card">
            <p class="acc-field"><label><span>Endpoint (API page de BC)</span><input id="endpoint" value="${esc(ch.endpoint || '')}"></label></p>
            <p class="acc-field"><label><span>Transformer (JUST.net)</span>
              <textarea id="transformer" rows="16" style="font-family:monospace;font-size:.82rem">${esc(ch.transformer || '')}</textarea></label>
              <span class="acc-hint">Convierte el JSON interno del portal al JSON que espera BC. Funciones: #valueof, #loop, #currentvalueatpath…</span></p>
            <div style="display:flex;gap:.6rem;flex-wrap:wrap">
              <button type="button" class="btn-ghost" id="test">${icons.spin(15)} Probar transformación</button>
              <button type="button" class="btn-ghost" id="restore">${icons.left(15)} Restaurar por defecto</button>
            </div>
            <div id="testbox" style="display:none;margin-top:1rem" class="ed-grid">
              <div class="ed-field"><label class="grow"><span>JSON base (entrada de ejemplo)</span>
                <textarea id="sample" rows="10" style="font-family:monospace;font-size:.78rem"></textarea></label></div>
              <div class="ed-field"><label class="grow"><span>Resultado</span>
                <textarea id="result" rows="10" readonly style="font-family:monospace;font-size:.78rem;background:var(--surface)"></textarea></label></div>
            </div>
          </div></section>
      ` : `
        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.send(20)}Destinatarios</h2></header>
          <div class="biz-card">
            <div class="biz-grid">
              <p class="acc-field"><label><span>Para (To)</span><input id="to" value="${esc(ch.toVars || '')}"></label></p>
              <p class="acc-field"><label><span>CC</span><input id="cc" value="${esc(ch.ccVars || '')}"></label></p>
              <p class="acc-field"><label><span>CCO (BCC)</span><input id="bcc" value="${esc(ch.bccVars || '')}"></label></p>
            </div>
            <p class="acc-hint">Variables: {companyEmail} {saleEmail} {clientEmail} {userEmail}, o emails literales separados por coma.</p>
          </div></section>`}
      <label class="mng-check" style="margin:.4rem 0 1rem"><input type="checkbox" id="active" ${ch.active !== false ? 'checked' : ''}> <span>Canal activo</span></label>
      <div class="acc-actions nc-actions">
        ${ch.fixed ? '' : `<button type="button" class="btn-danger" id="del">Eliminar canal</button>`}
        <a class="btn-ghost" href="#/notifications-config">Volver</a>
        <button type="submit" class="btn-primary">Guardar canal</button>
      </div>
    </form>`;

  const $ = id => main.querySelector('#' + id);
  if (isBc) {
    $('test').onclick = async () => {
      const box = $('testbox');
      if (box.style.display === 'none') { box.style.display = ''; if (!$('sample').value) $('sample').value = sampleFor($('endpoint').value.trim()); }
      try { const r = await api.intTestTransform($('transformer').value, $('sample').value); $('result').value = pretty(r.result); }
      catch (e) { $('result').value = 'ERROR: ' + (e.body?.error || e.message); }
    };
    $('restore').onclick = async () => {
      if (!confirm('¿Restaurar el transformer por defecto? Perderás los cambios de este canal.')) return;
      try { const r = await api.intChannelDefault(ch.id); $('transformer').value = r.transformer; flash('Transformer por defecto cargado. Recuerda Guardar.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
  }
  $('chf').onsubmit = async e => {
    e.preventDefault();
    const active = $('active').checked;
    const b = isBc
      ? { endpoint: $('endpoint').value.trim(), transformer: $('transformer').value, active }
      : { toVars: $('to').value.trim(), ccVars: $('cc').value.trim(), bccVars: $('bcc').value.trim(), active };
    try { await api.intSaveChannel(ch.id, b); flash('Canal guardado.'); location.hash = '#/notifications-config'; }
    catch (err) { $('notice').innerHTML = `<div class="notice notice-error">${esc(err.body?.error || err.message)}</div>`; }
  };
  if ($('del')) $('del').onclick = async () => {
    if (!confirm('¿Eliminar este canal?')) return;
    try { await api.intDelChannel(ch.id); flash('Canal eliminado.'); location.hash = '#/notifications-config'; }
    catch (err) { flash(err.body?.error || err.message, 'err'); }
  };
}

// ══════════ Conexiones ══════════
export async function connectionsView(main) {
  const s = await api.intSettings();
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs">Integración · Conectividad</p>
      <h1 class="title">Conexiones</h1>
      <p class="lead">Credenciales de Business Central (OAuth2) y API REST. Mientras BC esté sin configurar, los envíos se registran como “simulado”.</p>
    </div></div>
    <form class="mng-form" id="cf"><div id="notice"></div>
      <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.layers(20)}Business Central ${s.bcConfigured ? '<span class="grid-chip ok">Configurado</span>' : '<span class="grid-chip warn">Sin configurar</span>'}</h2></header>
        <div class="biz-card"><div class="biz-grid">
          <p class="acc-field wide"><label><span>URL base</span><input id="bcBaseUrl" value="${esc(s.bcBaseUrl || '')}" placeholder="https://api.businesscentral.dynamics.com/v2.0/{tenant}/{env}/api/mitoprojects/b2b/v1.0/companies({companyId})"></label></p>
          <p class="acc-field wide"><label><span>URL de token</span><input id="bcTokenUrl" value="${esc(s.bcTokenUrl || '')}" placeholder="https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"></label></p>
          <p class="acc-field"><label><span>Client ID</span><input id="bcClientId" value="${esc(s.bcClientId || '')}"></label></p>
          <p class="acc-field"><label><span>Client Secret</span><input id="bcClientSecret" type="password" placeholder="${s.hasSecret ? '•••••• (sin cambios)' : ''}"></label></p>
          <p class="acc-field"><label><span>Scope</span><input id="bcScope" value="${esc(s.bcScope || 'https://api.businesscentral.dynamics.com/.default')}"></label></p>
        </div></div></section>
      <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.layers(20)}API REST (genérica)</h2></header>
        <div class="biz-card"><div class="biz-grid">
          <p class="acc-field wide"><label><span>URL base</span><input id="apiRestBaseUrl" value="${esc(s.apiRestBaseUrl || '')}"></label></p>
        </div></div></section>
      <div class="acc-actions"><button type="submit" class="btn-primary">Guardar conexiones</button></div>
    </form>`;
  main.querySelector('#cf').onsubmit = async e => {
    e.preventDefault();
    const $ = id => main.querySelector('#' + id).value.trim();
    const b = { bcBaseUrl: $('bcBaseUrl'), bcTokenUrl: $('bcTokenUrl'), bcClientId: $('bcClientId'), bcClientSecret: main.querySelector('#bcClientSecret').value, bcScope: $('bcScope'), apiRestBaseUrl: $('apiRestBaseUrl') };
    try { const r = await api.intSaveSettings(b); flash(r.bcConfigured ? 'Conexiones guardadas · BC configurado.' : 'Conexiones guardadas.'); connectionsView(main); }
    catch (err) { main.querySelector('#notice').innerHTML = `<div class="notice notice-error">${esc(err.body?.error || err.message)}</div>`; }
  };
}

// ══════════ Origen de documentos ══════════
export async function docSourcesView(main) {
  const data = await api.intDocSources();
  const label = { order: 'Pedido', 'delivery-note': 'Albarán', invoice: 'Factura' };
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs">Integración · Conectividad</p>
      <h1 class="title">Origen de documentos</h1>
      <p class="lead">Cómo se resuelve el PDF de cada documento. Usa {id} (SystemId del documento en BC) y {externalReference}.</p>
    </div></div>
    ${data.items.map(d => `
      <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.fileDown(20)}${esc(label[d.docType] || d.docType)}</h2></header>
        <form class="biz-card mng-form" data-type="${esc(d.docType)}">
          <div class="biz-grid">
            <p class="acc-field"><label><span>Método</span><input value="${esc(d.method)}" name="method"></label></p>
            <p class="acc-field wide"><label><span>Endpoint</span><input value="${esc(d.endpoint)}" name="endpoint"></label></p>
          </div>
          <p class="acc-field"><label><span>Transformer (JUST.net)</span><textarea rows="3" name="transformer" style="font-family:monospace;font-size:.82rem">${esc(d.transformer)}</textarea></label></p>
          <div class="acc-actions"><button type="submit" class="btn-primary">Guardar</button></div>
        </form></section>`).join('')}`;
  main.querySelectorAll('form[data-type]').forEach(f => f.onsubmit = async e => {
    e.preventDefault();
    const b = { method: f.method.value.trim(), endpoint: f.endpoint.value.trim(), transformer: f.transformer.value, sourceType: 'business-central', active: true };
    try { await api.intSaveDocSource(f.dataset.type, b); flash('Origen guardado.'); }
    catch (err) { flash(err.body?.error || err.message, 'err'); }
  });
}

// ══════════ Notificaciones realizadas ══════════
export async function logsView(main) {
  const data = await api.intLogs();
  const chip = s => ({ completed: 'ok', errors: 'danger', simulated: 'warn', skipped: 'off' }[s] || 'off');
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs">Integración · Notificaciones</p>
      <h1 class="title">Notificaciones realizadas</h1>
      <p class="lead">Historial de envíos por canal y su estado.</p>
    </div></div>
    <div class="grid-scroll"><table class="grid">
      <thead><tr><th>Fecha</th><th>Evento</th><th>Entidad</th><th>Canal</th><th>Estado</th><th>Detalle</th></tr></thead>
      <tbody>${(data.items || []).length ? data.items.map(l => `
        <tr><td>${esc(new Date(l.createdAt).toLocaleString('es-ES'))}</td>
          <td>${esc(l.eventKey)}</td>
          <td>${esc(l.entityType)} <span class="grid-id">${esc(String(l.entityId).slice(0, 12))}</span></td>
          <td>${l.channelType === 'email' ? 'Email' : 'Business Central'}</td>
          <td><span class="grid-chip ${chip(l.status)}">${esc(l.status)}</span></td>
          <td class="muted" style="max-width:22rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(l.detail || '')}">${esc(l.detail || '')}</td></tr>`).join('')
        : '<tr class="grid-empty"><td colspan="6">Todavía no hay notificaciones.</td></tr>'}</tbody>
    </table></div>`;
}

const pretty = s => { try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; } };
