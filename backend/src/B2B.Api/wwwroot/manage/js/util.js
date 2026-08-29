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

// Carga los documentos de un maestro como filas {id, parentId, payload}
export async function loadRows(type) {
  const data = await api.docs(type);
  return (data.items || []).map(d => {
    let payload = {}; try { payload = JSON.parse(d.payload); } catch {}
    if (Array.isArray(payload)) payload = payload[0] ?? {};
    return { id: d.externalId, parentId: d.parentId, payload };
  });
}
