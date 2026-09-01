// Marca configurable del despliegue (multi-cliente): nombre, color de acento y logo.
// Fuente: GET /api/portal/branding (público). Se cachea en sessionStorage para que
// las visitas siguientes pinten la marca correcta al instante (sin parpadeo) y se
// refresca en segundo plano. Si la red falla, quedan los valores por defecto.
//
// Lo comparten el PORTAL y el back-office de GESTIÓN (que ya reutiliza /portal/app.css):
//   await initBranding(name => `${name}™ — B2B`);        // portal
//   await initBranding(name => `${name} · Gestión`);     // manage
//
// El color sobrescribe las variables CSS --blue / --blue-deep / --blue-soft en
// document.documentElement, de modo que TODO el acento (botones, focos, header
// activo…) cambia sin tocar app.css:
//   --blue-deep = color oscurecido un 10 % (cada canal RGB × 0.90)
//   --blue-soft = tinte muy suave: 14 % del color sobre blanco (canal = 255 − (255−c)·0.14)

const KEY = 'b2b_branding';
const DEFAULTS = { name: 'MITO PROJECTS', color: '#ec3013', logoUrl: null };

let brand = { ...DEFAULTS };
let makeTitle = null;       // name => título del documento (difiere portal/gestión)
let defaultTitle = '';      // título original del index.html (marca por defecto)

const esc = value => String(value ?? '').replace(/[&<>"']/g,
  c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// ── Matemática de color (hex #rrggbb) ─────────────────────────────────────────
const hexToRgb = hex => {
  const m = /^#([0-9a-f]{6})$/i.exec(String(hex || '').trim());
  if (!m) return null;
  const n = parseInt(m[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
};
const rgbToHex = rgb =>
  '#' + rgb.map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('');

/** Oscurece un hex multiplicando cada canal por (1 − f). f=0.10 → ~10 % más oscuro. */
export const darken = (hex, f = 0.10) => {
  const c = hexToRgb(hex);
  return c ? rgbToHex(c.map(v => v * (1 - f))) : hex;
};

/** Tinte muy suave: mezcla el color con blanco (f = fracción de color que sobrevive). */
export const tint = (hex, f = 0.14) => {
  const c = hexToRgb(hex);
  return c ? rgbToHex(c.map(v => 255 - (255 - v) * f)) : hex;
};

// ── Estado de marca ───────────────────────────────────────────────────────────
const normalize = data => ({
  name: String(data?.name || '').trim() || DEFAULTS.name,
  color: hexToRgb(data?.color) ? String(data.color).trim().toLowerCase() : DEFAULTS.color,
  logoUrl: String(data?.logoUrl || '').trim() || null
});

/** Marca vigente ({ name, color, logoUrl }), siempre normalizada. */
export const getBrand = () => brand;

/** Contenido HTML del elemento .brand: logo si lo hay; si no, nombre + ™. */
export const brandMark = () => brand.logoUrl
  ? `<img src="${esc(brand.logoUrl)}" alt="${esc(brand.name)}">`
  : `${esc(brand.name)}<sup>™</sup>`;

/** Sustituye "MITO PROJECTS"/"Mito Projects" en textos (footer, legal) por la marca.
    Con la marca por defecto no toca nada (respeta las mayúsculas del traductor). */
export function brandText(text) {
  const s = String(text ?? '');
  if (brand.name === DEFAULTS.name) return s;
  return s.replace(/MITO PROJECTS/g, brand.name).replace(/Mito Projects/g, brand.name);
}

// ── Aplicación al documento ───────────────────────────────────────────────────
function apply() {
  // Título: con la marca por defecto se conserva el del index.html tal cual.
  if (makeTitle) document.title = brand.name === DEFAULTS.name && defaultTitle
    ? defaultTitle : makeTitle(brand.name);

  const rootStyle = document.documentElement.style;
  if (brand.color !== DEFAULTS.color) {
    rootStyle.setProperty('--blue', brand.color);
    rootStyle.setProperty('--blue-deep', darken(brand.color, 0.10));
    rootStyle.setProperty('--blue-soft', tint(brand.color, 0.14));
  } else {
    rootStyle.removeProperty('--blue');
    rootStyle.removeProperty('--blue-deep');
    rootStyle.removeProperty('--blue-soft');
  }

  // Marcas ya pintadas (el refresco en segundo plano puede llegar tras el render)
  for (const el of document.querySelectorAll('.brand')) {
    el.innerHTML = brandMark();
    if (el.hasAttribute('aria-label')) el.setAttribute('aria-label', brand.name);
  }
}

async function refresh() {
  try {
    const res = await fetch('/api/portal/branding');
    if (!res.ok) return;
    const data = await res.json();
    try { sessionStorage.setItem(KEY, JSON.stringify(data)); } catch { /* modo privado */ }
    const next = normalize(data);
    if (JSON.stringify(next) !== JSON.stringify(brand)) { brand = next; apply(); }
  } catch { /* sin red: se queda la marca por defecto / cacheada */ }
}

/**
 * Arranque: aplica la marca cacheada al instante (sin flash) y refresca en segundo
 * plano; en la primera visita (sin caché) espera al fetch, que es local y rápido.
 */
export async function initBranding(titleFn) {
  makeTitle = titleFn;
  defaultTitle = document.title;
  let cached = null;
  try { cached = JSON.parse(sessionStorage.getItem(KEY) || 'null'); } catch { /* caché corrupta */ }
  if (cached) {
    brand = normalize(cached);
    apply();
    refresh();              // en segundo plano, por si cambió desde otra pestaña
  } else {
    await refresh();
    apply();                // aunque el fetch falle: fija título/variables por defecto
  }
}

/** Aplica en vivo una marca recién guardada (la usa Gestión tras el PUT). */
export function setBrand(data) {
  try { sessionStorage.setItem(KEY, JSON.stringify(data)); } catch { /* modo privado */ }
  brand = normalize(data);
  apply();
}
