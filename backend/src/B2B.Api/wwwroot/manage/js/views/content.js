// Web / Portada del portal — vista NATIVA del back-office de gestión.
// Porta el editor de portada del CMS clásico (admin.html) con el diseño Modernist de
// GESTIÓN. Edita dos bloques por idioma: el carrusel de portada (dashboard.hero) y las
// tarjetas de acceso (dashboard.tiles). Lo que se publica aquí lo lee el portal en
// /api/portal/content/{clave} respetando la ventana de publicación.
//
// IMPORTANTE (compatibilidad): el endpoint de contenido usa locales CORTOS —
// 'es','en','fr','it' y '*' (común)— NO la forma 'es_ES' de util.LOCALES. Enviar
// 'es_ES' daría 400 ("Idioma no soportado"). Por eso aquí se define la lista propia,
// idéntica a la del CMS clásico, para que el portal siga leyendo lo publicado.
import { api } from '../api.js';
import { esc, flash } from '../util.js';
import { icons } from '../icons.js';

const LOCALES = [['*', 'Común'], ['es', 'ES'], ['en', 'EN'], ['fr', 'FR'], ['it', 'IT']];
const BLOCKS = [
  { key: 'dashboard.hero', title: 'Carrusel de portada', kind: 'hero',
    help: 'Imágenes a ancho completo sobre el H1. Se pasan solas cada 6,5 s. Medida recomendada: 1600×470 px (3,4:1).' },
  { key: 'dashboard.tiles', title: 'Tarjetas de acceso', kind: 'tiles',
    help: 'Reposición y Programación: fijan la ventana de servicio del carrito y llevan al catálogo. Medida recomendada: 1200×675 px (16:9).' },
];
const WINDOWS = [['replenishment', 'Reposición'], ['scheduled', 'Programación']];

// El backend guarda las fechas en ISO; el input datetime-local las quiere en hora local
const toInput = iso => {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d)) return '';
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
const fromInput = value => (value ? new Date(value).toISOString() : '');

// La miniatura vacía recuerda la medida que espera el portal
const emptyThumb = kind => `<span>sin imagen<br>${kind === 'hero' ? '1600×470' : '1200×675'}</span>`;
const nextWindow = items => (items.some(i => i.window === 'replenishment') ? 'scheduled' : 'replenishment');

export default async function content(main) {
  injectCss();

  let locale = '*';
  let blocks = {};   // clave -> { items, updatedAt, updatedBy }
  let media = [];    // [{ url, name, size }]

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Contenido</p>
        <h1 class="title">Portada del portal</h1>
        <p class="lead">Carrusel y tarjetas de acceso de la página de inicio, por idioma.
          El portal usa el idioma pedido y, si no lo tiene, el contenido <b>Común</b>.</p>
      </div>
    </div>
    <div id="pf-root"><p class="pf-loading">Cargando la portada…</p></div>`;
  const root = main.querySelector('#pf-root');

  async function loadBlocks() {
    blocks = {};
    for (const block of BLOCKS) {
      try {
        const data = await api.get(`/api/admin/content/${block.key}?locale=${encodeURIComponent(locale)}`);
        blocks[block.key] = { items: data.items || [], updatedAt: data.updatedAt, updatedBy: data.updatedBy };
      } catch {
        blocks[block.key] = { items: [], updatedAt: null, updatedBy: null };   // bloque aún sin publicar (400/404)
      }
    }
  }

  async function loadMedia() {
    try { media = (await api.get('/api/admin/media')).items || []; }
    catch { media = []; }
  }

  async function reload() {
    root.innerHTML = '<p class="pf-loading">Cargando la portada…</p>';
    await Promise.all([loadBlocks(), loadMedia()]);
    paint();
  }

  function paint() {
    root.innerHTML = `
      <div class="mng-tools pf-tools">
        <div class="pf-tabs" role="tablist" aria-label="Idioma del contenido">
          ${LOCALES.map(([code, label]) =>
            `<button type="button" role="tab" data-locale="${code}" aria-selected="${code === locale}"
               class="pf-tab ${code === locale ? 'on' : ''}">${label}</button>`).join('')}
        </div>
        <span class="spacer"></span>
        <a class="btn-ghost pf-view" href="/es/es/dashboard" target="_blank" rel="noopener">Ver la portada ↗</a>
      </div>
      ${BLOCKS.map(paintBlock).join('')}
      <div class="pf-block">
        <header class="pf-block-head">
          <div><h2>Imágenes subidas</h2>
            <p class="pf-help">Se guardan en /media/portal y las sirve el propio portal.</p></div>
        </header>
        ${media.length
          ? `<div class="pf-media">${media.map(f => `
              <figure class="pf-media-item">
                <img src="${esc(f.url)}" alt="" loading="lazy">
                <figcaption>
                  <code title="${esc(f.url)}">${esc(f.name)}</code>
                  <button type="button" class="pf-media-del" data-media-del="${esc(f.name)}"
                    title="Eliminar" aria-label="Eliminar ${esc(f.name)}">${icons.trash(14)}</button>
                </figcaption>
              </figure>`).join('')}</div>`
          : '<div class="pf-empty">Todavía no has subido ninguna imagen.</div>'}
      </div>`;

    root.querySelectorAll('[data-locale]').forEach(b =>
      b.onclick = () => { locale = b.dataset.locale; reload(); });
    root.querySelectorAll('[data-media-del]').forEach(b =>
      b.onclick = () => deleteMedia(b.dataset.mediaDel));
    BLOCKS.forEach(wireBlock);
  }

  function paintBlock(block) {
    const data = blocks[block.key];
    const stamp = data.updatedAt
      ? `Publicado el ${new Date(data.updatedAt).toLocaleString()}${data.updatedBy ? ' por ' + esc(data.updatedBy) : ''}`
      : 'Sin publicar en este idioma';

    return `
      <div class="pf-block" data-block="${block.key}" data-kind="${block.kind}">
        <header class="pf-block-head">
          <div><h2>${esc(block.title)}</h2><p class="pf-help">${esc(block.help)}</p></div>
          <span class="spacer"></span>
          <span class="pf-badge">${stamp}</span>
          <span class="pf-block-actions">
            <button type="button" class="btn-ghost" data-action="add">${icons.plus(15)} Añadir</button>
            <button type="button" class="btn-ghost" data-action="clear">Vaciar</button>
            <button type="button" class="btn-primary" data-action="save">Publicar</button>
          </span>
        </header>
        ${data.items.length
          ? `<div class="pf-list">${data.items.map((item, i) => paintItem(block, item, i, data.items.length)).join('')}</div>`
          : `<div class="pf-empty">Sin elementos. Pulsa <b>Añadir</b> — mientras tanto el portal
               ${block.kind === 'hero' ? 'no muestra carrusel' : 'pinta las dos tarjetas con el color de marca'}.</div>`}
      </div>`;
  }

  function paintItem(block, item, index, total) {
    const field = (name, label, type = 'text', extra = '') => `
      <label class="pf-field">${esc(label)}
        <input class="pf-input" type="${type}" data-field="${name}"
          value="${esc(type === 'datetime-local' ? toInput(item[name]) : item[name] || '')}" ${extra}>
      </label>`;

    return `
      <div class="pf-item ${item.active === false ? 'off' : ''}" data-index="${index}">
        <div class="pf-thumb">${item.imageUrl
          ? `<img src="${esc(item.imageUrl)}" alt="">`
          : emptyThumb(block.kind)}</div>
        <div class="pf-fields">
          <div class="pf-row">
            ${field('imageUrl', 'Imagen (URL)')}
            <button type="button" class="btn-ghost pf-upload" data-action="upload">${icons.upload(14)} Subir…</button>
            ${block.kind === 'tiles' ? `
              <label class="pf-field pf-field-sm">Ventana
                <select class="pf-input" data-field="window">${WINDOWS.map(([value, label]) =>
                  `<option value="${value}" ${item.window === value ? 'selected' : ''}>${label}</option>`).join('')}</select>
              </label>` : ''}
            <label class="pf-check"><input type="checkbox" data-field="active" ${item.active === false ? '' : 'checked'}> Activo</label>
          </div>
          <div class="pf-row">
            ${field('title', block.kind === 'tiles' ? 'Rótulo (vacío = el del idioma)' : 'Título')}
            ${field('subtitle', 'Subtítulo')}
            ${field('alt', 'Texto alternativo')}
          </div>
          <div class="pf-row">
            ${field('ctaText', 'Texto del botón')}
            ${field('ctaHref', 'Enlace', 'text', 'placeholder="/es/es/catalog/catalog"')}
            ${field('imageUrlMobile', 'Imagen móvil (opcional)')}
          </div>
          <div class="pf-row">
            ${field('publishFrom', 'Publicar desde', 'datetime-local')}
            ${field('publishTo', 'Publicar hasta', 'datetime-local')}
          </div>
        </div>
        <div class="pf-actions">
          <button type="button" data-action="up" ${index === 0 ? 'disabled' : ''} title="Subir" aria-label="Subir">↑</button>
          <button type="button" data-action="down" ${index === total - 1 ? 'disabled' : ''} title="Bajar" aria-label="Bajar">↓</button>
          <button type="button" class="pf-del" data-action="del">Eliminar</button>
        </div>
      </div>`;
  }

  function wireBlock(block) {
    const el = root.querySelector(`[data-block="${block.key}"]`);
    const items = blocks[block.key].items;
    const indexOf = node => Number(node.closest('.pf-item').dataset.index);

    // Escribir NO repinta (se perdería el foco): solo actualiza el modelo en memoria
    el.oninput = event => {
      const input = event.target.closest('[data-field]');
      if (!input) return;
      const item = items[indexOf(input)];
      const name = input.dataset.field;
      if (name === 'active') { item.active = input.checked; input.closest('.pf-item').classList.toggle('off', !input.checked); }
      else if (name === 'publishFrom' || name === 'publishTo') item[name] = fromInput(input.value);
      else item[name] = input.value;
      if (name === 'imageUrl') {
        const thumb = input.closest('.pf-item').querySelector('.pf-thumb');
        thumb.innerHTML = item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="">` : emptyThumb(block.kind);
      }
    };

    el.onclick = event => {
      const button = event.target.closest('[data-action]');
      if (!button) return;
      const action = button.dataset.action;

      if (action === 'add') {
        items.push({ imageUrl: '', title: '', active: true, window: block.kind === 'tiles' ? nextWindow(items) : undefined });
        return paint();
      }
      if (action === 'save') return saveBlock(block);
      if (action === 'clear') {
        if (!items.length || confirm('¿Vaciar el bloque en este idioma? El portal dejará de mostrarlo.')) deleteBlock(block);
        return;
      }

      const index = indexOf(button);
      if (action === 'del') items.splice(index, 1);
      if (action === 'up' && index > 0) items.splice(index - 1, 0, items.splice(index, 1)[0]);
      if (action === 'down' && index < items.length - 1) items.splice(index + 1, 0, items.splice(index, 1)[0]);
      if (action === 'upload') return pickImage(items[index]);
      paint();
    };
  }

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
        await loadMedia();
        paint();
      } catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
    input.click();
  }

  async function saveBlock(block) {
    // El orden de la lista manda; el backend valida y normaliza el resto
    const items = blocks[block.key].items.map((item, order) => ({ ...item, order }));
    try {
      const saved = await api.put(`/api/admin/content/${block.key}?locale=${encodeURIComponent(locale)}`, { items });
      blocks[block.key] = { items: saved.items, updatedAt: saved.updatedAt, updatedBy: saved.updatedBy };
      flash(`${block.title}: publicado. Recarga el portal para verlo.`);
      paint();
    } catch (e) { flash(`${block.title}: ${e.body?.error || e.message}`, 'err'); }
  }

  async function deleteBlock(block) {
    try { await api.del(`/api/admin/content/${block.key}?locale=${encodeURIComponent(locale)}`); }
    catch { /* si no existía, ya está vacío */ }
    blocks[block.key] = { items: [], updatedAt: null, updatedBy: null };
    flash(`${block.title}: bloque vaciado en este idioma.`);
    paint();
  }

  async function deleteMedia(name) {
    if (!confirm(`¿Eliminar ${name}? Los bloques que la usen se quedarán sin imagen.`)) return;
    try {
      await api.del(`/api/admin/media/${encodeURIComponent(name)}`);
      await loadMedia();
      paint();
      flash('Imagen eliminada.');
    } catch (e) { flash(e.body?.error || e.message, 'err'); }
  }

  await reload();
}

// CSS propio inyectado una sola vez (prefijo .pf- para no colisionar con manage.css)
function injectCss() {
  if (document.getElementById('content-css')) return;
  const s = document.createElement('style');
  s.id = 'content-css';
  s.textContent = `
    .pf-loading { color:var(--muted); }
    .pf-tools { align-items:center; }
    .pf-tabs { display:inline-flex; border:1px solid var(--line-control); }
    .pf-tab { background:none; border:none; border-radius:0; cursor:pointer; font:inherit;
      padding:.42rem .95rem; font-size:.74rem; font-weight:700; letter-spacing:.09em; text-transform:uppercase;
      color:var(--ink-2); }
    .pf-tab + .pf-tab { border-left:1px solid var(--line-control); }
    .pf-tab:hover { color:var(--ink); background:var(--paper); }
    .pf-tab.on { background:var(--blue); color:#fff; }
    .pf-view { text-decoration:none; }

    .pf-block { background:var(--card); border:1px solid var(--line); border-radius:0; margin-bottom:1.4rem; overflow:hidden; }
    .pf-block-head { display:flex; align-items:flex-start; gap:.9rem; flex-wrap:wrap;
      padding:1rem 1.2rem; border-bottom:1px solid var(--line); }
    .pf-block-head h2 { margin:0; font-size:1.05rem; letter-spacing:-.01em; }
    .pf-help { margin:.25rem 0 0; color:var(--ink-2); font-size:.8rem; max-width:44rem; }
    .pf-block-head .spacer { flex:1; }
    .pf-badge { font-size:.72rem; color:var(--muted); align-self:center; }
    .pf-block-actions { display:inline-flex; gap:.5rem; white-space:nowrap; align-items:center; }
    .pf-block-actions .btn-primary svg, .pf-block-actions .btn-ghost svg { vertical-align:-2px; }

    .pf-list { padding:1rem 1.2rem; display:flex; flex-direction:column; gap:1rem; }
    .pf-item { display:grid; grid-template-columns:10rem 1fr auto; gap:1rem; align-items:start;
      border:1px solid var(--line-control); border-radius:0; padding:.9rem; background:var(--paper); }
    .pf-item.off { opacity:.55; }
    .pf-thumb { aspect-ratio:16/9; overflow:hidden; background:var(--card); border:1px solid var(--line);
      display:grid; place-items:center; color:var(--muted); font-size:.7rem; text-align:center; line-height:1.3; }
    .pf-block[data-kind="hero"] .pf-thumb { aspect-ratio:3.4/1; }
    .pf-thumb img { width:100%; height:100%; object-fit:cover; display:block; }

    .pf-fields { min-width:0; }
    .pf-row { display:flex; gap:.6rem; flex-wrap:wrap; margin-bottom:.6rem; align-items:flex-end; }
    .pf-row:last-child { margin-bottom:0; }
    .pf-field { display:flex; flex-direction:column; gap:.25rem; flex:1; min-width:9rem;
      font-size:.68rem; font-weight:600; letter-spacing:.04em; text-transform:uppercase; color:var(--ink-2); }
    .pf-field-sm { flex:0 0 auto; min-width:0; }
    .pf-input { font:inherit; font-size:.85rem; padding:.45rem .6rem; border:1px solid var(--line-control);
      border-radius:0; background:var(--card); color:var(--ink); width:100%; }
    .pf-input:focus { outline:none; border-color:var(--blue); }
    .pf-check { display:inline-flex; align-items:center; gap:.4rem; font-size:.82rem; color:var(--ink);
      font-weight:600; padding-bottom:.5rem; white-space:nowrap; }
    .pf-check input { width:1.05rem; height:1.05rem; accent-color:var(--blue); }
    .pf-upload { align-self:flex-end; white-space:nowrap; }
    .pf-upload svg { vertical-align:-2px; }

    .pf-actions { display:flex; flex-direction:column; gap:.35rem; }
    .pf-actions button { font:inherit; padding:.32rem .65rem; font-size:.78rem; background:var(--card);
      border:1px solid var(--line-control); border-radius:0; color:var(--ink-2); cursor:pointer; }
    .pf-actions button:hover:not([disabled]) { border-color:var(--blue); color:var(--blue); }
    .pf-actions button[disabled] { opacity:.35; cursor:default; }
    .pf-actions .pf-del:hover { border-color:var(--out); color:var(--out); }

    .pf-media { display:flex; gap:.8rem; flex-wrap:wrap; padding:1rem 1.2rem; }
    .pf-media-item { margin:0; width:9rem; }
    .pf-media-item img { width:100%; aspect-ratio:16/9; object-fit:cover; border:1px solid var(--line); background:var(--card); display:block; }
    .pf-media-item figcaption { display:flex; gap:.35rem; align-items:center; margin-top:.3rem; }
    .pf-media-item code { flex:1; font:.68rem ui-monospace,Consolas,monospace; color:var(--muted);
      overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
    .pf-media-del { display:inline-grid; place-items:center; padding:.2rem; background:none;
      border:1px solid var(--line-control); border-radius:0; color:var(--muted); cursor:pointer; }
    .pf-media-del:hover { border-color:var(--out); color:var(--out); }

    .pf-empty { padding:1.6rem 1.2rem; color:var(--muted); font-size:.9rem; }

    @media (max-width:44rem) {
      .pf-item { grid-template-columns:1fr; }
      .pf-actions { flex-direction:row; flex-wrap:wrap; }
    }`;
  document.head.append(s);
}
