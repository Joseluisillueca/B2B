// Arranque del back-office de gestión.
import { resolve } from './router.js';
import { setUnauthorizedHandler } from './api.js';
import { initBranding } from '/portal/js/branding.js';

setUnauthorizedHandler(() => { location.hash = '#/login'; });

// Marca del despliegue (nombre/color/logo) antes del primer render — mismo módulo
// que el portal (Gestión ya reutiliza /portal/app.css).
await initBranding(name => `${name} · Gestión`);
window.addEventListener('hashchange', resolve);
if (!location.hash) location.hash = '#/dashboard';
resolve();
