// Arranque del portal. Enlaza el router con el chrome y con el cierre de sesión
// por 401, y resuelve la ruta con la que se ha entrado (recarga directa incluida).

import { start, onRender, go, resolve } from './router.js';
import { renderChrome } from './ui/chrome.js';
import { setUnauthorizedHandler, api } from './api.js';
import { state } from './state.js';
import { initBranding } from './branding.js';

setUnauthorizedHandler(() => go('/login', { replace: true }));
onRender(renderChrome);

// Marca del despliegue (nombre/color/logo) ANTES del primer render, para que el
// login ya salga con la marca correcta (endpoint público, cacheado en sesión).
await initBranding(name => `${name}™ — B2B`);

// Si hay token de una sesión previa pero la ficha se perdió, se recupera antes de pintar
if (state.token && !state.me) {
  try { state.me = await api.me(); }
  catch { state.clear(); }
}

await start();

/** Refresca la ficha de sesión y repinta (lo usan login y credentials) */
export async function reload() {
  state.me = await api.me();
  await resolve();
}
