// Estado de sesión del portal. Vive en sessionStorage (token y ficha) y
// localStorage (carrito y preferencias), nunca en variables globales sueltas:
// cualquier vista lo lee con las mismas funciones.

const TOKEN = 'b2b.portal.token';
const ME = 'b2b.portal.me';
const CREDENTIAL = 'b2b.portal.credential';
const CART = 'b2b.portal.cart';
// Preselección del Lookbook: modelos que el comprador aparta SIN cantidades; las
// tallas se ponen después. Vive junto al carrito (localStorage) por ventana.
const PRESEL = 'b2b.portal.preselection';
const PREFS = 'b2b.portal.prefs';
// Suplantación de agente: token + ficha del cliente sobre el que se "actúa como".
// Vive en sessionStorage para sobrevivir a recargas igual que la sesión.
const ACTING = 'b2b.portal.acting';

const read = (store, key, fallback) => {
  try { return JSON.parse(store.getItem(key)) ?? fallback; }
  catch { return fallback; }
};
const write = (store, key, value) => {
  if (value === null || value === undefined) store.removeItem(key);
  else store.setItem(key, JSON.stringify(value));
};

// Ámbito del carrito y de la preselección: USUARIO + CLIENTE (UX-A2). La clave era
// global al navegador: un agente que soltaba un cliente arrastraba su carrito al
// siguiente, y en un equipo compartido el carrito de un usuario le aparecía al otro.
// Suplantando se compra en el ámbito del cliente suplantado; al soltarlo el agente
// recupera el suyo y el del cliente queda esperando para la próxima vez.
const scopeKey = base => {
  const me = read(sessionStorage, ME, null);
  const acting = read(sessionStorage, ACTING, null);
  const credential = read(sessionStorage, CREDENTIAL, null);
  const user = me?.email || me?.id || 'anon';
  const client = acting?.client?.id || acting?.client?.clientId || credential?.clientId || '-';
  return `${base}:${String(user).toLowerCase()}:${String(client).toLowerCase()}`;
};
// Migración única de la clave global antigua: la adopta el primer ámbito que la lee
const readScoped = base => {
  const own = read(localStorage, scopeKey(base), null);
  if (own) return own;
  const legacy = read(localStorage, base, null);
  if (legacy && Object.keys(legacy).length) {
    write(localStorage, scopeKey(base), legacy);
    localStorage.removeItem(base);
    return legacy;
  }
  return {};
};

// El contador del header y el drawer se repintan solos cuando cambia el carrito o
// la ventana de servicio; nadie tiene que acordarse de avisar al chrome.
const listeners = new Set();
export const onCartChange = fn => { listeners.add(fn); return () => listeners.delete(fn); };
const emit = () => { for (const fn of [...listeners]) fn(); };

/** Clave de línea del carrito: un modelo puede repetir talla entre ventanas, no dentro */
export const lineKey = line => `${line.modelId}|${line.size}`;

export const state = {
  // El token EFECTIVO de las llamadas: si el agente está suplantando a un cliente
  // manda el token de suplantación; si no, el de la sesión (login). api.js lee
  // siempre `state.token`, así que basta con esto para que todo /api/portal/* opere
  // como el cliente mientras dure la suplantación, y vuelva al agente al soltarla.
  get token() {
    const acting = read(sessionStorage, ACTING, null);
    return (acting && acting.token) || sessionStorage.getItem(TOKEN) || '';
  },
  set token(value) {
    if (value) sessionStorage.setItem(TOKEN, value);
    else sessionStorage.removeItem(TOKEN);
  },

  /** Token propio de la sesión (agente/cliente), sin la capa de suplantación */
  get baseToken() { return sessionStorage.getItem(TOKEN) || ''; },

  /** Ficha completa de /api/portal/me */
  get me() { return read(sessionStorage, ME, null); },
  set me(value) { write(sessionStorage, ME, value); },

  /** Credencial elegida en la pantalla "SELECCIONA AHORA TUS CREDENCIALES" */
  get credential() { return read(sessionStorage, CREDENTIAL, null); },
  set credential(value) { write(sessionStorage, CREDENTIAL, value); },

  /** ¿La credencial elegida (o la ficha) es de agente? */
  get isAgent() {
    const cred = state.credential;
    return !!(cred?.agent || cred?.type === 'agent' || state.me?.isAgent);
  },

  /** { token, client } mientras el agente actúa como un cliente; null si no */
  get acting() { return read(sessionStorage, ACTING, null); },
  get actingClient() { return read(sessionStorage, ACTING, null)?.client || null; },

  /** Entra en "actuando como cliente": guarda su token y ficha, y fija la ventana */
  actAs({ token, client, window } = {}) {
    write(sessionStorage, ACTING, { token, client });
    if (window) write(localStorage, PREFS, { ...state.prefs, window });
    emit();
  },

  /** Suelta la suplantación: vuelve al token del agente */
  stopActing() {
    sessionStorage.removeItem(ACTING);
    emit();
  },

  /** Preferencias locales: ventana de servicio activa y toggle del ojo */
  get prefs() { return read(localStorage, PREFS, { window: 'replenishment', focus: false }); },
  set prefs(value) { write(localStorage, PREFS, value); emit(); },

  /** Carrito por ventana de servicio: { [windowKey]: { [lineKey]: linea } } */
  get cart() { return readScoped(CART); },
  set cart(value) { write(localStorage, scopeKey(CART), value); emit(); },

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

  // ── Preselección del Lookbook (modelos apartados sin tallas) ────────────────
  /** { [windowKey]: { [modelId]: item de catálogo } } */
  get preselection() { return readScoped(PRESEL); },
  set preselection(value) { write(localStorage, scopeKey(PRESEL), value); emit(); },

  preselections(windowKey = state.prefs.window) {
    return Object.values(state.preselection[windowKey] || {});
  },
  preselectionCount(windowKey = state.prefs.window) {
    return state.preselections(windowKey).length;
  },
  isPreselected(modelId, windowKey = state.prefs.window) {
    return !!(state.preselection[windowKey] || {})[modelId];
  },
  /** Aparta un modelo (guarda el item entero para poder tallar después sin recargar) */
  preselect(item, windowKey = state.prefs.window) {
    if (!item?.modelId) return;
    const all = state.preselection;
    const bucket = { ...(all[windowKey] || {}) };
    bucket[item.modelId] = item;
    state.preselection = { ...all, [windowKey]: bucket };
  },
  unpreselect(modelId, windowKey = state.prefs.window) {
    const all = state.preselection;
    const bucket = { ...(all[windowKey] || {}) };
    delete bucket[modelId];
    state.preselection = { ...all, [windowKey]: bucket };
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
    sessionStorage.removeItem(ACTING);
  }
};
