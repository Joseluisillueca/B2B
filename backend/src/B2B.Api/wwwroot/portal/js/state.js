// Estado de sesión del portal. Vive en sessionStorage (token y ficha) y
// localStorage (carrito y preferencias), nunca en variables globales sueltas:
// cualquier vista lo lee con las mismas funciones.

const TOKEN = 'b2b.portal.token';
const ME = 'b2b.portal.me';
const CREDENTIAL = 'b2b.portal.credential';
const CART = 'b2b.portal.cart';
const PREFS = 'b2b.portal.prefs';

const read = (store, key, fallback) => {
  try { return JSON.parse(store.getItem(key)) ?? fallback; }
  catch { return fallback; }
};
const write = (store, key, value) => {
  if (value === null || value === undefined) store.removeItem(key);
  else store.setItem(key, JSON.stringify(value));
};

export const state = {
  get token() { return sessionStorage.getItem(TOKEN) || ''; },
  set token(value) {
    if (value) sessionStorage.setItem(TOKEN, value);
    else sessionStorage.removeItem(TOKEN);
  },

  /** Ficha completa de /api/portal/me */
  get me() { return read(sessionStorage, ME, null); },
  set me(value) { write(sessionStorage, ME, value); },

  /** Credencial elegida en la pantalla "SELECCIONA AHORA TUS CREDENCIALES" */
  get credential() { return read(sessionStorage, CREDENTIAL, null); },
  set credential(value) { write(sessionStorage, CREDENTIAL, value); },

  /** Preferencias locales: ventana de servicio activa y toggle del ojo */
  get prefs() { return read(localStorage, PREFS, { window: 'replenishment', focus: false }); },
  set prefs(value) { write(localStorage, PREFS, value); },

  /** Carrito por ventana de servicio: { [windowKey]: { [lineKey]: linea } } */
  get cart() { return read(localStorage, CART, {}); },
  set cart(value) { write(localStorage, CART, value); },

  cartUnits(windowKey = state.prefs.window) {
    return Object.values(state.cart[windowKey] || {})
      .reduce((total, line) => total + (Number(line.qty) || 0), 0);
  },

  clear() {
    sessionStorage.removeItem(TOKEN);
    sessionStorage.removeItem(ME);
    sessionStorage.removeItem(CREDENTIAL);
  }
};
