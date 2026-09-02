// Formulario genérico de alta/edición de un maestro, por SECCIONES. Reutiliza el
// lenguaje de formulario del portal (.biz-section/.biz-card/.biz-grid/.acc-field)
// para verse igual que el alta de cliente del portal.
import { SCHEMAS, OPTS } from '../schemas.js';
import { icons } from '../icons.js';
import { api } from '../api.js';
import { esc, dig, setPath, delPath, slugify, i18nObject, fkOptions, flash, invalidateOptions, showJson, loadRows } from '../util.js';
import { go } from '../router.js';

const SLUG = { model: 'models', product: 'products', offer: 'offers', inventory: 'inventory',
  'service-window': 'service-windows', category: 'categories', family: 'families', attribute: 'attributes',
  warehouse: 'warehouses', 'payment-method': 'payment-methods', 'client-group': 'client-groups', agent: 'agents' };

export default async function form(main, type, id) {
  const sc = SCHEMAS[type];
  const editing = !!id;
  const article = sc.fem ? 'Nueva' : 'Nuevo';
  let payload = {}, rawPayload = null;
  if (editing) {
    try { const d = await api.doc(type, id); rawPayload = d.payload; payload = JSON.parse(d.payload); } catch { payload = {}; }
    if (Array.isArray(payload)) payload = payload[0] ?? {};
    // Ofertas del conector llegan envueltas en offerData{}: se aplanan para prefijar el form
    if (type === 'offer' && payload.offerData) payload = { ...payload, ...payload.offerData };
  }

  // Precarga de opciones de todas las FK del esquema
  const srcs = [...new Set(sc.sections.flatMap(s => s.fields).filter(f => f.src).map(f => f.src))];
  const optionsBySrc = {};
  await Promise.all(srcs.map(async s => { optionsBySrc[s] = await fkOptions(s); }));

  const idFrom = sc.id.mode !== 'guid' ? sc.id.from : null;

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs"><a href="#/${SLUG[type]}">${esc(sc.plural)}</a> · <span>${editing ? 'Editar' : article}</span></p>
        <h1 class="title">${editing ? 'Editar' : article} ${esc(sc.singular)}</h1>
      </div>
      ${editing ? `<button type="button" class="btn-ghost" id="viewJson">${icons.list ? icons.list(15) : ''} Ver JSON recibido</button>` : ''}
    </div>
    <form class="mng-form nc-form" id="f" novalidate>
      <div id="notice"></div>
      ${sc.sections.map(sectionHtml).join('')}
      ${editing && type === 'model' ? '<div id="imgHost"></div><div id="relHost"></div>' : ''}
      <div class="acc-actions nc-actions">
        ${editing ? '<button type="button" class="btn-danger" id="del">Eliminar</button>' : ''}
        <a class="btn-ghost" href="#/${SLUG[type]}">Cancelar</a>
        <button type="submit" class="btn-primary">${editing ? 'Guardar cambios' : `Crear ${esc(sc.singular)}`}</button>
      </div>
    </form>`;

  const formEl = main.querySelector('#f');
  const notice = main.querySelector('#notice');

  // "Ver JSON recibido": el payload original que envió Business Central para esta ficha,
  // sin salir a Comunicación BC (trazabilidad a un clic).
  const jsonBtn = main.querySelector('#viewJson');
  if (jsonBtn) jsonBtn.onclick = () =>
    showJson(`${cap(sc.singular)} · ${id}`, rawPayload ?? payload,
      rawPayload ? 'JSON recibido de Business Central' : 'Datos actuales de la ficha (creada en el portal)');

  function sectionHtml(section) {
    return `
      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons[section.icon] ? icons[section.icon](20) : ''}${esc(section.title)}</h2></header>
        <div class="biz-card"><div class="biz-grid">${section.fields.map(fieldHtml).join('')}</div></div>
      </section>`;
  }

  function fieldHtml(f) {
    const wide = f.wide || f.t === 'multi' || f.t === 'valuelist' || f.t === 'i18narea' || f.t === 'area';
    const isIdSrc = (idFrom && f.k === idFrom) || f.k.startsWith('__');
    const lockId = editing && isIdSrc;
    const help = f.help ? `<span class="acc-hint">${esc(f.help)}</span>` : '';

    if (f.t === 'bool') {
      const on = boolVal(f);
      return `<p class="acc-field${wide ? ' wide' : ''}"><label class="mng-check">
        <input type="checkbox" data-key="${esc(f.k)}" ${on ? 'checked' : ''}> <span>${esc(f.l)}</span></label>${help}</p>`;
    }
    let control;
    const val = fieldValue(f, lockId);
    const attrs = `data-key="${esc(f.k)}" data-t="${f.t}"${lockId ? ' disabled' : ''}`;
    if (f.t === 'i18n' || f.t === 'text' || f.t === 'num' || f.t === 'money' || f.t === 'date') {
      const inputType = f.t === 'num' || f.t === 'money' ? 'number' : f.t === 'date' ? 'date' : 'text';
      const step = (f.t === 'money' || f.t === 'num') ? ' step="any"' : '';
      control = `<input type="${inputType}"${step} ${attrs} value="${esc(val)}" maxlength="240">`;
    } else if (f.t === 'i18narea' || f.t === 'area') {
      control = `<textarea rows="3" ${attrs}>${esc(val)}</textarea>`;
    } else if (f.t === 'valuelist') {
      control = `<textarea rows="4" ${attrs} placeholder="Un valor por línea">${esc(val)}</textarea>`;
    } else if (f.t === 'select') {
      const opts = f.opts ? OPTS[f.opts] : (optionsBySrc[f.src] || []).map(o => [o.value, o.label]);
      const list = (!f.req && !f.opts) ? [['', '—'], ...opts] : opts;
      control = `<select ${attrs}>${list.map(([v, l]) =>
        `<option value="${esc(v)}"${String(v) === String(val) ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select>`;
    } else if (f.t === 'multi') {
      const chosen = (Array.isArray(dig(payload, f.k)) ? dig(payload, f.k) : []).map(x => String(typeof x === 'object' ? (x.id || x.code || '') : x));
      const opts = optionsBySrc[f.src] || [];
      control = `<div class="mng-multi" data-key="${esc(f.k)}" data-t="multi">${opts.length ? opts.map(o =>
        `<label><input type="checkbox" value="${esc(o.value)}"${chosen.includes(String(o.value)) ? ' checked' : ''}> ${esc(o.label)}</label>`).join('')
        : '<span class="mng-multi-empty">Todavía no hay opciones.</span>'}</div>`;
    } else {
      control = `<input type="text" ${attrs} value="${esc(val)}">`;
    }
    return `<p class="acc-field${wide ? ' wide' : ''}"><label><span>${esc(f.l)}${f.req ? ' *' : ''}</span>${control}</label>${help}</p>`;
  }

  function boolVal(f) {
    const v = dig(payload, f.k);
    return v === undefined ? !!f.def : !!v;
  }

  function fieldValue(f, lockId) {
    if (lockId && f.k.startsWith('__')) return id || '';
    if (f.t === 'i18n' || f.t === 'i18narea') return dig(payload, f.k + '.es_ES') ?? (typeof dig(payload, f.k) === 'string' ? dig(payload, f.k) : '') ?? '';
    if (f.t === 'valuelist') { const a = dig(payload, f.k); return Array.isArray(a) ? a.map(x => typeof x === 'object' ? (x.id ?? '') : x).join('\n') : ''; }
    if (f.t === 'date') { const v = dig(payload, f.k); return v ? String(v).slice(0, 10) : ''; }
    const v = dig(payload, f.k);
    return v === undefined || v === null ? (f.def ?? '') : v;
  }

  // ── Guardar ──
  formEl.onsubmit = async event => {
    event.preventDefault();
    notice.innerHTML = '';
    const body = collect();

    const missing = sc.sections.flatMap(s => s.fields).find(f => f.req && isEmpty(body, f, formEl));
    if (missing) return warn(notice, `Falta un campo obligatorio: ${missing.l}.`);

    const newId = computeId(body);
    if (!newId) return warn(notice, 'No se pudo determinar el identificador.');
    for (const k of Object.keys(body)) if (k.startsWith('__')) delete body[k];

    const btn = formEl.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      await api.saveEntity(type, newId, body);
      invalidateOptions(type);
      flash(editing ? 'Cambios guardados.' : `${cap(sc.singular)} creado.`);
      go(`#/${SLUG[type]}`);
    } catch (failure) {
      btn.disabled = false;
      warn(notice, failure.body?.error || failure.message || 'No se pudo guardar.');
    }
  };

  if (editing) main.querySelector('#del').onclick = async () => {
    if (!confirm(`¿Eliminar este ${sc.singular}? Desaparecerá del portal.`)) return;
    try { await api.delEntity(type, id); invalidateOptions(type); flash(`${cap(sc.singular)} eliminado.`); go(`#/${SLUG[type]}`); }
    catch (e) { warn(notice, e.body?.error || e.message); }
  };

  // Imagen del modelo (solo al editar; para uno nuevo se añade desde «Imágenes» tras crearlo)
  if (editing && type === 'model') { await mountModelImage(); await mountRelatedBc(); }

  async function mountModelImage() {
    const host = main.querySelector('#imgHost');
    if (!host) return;
    let uri = '';
    try { uri = JSON.parse((await api.doc('model-image', id)).payload)?.images?.[0]?.image?.uri || ''; } catch {}
    const render = () => {
      host.innerHTML = `
        <section class="biz-section">
          <header class="acc-head biz-head"><h2>${icons.image(20)}Imagen del modelo</h2></header>
          <div class="biz-card"><div class="mng-img-inline">
            <div class="mng-img-thumb2">${uri
              ? `<img src="${esc(uri)}" alt="">`
              : `<span class="mng-img-none">${icons.image(24)}<span>Sin imagen</span></span>`}</div>
            <div class="mng-img-actions">
              <button type="button" class="btn-ghost" id="imUp">${icons.upload(15)} Subir</button>
              <button type="button" class="btn-ghost" id="imUrl">${icons.image(15)} URL</button>
              ${uri ? `<button type="button" class="btn-ghost" id="imRm" style="color:var(--out)">${icons.trash(15)} Quitar</button>` : ''}
            </div>
          </div></div>
        </section>`;
      host.querySelector('#imUp').onclick = () => {
        const input = document.createElement('input');
        input.type = 'file'; input.accept = 'image/png,image/jpeg,image/webp,image/avif,image/gif';
        input.onchange = async () => {
          const file = input.files[0]; if (!file) return;
          try { const up = await api.uploadMedia(file); await api.setModelImage(id, up.url); uri = up.url + '?t=' + Date.now(); render(); flash('Imagen subida.'); }
          catch (e) { flash(e.body?.error || e.message, 'err'); }
        };
        input.click();
      };
      host.querySelector('#imUrl').onclick = async () => {
        const url = prompt('URL de la imagen (https://… o /media/…):');
        if (!url) return;
        try { await api.setModelImage(id, url.trim()); uri = url.trim(); render(); flash('Imagen asignada.'); }
        catch (e) { flash(e.body?.error || e.message, 'err'); }
      };
      const rm = host.querySelector('#imRm');
      if (rm) rm.onclick = async () => {
        if (!confirm('¿Quitar la imagen de este modelo?')) return;
        try { await api.delModelImage(id); uri = ''; render(); flash('Imagen quitada.'); }
        catch (e) { flash(e.body?.error || e.message, 'err'); }
      };
    };
    render();
  }

  // ── "Relacionados (de BC)": solo lectura ──────────────────────────────────────
  // Los cross/up-selling los decide Business Central en la ficha del modelo y viajan
  // en el payload crudo (crossSellingIds / upSellingIds). Aquí solo se comprueban:
  // cada id se resuelve a nombre + referencia, y los que aún no están sincronizados
  // en el portal se marcan en rojo. Nada es editable (la fuente es BC).
  async function mountRelatedBc() {
    const relHost = main.querySelector('#relHost');
    if (!relHost) return;

    const clean = value => String(value ?? '').trim().replace(/^\{|\}$/g, '');
    let raw = payload;
    try { if (typeof rawPayload === 'string') raw = JSON.parse(rawPayload); } catch { raw = payload; }
    if (Array.isArray(raw)) raw = raw[0] ?? {};
    const cross = Array.isArray(raw?.crossSellingIds) ? raw.crossSellingIds.map(clean).filter(Boolean) : [];
    const up = Array.isArray(raw?.upSellingIds) ? raw.upSellingIds.map(clean).filter(Boolean) : [];

    let byId = new Map();
    if (cross.length || up.length) {
      try { byId = new Map((await loadRows('model')).map(r => [String(r.id).toLowerCase(), r])); }
      catch { /* sin catálogo no hay resolución: los ids se pintan igualmente */ }
    }

    // role="listitem" va en un contenedor propio, nunca sobre el <a>/<span> del
    // chip (a un enlace no se le pisa el rol de link).
    const chip = relId => {
      const row = byId.get(relId.toLowerCase());
      if (!row) return `
        <div role="listitem"><span class="mng-rel-chip is-missing"
          title="Este SystemId no existe (todavía) como modelo sincronizado en el portal">
          ${icons.alert(13)} <code>${esc(relId)}</code><em>sin sincronizar</em></span></div>`;
      const p = row.payload || {};
      const name = typeof p.name === 'string' ? p.name
        : (p.name?.es_ES || Object.values(p.name || {})[0] || row.id);
      const ref = p.externalReference || '';
      return `
        <div role="listitem"><a class="mng-rel-chip" href="#/models/edit/${encodeURIComponent(row.id)}"
          title="Abrir la ficha de este modelo">
          <b>${esc(name)}</b>${ref ? `<span>Ref. ${esc(ref)}</span>` : ''}</a></div>`;
    };

    const group = (title, hint, ids) => {
      const missing = ids.filter(relId => !byId.get(relId.toLowerCase())).length;
      return `
      <div class="mng-rel-group">
        <h3>${esc(title)} <span class="mng-rel-count">${ids.length}</span></h3>
        <p class="mng-rel-hint">${esc(hint)}</p>
        ${ids.length
          ? `<div class="mng-rel-chips" role="list">${ids.map(chip).join('')}</div>`
          : '<p class="mng-rel-none">BC no envía ninguno para este modelo.</p>'}
        ${missing ? `
          <p class="mng-rel-missing-note">${icons.alert(13)}
            <span>${missing === 1 ? 'Un modelo aún no ha llegado' : `${missing} modelos aún no han llegado`}
            del conector — <a href="#/received">revísalo en Comunicación BC</a>.</span></p>` : ''}
      </div>`;
    };

    relHost.innerHTML = `
      <section class="biz-section">
        <header class="acc-head biz-head"><h2>${icons.layers(20)}Relacionados (de BC)</h2></header>
        <div class="biz-card">
          <p class="mng-rel-note">Solo lectura: los fija Business Central en la ficha del modelo
            (<code>crossSellingIds</code> / <code>upSellingIds</code> del JSON recibido). El portal los
            enseña en la ficha de producto («Completa la gama») y en el checkout («Añade también»).</p>
          <div class="mng-rel-grid">
            ${group('Venta cruzada', 'Otros colores o modelos hermanos.', cross)}
            ${group('Complementos (up-selling)', 'Se ofrecen con la etiqueta «Complemento».', up)}
          </div>
        </div>
      </section>`;
  }

  function collect() {
    const body = { ...structuredClone(sc.defaults || {}), ...structuredClone(payload) };
    for (const f of sc.sections.flatMap(s => s.fields)) {
      const el = formEl.querySelector(`[data-key="${cssEsc(f.k)}"]`);
      if (!el) continue;
      if (f.t === 'bool') { setPath(body, f.k, el.checked); continue; }
      if (f.t === 'multi') { setPath(body, f.k, [...el.querySelectorAll('input:checked')].map(i => i.value)); continue; }
      const raw = (el.value ?? '').trim();
      if (f.k.startsWith('__')) { body[f.k] = raw; continue; }   // solo para el id, se quita luego
      // Vacío → se BORRA la clave (permite vaciar un campo al editar y evita ceros/valores espurios)
      if (f.t === 'i18n' || f.t === 'i18narea') { raw ? setPath(body, f.k, i18nObject(raw)) : delPath(body, f.k); continue; }
      if (f.t === 'num') { raw === '' ? delPath(body, f.k) : setPath(body, f.k, Number(raw)); continue; }
      if (f.t === 'money') {
        if (raw === '') { delPath(body, f.k); }
        else { setPath(body, f.k, Number(raw)); setPath(body, f.k.replace(/\.value$/, '.code'), 'EUR'); }
        continue;
      }
      if (f.t === 'valuelist') {
        const items = raw.split(/\r?\n|,/).map(s => s.trim()).filter(Boolean).map((v, i) => ({ order: i, id: v }));
        items.length ? setPath(body, f.k, items) : delPath(body, f.k);
        continue;
      }
      raw ? setPath(body, f.k, raw) : delPath(body, f.k);   // select / text / area / date
    }
    return body;
  }

  function computeId(body) {
    if (editing) return id;
    if (sc.id.mode === 'guid') return crypto.randomUUID ? crypto.randomUUID() : String(Date.now());
    const base = String(body[sc.id.from] ?? dig(body, sc.id.from) ?? '').trim();
    return sc.id.mode === 'slug' ? slugify(base) : base;
  }
}

function isEmpty(body, f, formEl) {
  if (f.t === 'multi') return false;
  if (f.k.startsWith('__') || (f.t === 'select')) {
    const el = formEl.querySelector(`[data-key="${cssEsc(f.k)}"]`);
    return !el || !String(el.value).trim();
  }
  const v = f.t === 'i18n' || f.t === 'i18narea' ? dig(body, f.k + '.es_ES') : dig(body, f.k);
  return v === undefined || v === null || String(v).trim() === '';
}

const cssEsc = s => (window.CSS && CSS.escape) ? CSS.escape(s) : s.replace(/[^a-zA-Z0-9_-]/g, '\\$&');
const cap = s => s.charAt(0).toUpperCase() + s.slice(1);
function warn(host, text) {
  host.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>${esc(text)}</span></div></div>`;
  host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}
