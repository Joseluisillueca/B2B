// Formato de moneda, fecha y número. La moneda del negocio es el euro y el
// separador decimal es la coma en los 4 idiomas del portal, pero el orden de la
// fecha cambia, así que se usa Intl con la locale de la ruta.

import { lang } from './i18n.js';

const LOCALES = { es: 'es-ES', en: 'en-GB', fr: 'fr-FR', it: 'it-IT' };
const locale = () => LOCALES[lang()] || 'es-ES';

export const eur = value => Number(value ?? 0).toLocaleString(locale(),
  { style: 'currency', currency: 'EUR', minimumFractionDigits: 2 });

export const num = value => Number(value ?? 0).toLocaleString(locale());

export const date = value => {
  if (!value) return '';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '' : parsed.toLocaleDateString(locale());
};

/** Escapa texto que se inyecta con innerHTML (todo lo que venga de la API) */
export const esc = value => String(value ?? '').replace(/[&<>"']/g,
  c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

/** Inicial para el avatar circular del header y de las credenciales */
export const initial = text => String(text || '?').trim().charAt(0).toUpperCase() || '?';
