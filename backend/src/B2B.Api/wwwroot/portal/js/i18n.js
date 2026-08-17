// i18n del portal. El idioma es el segundo segmento de la ruta real
// (/es/es/dashboard), como en el portal actual. es es el principal; en, fr e it
// heredan de es cualquier clave que aún no esté traducida.

export const LANGS = ['es', 'en', 'fr', 'it'];
export const DEFAULT_LANG = 'es';

// El conector habla en culturas (es_ES, en_EN, fr_FR, it_IT); el portal en códigos de 2 letras
export const langFromCulture = culture => {
  const code = String(culture || '').slice(0, 2).toLowerCase();
  return LANGS.includes(code) ? code : DEFAULT_LANG;
};

const bundles = new Map();
let current = { lang: DEFAULT_LANG, dict: {} };

async function load(lang) {
  if (bundles.has(lang)) return bundles.get(lang);
  const response = await fetch(`/portal/js/i18n/${lang}.json`);
  if (!response.ok) throw new Error(`Faltan los textos de "${lang}"`);
  const dict = await response.json();
  bundles.set(lang, dict);
  return dict;
}

export async function setLang(lang) {
  const target = LANGS.includes(lang) ? lang : DEFAULT_LANG;
  const base = await load(DEFAULT_LANG);
  const dict = target === DEFAULT_LANG ? base : { ...base, ...(await load(target)) };
  current = { lang: target, dict };
  document.documentElement.lang = target;
  return current;
}

export const lang = () => current.lang;

/** t('login.submit') · t('catalog.count', { n: 99 }) — la clave se devuelve tal cual si falta */
export function t(key, vars) {
  const value = current.dict[key];
  if (value === undefined) return key;
  if (!vars) return value;
  return value.replace(/\{(\w+)\}/g, (match, name) => (name in vars ? vars[name] : match));
}
