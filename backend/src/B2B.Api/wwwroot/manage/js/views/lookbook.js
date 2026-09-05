// Editor del LOOKBOOK — vista nativa del back-office de GESTIÓN. Porta, con el
// diseño Modernist de /manage, el editor que vivía en admin.html (CMS clásico):
// portada (carrusel) + historias editoriales con su raíl de productos «Compra el
// look». Escribe las claves lookbook.hero y lookbook.stories de portal_content;
// el portal las lee en /es/es/lookbook.
//
// Endpoints (idénticos a admin.html):
//   GET    /api/admin/content/<key>?locale=<loc>   → { items, updatedAt, updatedBy }
//   PUT    /api/admin/content/<key>?locale=<loc>   { items }
//   DELETE /api/admin/content/<key>?locale=<loc>
//   POST   /api/admin/media  (api.uploadMedia)     → { url, name, size }
//   GET    /api/admin/model-images                 → catálogo para el selector
//
// Nota sobre las referencias del raíl: el portal (portal/js/views/lookbook.js)
// indexa el catálogo por `modelId` y casa `story.refs` contra ese modelId. Por
// eso las refs se guardan como IDs de MODELO — igual que en admin.html, que las
// tomaba de /api/admin/model-images (externalId = id de modelo). Usar variantes
// (product) rompería el emparejamiento: el raíl saldría vacío en el portal.
import { api } from '../api.js';
import { esc, flash } from '../util.js';
import { icons } from '../icons.js';

// ── Definición de bloques (portada + historias) ─────────────────────────────
const LB_BLOCKS = [
  { key: 'lookbook.hero', title: 'Portada del lookbook', kind: 'lb-hero',
    help: 'Apertura del lookbook. En una marca sobre papel es un índice tipográfico sin foto: el título es la palabra de temporada (corta, p. ej. «SS27»), el subtítulo la línea de estado («Abierta a pedidos.») y el botón la acción; la imagen se ignora y puede quedar vacía. En las demás marcas, con imagen es un carrusel (2880×1200 px o mayor).' },
  { key: 'lookbook.stories', title: 'Historias', kind: 'lb-story',
    help: 'Cada historia: una foto, un texto editorial, un color de acento y los productos de su raíl «Compra el look».' },
];
const LB_LAYOUTS = [['right', 'Foto a la derecha'], ['left', 'Foto a la izquierda']];

// Idiomas del contenido del portal. El backend (PortalContentModel.Locales) solo
// acepta estos códigos cortos: es, en, fr, it y * (Común). Por eso NO se usa el
// LOCALES de util.js (es_ES, en_EN…): el content API los rechazaría con 400.
const LB_LOCALES = [['*', 'Común'], ['es', 'ES'], ['en', 'EN'], ['fr', 'FR'], ['it', 'IT']];

// ── Fechas <input type="datetime-local"> ↔ ISO ──────────────────────────────
const toInput = iso => {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d)) return '';
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
const fromInput = value => value ? new Date(value).toISOString() : '';

export default async function lookbook(main) {
  injectCss();

  let lbLocale = '*';
  let lbBlocks = {};   // clave -> { items, updatedAt, updatedBy }
  let lbModels = [];   // modelos del catálogo, para el selector de productos
  const lbModelById = id => lbModels.find(m => m.externalId === id);

  // Armazón fijo: cabecera de página + contenedor que se repinta.
  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Contenido</p>
        <h1 class="title">Lookbook</h1>
        <p class="lead">La portada y las historias editoriales del lookbook, con su raíl «Compra el look».
          El portal usa el idioma pedido y, si no lo tiene, el contenido <b>Común</b>.</p>
      </div>
    </div>
    <div id="lbk-root"><div class="skeleton"></div><div class="skeleton short"></div></div>`;
  const root = main.querySelector('#lbk-root');

  await render();

  // ── Carga de datos ────────────────────────────────────────────────────────
  async function render() {
    root.innerHTML = '<div class="skeleton"></div><div class="skeleton short"></div>';
    await Promise.all([loadBlocks(), loadModels()]);
    paint();
  }

  async function loadBlocks() {
    lbBlocks = {};
    for (const block of LB_BLOCKS) {
      try {
        const data = await api.get(`/api/admin/content/${block.key}?locale=${encodeURIComponent(lbLocale)}`);
        lbBlocks[block.key] = { items: data.items || [], updatedAt: data.updatedAt, updatedBy: data.updatedBy };
      } catch {
        lbBlocks[block.key] = { items: [], updatedAt: null, updatedBy: null };   // bloque aún sin publicar
      }
    }
  }

  async function loadModels() {
    try { lbModels = (await api.modelImages()).items || []; }
    catch { lbModels = []; }
  }

  // ── Pintado ───────────────────────────────────────────────────────────────
  function paint() {
    root.innerHTML = `
      <div class="lbk-toolbar">
        <div class="lbk-tabs" role="tablist">${LB_LOCALES.map(([code, label]) =>
          `<button type="button" role="tab" data-lb-locale="${code}" class="${code === lbLocale ? 'on' : ''}"
             aria-selected="${code === lbLocale}">${esc(label)}</button>`).join('')}</div>
        <span class="spacer"></span>
        <a class="btn-ghost" href="/es/es/lookbook" target="_blank" rel="noopener">Ver el lookbook ↗</a>
      </div>
      ${LB_BLOCKS.map(paintBlock).join('')}`;

    root.querySelectorAll('[data-lb-locale]').forEach(b =>
      b.onclick = () => { lbLocale = b.dataset.lbLocale; render(); });
    LB_BLOCKS.forEach(wireBlock);
  }

  function paintBlock(block) {
    const data = lbBlocks[block.key];
    const stamp = data.updatedAt
      ? `Publicado el ${new Date(data.updatedAt).toLocaleString()}${data.updatedBy ? ' por ' + esc(data.updatedBy) : ''}`
      : 'Sin publicar en este idioma';
    const paintItem = block.kind === 'lb-hero' ? paintHeroItem : paintStoryItem;
    const emptyMsg = block.kind === 'lb-hero'
      ? 'Sin portada. Pulsa <b>+ Añadir</b> — mientras tanto el lookbook abre directamente con las historias.'
      : 'Sin historias. Pulsa <b>+ Añadir</b> para crear la primera.';
    return `
      <section class="lbk-block" data-lb-block="${block.key}" data-kind="${block.kind}">
        <header class="lbk-block-head">
          <div class="lbk-block-title">
            <h2>${esc(block.title)}</h2>
            <span class="lbk-help">${esc(block.help)}</span>
          </div>
          <span class="spacer"></span>
          <span class="lbk-badge">${stamp}</span>
          <span class="lbk-block-actions">
            <button type="button" class="btn-ghost" data-action="add">+ Añadir</button>
            <button type="button" class="btn-ghost" data-action="clear">Vaciar</button>
            <button type="button" class="btn-primary" data-action="save">Publicar</button>
          </span>
        </header>
        ${data.items.length
          ? `<div class="lbk-list">${data.items.map((item, i) => paintItem(item, i, data.items.length)).join('')}</div>`
          : `<div class="lbk-empty">${emptyMsg}</div>`}
      </section>`;
  }

  // Función declarada (hoisted): la usan paintHeroItem/paintStoryItem, invocadas
  // desde el primer paint() antes de que se ejecute esta línea en el cuerpo.
  function field(item, name, label, type = 'text', extra = '') {
    return `
    <label class="grow">${esc(label)}
      <input type="${type}" data-field="${name}" value="${esc(type === 'datetime-local' ? toInput(item[name]) : item[name] || '')}" ${extra}>
    </label>`;
  }

  function emptyThumb(kind) { return `<span>sin imagen<br>${kind === 'lb-hero' ? 'apertura tipográfica' : '4:3'}</span>`; }

  function itemActions(index, total) {
    return `
    <div class="lbk-item-actions">
      <button type="button" data-action="up" ${index === 0 ? 'disabled' : ''} title="Subir" aria-label="Subir">↑</button>
      <button type="button" data-action="down" ${index === total - 1 ? 'disabled' : ''} title="Bajar" aria-label="Bajar">↓</button>
      <button type="button" class="del" data-action="del">Eliminar</button>
    </div>`;
  }

  function paintHeroItem(item, index, total) {
    return `
      <div class="lbk-item ${item.active === false ? 'off' : ''}" data-index="${index}">
        <div class="lbk-thumb">${item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="">` : emptyThumb('lb-hero')}</div>
        <div class="lbk-fields">
          <div class="lbk-row">
            ${field(item, 'imageUrl', 'Imagen (URL)')}
            <button type="button" class="btn-ghost lbk-upload" data-action="upload">Subir…</button>
            <label class="chk"><input type="checkbox" data-field="active" ${item.active === false ? '' : 'checked'}> Activo</label>
          </div>
          <div class="lbk-row">
            ${field(item, 'title', 'Título')}
            ${field(item, 'subtitle', 'Subtítulo')}
            ${field(item, 'alt', 'Texto alternativo')}
          </div>
          <div class="lbk-row">
            ${field(item, 'ctaText', 'Texto del botón')}
            ${field(item, 'ctaHref', 'Enlace', 'text', 'placeholder="/es/es/catalog/catalog"')}
            ${field(item, 'imageUrlMobile', 'Imagen móvil (opcional)')}
          </div>
          <div class="lbk-hint">Fechas opcionales: si las dejas vacías, la portada se ve siempre.</div>
          <div class="lbk-row">
            ${field(item, 'publishFrom', 'Publicar desde', 'datetime-local')}
            ${field(item, 'publishTo', 'Publicar hasta', 'datetime-local')}
          </div>
        </div>
        ${itemActions(index, total)}
      </div>`;
  }

  function paintStoryItem(item, index, total) {
    const validHex = /^#[0-9a-fA-F]{6}$/.test(item.accent || '');
    const layout = item.layout === 'left' ? 'left' : 'right';
    return `
      <div class="lbk-item ${item.active === false ? 'off' : ''}" data-index="${index}">
        <div class="lbk-thumb">${item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="">` : emptyThumb('lb-story')}</div>
        <div class="lbk-fields">
          <div class="lbk-preview-wrap">
            <div class="lbk-preview-label">Vista previa</div>
            <div data-preview>${storyPreview(item)}</div>
          </div>
          <div class="lbk-row">
            ${field(item, 'imageUrl', 'Imagen (URL)')}
            <button type="button" class="btn-ghost lbk-upload" data-action="upload">Subir…</button>
            <label class="lbk-color" title="Déjalo vacío para usar el color de marca">Acento <span class="lbk-hintlabel">(vacío = marca)</span>
              <input type="color" data-field="accentColor" value="${validHex ? item.accent : '#1f5c46'}">
              <input type="text" data-field="accent" value="${esc(item.accent || '')}" placeholder="(marca)" maxlength="9">
            </label>
            <label>Disposición
              <select data-field="layout">${LB_LAYOUTS.map(([v, l]) =>
                `<option value="${v}" ${layout === v ? 'selected' : ''}>${esc(l)}</option>`).join('')}</select>
            </label>
            <label class="chk"><input type="checkbox" data-field="active" ${item.active === false ? '' : 'checked'}> Activo</label>
          </div>
          <div class="lbk-row">
            ${field(item, 'kicker', 'Etiqueta (kicker)', 'text', 'placeholder="p. ej. NUEVA TEMPORADA"')}
            ${field(item, 'title', 'Título')}
          </div>
          <div class="lbk-row lbk-body">
            <label class="grow">Texto
              <textarea data-field="body">${esc(item.body || '')}</textarea>
            </label>
          </div>
          <div class="lbk-row">
            <div class="lbk-refs">
              <div class="lbk-refs-title">Productos del raíl «Compra el look»${item.refs && item.refs.length ? ` · ${item.refs.length}` : ''}</div>
              <div class="lbk-chips">${(item.refs || []).map(refChip).join('')}</div>
              <div class="lbk-picker">
                <input type="search" data-ref-search aria-label="Buscar producto para el raíl"
                  placeholder="Buscar producto por referencia o nombre…" autocomplete="off">
                <div class="lbk-picker-list"></div>
              </div>
            </div>
          </div>
          <div class="lbk-hint">Fechas opcionales: si las dejas vacías, la historia se ve siempre.</div>
          <div class="lbk-row">
            ${field(item, 'publishFrom', 'Publicar desde', 'datetime-local')}
            ${field(item, 'publishTo', 'Publicar hasta', 'datetime-local')}
          </div>
        </div>
        ${itemActions(index, total)}
      </div>`;
  }

  // Vista previa en vivo de la historia: mini-tarjeta que imita el portal (imagen,
  // kicker con el acento, título y disposición foto izq/dcha).
  function storyPreview(item) {
    const accent = /^#[0-9a-fA-F]{3,8}$/.test(item.accent || '') ? item.accent : 'var(--blue)';
    const img = item.imageUrl
      ? `<div class="lbk-p-img"><img src="${esc(item.imageUrl)}" alt=""></div>`
      : `<div class="lbk-p-img"></div>`;
    const text = `<div class="lbk-p-text">
        <span class="lbk-p-kicker" style="color:${esc(accent)}">${esc(item.kicker || 'ETIQUETA')}</span>
        <span class="lbk-p-title">${esc(item.title || 'Título de la historia')}</span>
        <span class="lbk-p-count">${(item.refs || []).length} producto(s) en el raíl</span>
      </div>`;
    return `<div class="lbk-p">${item.layout === 'left' ? img + text : text + img}</div>`;
  }

  // ── Selector de referencias (chips + buscador con lista desplegable) ────────
  function refChip(id) {
    const model = lbModelById(id);
    const remove = `<button type="button" class="x" data-ref-del="${esc(id)}" title="Quitar" aria-label="Quitar">✕</button>`;
    if (!model)
      return `<span class="lbk-chip"><img alt=""><span class="miss" title="Este id ya no está en el catálogo">${esc(id)} ⚠</span>${remove}</span>`;
    const name = esc(model.name || model.reference || id);
    const title = esc([model.name, model.reference].filter(Boolean).join(' · '));
    const thumb = model.imageUri ? `<img src="${esc(model.imageUri)}" alt="">` : `<img alt="">`;
    return `<span class="lbk-chip" title="${title}">${thumb}${name}${remove}</span>`;
  }

  function pickerResults(query, selected) {
    const q = (query || '').trim().toLowerCase();
    const chosen = new Set(selected || []);
    const all = lbModels.filter(m => !chosen.has(m.externalId) &&
      (!q || (m.reference || '').toLowerCase().includes(q) || (m.name || '').toLowerCase().includes(q)));
    const matches = all.slice(0, 25);
    if (!matches.length) return `<div class="lbk-picker-empty">Sin productos que coincidan.</div>`;
    const more = all.length > matches.length
      ? `<div class="lbk-picker-empty">Mostrando 25 de ${all.length} — sigue escribiendo para afinar.</div>` : '';
    return matches.map(m => `
      <button type="button" data-ref-add="${esc(m.externalId)}">
        ${m.imageUri ? `<img src="${esc(m.imageUri)}" alt="">` : `<span class="ph"></span>`}
        <span>${esc(m.name || '—')}</span><span class="ref">${esc(m.reference || '')}</span>
      </button>`).join('') + more;
  }

  // Actualiza SOLO los chips + contador + lista + preview de una historia, sin
  // repintar toda la vista: así al añadir productos el buscador sigue abierto.
  function syncRefs(cItem, item, searchValue) {
    const chips = cItem.querySelector('.lbk-chips');
    if (chips) chips.innerHTML = (item.refs || []).map(refChip).join('');
    const title = cItem.querySelector('.lbk-refs-title');
    if (title) title.textContent = `Productos del raíl «Compra el look»${item.refs && item.refs.length ? ` · ${item.refs.length}` : ''}`;
    const list = cItem.querySelector('.lbk-picker-list');
    if (list) list.innerHTML = pickerResults(searchValue || '', item.refs || []);
    const preview = cItem.querySelector('[data-preview]');
    if (preview) preview.innerHTML = storyPreview(item);
  }

  // ── Cableado de eventos de un bloque ──────────────────────────────────────
  function wireBlock(block) {
    const el = root.querySelector(`[data-lb-block="${block.key}"]`);
    const items = lbBlocks[block.key].items;
    const indexOf = node => Number(node.closest('.lbk-item').dataset.index);
    const isStory = block.kind === 'lb-story';

    const openPicker = node => {
      const cItem = node.closest('.lbk-item');
      const item = items[indexOf(node)];
      const list = cItem.querySelector('.lbk-picker-list');
      list.innerHTML = pickerResults(cItem.querySelector('[data-ref-search]').value, item.refs || []);
      list.classList.add('on');
    };

    // Escribir no repinta (se perdería el foco): solo actualiza el modelo y la preview.
    el.oninput = event => {
      if (event.target.closest('[data-ref-search]')) { openPicker(event.target); return; }

      const input = event.target.closest('[data-field]');
      if (!input) return;
      const cItem = input.closest('.lbk-item');
      const item = items[indexOf(input)];
      const name = input.dataset.field;

      if (name === 'active') { item.active = input.checked; cItem.classList.toggle('off', !input.checked); return; }
      if (name === 'publishFrom' || name === 'publishTo') { item[name] = fromInput(input.value); return; }
      if (name === 'accentColor') {
        // El selector de color escribe el campo de texto, que es la fuente de verdad.
        item.accent = input.value;
        const text = cItem.querySelector('[data-field="accent"]');
        if (text) text.value = input.value;
      } else {
        item[name] = input.value;
        if (name === 'accent') {
          const color = cItem.querySelector('[data-field="accentColor"]');
          if (color && /^#[0-9a-fA-F]{6}$/.test(input.value)) color.value = input.value;
        }
        if (name === 'imageUrl') {
          const thumb = cItem.querySelector('.lbk-thumb');
          thumb.innerHTML = item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="">` : emptyThumb(block.kind);
        }
      }
      if (isStory) {
        const preview = cItem.querySelector('[data-preview]');
        if (preview) preview.innerHTML = storyPreview(item);
      }
    };

    el.onclick = event => {
      // Al enfocar/pulsar el buscador, abre la lista con los productos disponibles.
      if (event.target.closest('[data-ref-search]')) { openPicker(event.target); return; }

      // Añadir / quitar un producto del raíl: actualización incremental (no repinta).
      const add = event.target.closest('[data-ref-add]');
      if (add) {
        const cItem = add.closest('.lbk-item');
        const item = items[indexOf(add)];
        item.refs = item.refs || [];
        if (!item.refs.includes(add.dataset.refAdd)) item.refs.push(add.dataset.refAdd);
        const search = cItem.querySelector('[data-ref-search]');
        syncRefs(cItem, item, search ? search.value : '');
        cItem.querySelector('.lbk-picker-list').classList.add('on');
        if (search) search.focus();
        return;
      }
      const del = event.target.closest('[data-ref-del]');
      if (del) {
        const cItem = del.closest('.lbk-item');
        const item = items[indexOf(del)];
        item.refs = (item.refs || []).filter(r => r !== del.dataset.refDel);
        const search = cItem.querySelector('[data-ref-search]');
        syncRefs(cItem, item, search ? search.value : '');
        return;
      }

      // Pulsar fuera de un selector cierra los que estén abiertos.
      el.querySelectorAll('.lbk-picker-list.on').forEach(l => l.classList.remove('on'));

      const button = event.target.closest('[data-action]');
      if (!button) return;
      const action = button.dataset.action;

      if (action === 'add') {
        items.push(block.kind === 'lb-hero'
          ? { imageUrl: '', title: '', active: true }
          : { imageUrl: '', kicker: '', title: '', body: '', accent: '', layout: 'right', refs: [], active: true });
        return paint();
      }
      if (action === 'save') return saveBlock(block);
      if (action === 'clear') {
        if (!items.length || confirm('¿Vaciar este bloque en este idioma? El lookbook dejará de mostrarlo.')) deleteBlock(block);
        return;
      }
      if (action === 'upload') return pickImage(items[indexOf(button)]);

      const index = indexOf(button);
      if (action === 'del') items.splice(index, 1);
      if (action === 'up' && index > 0) items.splice(index - 1, 0, items.splice(index, 1)[0]);
      if (action === 'down' && index < items.length - 1) items.splice(index + 1, 0, items.splice(index, 1)[0]);
      paint();
    };
  }

  // ── Subir imagen ──────────────────────────────────────────────────────────
  function pickImage(item) {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/png,image/jpeg,image/webp,image/avif,image/gif,image/svg+xml';
    input.onchange = async () => {
      const file = input.files[0];
      if (!file) return;
      try {
        const data = await api.uploadMedia(file);
        item.imageUrl = data.url;
        flash(`Imagen subida: ${data.name}`);
        paint();
      } catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
    input.click();
  }

  // ── Publicar / vaciar ─────────────────────────────────────────────────────
  async function saveBlock(block) {
    // El orden de la lista manda; el backend valida y normaliza el resto.
    const items = lbBlocks[block.key].items.map((item, order) => ({ ...item, order }));
    try {
      const saved = await api.put(`/api/admin/content/${block.key}?locale=${encodeURIComponent(lbLocale)}`, { items });
      lbBlocks[block.key] = { items: saved.items, updatedAt: saved.updatedAt, updatedBy: saved.updatedBy };
      flash(`${block.title}: publicado. Recarga el lookbook para verlo.`);
      paint();
    } catch (e) { flash(`${block.title}: ${e.body?.error || e.message}`, 'err'); }
  }

  async function deleteBlock(block) {
    try { await api.del(`/api/admin/content/${block.key}?locale=${encodeURIComponent(lbLocale)}`); }
    catch { /* si no existía, ya está vacío */ }
    lbBlocks[block.key] = { items: [], updatedAt: null, updatedBy: null };
    flash(`${block.title}: bloque vaciado en este idioma.`);
    paint();
  }
}

// ── CSS del editor (inyectado una sola vez, prefijo .lbk-) ───────────────────
// No toca manage.css. Usa los tokens Modernist del portal (--blue, --ink, --line,
// --line-control, --paper, --card, --muted, --out). Radio 0, filetes finos.
function injectCss() {
  if (document.getElementById('lookbook-css')) return;
  const s = document.createElement('style');
  s.id = 'lookbook-css';
  s.textContent = `
  #lbk-root .lbk-toolbar { display:flex; align-items:center; gap:.8rem; flex-wrap:wrap; margin-bottom:1.3rem; }
  #lbk-root .lbk-toolbar .spacer { flex:1; }
  #lbk-root .lbk-toolbar .btn-ghost { text-decoration:none; }
  .lbk-tabs { display:inline-flex; border:1px solid var(--line-control); }
  .lbk-tabs button { background:#fff; color:var(--muted); padding:.42rem 1rem; border:none; border-right:1px solid var(--line-control);
    font-size:.78rem; font-weight:700; letter-spacing:.07em; text-transform:uppercase; cursor:pointer; }
  .lbk-tabs button:last-child { border-right:none; }
  .lbk-tabs button.on { background:var(--blue); color:#fff; }

  .lbk-block { background:var(--card); border:1px solid var(--line); margin-bottom:1.6rem; }
  .lbk-block-head { display:flex; align-items:center; gap:.8rem; padding:.9rem 1.1rem; border-bottom:1px solid var(--line); flex-wrap:wrap; }
  .lbk-block-head .spacer { flex:1; }
  .lbk-block-title { display:flex; flex-direction:column; gap:.15rem; }
  .lbk-block-title h2 { margin:0; font-size:1rem; font-family:var(--display); letter-spacing:-.01em; }
  .lbk-help { color:var(--muted); font-size:.8rem; max-width:34rem; }
  .lbk-badge { font-size:.72rem; color:var(--muted); }
  .lbk-block-actions { display:inline-flex; gap:.5rem; white-space:nowrap; }
  .lbk-block-actions .btn-ghost, .lbk-block-actions .btn-primary { padding:.42rem .9rem; font-size:.82rem; }
  .lbk-empty { padding:1.4rem 1.1rem; color:var(--muted); font-size:.88rem; }

  .lbk-list { padding:.9rem 1.1rem; display:flex; flex-direction:column; gap:.9rem; }
  .lbk-item { display:grid; grid-template-columns:9.5rem 1fr auto; gap:.9rem; align-items:start;
    border:1px solid var(--line); padding:.8rem; background:var(--paper); }
  .lbk-item.off { opacity:.55; }
  .lbk-fields { min-width:0; }
  .lbk-thumb { aspect-ratio:16/9; overflow:hidden; background:var(--surface); display:grid; place-items:center;
    color:var(--muted); font-size:.72rem; text-align:center; border:1px solid var(--line); }
  .lbk-block[data-kind="lb-hero"] .lbk-thumb { aspect-ratio:3.4/1; }
  .lbk-block[data-kind="lb-story"] .lbk-thumb { aspect-ratio:4/3; }
  .lbk-thumb img { width:100%; height:100%; object-fit:cover; display:block; }

  .lbk-row { display:flex; gap:.6rem; flex-wrap:wrap; margin-bottom:.55rem; }
  .lbk-row:last-child { margin-bottom:0; }
  .lbk-item label { display:flex; flex-direction:column; gap:.2rem; font-size:.72rem; color:var(--muted); }
  .lbk-item label.grow { flex:1; min-width:9rem; }
  .lbk-item label.chk { flex-direction:row; align-items:center; gap:.35rem; font-size:.8rem; color:var(--ink); align-self:end; padding-bottom:.5rem; }
  .lbk-item input, .lbk-item select { font:inherit; font-size:.82rem; padding:.4rem .55rem; border:1px solid var(--line-control);
    border-radius:0; background:#fff; color:var(--ink); }
  .lbk-item input:focus, .lbk-item select:focus, .lbk-item textarea:focus {
    border-color:var(--blue); box-shadow:0 0 0 3px rgba(236,48,19,.18); outline:none; }
  .lbk-item input[type=checkbox] { padding:0; }
  .lbk-upload { align-self:end; }

  .lbk-item-actions { display:flex; flex-direction:column; gap:.35rem; }
  .lbk-item-actions button { padding:.3rem .6rem; font-size:.78rem; background:#fff; border:1px solid var(--line-control); color:var(--muted); cursor:pointer; }
  .lbk-item-actions button:hover:not([disabled]) { border-color:var(--blue); color:var(--blue); }
  .lbk-item-actions button.del:hover { border-color:var(--out); color:var(--out); }
  .lbk-item-actions button[disabled] { opacity:.35; cursor:default; }

  .lbk-hint { flex:1 1 100%; font-size:.72rem; color:var(--muted); margin-top:-.1rem; }
  .lbk-hintlabel { font-weight:400; color:var(--muted); }

  .lbk-body { flex:1 1 100%; }
  .lbk-body textarea { width:100%; min-height:4.4rem; resize:vertical; font:inherit; font-size:.82rem;
    padding:.4rem .55rem; border-radius:0; border:1px solid var(--line-control); background:#fff; color:var(--ink); }

  .lbk-color { flex-direction:row !important; align-items:center; gap:.4rem; align-self:end; padding-bottom:.15rem; }
  .lbk-color input[type=color] { width:2.3rem; height:1.95rem; padding:.1rem; border:1px solid var(--line-control);
    border-radius:0; background:#fff; cursor:pointer; }
  .lbk-color input[type=text] { width:6rem; font:.76rem ui-monospace,Consolas,monospace; }

  .lbk-refs { flex:1 1 100%; border:1px dashed var(--line-control); border-radius:0; padding:.6rem .7rem; background:#fff; }
  .lbk-refs-title { font-size:.72rem; color:var(--muted); margin-bottom:.45rem; display:flex; gap:.5rem; align-items:center; }
  .lbk-chips { display:flex; flex-wrap:wrap; gap:.4rem; margin-bottom:.5rem; }
  .lbk-chips:empty { display:none; }
  .lbk-chip { display:inline-flex; align-items:center; gap:.4rem; background:var(--blue-soft); border:1px solid #f2b8ac;
    border-radius:0; padding:.12rem .35rem .12rem .12rem; font-size:.78rem; color:var(--ink); }
  .lbk-chip img { width:1.5rem; height:1.5rem; border-radius:0; object-fit:cover; background:var(--surface); }
  .lbk-chip .miss { color:var(--out); font:.72rem ui-monospace,Consolas,monospace; }
  .lbk-chip .x { background:none; border:none; color:var(--muted); cursor:pointer; padding:0 .15rem; font-size:.95rem; line-height:1; }
  .lbk-chip .x:hover { color:var(--out); }

  .lbk-picker { position:relative; max-width:26rem; }
  .lbk-picker input { width:100%; font-size:.82rem; padding:.4rem .55rem; border:1px solid var(--line-control); border-radius:0; background:#fff; }
  .lbk-picker input:focus { border-color:var(--blue); box-shadow:0 0 0 3px rgba(236,48,19,.18); outline:none; }
  .lbk-picker-list { position:absolute; z-index:6; left:0; right:0; top:calc(100% + .2rem); background:#fff;
    border:1px solid var(--line-control); border-radius:0; box-shadow:var(--shadow-card); max-height:15rem; overflow-y:auto; display:none; }
  .lbk-picker-list.on { display:block; }
  .lbk-picker-list button { display:flex; align-items:center; gap:.5rem; width:100%; text-align:left; background:none;
    border:none; border-bottom:1px solid var(--line); padding:.4rem .55rem; font:inherit; font-size:.8rem; font-weight:500; cursor:pointer; color:var(--ink); }
  .lbk-picker-list button:hover { background:var(--blue-soft); }
  .lbk-picker-list button:last-child { border-bottom:none; }
  .lbk-picker-list img, .lbk-picker-list .ph { width:1.9rem; height:1.9rem; border-radius:0; object-fit:cover; background:var(--surface); flex:none; }
  .lbk-picker-list .ref { color:var(--muted); font:.72rem ui-monospace,Consolas,monospace; margin-left:auto; padding-left:.5rem; }
  .lbk-picker-empty { padding:.5rem .6rem; color:var(--muted); font-size:.78rem; }

  /* Vista previa en vivo de la historia */
  .lbk-preview-wrap { margin-bottom:.55rem; }
  .lbk-preview-label { font-size:.68rem; color:var(--muted); text-transform:uppercase; letter-spacing:.05em; margin-bottom:.25rem; }
  .lbk-p { display:flex; align-items:center; gap:.7rem; background:var(--paper); border:1px solid var(--line); border-radius:0; padding:.55rem .65rem; }
  .lbk-p-img { width:5.5rem; flex:none; aspect-ratio:4/3; border-radius:0; overflow:hidden; background:var(--surface); }
  .lbk-p-img img { width:100%; height:100%; object-fit:cover; display:block; }
  .lbk-p-text { flex:1; min-width:0; display:flex; flex-direction:column; gap:.2rem; }
  .lbk-p-kicker { align-self:flex-start; font-size:.62rem; font-weight:700; letter-spacing:.07em; text-transform:uppercase;
    border-bottom:2px solid currentColor; padding-bottom:.1rem; }
  .lbk-p-title { font-size:.98rem; font-weight:650; line-height:1.15; letter-spacing:-.01em; font-family:var(--display);
    overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .lbk-p-count { font-size:.68rem; color:var(--muted); }

  @media (max-width:56rem){
    .lbk-item { grid-template-columns:1fr; }
    .lbk-item-actions { flex-direction:row; }
  }`;
  document.head.append(s);
}
