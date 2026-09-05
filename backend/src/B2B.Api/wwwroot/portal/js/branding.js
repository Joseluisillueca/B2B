// Marca configurable del despliegue (multi-cliente): nombre, color de acento, logo y
// TOKENS de diseño (fase 2). Fuente: GET /api/portal/branding (público). Se cachea en
// sessionStorage para que las visitas siguientes pinten la marca correcta al instante
// (sin parpadeo) y se refresca en segundo plano. Si la red falla, quedan los valores
// por defecto.
//
// Lo comparten el PORTAL y el back-office de GESTIÓN (que ya reutiliza /portal/app.css):
//   await initBranding(name => `${name}™ — B2B`);        // portal
//   await initBranding(name => `${name} · Gestión`);     // manage
//
// El color sobrescribe las variables CSS --blue / --blue-deep / --blue-soft /
// --blue-text en document.documentElement, de modo que TODO el acento (botones, focos,
// header activo…) cambia sin tocar app.css:
//   --blue-deep = color oscurecido un 10 % (cada canal RGB × 0.90)
//   --blue-soft = tinte muy suave: 14 % del color sobre blanco (canal = 255 − (255−c)·0.14)
//   --blue-text = variante legible para TEXTO PEQUEÑO (pestaña activa de la cinta,
//                 recuentos, lookups): el color movido hacia el contrario del papel
//                 hasta llegar a 4,5:1 (AA) — ver readableOnPaper()
//
// ── Tokens de diseño (`tokens`, fase 2 del theming) ───────────────────────────
// La lista es CERRADA (la misma que valida el servidor): lo que no case se ignora en
// silencio. TODOS son opcionales y su ausencia significa "el valor por defecto de
// app.css": SIN tokens el portal queda exactamente como el de MITO PROJECTS. Cada
// token presente se vuelca como variable CSS en <html> (style inline, que gana a
// :root) y se RETIRA en cuanto deja de venir, porque la marca se refresca en vivo
// desde Gestión y no puede quedarse pegada:
//
//   paper/surface/ink → --brand-paper/--brand-surface/--brand-ink  (app.css los lee
//                       como var(--brand-*, <valor actual>) desde :root)
//   headerBg/headerInk → --header-bg/--header-ink   (variables que ya existían)
//                       + --header-veil, el color de los velos/filetes/focos de la
//                       cabecera (blanco sobre la cabecera negra por defecto)
//   radius            → --r y --r-sm  (el radio general que ya usa todo app.css)
//   radiusButton      → --r-btn       (radio propio de los botones)
//   tracking          → --brand-tracking      caps → --brand-caps (uppercase)
//   fontFamily        → --brand-font          heroFilter → --hero-filter
//   fontUrl           → <link>/@font-face inyectado en <head> (id "brand-font")
//   faviconUrl        → href del <link rel=icon>
//   logoUrlDark       → logo alternativo para fondos oscuros (ver brandMark)
//   tagline/supportEmail → textos del login (ver brandTagline/brandSupport)
//   legal             → texto legal del login (ver brandLegal; hasta 400 caracteres)
//   card              → --brand-card   (fondo de paneles y de la banda de pestañas)
//   rule/ruleWidth    → --brand-rule/--brand-rule-w  (color y grosor de los filetes de
//                       CAPÍTULO: los 2px de tinta que abren cada bloque, no los hilos)
//   accent            → --accent/--accent-deep/--accent-soft  (segundo acento: favoritos,
//                       barras de los cuadros de mando, avisos; se deriva como el color
//                       de marca, ver applyTokenVars)
//   displayWeight     → --brand-display-weight  (peso de los TITULARES: "100".."900"; sin
//                       token app.css conserva sus 500/800 de siempre)
//   heroStyle         → <html data-hero-style="paper">: no es una variable sino un atributo
//                       del que cuelga el bloque «hero sobre papel» de app.css (portada,
//                       banda de ventanas y hero del lookbook con el titular en tinta bajo
//                       la foto en vez de blanco sobre velo). Único valor admitido: "paper".
//   accentSoft        → --accent-soft y, además, --blue-soft/--blue-soft-text (el tinte del
//                       COLOR DE MARCA): el fondo de avisos, chips y píldoras. Sin él se deriva
//                       como siempre (14 % del acento sobre blanco). Una marca «sin tintes» pone
//                       aquí su papel y el texto encima se queda en su rojo único
//   displayStretch    → --brand-display-stretch (anchura de los TITULARES, "50%".."200%";
//                       exige una webfont con eje wdth; sin token, 100 % = lo de hoy)
//   ctaCaps           → --brand-cta-caps/--brand-cta-tracking (botones secundarios y enlaces
//                       de acción en mayúscula con el tracking de .btn-primary)

const KEY = 'b2b_branding';
const DEFAULTS = { name: 'MITO PROJECTS', color: '#ec3013', logoUrl: null };

let brand = { ...DEFAULTS, tokens: {} };
let makeTitle = null;       // name => título del documento (difiere portal/gestión)
let defaultTitle = '';      // título original del index.html (marca por defecto)
let defaultIcon = null;     // <link rel=icon> original ({ href, type }), para restaurarlo

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

/** ¿El color es CLARO? (luma perceptual > 60 %). Decide si sobre él va tinta oscura. */
const isLight = hex => {
  const c = hexToRgb(hex);
  return !!c && (c[0] * 299 + c[1] * 587 + c[2] * 114) / 1000 > 152;
};

/** Luminancia relativa WCAG 2.x (0 negro … 1 blanco). */
const luminance = hex => {
  const c = hexToRgb(hex);
  if (!c) return null;
  const [r, g, b] = c.map(v => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};

/** Ratio de contraste WCAG entre dos hex: 1 = idénticos, 21 = negro sobre blanco. */
const contrast = (a, b) => {
  const la = luminance(a), lb = luminance(b);
  if (la === null || lb === null) return 21;   // color ilegible: no bloquea nada
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
};

// Papel del portal cuando la instancia no pone token `paper` (--paper de app.css).
const PAPER = '#f3f2f2';

/** Variante de un color para TEXTO PEQUEÑO sobre un fondo (por defecto el papel): se mueve
    en la dirección contraria al fondo —oscurecer si es claro, aclarar si es oscuro— SOLO
    hasta AA (4,5:1), y si el color ya cumple se devuelve tal cual. Antes arrancaba con un
    oscurecido fijo del 22 % «como el #b8230c de MITO», y eso partía el rojo de una marca en
    dos: el #e70917 de BLOCCO da 4,7:1 sobre papel blanco y no necesitaba tocarse, pero
    salía #b40712 bajo su propio subrayado #e70917. Un paso del 12 % es lo bastante fino
    para no pasarse; el bucle hace falta porque un amarillo #ffd400 necesita varios.
    `bg` permite medir contra el TINTE del propio color (--accent-soft / --blue-soft) para
    los textos que van dentro de cajas teñidas, donde el papel no es el fondo real. */
const readableOnPaper = (color, bg = brand.tokens.paper || PAPER) => {
  const down = isLight(bg);
  let out = color;
  for (let step = 0; step < 16 && contrast(out, bg) < 4.5; step++) {
    out = down ? darken(out, 0.12) : tint(out, 0.88);
  }
  return out;
};

// ── Validación de tokens (espejo EXACTO de la del servidor) ───────────────────
// Cada regla de aquí es la misma que aplica IntegrationEndpoints.NormalizeBrandTokens:
// ni más laja (un token guardado con 200 no puede caerse aquí en silencio) ni más
// estricta (lo que aquí se descarta el servidor lo devuelve 400 con su mensaje).
const CSS_LENGTH = /^-?(\d+(\.\d+)?|\.\d+)(px|rem|em|%)$/;   // "..px"/"1.2.3px" fuera

const asColor = value => (hexToRgb(value) ? String(value).trim().toLowerCase() : null);
const asLength = value => {
  const text = String(value).trim();
  return text.length <= 20 && CSS_LENGTH.test(text) ? text : null;
};
const asBool = value => (value === true || value === 'true' ? true : null);
/** Titular del acceso: texto libre, pero sin HTML (el servidor lo rechaza igual). */
const asTagline = value => {
  const text = String(value).trim();
  return text && text.length <= 120 && !/[<>]/.test(text) ? text : null;
};
const asEmail = value => {
  const text = String(value).trim();
  return text.length <= 120 && /^[^\s@<>"']+@[^\s@<>"']+\.[^\s@<>"']+$/.test(text) ? text : null;
};
/** Texto legal del login: como el tagline (sin HTML) pero con sitio para dos frases. */
const asLegal = value => {
  const text = String(value).trim();
  return text && text.length <= 400 && !/[<>]/.test(text) ? text : null;
};
/** Estilo del hero: lista CERRADA de recetas de app.css (hoy solo "paper"); cualquier otra
    cosa se descarta, porque acaba en un atributo del <html> que selecciona CSS. */
const asHeroStyle = value => (String(value).trim().toLowerCase() === 'paper' ? 'paper' : null);
/** Peso de los titulares: una centena de 100 a 900 (lo que entiende font-weight). */
const asWeight = value => {
  const text = String(value).trim();
  return /^[1-9]00$/.test(text) ? text : null;
};
/** Anchura de los titulares: porcentaje de font-stretch. CSS solo admite de 50 % a 200 %; fuera
    de rango la variable es inválida y el titular volvería a la anchura normal sin aviso. */
const asStretch = value => {
  const text = String(value).trim();
  if (!/^\d{1,3}(\.\d{1,2})?%$/.test(text)) return null;
  const pct = parseFloat(text);
  return pct >= 50 && pct <= 200 ? text : null;
};
/** URL de recurso (logo, favicon, fuente): ni esquemas ejecutables ni nada que pueda
    cerrar el atributo, el url() del @font-face o la propia declaración (espacios,
    controles, comillas, paréntesis, `\`, `;`, `{}` — la misma lista del servidor). */
const asUrl = value => {
  const text = String(value).trim();
  if (!text || text.length > 500) return null;
  // Rango de control = el char.IsControl del servidor (\s cubre el resto de blancos).
  // eslint-disable-next-line no-control-regex
  if (/[\s"'()<>\\;{}\u0000-\u001f\u007f-\u009f]/.test(text)) return null;
  return /^(javascript|data|vbscript):/i.test(text) ? null : text;
};
/** Familia tipográfica: se emite entre comillas, así que se RECHAZA —no se mutila, que
    era lo de antes: `Gill Sans "MT"` se convertía en silencio en otra familia— todo lo
    que pueda romperlas o salirse de la declaración. */
const asFamily = value => {
  const text = String(value).trim();
  return text && text.length <= 60 && !/["'\\<>{};]/.test(text) ? text : null;
};
/** Valor de `filter`: acaba dentro de una declaración CSS, no puede cerrarla. El `\` y
    los comentarios van a la lista negra porque `\75rl(` es `url(` para el parser CSS y
    se colaba por el filtro de abajo. */
const asFilter = value => {
  const text = String(value).trim();
  if (!text || text.length > 120) return null;
  if (/[;{}<\\]/.test(text) || text.includes('/*') || text.includes('*/')) return null;
  return /url\s*\(/i.test(text.replace(/\s/g, '')) ? null : text;
};

const TOKEN_SPEC = {
  logoUrlDark: asUrl, faviconUrl: asUrl, fontUrl: asUrl,
  fontFamily: asFamily, caps: asBool,
  tracking: asLength, radius: asLength, radiusButton: asLength, ruleWidth: asLength,
  paper: asColor, surface: asColor, ink: asColor, headerBg: asColor, headerInk: asColor,
  card: asColor, rule: asColor, accent: asColor,
  heroFilter: asFilter, tagline: asTagline, supportEmail: asEmail,
  // Ronda 1 de crítica de BLOCCO 5: van al final para no alterar el orden (y por tanto el
  // JSON normalizado) de las instancias que no los usan.
  heroStyle: asHeroStyle, displayWeight: asWeight, legal: asLegal,
  // Ronda 2 de crítica: mismo criterio (al final, y todos opcionales).
  accentSoft: asColor, displayStretch: asStretch, ctaCaps: asBool
};

/** Deja solo los tokens conocidos y válidos, SIEMPRE en el mismo orden (así la
    comparación por JSON del refresco no se dispara por un cambio de orden de claves). */
const normalizeTokens = raw => {
  const out = {};
  if (!raw || typeof raw !== 'object') return out;
  for (const [key, check] of Object.entries(TOKEN_SPEC)) {
    if (raw[key] === undefined || raw[key] === null) continue;
    const value = check(raw[key]);
    if (value !== null) out[key] = value;
  }
  return out;
};

// ── Estado de marca ───────────────────────────────────────────────────────────
const normalize = data => ({
  name: String(data?.name || '').trim() || DEFAULTS.name,
  color: hexToRgb(data?.color) ? String(data.color).trim().toLowerCase() : DEFAULTS.color,
  logoUrl: String(data?.logoUrl || '').trim() || null,
  tokens: normalizeTokens(data?.tokens)
});

/** Marca vigente ({ name, color, logoUrl, tokens }), siempre normalizada. */
export const getBrand = () => brand;

/** Tokens de diseño vigentes (objeto vacío si la instancia no configura ninguno). */
export const getTokens = () => brand.tokens;

// El <span class="brand"> vive sobre DOS superficies distintas, y cuál de ellas es
// oscura se decide por separado: el chrome (cabecera y pie) va con --header-bg, negro
// salvo que el token headerBg diga otra cosa; los heroes de acceso (/login y
// credenciales) van con el COLOR DE MARCA. Una instancia puede tener la cabecera clara
// y el color oscuro —el caso ALMA: headerBg #ffffff con marca negra— o al revés, así
// que el logo alternativo (logoUrlDark) NO puede elegirse una sola vez para todos.
const onDarkChrome = () => !(brand.tokens.headerBg && isLight(brand.tokens.headerBg));
// Sobre papel (heroStyle=paper) el cartel del acceso ya no es un bloque del color de marca,
// así que va el logotipo de tinta aunque el color de marca sea oscuro.
const onDarkBrand = () => document.documentElement.dataset.heroStyle !== 'paper' && !isLight(brand.color);

/** Contenido HTML del elemento .brand: logo si lo hay; si no, nombre + ™.
    `onDark` = ¿la superficie que lo recibe es oscura? Por defecto, la del chrome (lo
    que esperan los llamantes sin argumento: chrome.js, activate.js y GESTIÓN). */
export const brandMark = (onDark = onDarkChrome()) => {
  const logo = (onDark && brand.tokens.logoUrlDark) || brand.logoUrl || null;
  return logo
    ? `<img src="${esc(logo)}" alt="${esc(brand.name)}">`
    : `${esc(brand.name)}<sup>™</sup>`;
};

/** brandMark para las superficies pintadas con el COLOR DE MARCA: los heroes de
    /login y de credenciales (.login-hero / .cred-hero, background:var(--blue)). */
export const brandMarkOnBrand = () => brandMark(onDarkBrand());

/** Sustituye "MITO PROJECTS"/"Mito Projects" en textos (footer, legal) por la marca.
    Con la marca por defecto no toca nada (respeta las mayúsculas del traductor). */
export function brandText(text) {
  const s = String(text ?? '');
  if (brand.name === DEFAULTS.name) return s;
  return s.replace(/MITO PROJECTS/g, brand.name).replace(/Mito Projects/g, brand.name);
}

/** Titular del login: el token `tagline` manda sobre el texto traducido.
    Devuelve HTML ESCAPADO, como brandMark: el llamante lo inserta tal cual (nada de
    esc() encima, o se vería el &#39; de "n'est"). `tagline` es el único token de texto
    libre, así que escapar en origen es lo que evita el XSS almacenado del próximo que
    lo pinte sin acordarse. */
export const brandTagline = fallback => esc(brand.tokens.tagline || String(fallback ?? ''));

/** Cambia el email de soporte que va DENTRO de un texto traducido por el del token.
    Devuelve HTML ESCAPADO, igual que brandTagline. */
export function brandSupport(text, textWithoutEmail) {
  const s = String(text ?? '');
  const email = brand.tokens.supportEmail;
  // Sin buzón configurado en la marca, el texto NEUTRO: los textos traducidos llevan el
  // buzón de la marca por defecto del producto y una instancia con su propia marca no
  // puede enseñar el email de otra empresa en su pantalla de acceso.
  if (!email) return esc(textWithoutEmail != null ? String(textWithoutEmail) : s.replace(/\s*[^\s<>()]+@[^\s<>()]+\.[^\s<>().,;:]+/g, ''));
  // Reemplazo por función: un `$&` dentro del token no puede reinyectar la coincidencia.
  return esc(s.replace(/[^\s<>()]+@[^\s<>()]+\.[^\s<>().,;:]+/g, () => email));
}

/** Buzón de soporte de la instancia, como VALOR crudo (no HTML): lo necesitan a la vez
    el href de un mailto: y su texto. El llamante lo escapa según dónde lo ponga. */
export const brandSupportEmail = fallback => brand.tokens.supportEmail || String(fallback ?? '');

/** Texto legal del login: el token `legal` manda sobre el texto traducido (que es el de un
    distribuidor multimarca y no vale para un fabricante). HTML ESCAPADO, como brandTagline:
    el llamante lo inserta tal cual. */
export const brandLegal = fallback => esc(brand.tokens.legal || String(fallback ?? ''));

// ── Aplicación al documento ───────────────────────────────────────────────────
// Todas las variables que ESTE módulo gobierna. Se retiran en bloque antes de volver
// a fijar las presentes: así, cuando Gestión quita un token, el portal vuelve al
// valor por defecto de app.css en vez de quedarse con el anterior pegado.
const MANAGED_VARS = [
  '--brand-paper', '--brand-surface', '--brand-ink',
  '--header-bg', '--header-ink', '--header-veil',
  '--r', '--r-sm', '--r-btn', '--hero-filter',
  '--brand-caps', '--brand-tracking', '--brand-font',
  '--brand-card', '--brand-rule', '--brand-rule-w',
  // Los TRES del segundo acento: si faltara uno, al vaciar el token se quedaría pegado.
  '--accent', '--accent-deep', '--accent-soft',
  '--brand-display-weight',
  // Ronda 2. --blue-soft/--blue-soft-text NO van aquí a propósito: los fija (o borra) apply()
  // justo antes de llamar a applyTokenVars según el color de marca, y retirarlos en bloque
  // aquí dejaría a MITO sin su tinte. Al quitar accentSoft, apply() ya los restaura.
  '--brand-display-stretch', '--brand-cta-caps', '--brand-cta-tracking'
];

function applyTokenVars(style) {
  const tokens = brand.tokens;
  for (const name of MANAGED_VARS) style.removeProperty(name);

  if (tokens.paper) style.setProperty('--brand-paper', tokens.paper);
  if (tokens.surface) style.setProperty('--brand-surface', tokens.surface);
  if (tokens.ink) style.setProperty('--brand-ink', tokens.ink);
  if (tokens.headerBg) {
    style.setProperty('--header-bg', tokens.headerBg);
    // headerBg y headerInk son INDEPENDIENTES en el contrato: una instancia puede pedir
    // solo la cabecera blanca. Sin esto se quedaría con el --header-ink #f3f2f2 de
    // app.css (casi blanco sobre blanco) y con el velo blanco, o sea separadores,
    // hovers y anillos de foco invisibles. La tinta explícita, si viene, manda.
    if (!tokens.headerInk && isLight(tokens.headerBg)) {
      style.setProperty('--header-ink', '#201e1d');    // = --ink por defecto de app.css
      style.setProperty('--header-veil', '#201e1d');
    }
  }
  if (tokens.headerInk) {
    style.setProperty('--header-ink', tokens.headerInk);
    // Velos, filetes y anillos de foco de la cabecera: van en el color de su tinta
    // (el blanco por defecto sería invisible sobre una cabecera clara).
    style.setProperty('--header-veil', tokens.headerInk);
  }
  if (tokens.radius) {
    style.setProperty('--r', tokens.radius);
    style.setProperty('--r-sm', tokens.radius);
  }
  if (tokens.radiusButton) style.setProperty('--r-btn', tokens.radiusButton);
  if (tokens.heroFilter) style.setProperty('--hero-filter', tokens.heroFilter);
  if (tokens.tracking) style.setProperty('--brand-tracking', tokens.tracking);
  if (tokens.caps) style.setProperty('--brand-caps', 'uppercase');
  if (tokens.fontFamily) style.setProperty('--brand-font', `"${tokens.fontFamily}"`);
  if (tokens.card) style.setProperty('--brand-card', tokens.card);
  if (tokens.rule) style.setProperty('--brand-rule', tokens.rule);
  if (tokens.ruleWidth) style.setProperty('--brand-rule-w', tokens.ruleWidth);
  // Segundo acento de la instancia. Se deriva igual que el color de marca y por la misma
  // razón: --accent-deep se usa como TEXTO (cifra del KPI de deuda, kicker del lookbook,
  // chips, avisos), así que pasa por readableOnPaper() y no por un oscurecido fijo. Se mide
  // contra SU PROPIO TINTE y no contra el papel porque casi siempre va dentro de una caja
  // --accent-soft (aviso de error, chip, KPI de deuda): el #e70917 de BLOCCO cumple sobre
  // papel blanco (4,7:1) pero sobre su tinte #fcdddf se queda en 3,7:1. Lo que cumple sobre
  // el tinte cumple sobre el papel, que es más claro todavía.
  // --accent se deja crudo porque es FONDO con tinta blanca encima, y --accent-soft usa el
  // mismo 0.14 que --blue-soft: con accent = color de marca los dos coinciden EXACTAMENTE,
  // que es lo que significa «un solo acento».
  // accentSoft (ronda 2) sustituye al tinte derivado. Una marca «de un solo rojo, sin tintes»
  // pone aquí su PAPEL: como --accent-deep se mide contra ese fondo, el #e70917 de BLOCCO
  // sobre blanco (4,7:1) se queda tal cual en vez de oscurecerse a un segundo rojo (#cb0814).
  // Con la placa #f0efed daría 4,1:1 y volvería a oscurecer: por eso el valor es el papel.
  const soft = tokens.accentSoft || (tokens.accent ? tint(tokens.accent, 0.14) : null);
  if (tokens.accent) {
    style.setProperty('--accent', tokens.accent);
    style.setProperty('--accent-deep', readableOnPaper(tokens.accent, soft));
  }
  if (soft) style.setProperty('--accent-soft', soft);
  if (tokens.accentSoft) {
    // El mismo fondo para el tinte del COLOR DE MARCA (píldora de unidades, chips de estado,
    // cuadrados de Gestión): apply() acaba de fijar --blue-soft al 14 % y aquí se pisa, y su
    // texto se vuelve a medir contra el fondo real. Al quitar el token, apply() lo restaura.
    style.setProperty('--blue-soft', tokens.accentSoft);
    style.setProperty('--blue-soft-text', readableOnPaper(brand.color, tokens.accentSoft));
  }
  if (tokens.displayWeight) style.setProperty('--brand-display-weight', tokens.displayWeight);
  if (tokens.displayStretch) style.setProperty('--brand-display-stretch', tokens.displayStretch);
  // Una sola voz de botón: el tracking es el que ya lleva .btn-primary, es decir, el token
  // `tracking` si la instancia lo configura y si no sus .045em de siempre.
  if (tokens.ctaCaps) {
    style.setProperty('--brand-cta-caps', 'uppercase');
    style.setProperty('--brand-cta-tracking', tokens.tracking || '.045em');
  }
  // heroStyle no es una variable: es un atributo del <html> del que cuelga un bloque entero
  // de app.css. Se borra si el token deja de venir, por la misma razón que MANAGED_VARS.
  if (tokens.heroStyle) document.documentElement.dataset.heroStyle = tokens.heroStyle;
  else delete document.documentElement.dataset.heroStyle;
}

/** Webfont de la instancia. UN solo elemento (id "brand-font"), reutilizado entre
    refrescos: un .woff2 se declara con @font-face; cualquier otra URL (Google Fonts…)
    se enlaza como hoja de estilo. */
function applyFont() {
  const { fontUrl, fontFamily } = brand.tokens;
  const head = document.head;
  let node = document.getElementById('brand-font');
  const isFile = fontUrl && /\.woff2?([?#].*)?$/i.test(fontUrl);
  const wanted = !fontUrl ? null : isFile ? (fontFamily ? 'STYLE' : null) : 'LINK';

  if (!wanted) { node?.remove(); return; }
  if (node && node.tagName !== wanted) { node.remove(); node = null; }
  if (!node) {
    node = document.createElement(wanted === 'STYLE' ? 'style' : 'link');
    node.id = 'brand-font';
    if (wanted === 'LINK') node.rel = 'stylesheet';
    head.appendChild(node);
  }

  if (wanted === 'STYLE') {
    const css = `@font-face{font-family:"${fontFamily}";`
      + `src:url("${fontUrl}") format("woff2");font-display:swap;}`;
    if (node.textContent !== css) node.textContent = css;
  } else if (node.getAttribute('href') !== fontUrl) {
    node.setAttribute('href', fontUrl);
  }
}

/** Favicon de la instancia; sin token vuelve el del index.html. */
function applyFavicon() {
  const link = document.querySelector('link[rel~="icon"]');
  if (!link) return;
  if (!defaultIcon) defaultIcon = { href: link.getAttribute('href'), type: link.getAttribute('type') };
  const url = brand.tokens.faviconUrl;
  const href = url || defaultIcon.href;
  // El type del index.html es image/svg+xml: solo vale si el favicon nuevo también lo es.
  const type = url ? (/\.svg([?#].*)?$/i.test(url) ? 'image/svg+xml' : null) : defaultIcon.type;
  if (href && link.getAttribute('href') !== href) link.setAttribute('href', href);
  if (type) link.setAttribute('type', type); else link.removeAttribute('type');
}

function apply() {
  // Título: con la marca por defecto se conserva el del index.html tal cual.
  if (makeTitle) document.title = brand.name === DEFAULTS.name && defaultTitle
    ? defaultTitle : makeTitle(brand.name);

  const rootStyle = document.documentElement.style;
  if (brand.color !== DEFAULTS.color) {
    rootStyle.setProperty('--blue', brand.color);
    rootStyle.setProperty('--blue-deep', darken(brand.color, 0.10));
    rootStyle.setProperty('--blue-soft', tint(brand.color, 0.14));
    // Sin esta, el texto pequeño de acento (pestaña activa de la cinta y su recuento,
    // lookups) se quedaba clavado en el #b8230c de MITO sobre un portal de otro color.
    rootStyle.setProperty('--blue-text', readableOnPaper(brand.color));
    // Y el texto de marca DENTRO de su propio tinte (píldora de unidades, tag de panel):
    // medido contra --blue-soft, que es su fondo real. Con marca negra sigue dando negro.
    rootStyle.setProperty('--blue-soft-text', readableOnPaper(brand.color, tint(brand.color, 0.14)));
  } else {
    rootStyle.removeProperty('--blue');
    rootStyle.removeProperty('--blue-deep');
    rootStyle.removeProperty('--blue-soft');
    rootStyle.removeProperty('--blue-text');
    rootStyle.removeProperty('--blue-soft-text');
  }

  applyTokenVars(rootStyle);
  applyFont();
  applyFavicon();

  // Marcas ya pintadas (el refresco en segundo plano puede llegar tras el render).
  // El logo se resuelve POR SUPERFICIE: los heroes de acceso van con el color de marca,
  // todo lo demás (chrome del portal, cabecera de GESTIÓN) con --header-bg.
  for (const el of document.querySelectorAll('.brand')) {
    el.innerHTML = brandMark(el.closest('.login-hero, .cred-hero') ? onDarkBrand() : onDarkChrome());
    if (el.hasAttribute('aria-label')) el.setAttribute('aria-label', brand.name);
  }
  // Textos gobernados por tokens que ya se pintaron: mismo motivo que el bucle de
  // arriba (el refresco en segundo plano llega DESPUÉS del render y hasta ahora dejaba
  // el titular y el email del acceso con el valor cacheado hasta un F5). El texto
  // traducido original viaja en data-fallback para poder recomponerlos sin la vista.
  for (const el of document.querySelectorAll('[data-brand-tagline]')) {
    el.innerHTML = brandTagline(el.dataset.fallback);
  }
  for (const el of document.querySelectorAll('[data-brand-support]')) {
    el.innerHTML = brandSupport(el.dataset.fallback, el.dataset.fallbackNoemail);
  }
  for (const el of document.querySelectorAll('[data-brand-legal]')) {
    el.innerHTML = brandLegal(el.dataset.fallback);
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
