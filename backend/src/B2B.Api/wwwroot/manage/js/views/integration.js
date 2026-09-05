// Integración BC / Notificaciones — 4 vistas: Configuración (eventos×canales+transformers),
// Conexiones, Origen de documentos y Notificaciones realizadas. Reutiliza el diseño /manage.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash, showJson } from '../util.js';
import { setBrand } from '/portal/js/branding.js';

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
      <p class="crumbs"><a href="#/notifications-config" id="crumbBack">Configuración</a> · <span>${esc(ev.name)}</span></p>
      <h1 class="title">Canal ${isBc ? 'Business Central' : 'Email'}</h1>
    </div></div>
    <form class="mng-form" id="chf">
      <div id="notice"></div>
      ${isBc ? `
        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.layers(20)}Endpoint y transformer</h2></header>
          <div class="biz-card">
            <p class="acc-field"><label><span>Endpoint (API page de BC)</span><input id="endpoint" value="${esc(ch.endpoint || '')}"></label></p>
            <p class="acc-field"><label><span>1 · JSON de entrada <span class="acc-hint" style="display:inline;font-weight:400">(lo que genera el portal)</span></span>
              <textarea id="sample" rows="8" style="font-family:monospace;font-size:.8rem"></textarea></label>
              <span class="acc-hint">Ejemplo según el endpoint; edítalo para probar con tus datos.</span></p>
            <div class="nc-flowarrow">${icons.down ? icons.down(18) : '↓'}</div>
            <p class="acc-field"><label><span>2 · Transformer (JUST.net)</span>
              <textarea id="transformer" rows="14" style="font-family:monospace;font-size:.82rem">${esc(ch.transformer || '')}</textarea></label>
              <span class="acc-hint">Convierte el JSON de entrada al JSON que espera BC. Funciones: #valueof, #loop, #currentvalueatpath…</span></p>
            <div style="display:flex;gap:.6rem;flex-wrap:wrap">
              <button type="button" class="btn-primary" id="test">${icons.spin(15)} Probar transformación</button>
              <button type="button" class="btn-ghost" id="restore">${icons.left(15)} Restaurar por defecto</button>
            </div>
            <div class="nc-flowarrow">${icons.down ? icons.down(18) : '↓'}</div>
            <p class="acc-field"><label><span>3 · Resultado <span class="acc-hint" style="display:inline;font-weight:400">(JSON que se envía a BC)</span></span>
              <textarea id="result" rows="10" readonly style="font-family:monospace;font-size:.8rem;background:var(--surface)" placeholder="Pulsa «Probar transformación» para ver el resultado."></textarea></label></p>
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
          </div></section>
        <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.send(20)}Contenido del email</h2></header>
          <div class="biz-card">
            <p class="acc-field"><label><span>Asunto</span><input id="subject" value="${esc(ch.subject || '')}"></label></p>
            <p class="acc-field"><label><span>Cuerpo (HTML)</span>
              <textarea id="bodyHtml" rows="12" style="font-family:monospace;font-size:.82rem">${esc(ch.bodyHtml || '')}</textarea></label>
              <span class="acc-hint">Solo el <b>cuerpo</b>; la cabecera/pie de marca los pone el diseño global (Conexiones → Diseño de email). Variables <code>{{eventName}}</code> <code>{{ref}}</code> <code>{{clientEmail}}</code>… y para activación <code>{{name}}</code> <code>{{link}}</code> <code>{{button}}</code>.</span></p>
            <div style="display:flex;gap:.6rem;flex-wrap:wrap">
              <button type="button" class="btn-primary" id="preview">Vista previa</button>
              <button type="button" class="btn-ghost" id="restore">${icons.left(15)} Restaurar por defecto</button>
            </div>
            <div id="previewbox" style="display:none;margin-top:1rem">
              <p class="acc-hint" style="margin:0 0 .35rem"><b>Asunto:</b> <span id="previewSubject"></span></p>
              <iframe id="previewFrame" title="Vista previa del email" style="width:100%;height:440px;border:1px solid var(--line);background:#fff"></iframe>
            </div>
          </div></section>`}
      <label class="mng-check" style="margin:.4rem 0 1rem"><input type="checkbox" id="active" ${ch.active !== false ? 'checked' : ''}> <span>Canal activo</span></label>
      <div class="acc-actions nc-actions">
        ${ch.fixed ? '' : `<button type="button" class="btn-danger" id="del">Eliminar canal</button>`}
        <button type="button" class="btn-ghost" id="back">Volver</button>
        <button type="submit" class="btn-primary">Guardar canal</button>
      </div>
    </form>`;

  const $ = id => main.querySelector('#' + id);
  if (isBc) {
    // Entrada de ejemplo visible desde el principio (flujo entrada → transformer → resultado).
    if (!$('sample').value) $('sample').value = sampleFor($('endpoint').value.trim());
    $('test').onclick = async () => {
      try { const r = await api.intTestTransform($('transformer').value, $('sample').value); $('result').value = pretty(r.result); }
      catch (e) { $('result').value = 'ERROR: ' + (e.body?.error || e.message); }
    };
    $('restore').onclick = async () => {
      if (!confirm('¿Restaurar el transformer por defecto? Perderás los cambios de este canal.')) return;
      try { const r = await api.intChannelDefault(ch.id); $('transformer').value = r.transformer; flash('Transformer por defecto cargado. Recuerda Guardar.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
  } else {
    // Canal Email: vista previa (cuerpo dentro del layout de marca, con variables de ejemplo).
    $('preview').onclick = async () => {
      try {
        const r = await api.intPreviewEmail({ eventKey: ev.key, subject: $('subject').value, bodyHtml: $('bodyHtml').value });
        $('previewbox').style.display = '';
        $('previewSubject').textContent = r.subject || '(sin asunto)';
        $('previewFrame').srcdoc = r.html || '';
      } catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
    $('restore').onclick = async () => {
      if (!confirm('¿Restaurar el asunto y el cuerpo por defecto? Perderás los cambios de este canal.')) return;
      try { const r = await api.intChannelDefault(ch.id); $('subject').value = r.subject || ''; $('bodyHtml').value = r.bodyHtml || ''; flash('Contenido por defecto cargado. Recuerda Guardar.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
  }
  $('chf').onsubmit = async e => {
    e.preventDefault();
    const active = $('active').checked;
    const b = isBc
      ? { endpoint: $('endpoint').value.trim(), transformer: $('transformer').value, active }
      : { toVars: $('to').value.trim(), ccVars: $('cc').value.trim(), bccVars: $('bcc').value.trim(),
          subject: $('subject').value, bodyHtml: $('bodyHtml').value, active };
    try { await api.intSaveChannel(ch.id, b); flash('Canal guardado.'); configView(main); }
    catch (err) { $('notice').innerHTML = `<div class="notice notice-error">${esc(err.body?.error || err.message)}</div>`; }
  };
  // "Volver": editChannel se pinta SIN cambiar el hash (es una sub-vista), así que un
  // enlace al mismo hash no dispara el router. Se repinta la configuración directamente.
  main.querySelector('#back').onclick = () => configView(main);
  main.querySelector('#crumbBack').onclick = e => { e.preventDefault(); configView(main); };
  if ($('del')) $('del').onclick = async () => {
    if (!confirm('¿Eliminar este canal?')) return;
    try { await api.intDelChannel(ch.id); flash('Canal eliminado.'); configView(main); }
    catch (err) { flash(err.body?.error || err.message, 'err'); }
  };
}

// ══════════ Tokens de estilo de la instancia (marca configurable extendida) ══════════
// Contrato compartido con el portal y el backend:
//   GET  /api/portal/branding            →  { …, "tokens": { … } | null }
//   PUT  /api/admin/integration/branding →  acepta "tokens" (null o {} = limpiar)
// CADA token es OPCIONAL y su ausencia significa «el valor que hoy trae app.css»: por eso
// los campos vacíos NO se mandan y el portal de MITO se queda exactamente como está.
const HERO_DEFAULT = 'grayscale(1) contrast(1.05)';

// [clave, etiqueta, valor por defecto (app.css), ayuda en cristiano]
const TOKEN_COLORS = [
  ['paper', 'Fondo de página', '#f3f2f2', 'El papel sobre el que se apoya todo el portal.'],
  ['surface', 'Fondo de bloques', '#eae9e9', 'Bandas y zonas destacadas: filtros, pies de sección, resúmenes.'],
  ['ink', 'Texto principal', '#201e1d', 'El color de los textos y los titulares.'],
  ['headerBg', 'Fondo de la cabecera', '#201e1d', 'La barra superior del portal (hoy, negra).'],
  ['headerInk', 'Texto de la cabecera', '#f3f2f2', 'Nombre, menús e iconos sobre esa barra.'],
  ['card', 'Fondo de paneles', '#f8f4f4', 'Banda de pestañas, tarjetas, resumen del pedido y cajón del carrito.'],
  ['rule', 'Color de los filetes de sección', '#111111', 'Las reglas que abren cada bloque: cabecera de tabla, KPI, precio, resumen del pedido.'],
  ['accent', 'Acento secundario', '#e15b47', 'Favoritos, avisos de deuda, barras de los cuadros de mando. Ponlo igual que el color de marca si quieres un único acento.'],
];
// Medidas con unidad: [clave, etiqueta, ejemplo, ayuda]
const TOKEN_SIZES = [
  ['radius', 'Redondeo general', '12px', 'Esquinas de tarjetas, campos y fotos. Hoy el portal va a 0px (esquina viva).'],
  ['radiusButton', 'Redondeo de botones', '50px', 'Solo los botones. 50px los deja en forma de píldora.'],
  ['tracking', 'Espaciado entre letras', '.06em', 'Separa las letras de titulares y botones. Admite px, rem, em o %.'],
  ['ruleWidth', 'Grosor de los filetes de sección', '1px', 'Hoy el portal usa 2px. A 1px las reglas se leen como filete editorial.'],
];
// ── Espejo de la validación del servidor (IntegrationEndpoints.NormalizeBrandTokens) ──
// Ni más laja ni más estricta: lo que pasa aquí lo acepta el PUT, y lo que aquí se rechaza
// también lo rechazaría él. Si fuera más laja, el usuario vería el 400 crudo del servidor
// en vez del mensaje del formulario (y sin que se le señale el campo); si fuera más
// estricta, el back-office prohibiría valores que el portal sí sabe aplicar.
const isHex = v => /^#[0-9a-f]{6}$/i.test(v);
// Medida CSS de verdad: un solo punto decimal, al menos un dígito y unidad OBLIGATORIA.
// Fuera «pt» (el servidor solo admite px|rem|em|%) y fuera el «0» pelado, que también
// daba 400. Sin `i`, como el regex del servidor y el del portal.
const isSize = v => v.length <= 20 && /^-?(\d+(\.\d+)?|\.\d+)(px|rem|em|%)$/.test(v);
// URL de recurso (logo oscuro, favicon, hoja de la tipografía): acaba en un src/href y
// dentro del url("…") de un @font-face, así que se cierran esquemas ejecutables y todo
// lo que pueda romper esos dos contextos.
const badScheme = v => /^(javascript|data|vbscript):/i.test(v.replace(/[\s\p{Cc}]/gu, ''));
const badUrlChar = v => /[\s\p{Cc}"'()<>\\;{}]/u.test(v);
// Valor que acaba DENTRO de una declaración CSS (heroFilter): no puede cerrarla, ni
// escapar un carácter («\75rl(» era un url() válido en CSS), ni abrir un comentario.
const badCss = v => /[;{}<\\]/.test(v) || v.includes('/*') || v.includes('*/')
  || v.replace(/\s/g, '').toLowerCase().includes('url(');
// Valor que acaba dentro de una cadena CSS entre comillas (fontFamily).
const badCssString = v => /["'\\<>{};]/.test(v);
// Correo: el patrón exacto de asEmail() del portal y de BrandEmail del servidor.
const isEmail = v => /^[^\s@<>"']+@[^\s@<>"']+\.[^\s@<>"']+$/.test(v);
// Peso de los titulares: una centena de 100 a 900 (asWeight del portal / BrandWeight del servidor).
const isWeight = v => /^[1-9]00$/.test(v);
// Pesos que ofrece el desplegable: todos los que entiende font-weight, con el nombre usual.
const WEIGHTS = [['400', '400 · Normal'], ['500', '500 · Medio'], ['600', '600 · Semibold'],
  ['700', '700 · Bold'], ['800', '800 · Extrabold'], ['900', '900 · Black']];

/** Nº de tokens con valor (para el chip del acordeón). */
const countTokens = tk => Object.values(tk || {}).filter(v => v !== null && v !== '' && v !== false).length;

/** Campo de color: selector + hexadecimal, como el acento de la marca. */
const colorField = (key, label, def, hint, value) => `
  <p class="acc-field"><label for="tk_${key}"><span>${esc(label)}</span></label>
    <span class="brt-color">
      <input type="color" id="tk_${key}_pick" data-tkpick="${key}" aria-label="Elegir ${esc(label.toLowerCase())}"
        value="${esc(isHex(value) ? value : def)}">
      <input id="tk_${key}" data-tkhex="${key}" value="${esc(isHex(value) ? value : '')}"
        placeholder="${esc(def)}" spellcheck="false" inputmode="text">
    </span>
    <span class="acc-hint">${hint} Vacío = <code>${esc(def)}</code>, el de MITO.</span></p>`;

// Estado del acordeón, a nivel de módulo: `saveBranding` repinta TODA la vista con
// `connectionsView(main)`, así que sin recordarlo quien está afinando el estilo (varios
// guardados seguidos) tenía que volver a abrir «Avanzado» y bajar hasta su campo cada vez.
let brtOpen = false;

/** Acordeón «Avanzado · estilo de la instancia», plegado la primera vez. */
function tokensPanel(tk, open) {
  const v = k => (tk[k] === null || tk[k] === undefined ? '' : String(tk[k]));
  const heroRaw = v('heroFilter').trim();
  const hero = !heroRaw || heroRaw === HERO_DEFAULT ? 'default'
    : heroRaw.toLowerCase() === 'none' ? 'none' : 'custom';
  const n = countTokens(tk);
  return `
    <div class="brt">
      <button type="button" class="brt-toggle" id="brtToggle" aria-expanded="${open ? 'true' : 'false'}" aria-controls="brtPanel">
        <span class="brt-caret">${icons.down(16)}</span>
        <span class="brt-toggle-t">Avanzado · estilo de la instancia</span>
        <span class="grid-chip ${n ? 'ok' : 'off'}">${n ? `${n} ajuste${n === 1 ? '' : 's'}` : 'Por defecto'}</span>
      </button>
      <div class="brt-panel" id="brtPanel" ${open ? '' : 'hidden'}>
        <p class="acc-hint brt-intro">Tipografía, colores y formas de <b>esta</b> instancia, para clientes con
          una estética propia. Todo es opcional: <b>lo que dejes vacío se queda como está hoy</b>. Se guarda con
          el mismo botón «Guardar marca».</p>

        <h3 class="brt-group">Tipografía</h3>
        <p class="acc-field wide"><label><span>Hoja de la tipografía (URL)</span>
          <input id="tk_fontUrl" value="${esc(v('fontUrl'))}" placeholder="https://fonts.googleapis.com/css2?family=…" spellcheck="false"></label>
          <span class="brt-inline"><button type="button" class="btn-ghost" id="tkFontUp">${icons.upload(15)} Subir .woff2</button></span>
          <span class="acc-hint">El enlace de la webfont (por ejemplo el CSS que da Google Fonts) o el fichero
            <code>.woff2</code> ya alojado. Vacío = la tipografía de siempre del portal.</span></p>
        <!-- Solo por extensión: Windows no registra MIME para .woff2 y el navegador la
             manda como application/octet-stream (el servidor ya lo contempla y comprueba
             la cabecera wOF2 del fichero), así que filtrar por «font/woff2» no casaría. -->
        <input type="file" id="tkFontFile" accept=".woff2" hidden>
        <div class="biz-grid">
          <p class="acc-field"><label><span>Familia tipográfica</span>
            <input id="tk_fontFamily" value="${esc(v('fontFamily'))}" placeholder="GillSansMTLight" spellcheck="false"></label>
            <span class="acc-hint">El nombre de la familia tal y como la declara la webfont. Si no cuadra con la
              hoja de arriba, no se verá el cambio.</span></p>
          <p class="acc-field"><span>Mayúsculas</span>
            <label class="mng-check"><input type="checkbox" id="tk_caps" ${tk.caps === true ? 'checked' : ''}>
              <span>Titulares y botones en MAYÚSCULAS</span></label>
            <span class="acc-hint">Estética de moda/lujo. Desactivado, los textos van tal y como se escriben.</span></p>
          <p class="acc-field"><label for="tk_displayWeight"><span>Peso de los titulares</span></label>
            <select id="tk_displayWeight">
              <option value="">Por defecto (500 en página, 800 en acceso y portada)</option>
              ${WEIGHTS.map(([w, label]) => `<option value="${w}" ${v('displayWeight') === w ? 'selected' : ''}>${label}</option>`).join('')}
            </select>
            <span class="acc-hint">Un solo peso para TODOS los titulares (catálogo, ficha, pedidos, portada,
              acceso). La webfont tiene que traer ese peso: con Google Fonts, pide el rango
              (<code>wght@400..900</code>).</span></p>
        </div>

        <h3 class="brt-group">Formas y espaciado</h3>
        <div class="biz-grid">
          ${TOKEN_SIZES.map(([key, label, ex, hint]) => `
            <p class="acc-field"><label><span>${esc(label)}</span>
              <input id="tk_${key}" value="${esc(v(key))}" placeholder="${esc(ex)}" spellcheck="false"></label>
              <span class="acc-hint">${hint} <b>Con unidad</b> (p. ej. <code>${esc(ex)}</code>).</span></p>`).join('')}
        </div>

        <h3 class="brt-group">Colores del portal</h3>
        <div class="biz-grid">
          ${TOKEN_COLORS.map(([key, label, def, hint]) => colorField(key, label, def, hint, v(key))).join('')}
        </div>

        <h3 class="brt-group">Imágenes</h3>
        <div class="biz-grid">
          <p class="acc-field"><span>Logo para fondos oscuros</span>
            <span id="tkDarkBox" class="brt-media"></span>
            <input type="file" id="tkDarkFile" accept="image/*" hidden>
            <span class="acc-hint">Versión clara del logo para la cabecera del portal cuando el fondo es
              oscuro. Sobre fondo claro se sigue usando el logo de arriba. Sin esta versión se usa el de
              siempre.</span></p>
          <p class="acc-field"><span>Icono de pestaña (favicon)</span>
            <span id="tkFavBox" class="brt-media"></span>
            <input type="file" id="tkFavFile" accept="image/png,image/svg+xml,image/x-icon,.ico" hidden>
            <span class="acc-hint">El iconito que sale en la pestaña del navegador. Cuadrado, PNG, SVG o ICO
              (32×32 o mayor).</span></p>
          <p class="acc-field"><label for="tk_heroMode"><span>Filtro de las fotos de portada</span></label>
            <select id="tk_heroMode">
              <option value="default" ${hero === 'default' ? 'selected' : ''}>Por defecto (gris)</option>
              <option value="none" ${hero === 'none' ? 'selected' : ''}>Sin filtro (color original)</option>
              <option value="custom" ${hero === 'custom' ? 'selected' : ''}>Personalizado…</option>
            </select>
            <!-- El <label for="tk_heroMode"> nombra al <select>, no a este campo: sin aria-label
                 un lector de pantalla solo anunciaba «edición, en blanco» (WCAG 4.1.2). -->
            <input id="tk_heroFilter" class="brt-sub" value="${esc(hero === 'custom' ? heroRaw : '')}"
              aria-label="Filtro CSS personalizado de las fotos de portada"
              placeholder="sepia(.3) contrast(1.1)" spellcheck="false" ${hero === 'custom' ? '' : 'hidden'}>
            <span class="acc-hint">Las campañas en color se ven <b>grises</b> con el filtro por defecto. «Sin
              filtro» las deja tal cual se subieron. Personalizado admite cualquier <code>filter</code> de CSS.</span></p>
          <p class="acc-field"><label for="tk_heroStyle"><span>Composición de la portada</span></label>
            <select id="tk_heroStyle">
              <option value="" ${v('heroStyle') !== 'paper' ? 'selected' : ''}>Sobre la foto (velo oscuro, titular blanco encima)</option>
              <option value="paper" ${v('heroStyle') === 'paper' ? 'selected' : ''}>Sobre papel (foto arriba, titular en tinta debajo)</option>
            </select>
            <span class="acc-hint">«Sobre papel» pone el titular, el saludo y los rótulos de las ventanas
              sobre el fondo de página, bajo la foto: el texto lee igual sea cual sea la campaña.
              También cambia el pie del hero del lookbook y deja el monograma y el botón del
              asistente sin relleno de color.</span></p>
        </div>

        <h3 class="brt-group">Textos del acceso</h3>
        <div class="biz-grid">
          <p class="acc-field wide"><label><span>Titular de la pantalla de acceso</span>
            <input id="tk_tagline" value="${esc(v('tagline'))}" placeholder="Tu tienda mayorista, siempre abierta"></label>
            <span class="acc-hint">Sustituye la frase grande del login. Vacío = la de siempre.</span></p>
          <p class="acc-field"><label><span>Email de soporte</span>
            <input id="tk_supportEmail" type="email" value="${esc(v('supportEmail'))}" placeholder="soporte@tudominio.com" spellcheck="false"></label>
            <span class="acc-hint">El email al que se escribe desde el login. Vacío = el de siempre.</span></p>
          <p class="acc-field wide"><label><span>Texto legal del acceso</span>
            <textarea id="tk_legal" rows="3" maxlength="400" placeholder="Vendemos exclusivamente a distribuidores y profesionales del sector…">${esc(v('legal'))}</textarea></label>
            <span class="acc-hint">La nota pequeña bajo «¿No tienes cuenta?». El texto de siempre habla de un
              distribuidor multimarca; una marca que fabrica su producto pone aquí el suyo (máx. 400
              caracteres, sin HTML). Vacío = el de siempre.</span></p>
        </div>
      </div>
    </div>`;
}

/** Lee el acordeón → { tokens } | { error, key } (los campos vacíos NO viajan).
    `media` trae las imágenes del panel (logo para fondos oscuros y favicon), que NO son
    campos del DOM: viven en el estado de la vista. Sin ellas el PUT salía sin esos dos
    tokens, así que subirlos no guardaba nada y, peor, CUALQUIER guardado posterior
    —aunque solo cambiara el nombre— borraba los que ya hubiera, porque el servidor
    reemplaza el JSON de tokens entero. */
function readTokens(main, media) {
  const t = {};
  const val = id => (main.querySelector('#' + id)?.value || '').trim();
  // La clave viaja con el error para que el guardado pueda señalar el campo culpable.
  const bad = (key, error) => ({ key, error });
  for (const [key, label, def] of TOKEN_COLORS) {
    const v = val('tk_' + key);
    if (!v) continue;
    if (!isHex(v)) return bad(key, `«${label}» debe ser un color hexadecimal #rrggbb, p. ej. ${def}.`);
    t[key] = v.toLowerCase();
  }
  for (const [key, label, ex] of TOKEN_SIZES) {
    const v = val('tk_' + key);
    if (!v) continue;
    if (!isSize(v)) return bad(key, `«${label}» necesita una medida con unidad px, rem, em o %, p. ej. ${ex}.`);
    t[key] = v;
  }
  // Las tres URLs: las dos imágenes del panel (subidas con api.uploadMedia, que devuelve
  // siempre un nombre saneado) y la hoja de la tipografía, que sí se puede teclear.
  for (const [key, label, raw] of [
    ['logoUrlDark', 'Logo para fondos oscuros', media?.logoUrlDark],
    ['faviconUrl', 'Icono de pestaña (favicon)', media?.faviconUrl],
    ['fontUrl', 'Hoja de la tipografía', val('tk_fontUrl')],
  ]) {
    const v = (raw || '').trim();
    if (!v) continue;
    if (v.length > 500) return bad(key, `«${label}» es demasiado largo (máx. 500 caracteres).`);
    if (badScheme(v)) return bad(key, `«${label}» no admite direcciones javascript: ni data:.`);
    if (badUrlChar(v)) return bad(key, `«${label}» no admite espacios, comillas, paréntesis ni los signos < > ; { }.`);
    t[key] = v;
  }
  const fontFamily = val('tk_fontFamily');
  if (fontFamily) {
    if (fontFamily.length > 60) return bad('fontFamily', '«Familia tipográfica» es demasiado larga (máx. 60 caracteres).');
    if (badCssString(fontFamily)) return bad('fontFamily', '«Familia tipográfica» no admite comillas ni los signos ; { } < >.');
    t.fontFamily = fontFamily;
  }
  if (main.querySelector('#tk_caps')?.checked) t.caps = true;   // false = el valor de siempre
  const mode = main.querySelector('#tk_heroMode')?.value;
  if (mode === 'none') t.heroFilter = 'none';
  else if (mode === 'custom') {
    const v = val('tk_heroFilter');
    if (v.length > 120) return bad('heroFilter', 'El filtro de las fotos de portada es demasiado largo (máx. 120 caracteres).');
    if (v && badCss(v)) return bad('heroFilter', 'El filtro de las fotos de portada no admite «url(», comentarios CSS ni los signos ; { } <.');
    if (v) t.heroFilter = v;
  }
  const tagline = val('tk_tagline');
  if (tagline) {
    if (tagline.length > 120) return bad('tagline', '«Titular de la pantalla de acceso» es demasiado largo (máx. 120 caracteres).');
    if (/[<>]/.test(tagline)) return bad('tagline', '«Titular de la pantalla de acceso» no admite «<» ni «>».');
    t.tagline = tagline;
  }
  const email = val('tk_supportEmail');
  if (email) {
    if (email.length > 120 || !isEmail(email)) return bad('supportEmail', 'El email de soporte no parece un email válido.');
    t.supportEmail = email;
  }
  // Los tres de la ronda 1 de crítica de BLOCCO 5. Los desplegables solo ofrecen valores
  // válidos, pero se vuelve a comprobar por si el DOM trae otra cosa (misma regla que el PUT).
  if (main.querySelector('#tk_heroStyle')?.value === 'paper') t.heroStyle = 'paper';
  const weight = val('tk_displayWeight');
  if (weight) {
    if (!isWeight(weight)) return bad('displayWeight', '«Peso de los titulares» debe ser una centena de 100 a 900 (p. ej. 900).');
    t.displayWeight = weight;
  }
  const legal = val('tk_legal');
  if (legal) {
    if (legal.length > 400) return bad('legal', '«Texto legal del acceso» es demasiado largo (máx. 400 caracteres).');
    if (/[<>]/.test(legal)) return bad('legal', '«Texto legal del acceso» no admite «<» ni «>».');
    t.legal = legal;
  }
  return { tokens: t };
}

// ══════════ Conexiones ══════════
export async function connectionsView(main) {
  const s = await api.intSettings();
  // Tokens guardados: los trae ya `GET /api/admin/integration/settings` como `brandTokens`.
  // (Antes se pedía además /api/portal/branding, un segundo viaje EN SERIE que retrasaba
  // el pintado de toda la pantalla para leer lo mismo.)
  let tk = s.brandTokens;
  if (!tk || typeof tk !== 'object' || Array.isArray(tk)) tk = {};
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
    </form>
    <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.layers(20)}Modo de pedidos <span class="grid-chip ${s.ordersMode === 'portal' ? 'ok' : 'off'}">${s.ordersMode === 'portal' ? 'Comunica a BC' : 'ERP'}</span></h2></header>
      <div class="biz-card">
        <p class="lead" style="margin:0 0 .8rem">Cómo se tratan los pedidos que se terminan en el portal.</p>
        <label style="display:flex;gap:.55rem;align-items:flex-start;margin:.5rem 0;cursor:pointer">
          <input type="radio" name="ordersMode" value="portal" ${s.ordersMode === 'portal' ? 'checked' : ''} style="margin-top:.25rem">
          <span><b>Comunicar a Business Central</b> — el portal es dueño del pedido y lo <b>envía a BC</b> al terminarlo (asigna nº de pedido, dispara el email y despacha al canal Business Central).</span></label>
        <label style="display:flex;gap:.55rem;align-items:flex-start;margin:.5rem 0;cursor:pointer">
          <input type="radio" name="ordersMode" value="erp" ${s.ordersMode === 'erp' ? 'checked' : ''} style="margin-top:.25rem">
          <span><b>Los gobierna el ERP</b> — el pedido queda a la espera; los maestros y pedidos los maneja Business Central.</span></label>
        <div style="margin-top:.9rem"><button type="button" class="btn-primary" id="ordersModeSave">Guardar modo</button></div>
      </div></section>
    <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.box(20)}Catálogo <span class="grid-chip ${s.requireModelImage ? 'ok' : 'off'}">${s.requireModelImage ? 'Solo con foto' : 'Todos'}</span></h2></header>
      <div class="biz-card">
        <p class="lead" style="margin:0 0 .8rem">Qué artículos llegan al escaparate.</p>
        <label style="display:flex;gap:.55rem;align-items:flex-start;margin:.5rem 0;cursor:pointer">
          <input type="checkbox" id="requireModelImage" ${s.requireModelImage ? 'checked' : ''} style="margin-top:.25rem">
          <span><b>Enseñar solo los artículos con foto</b> — los que todavía no tienen ninguna imagen se quedan fuera del catálogo, del buscador, de los relacionados y del PDF, hasta que la tengan. Útil mientras el ERP va subiendo fotos.</span></label>
        <p class="acc-hint">Afecta también a los recuentos: no se anuncian artículos que no se enseñan. Se aplica al momento, sin resincronizar nada.</p>
        <div style="margin-top:.9rem"><button type="button" class="btn-primary" id="catalogOptionsSave">Guardar catálogo</button></div>
      </div></section>
    <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.image(20)}Marca del portal</h2></header>
      <div class="biz-card">
        <p class="lead" style="margin:0 0 .8rem">El nombre, el color y el logo se aplican al portal, al back-office, a los emails y a los PDFs. Vacío = marca por defecto (MITO PROJECTS, rojo).</p>
        <div class="biz-grid">
          <p class="acc-field"><label><span>Nombre</span>
            <input id="brName" value="${esc(s.brandName === 'MITO PROJECTS' ? '' : (s.brandName || ''))}" placeholder="MITO PROJECTS"></label></p>
          <p class="acc-field"><label for="brColor"><span>Color de acento</span></label>
            <span style="display:flex;gap:.6rem;align-items:center">
              <input type="color" id="brColorPick" value="${esc(/^#[0-9a-f]{6}$/i.test(s.brandColor || '') ? s.brandColor : '#ec3013')}"
                aria-label="Elegir color de acento" style="width:2.6rem;height:2.2rem;padding:.15rem;border:1px solid var(--line-control);background:#fff;cursor:pointer">
              <input id="brColor" value="${esc((s.brandColor || '').toLowerCase() === '#ec3013' ? '' : (s.brandColor || ''))}"
                placeholder="#ec3013" style="flex:1" spellcheck="false"></span></p>
          <p class="acc-field wide"><span style="display:block;font-size:.78rem;font-weight:600;color:var(--hint);margin:0 0 .35rem">Logo</span>
            <span id="brLogoBox" class="brt-media"></span>
            <input type="file" id="brLogoFile" accept="image/*" hidden>
            <span class="acc-hint" style="display:block;margin-top:.4rem">PNG o SVG con fondo transparente (se muestra a ~28 px de alto sobre la cabecera negra). Sin logo, se muestra el nombre en texto.</span></p>
        </div>
        ${tokensPanel(tk, brtOpen)}
        <div style="display:flex;gap:.6rem;flex-wrap:wrap;margin-top:.4rem">
          <button type="button" class="btn-primary" id="brandSave">Guardar marca</button>
          <button type="button" class="btn-ghost" id="brandReset">${icons.left(15)} Restablecer estilo</button>
        </div>
      </div></section>
    <section class="biz-section"><header class="acc-head biz-head"><h2>${icons.send(20)}Diseño de email (marca)</h2></header>
      <div class="biz-card">
        <p class="lead" style="margin:0 0 .8rem">Envoltorio HTML común a TODOS los emails (cabecera y pie de marca). Debe contener <code>{{content}}</code> (donde entra el cuerpo de cada email) y admite <code>{{subject}}</code> <code>{{year}}</code>.</p>
        <p class="acc-field"><label><span>Layout HTML</span>
          <textarea id="emailLayout" rows="12" style="font-family:monospace;font-size:.8rem">${esc(s.emailLayoutHtml || '')}</textarea></label></p>
        <div style="display:flex;gap:.6rem;flex-wrap:wrap">
          <button type="button" class="btn-primary" id="layoutPreview">Vista previa</button>
          <button type="button" class="btn-ghost" id="layoutSave">Guardar diseño</button>
          <button type="button" class="btn-ghost" id="layoutRestore">${icons.left(15)} Restaurar por defecto</button>
        </div>
        <div id="layoutPreviewBox" style="display:none;margin-top:1rem">
          <iframe id="layoutFrame" title="Vista previa del diseño" style="width:100%;height:420px;border:1px solid var(--line);background:#fff"></iframe>
        </div>
      </div></section>`;
  main.querySelector('#cf').onsubmit = async e => {
    e.preventDefault();
    const $ = id => main.querySelector('#' + id).value.trim();
    const b = { bcBaseUrl: $('bcBaseUrl'), bcTokenUrl: $('bcTokenUrl'), bcClientId: $('bcClientId'), bcClientSecret: main.querySelector('#bcClientSecret').value, bcScope: $('bcScope'), apiRestBaseUrl: $('apiRestBaseUrl') };
    try { const r = await api.intSaveSettings(b); flash(r.bcConfigured ? 'Conexiones guardadas · BC configurado.' : 'Conexiones guardadas.'); connectionsView(main); }
    catch (err) { main.querySelector('#notice').innerHTML = `<div class="notice notice-error">${esc(err.body?.error || err.message)}</div>`; }
  };

  // Modo de pedidos — guardado por su propio endpoint (no toca la conexión BC).
  main.querySelector('#ordersModeSave').onclick = async () => {
    const mode = main.querySelector('input[name="ordersMode"]:checked')?.value;
    if (!mode) return;
    try { const r = await api.intSaveOrdersMode(mode); flash(`Modo de pedidos guardado: ${r.ordersMode === 'portal' ? 'Comunica a Business Central' : 'ERP'}.`); connectionsView(main); }
    catch (e) { flash(e.body?.error || e.message, 'err'); }
  };

  // Catálogo de la instancia — su propio endpoint, igual que el modo de pedidos.
  main.querySelector('#catalogOptionsSave').onclick = async () => {
    const solo = main.querySelector('#requireModelImage').checked;
    try {
      await api.intSaveCatalogOptions(solo);
      flash(solo ? 'Guardado: el portal solo enseña los artículos con foto.'
                 : 'Guardado: el portal enseña todos los artículos.');
      connectionsView(main);
    } catch (e) { flash(e.body?.error || e.message, 'err'); }
  };

  // ── Marca del portal (nombre + color + logo) + tokens de estilo ─────────────
  // Imágenes: mismo mecanismo para el logo, el logo oscuro y el favicon (api.uploadMedia).
  const media = { logoUrl: s.brandLogoUrl || '', logoUrlDark: tk.logoUrlDark || '', faviconUrl: tk.faviconUrl || '' };
  const mediaField = (key, boxId, fileId, cta, alt, preview) => {
    const box = main.querySelector('#' + boxId);
    if (!box) return;
    const paint = () => {
      box.innerHTML = media[key]
        ? `<img src="${esc(media[key])}" alt="${esc(alt)}" style="${preview}">
           <button type="button" class="btn-ghost" data-up>Cambiar</button>
           <button type="button" class="btn-ghost" data-off>Quitar</button>`
        : `<button type="button" class="btn-ghost" data-up>${icons.upload(15)} ${esc(cta)}</button>`;
      box.querySelector('[data-up]').onclick = () => main.querySelector('#' + fileId).click();
      const off = box.querySelector('[data-off]');
      if (off) off.onclick = () => { media[key] = ''; paint(); };
    };
    main.querySelector('#' + fileId).onchange = async e => {
      const file = e.target.files[0];
      e.target.value = '';
      if (!file) return;
      try { const r = await api.uploadMedia(file); media[key] = r.url; paint(); flash('Imagen subida. Pulsa «Guardar marca» para aplicarla.'); }
      catch (err) { flash(err.body?.error || err.message, 'err'); }
    };
    paint();
  };
  const logoPreview = 'height:34px;max-width:220px;object-fit:contain;display:block;background:var(--header-bg);padding:.3rem .6rem';
  mediaField('logoUrl', 'brLogoBox', 'brLogoFile', 'Subir logo', 'Logo actual de la marca', logoPreview);
  mediaField('logoUrlDark', 'tkDarkBox', 'tkDarkFile', 'Subir logo claro', 'Logo para fondos oscuros', logoPreview);
  mediaField('faviconUrl', 'tkFavBox', 'tkFavFile', 'Subir favicon', 'Icono de pestaña',
    'height:32px;width:32px;object-fit:contain;display:block;background:#fff;border:1px solid var(--line);padding:.15rem');

  // El selector de color y el hex visible van de la mano en ambos sentidos.
  const brPick = main.querySelector('#brColorPick'), brHex = main.querySelector('#brColor');
  brPick.oninput = () => { brHex.value = brPick.value; };
  brHex.oninput = () => { const v = brHex.value.trim(); if (/^#[0-9a-f]{6}$/i.test(v)) brPick.value = v; };

  // ── Acordeón «Avanzado · estilo de la instancia» (plegado por defecto) ──────
  const brtToggle = main.querySelector('#brtToggle'), brtPanel = main.querySelector('#brtPanel');
  brtToggle.onclick = () => {
    const open = brtToggle.getAttribute('aria-expanded') === 'true';
    brtToggle.setAttribute('aria-expanded', String(!open));
    brtPanel.hidden = open;
    brtOpen = !open;                  // sobrevive al repintado de connectionsView()
  };
  // Un campo marcado como inválido deja de estarlo en cuanto se toca.
  brtPanel.addEventListener('input', e => e.target?.removeAttribute?.('aria-invalid'));
  // Cada color del acordeón: mismo baile selector ↔ hexadecimal que el acento.
  for (const [key] of TOKEN_COLORS) {
    const pick = main.querySelector(`[data-tkpick="${key}"]`), hex = main.querySelector(`[data-tkhex="${key}"]`);
    pick.oninput = () => { hex.value = pick.value; };
    hex.oninput = () => { const v = hex.value.trim(); if (isHex(v)) pick.value = v; };
  }
  // El campo libre del filtro solo aparece con «Personalizado…».
  const heroMode = main.querySelector('#tk_heroMode'), heroFree = main.querySelector('#tk_heroFilter');
  heroMode.onchange = () => { heroFree.hidden = heroMode.value !== 'custom'; if (!heroFree.hidden) heroFree.focus(); };
  // Subida de la tipografía (.woff2): rellena la URL de la hoja.
  main.querySelector('#tkFontUp').onclick = () => main.querySelector('#tkFontFile').click();
  main.querySelector('#tkFontFile').onchange = async e => {
    const file = e.target.files[0];
    e.target.value = '';
    if (!file) return;
    try { const r = await api.uploadMedia(file); main.querySelector('#tk_fontUrl').value = r.url; flash('Tipografía subida. Pulsa «Guardar marca» para aplicarla.'); }
    catch (err) { flash(err.body?.error || err.message, 'err'); }
  };

  // Un único guardado para la marca y los tokens.
  const saveBranding = async (tokens, done) => {
    const name = main.querySelector('#brName').value.trim();
    const color = brHex.value.trim();
    if (color && !/^#[0-9a-f]{6}$/i.test(color)) { flash('El color debe ser hexadecimal #rrggbb, p. ej. #ec3013.', 'err'); return; }
    try {
      await api.intSaveBranding({ name, color, logoUrl: media.logoUrl, tokens });
      // Aplica la marca en vivo (título, cabecera, acento, tokens) leyendo el efectivo público.
      try { setBrand(await (await fetch('/api/portal/branding')).json()); } catch { /* se aplicará al recargar */ }
      flash(done);
      connectionsView(main);
    } catch (err) { flash(err.body?.error || err.message, 'err'); }
  };
  main.querySelector('#brandSave').onclick = () => {
    const read = readTokens(main, media);
    if (read.error) {
      flash(read.error, 'err');
      brtPanel.hidden = false; brtToggle.setAttribute('aria-expanded', 'true'); brtOpen = true;
      // Con 13 campos validados repartidos en cinco grupos, el aviso no basta: se marca
      // el campo, se lleva el foco y se trae a la vista (el flash se desvanece a los 6 s).
      const field = read.key && main.querySelector('#tk_' + read.key);
      if (field) { field.setAttribute('aria-invalid', 'true'); field.focus(); field.scrollIntoView({ block: 'center' }); }
      return;
    }
    // Sin ningún token → null: la instancia vuelve al estilo por defecto del portal.
    saveBranding(countTokens(read.tokens) ? read.tokens : null, 'Marca guardada y aplicada. El portal la mostrará al recargar.');
  };
  main.querySelector('#brandReset').onclick = () => {
    if (!confirm('¿Restablecer el estilo? Se borran tipografía, colores, formas, favicon y textos de esta instancia; el portal vuelve a su estilo por defecto. El nombre, el color de acento y el logo se conservan.')) return;
    // Se conserva lo que hay EN PANTALLA (nombre, color y logo, incluido lo cambiado sin
    // guardar), que es lo que promete el aviso: `saveBranding` ya lee #brName, brHex y
    // media.logoUrl. Antes se sobrescribían con los valores guardados y el PUT los
    // persistía, tirando el trabajo a medio hacer sin decir nada.
    saveBranding(null, 'Estilo restablecido: la instancia vuelve al diseño por defecto.');
  };

  // Diseño global del email (layout de marca) — guardado por su propio endpoint.
  const sampleBody = '<p style="margin:0 0 12px;font-size:16px;font-weight:700">Título del email</p><p style="margin:0 0 12px">Cuerpo de ejemplo dentro de tu diseño de marca.</p><p style="margin:24px 0"><a href="#" style="background:#ec3013;color:#fff;text-decoration:none;padding:12px 22px;font-weight:700;display:inline-block">Botón de acción</a></p>';
  main.querySelector('#layoutPreview').onclick = async () => {
    try { const r = await api.intPreviewEmail({ layout: main.querySelector('#emailLayout').value, bodyHtml: sampleBody });
      main.querySelector('#layoutPreviewBox').style.display = ''; main.querySelector('#layoutFrame').srcdoc = r.html || ''; }
    catch (e) { flash(e.body?.error || e.message, 'err'); }
  };
  main.querySelector('#layoutSave').onclick = async () => {
    try { await api.intSaveEmailLayout(main.querySelector('#emailLayout').value); flash('Diseño de email guardado.'); }
    catch (e) { flash(e.body?.error || e.message, 'err'); }
  };
  main.querySelector('#layoutRestore').onclick = async () => {
    if (!confirm('¿Restaurar el diseño por defecto? Perderás tu layout personalizado.')) return;
    try { await api.intSaveEmailLayout(''); flash('Diseño por defecto restaurado.'); connectionsView(main); }
    catch (e) { flash(e.body?.error || e.message, 'err'); }
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
  const btnCss = 'font-size:.72rem;font-weight:600;padding:.28rem .6rem;border:1px solid var(--line,#d8d8d8);border-radius:.4rem;background:#fff;color:inherit;cursor:pointer;white-space:nowrap';
  // Acciones (solo canal Business Central): reprocesar y ver el JSON enviado. En horizontal
  // junto al chip, para que todas las filas midan lo mismo.
  const acts = l => l.channelType !== 'business-central' ? '' :
    `${l.canReprocess ? `<button type="button" class="log-retry" data-id="${esc(l.id)}" style="${btnCss}">↻ Reprocesar</button>` : ''}${l.payloadJson ? `<button type="button" class="log-json" data-id="${esc(l.id)}" style="${btnCss}">Ver JSON</button>` : ''}`;
  main.innerHTML = `
    <div class="mng-page-head"><div>
      <p class="crumbs">Integración · Notificaciones</p>
      <h1 class="title">Notificaciones realizadas</h1>
      <p class="lead">Historial de envíos por canal y su estado. En los envíos a Business Central puedes ver el JSON que se manda y reprocesarlo.</p>
    </div></div>
    <div class="grid-scroll"><table class="grid">
      <thead><tr><th>Fecha</th><th>Evento</th><th>Entidad</th><th>Canal</th><th>Estado</th><th>Detalle</th></tr></thead>
      <tbody>${(data.items || []).length ? data.items.map(l => `
        <tr><td>${esc(new Date(l.createdAt).toLocaleString('es-ES'))}</td>
          <td>${esc(l.eventKey)}</td>
          <td>${esc(l.entityType)} <span class="grid-id">${esc(String(l.entityId).slice(0, 12))}</span></td>
          <td>${l.channelType === 'email' ? 'Email' : 'Business Central'}</td>
          <td><span style="display:inline-flex;gap:.5rem;align-items:center;white-space:nowrap"><span class="grid-chip ${chip(l.status)}">${esc(l.status)}</span>${acts(l)}</span></td>
          <td class="muted"><div style="max-width:16rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(l.detail || '')}">${esc(l.detail || '')}</div></td></tr>`).join('')
        : '<tr class="grid-empty"><td colspan="6">Todavía no hay notificaciones.</td></tr>'}</tbody>
    </table></div>`;

  const byId = id => (data.items || []).find(x => x.id === id);

  // Reprocesar un envío a BC: re-aplica el transformer ACTUAL y reenvía (útil tras corregir
  // un transformer o un fallo transitorio, sin repetir la operación de origen).
  main.querySelectorAll('.log-retry').forEach(btn => btn.addEventListener('click', async () => {
    btn.disabled = true; btn.textContent = 'Reprocesando…';
    try { const r = await api.intReprocess(btn.dataset.id); flash(r.message || 'Reprocesado.'); }
    catch (e) { flash(e.body?.error || e.message, 'err'); }
    logsView(main);
  }));

  // Ver el JSON exacto que se envió (o se enviaría, en 'simulated') a Business Central.
  main.querySelectorAll('.log-json').forEach(btn => btn.addEventListener('click', () => {
    const l = byId(btn.dataset.id);
    if (l) showJson(`JSON enviado a BC · ${l.eventKey}`, l.payloadJson, l.detail || '');
  }));
}

const pretty = s => { try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; } };
