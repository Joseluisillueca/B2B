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

// El contador del header y el drawer se repintan solos cuando cambia el carrito o
// la ventana de servicio; nadie tiene que acordarse de avisar al chrome.
const listeners = new Set();
export const onCartChange = fn => { listeners.add(fn); return () => listeners.delete(fn); };
const emit = () => { for (const fn of [...listeners]) fn(); };

/** Clave de línea del carrito: un modelo puede repetir talla entre ventanas, no dentro */
export const lineKey = line => `${line.modelId}|${line.size}`;

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
  set prefs(value) { write(localStorage, PREFS, value); emit(); },

  /** Carrito por ventana de servicio: { [windowKey]: { [lineKey]: linea } } */
  get cart() { return read(localStorage, CART, {}); },
  set cart(value) { write(localStorage, CART, value); emit(); },

  /** Ventana activa: 'replenishment' | 'scheduled' — la que cuenta el botón azul */
  get window() { return state.prefs.window; },

  cartLines(windowKey = state.prefs.window) {
    return Object.values(state.cart[windowKey] || {});
  },

  cartUnits(windowKey = state.prefs.window) {
    return state.cartLines(windowKey).reduce((total, line) => total + (Number(line.qty) || 0), 0);
  },

  cartTotal(windowKey = state.prefs.window) {
    return state.cartLines(windowKey)
      .reduce((total, line) => total + (Number(line.qty) || 0) * (Number(line.price) || 0), 0);
  },

  /** Cantidad de una talla; 0 la quita del carrito */
  setCartLine(line, windowKey = state.prefs.window) {
    const cart = state.cart;
    const bucket = { ...(cart[windowKey] || {}) };
    const key = lineKey(line);
    if (Number(line.qty) > 0) bucket[key] = { ...line, qty: Number(line.qty) };
    else delete bucket[key];
    state.cart = { ...cart, [windowKey]: bucket };
  },

  removeCartLine(key, windowKey = state.prefs.window) {
    const cart = state.cart;
    const bucket = { ...(cart[windowKey] || {}) };
    delete bucket[key];
    state.cart = { ...cart, [windowKey]: bucket };
  },

  clearCart(windowKey = state.prefs.window) {
    state.cart = { ...state.cart, [windowKey]: {} };
  },

  /** Vuelca un carrito guardado sobre la ventana activa (sustituye, no acumula) */
  loadCart(lines, windowKey = state.prefs.window) {
    const bucket = {};
    for (const line of lines || []) {
      if (!(Number(line.qty) > 0)) continue;
      bucket[lineKey(line)] = { ...line, qty: Number(line.qty) };
    }
    state.cart = { ...state.cart, [windowKey]: bucket };
  },

  clear() {
    sessionStorage.removeItem(TOKEN);
    sessionStorage.removeItem(ME);
    sessionStorage.removeItem(CREDENTIAL);
  }
};
