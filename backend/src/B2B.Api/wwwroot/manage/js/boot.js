// Arranque del back-office de gestión.
import { resolve } from './router.js';
import { setUnauthorizedHandler } from './api.js';

setUnauthorizedHandler(() => { location.hash = '#/login'; });
window.addEventListener('hashchange', resolve);
if (!location.hash) location.hash = '#/dashboard';
resolve();
