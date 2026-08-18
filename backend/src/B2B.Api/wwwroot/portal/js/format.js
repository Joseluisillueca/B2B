// Formato de moneda, fecha y número. La moneda del negocio es el euro y el
// separador decimal es la coma en los 4 idiomas del portal, pero el orden de la
// fecha cambia, así que se usa Intl con la locale de la ruta.

import { lang, t } from './i18n.js';

const LOCALES = { es: 'es-ES', en: 'en-GB', fr: 'fr-FR', it: 'it-IT' };
const locale = () => LOCALES[lang()] || 'es-ES';

export const eur = value => Number(value ?? 0).toLocaleString(locale(),
  { style: 'currency', currency: 'EUR', minimumFractionDigits: 2 });

export const num = value => Number(value ?? 0).toLocaleString(locale());

// Día y mes con dos cifras: en un listado de documentos las fechas tienen que
// cuadrar en columna (06/08/2026, no 6/8/2026)
export const date = value => {
  if (!value) return '';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '' : parsed.toLocaleDateString(locale(),
    { day: '2-digit', month: '2-digit', year: 'numeric' });
};

/** Escapa texto que se inyecta con innerHTML (todo lo que venga de la API) */
export const esc = value => String(value ?? '').replace(/[&<>"']/g,
  c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

/** Inicial para el avatar circular del header y de las credenciales */
export const initial = text => String(text || '?').trim().charAt(0).toUpperCase() || '?';

// El rol y el tipo de credencial los manda Business Central: unas veces como clave
// estable (roleKey: admin|integration|user, typeKey: client), otras como literal en
// español ("Administrador") y otras como código interno ("client-admin"). Se prueban
// en ese orden —clave, valor entero, último tramo del valor— y si nada casa con el
// diccionario se muestra el literal recibido, que es lo que se veía hasta ahora.
const keyed = (prefix, key, raw) => {
  const text = String(raw ?? '').trim();
  const tail = text.toLowerCase().split(/[-_.\s]+/).filter(Boolean).at(-1) || '';

  for (const candidate of [key, text.toLowerCase(), tail]) {
    if (!candidate) continue;
    const label = t(`${prefix}.${candidate}`);
    if (label !== `${prefix}.${candidate}`) return label;
  }
  return text || String(key ?? '');
};

/** Rol de la credencial/usuario: roles.admin · roles.integration · roles.user */
export const roleLabel = source =>
  keyed('roles', source?.roleKey, source?.role ?? source?.rol);

/** Tipo de credencial: credentialType.client */
export const typeLabel = source =>
  keyed('credentialType', source?.typeKey, source?.type);
