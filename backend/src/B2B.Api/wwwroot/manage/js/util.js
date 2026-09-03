// Utilidades compartidas del back-office.
import { esc } from '/portal/js/format.js';
import { api } from './api.js';
import { FK_LABEL } from './schemas.js';

export { esc };

// Aviso flotante (toast) reutilizando los colores del portal
export function flash(message, kind = 'ok') {
  let box = document.getElementById('mng-flash');
  if (!box) { box = document.createElement('div'); box.id = 'mng-flash'; document.body.append(box); }
  box.className = kind;
  // Lectores de pantalla: los errores interrumpen (alert), las confirmaciones no (status)
  box.setAttribute('role', kind === 'ok' ? 'status' : 'alert');
  box.textContent = message;
  // Reflow para reiniciar la transición si ya estaba visible
  void box.offsetWidth;
  box.classList.add('on');
  clearTimeout(flash.timer);
  flash.timer = setTimeout(() => box.classList.remove('on'), kind === 'ok' ? 3200 : 6000);
}

// Lee obj por ruta "a.b.c" (soporta fallback "a|b")
export function dig(obj, path) {
  for (const alt of path.split('|')) {
    let node = obj;
    let ok = true;
    for (const key of alt.split('.')) {
      if (node == null) { ok = false; break; }
      node = node[key];
    }
    if (ok && node !== undefined && node !== null && node !== '') return node;
  }
  return undefined;
}

// setPath(obj,"a.b.0.c",v) creando objetos/arrays por el camino
export function setPath(root, path, value) {
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

// Borra obj.a.b.c (para vaciar un campo opcional al editar, sin dejar el valor viejo)
export function delPath(root, path) {
  const parts = path.replace(/\[(\d+)\]/g, '.$1').split('.');
  let node = root;
  for (let i = 0; i < parts.length - 1; i++) {
    if (node == null) return;
    node = node[parts[i]];
  }
  if (node && typeof node === 'object') delete node[parts[parts.length - 1]];
}

export const slugify = s => String(s || '').toLowerCase().trim()
  .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

export const LOCALES = ['es_ES', 'en_EN', 'fr_FR', 'it_IT'];
export const i18nObject = text => Object.fromEntries(LOCALES.map(l => [l, text]));

// Opciones de una FK (otro maestro), cacheadas
const optionsCache = {};
export function invalidateOptions(type) { delete optionsCache[type]; }
export async function fkOptions(type) {
  if (optionsCache[type]) return optionsCache[type];
  const data = await api.docs(type).catch(() => ({ items: [] }));
  const label = FK_LABEL[type] || '__externalId';
  optionsCache[type] = (data.items || []).map(d => {
    let p = {}; try { p = JSON.parse(d.payload); } catch {}
    if (Array.isArray(p)) p = p[0] ?? {};
    p.__externalId = d.externalId;
    return { value: d.externalId, label: String(dig(p, label) ?? d.externalId) };
  }).sort((a, b) => a.label.localeCompare(b.label, 'es'));
  return optionsCache[type];
}

// Modal reutilizable para ver el JSON recibido (Comunicación BC y "Ver JSON" por ficha).
// Se cierra con la ✕, clic fuera o Escape. Estilo Modernist (radio 0), CSS inyectado 1 vez.
export function showJson(title, payload, meta = '') {
  ensureJsonCss();
  let pretty;
  try { pretty = typeof payload === 'string' ? JSON.stringify(JSON.parse(payload), null, 2) : JSON.stringify(payload, null, 2); }
  catch { pretty = String(payload); }
  const overlay = document.createElement('div');
  overlay.className = 'json-modal';
  overlay.innerHTML = `<div class="json-box" role="dialog" aria-modal="true" aria-label="${esc(title)}">
    <header><h3>${esc(title)}</h3>${meta ? `<span class="json-meta">${esc(meta)}</span>` : ''}
      <button class="json-close" aria-label="Cerrar">✕</button></header>
    <pre></pre></div>`;
  overlay.querySelector('pre').textContent = pretty;
  const opener = document.activeElement;   // para devolver el foco al cerrar
  const close = () => {
    overlay.remove();
    document.removeEventListener('keydown', onKey);
    window.removeEventListener('hashchange', close);
    if (opener && typeof opener.focus === 'function') opener.focus();
  };
  const onKey = e => { if (e.key === 'Escape') { e.stopPropagation(); close(); } };
  overlay.querySelector('.json-close').onclick = close;
  overlay.onclick = e => { if (e.target === overlay) close(); };
  document.addEventListener('keydown', onKey);
  window.addEventListener('hashchange', close);   // cambiar de vista no deja el modal colgado
  document.body.append(overlay);
  overlay.querySelector('.json-close').focus();
}

function ensureJsonCss() {
  if (document.getElementById('json-modal-css')) return;
  const s = document.createElement('style');
  s.id = 'json-modal-css';
  s.textContent = `
  .json-modal{position:fixed;inset:0;z-index:70;background:rgba(20,18,17,.55);display:flex;align-items:center;justify-content:center;padding:2rem}
  .json-box{background:var(--card,#faf7f7);border:1px solid var(--line,#d8d3d3);box-shadow:0 20px 60px -20px rgba(0,0,0,.45);width:min(56rem,95vw);max-height:86vh;display:flex;flex-direction:column}
  .json-box header{display:flex;align-items:center;gap:.8rem;padding:.85rem 1.1rem;border-bottom:2px solid var(--ink,#201e1d)}
  .json-box h3{margin:0;font-size:1rem;font-weight:800;letter-spacing:-.01em;color:var(--ink,#201e1d)}
  .json-meta{font-size:.76rem;color:var(--muted,#7d7979)}
  .json-close{margin-left:auto;background:none;border:1px solid var(--line-control,#cfcaca);width:1.9rem;height:1.9rem;cursor:pointer;font-size:.85rem;line-height:1;color:var(--ink,#201e1d);border-radius:0}
  .json-close:hover{border-color:var(--blue,#ec3013);color:var(--blue,#ec3013)}
  .json-box pre{margin:0;padding:1rem 1.1rem;overflow:auto;font:.8rem/1.5 ui-monospace,Consolas,monospace;color:var(--ink,#201e1d);white-space:pre;tab-size:2}`;
  document.head.append(s);
}

// Carga los documentos de un maestro como filas {id, parentId, payload}
export async function loadRows(type) {
  const data = await api.docs(type);
  return (data.items || []).map(d => {
    let payload = {}; try { payload = JSON.parse(d.payload); } catch {}
    if (Array.isArray(payload)) payload = payload[0] ?? {};
    return { id: d.externalId, parentId: d.parentId, payload };
  });
}
