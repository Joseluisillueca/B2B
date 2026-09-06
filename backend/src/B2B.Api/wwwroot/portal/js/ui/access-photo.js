// Foto del acceso: el bloque login.background del CMS (público: se pide sin sesión, ver
// PortalEndpoints). Se inserta SOLO si hay un elemento publicado con imagen, entre la marca y el
// titular del cartel; sin él las vistas quedan como estaban. Neutro: cómo se compone (o si se
// pinta) lo decide el CSS de cada marca (.access-photo es display:none en la base).
//
// Va por fetch y no por api.js a propósito: el bloque no necesita token, y el cliente HTTP trata
// cualquier 401 como fin de sesión (state.clear + vuelta al login), que aquí sería un repintado
// del login a mitad de escribir contra un backend que aún no publique el bloque.
//
// Sin salto de layout: el último elemento publicado se recuerda por idioma en localStorage y la
// caja se inserta de forma síncrona con él ANTES de preguntar al servidor; la respuesta la
// actualiza (src/alt) o la retira si el bloque ya no trae imagen. En la PRIMERA visita (sin
// memoria) se reserva la caja vacía —la placa gris del CSS de la marca— y se retira si el bloque
// no trae nada. En móvil la fila de la foto (36 vh) no existe hasta que hay caja: sin esto el
// campo Email bajaba 304 px con el foco puesto. Un fallo de red (no un 401) deja lo pintado y no
// toca la memoria; la caja reservada (sin firma) sí se retira, porque no hay nada que meterle.
import { lang } from '../i18n.js';
import { esc } from '../format.js';

const cache = new Map();
const STORE = 'access-photo.';

const remembered = key => { try { return JSON.parse(localStorage.getItem(STORE + key) || 'null'); } catch { return null; } };
const remember = (key, item) => {
  try {
    if (item) localStorage.setItem(STORE + key, JSON.stringify({ imageUrl: item.imageUrl, imageUrlMobile: item.imageUrlMobile || '', alt: item.alt || '' }));
    else localStorage.removeItem(STORE + key);
  } catch { /* sin almacenamiento: solo se pierde el precalentado */ }
};

// items publicados; null = no se pudo preguntar (red), distinto de [] = el bloque no trae nada.
function content() {
  const key = lang();
  if (!cache.has(key)) {
    cache.set(key, fetch(`/api/portal/content/login.background?locale=${encodeURIComponent(key)}`)
      .then(response => (response.ok ? response.json() : null))
      .then(body => (Array.isArray(body?.items) ? body.items : []))
      .catch(() => null));
  }
  return cache.get(key);
}

const signature = item => `${item.imageUrl}|${item.imageUrlMobile || ''}|${item.alt || ''}`;

function paint(hero, before, item) {
  let box = hero.querySelector('.access-photo');
  if (!item) { box?.remove(); return; }
  if (!box) {
    box = document.createElement('div');
    box.className = 'access-photo';
    hero.insertBefore(box, before || null);
  }
  if (box.dataset.signature === signature(item)) return;
  box.dataset.signature = signature(item);
  box.innerHTML = `<picture>${
    item.imageUrlMobile ? `<source media="(max-width:52rem)" srcset="${esc(item.imageUrlMobile)}">` : ''
  }<img src="${esc(item.imageUrl)}" alt="${esc(item.alt || '')}" decoding="async" fetchpriority="high"></picture>`;
}

// Primera visita: la caja vacía ya ocupa su fila (36 vh en móvil bajo paper). paint() la rellena
// después o la retira con item=null. Neutro: en la base .access-photo es display:none.
function reserve(hero, before) {
  if (hero.querySelector('.access-photo')) return;
  const box = document.createElement('div');
  box.className = 'access-photo';
  hero.insertBefore(box, before || null);
}

/** hero = aside.login-hero | aside.cred-hero; `before` = el titular ante el que va la foto. */
export async function paintAccessPhoto(hero, before) {
  if (!hero) return;
  const key = lang();
  const known = remembered(key);
  if (known?.imageUrl) paint(hero, before, known);
  else reserve(hero, before);
  const items = await content();
  if (!hero.isConnected) return;
  if (items === null) { hero.querySelector('.access-photo:not([data-signature])')?.remove(); return; }
  const item = items.find(i => i && i.imageUrl) || null;
  remember(key, item);
  paint(hero, before, item);
}
