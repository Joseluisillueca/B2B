// Cinta del catálogo (Tarea 10): la banda de pestañas bajo CATÁLOGO | LOOKBOOK con la
// que se navega el catálogo del portal. Aquí se elige QUÉ la alimenta (las familias
// siempre + los valores de los atributos marcados), en qué orden, qué entradas se
// ocultan y con qué título por idioma.
//
// Moneda de datos:
// - Config cruda → IntegrationSettings.CatalogRibbonJson: GET settings.catalogRibbon,
//   PUT /api/admin/integration/ribbon { ribbon: {...} | null } (null = por defecto).
// - Entradas candidatas → las MISMAS facetas del pipeline del catálogo
//   (GET /api/shop/catalog, actor admin sin restricciones): families + attributes
//   (keySlug/values.slug = CatalogVocabulary.Slug, la clave con la que el servidor
//   computa GET /api/shop/ribbon). La vista previa refleja los cambios SIN guardar
//   aplicando los overrides locales a esas candidatas — jamás inventa entradas. Las
//   facetas se piden POR IDIOMA (cacheadas): etiquetas y orden natural de las
//   familias son los del locale, igual que en el servidor.
// - Selector de atributos → el vocabulario de visibility.js (maestros sincronizados
//   ∪ lo observado en modelos), el mismo que usa la visibilidad por cliente/agente.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash } from '../util.js';
import { loadVocabulary, visSlug } from './visibility.js';
import { setLeaveGuard } from '../router.js';

const LANGS = [['es', 'ES'], ['en', 'EN'], ['fr', 'FR'], ['it', 'IT']];
// "Todo" de la cinta real por idioma. DUPLICA la clave `ribbon.all` de los i18n del
// portal (portal/js/i18n/*.json): si allí cambia, cambiar aquí. Fija: no configurable.
const ALL_LABEL = { es: 'Todo', en: 'All', fr: 'Tout', it: 'Tutto' };
const BIG = 1e9;   // "sin orden" para los sort (Infinity-Infinity = NaN rompería el comparador)

// Centinela de navegación: tras cada await, si el usuario ya se fue de #/ribbon no se
// toca `main` (otra vista lo estaría pintando).
const here = () => location.hash.replace(/^#\/?/, '').split('/')[0] === 'ribbon';

export default async function ribbonView(main) {
  let settings, catalog, vocab;
  try {
    [settings, catalog, vocab] = await Promise.all([api.intSettings(), api.shopFacets('es'), loadVocabulary()]);
  } catch (e) {
    if (!here()) return;
    main.innerHTML = `<div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>
      No se pudo cargar la cinta: ${esc(e.body?.error || e.message)}</span></div></div>`;
    return;
  }
  if (!here()) return;

  // Facetas por idioma (la vista previa en EN/FR/IT usa etiquetas y orden natural de
  // ese locale, como el servidor). ES es la base del editor.
  const facetsByLang = new Map([['es', catalog?.facets || {}]]);
  const facetIndex = facets => new Map((facets?.attributes || []).map(f => [String(f.keySlug || '').toLowerCase(), f]));
  const facetBySlug = facetIndex(facetsByLang.get('es'));

  // ── Estado editable, sembrado con la config guardada ───────────────────────────
  const saved = settings?.catalogRibbon && typeof settings.catalogRibbon === 'object'
    && !Array.isArray(settings.catalogRibbon) ? settings.catalogRibbon : null;

  let attrsSelected = (Array.isArray(saved?.attributes) ? saved.attributes : [])
    .map(visSlug).filter(Boolean).filter((s, i, arr) => arr.indexOf(s) === i);

  // key (minúsculas, como compara el servidor) → { hidden, order, titles:{es..}, key }
  // `key` conserva la grafía original para reemitirla tal cual (huérfanas).
  const overrides = new Map();
  for (const entry of (Array.isArray(saved?.entries) ? saved.entries : [])) {
    if (!entry || typeof entry !== 'object' || Array.isArray(entry)) continue;
    const key = String(entry.key || '').trim();
    if (!key) continue;
    const titles = {};
    if (entry.titles && typeof entry.titles === 'object')
      for (const [lang] of LANGS) {
        const t = entry.titles[lang];
        if (typeof t === 'string' && t.trim()) titles[lang] = t.trim();
      }
    overrides.set(key.toLowerCase(), {
      key,
      hidden: entry.hidden === true,
      order: Number.isInteger(entry.order) ? entry.order : null,
      titles,
    });
  }
  const stateOf = (lower, key = lower) => {
    if (!overrides.has(lower)) overrides.set(lower, { key, hidden: false, order: null, titles: {} });
    return overrides.get(lower);
  };
  const cleanTitles = st => {
    const titles = {};
    for (const [lang] of LANGS) if (st?.titles?.[lang]) titles[lang] = st.titles[lang];
    return titles;
  };
  const isEffective = st => st?.hidden || Object.keys(cleanTitles(st)).length > 0;

  // Candidatas en su ORDEN NATURAL (el mismo del servidor: familias por facetas,
  // luego cada atributo marcado en su orden con sus valores en orden de faceta).
  let missing = [];
  function candidatesFrom(facets) {
    const bySlug = facets === facetsByLang.get('es') ? facetBySlug : facetIndex(facets);
    const list = [];
    const lost = [];
    for (const f of (facets?.families || []))
      list.push({ key: `family:${f.id}`, kind: 'family', group: 'Familia', label: f.label, count: f.count });
    for (const slug of attrsSelected) {
      const facet = bySlug.get(slug);
      if (!facet) { lost.push(slug); continue; }
      for (const v of (facet.values || []))
        list.push({ key: `attr:${facet.keySlug}:${v.slug}`, kind: 'attr', group: facet.label || facet.key, label: v.label, count: v.count });
    }
    list.forEach((c, i) => { c.lower = c.key.toLowerCase(); c.natural = i; });
    return { list, lost };
  }
  function buildCandidates() {
    const { list, lost } = candidatesFrom(facetsByLang.get('es'));
    missing = lost;
    return list;
  }

  // `display` ES el orden del editor (y de la cinta). Primera carga: se reconstruye
  // como lo haría el servidor (order asc, luego orden natural).
  let display = buildCandidates().sort((a, b) =>
    ((overrides.get(a.lower)?.order ?? BIG) - (overrides.get(b.lower)?.order ?? BIG)) || (a.natural - b.natural));

  // Al (des)marcar un atributo: las entradas que siguen existiendo conservan su sitio;
  // las nuevas entran detrás, que es su posición natural (el atributo recién marcado
  // va el último en config.attributes).
  function rebuild() {
    const pos = new Map(display.map((c, i) => [c.lower, i]));
    display = buildCandidates().sort((a, b) => {
      const pa = pos.has(a.lower) ? pos.get(a.lower) : BIG + a.natural;
      const pb = pos.has(b.lower) ? pos.get(b.lower) : BIG + b.natural;
      return (pa - pb) || (a.natural - b.natural);
    });
  }

  // ¿El editor se aparta del orden natural (ES)? Si sí, se numera TODA la lista.
  const hasReorder = () => display.some((c, i) => i > 0 && display[i - 1].natural > c.natural);

  // Entradas con ajustes guardados que HOY no están en las facetas (valor estacional
  // sin stock, atributo desmarcado…). Se conservan al guardar (sin `order`) y se
  // enseñan para que el admin sepa que existen y pueda quitarlas.
  function orphans() {
    const shown = new Set(display.map(c => c.lower));
    return [...overrides.entries()].filter(([lower, st]) => !shown.has(lower) && isEffective(st));
  }

  // ── Config mínima a guardar: solo lo que difiere del comportamiento por defecto ──
  // Órdenes: las familias se ordenan por etiqueta LOCALIZADA en el servidor, así que
  // el orden natural cambia por idioma y un "prefijo mínimo" calculado en ES no vale
  // en EN. Regla: con cualquier reorden se numera TODA la lista (order = i+1 para
  // todas las entradas del editor); sin reorden, ninguna. Round-trip estable.
  function buildConfig() {
    const numberAll = hasReorder();
    const entries = [];
    display.forEach((c, i) => {
      const st = overrides.get(c.lower);
      const titles = cleanTitles(st);
      const hasTitles = Object.keys(titles).length > 0;
      const order = numberAll ? i + 1 : null;
      if (st?.hidden || hasTitles || order !== null)
        entries.push({
          key: c.key,
          ...(st?.hidden ? { hidden: true } : {}),
          ...(order !== null ? { order } : {}),
          ...(hasTitles ? { titles } : {}),
        });
    });
    for (const [, st] of orphans()) {
      const titles = cleanTitles(st);
      entries.push({
        key: st.key,
        ...(st.hidden ? { hidden: true } : {}),
        ...(Object.keys(titles).length ? { titles } : {}),
      });
    }
    if (!attrsSelected.length && !entries.length) return null;   // por defecto → null
    const config = {};
    if (attrsSelected.length) config.attributes = [...attrsSelected];
    if (entries.length) config.entries = entries;
    return config;
  }

  let baseline = JSON.stringify(buildConfig());
  const isDirty = () => JSON.stringify(buildConfig()) !== baseline;

  // Salir con cambios sin guardar: aviso al cambiar de vista (guardia del router) y al
  // cerrar o recargar la pestaña (beforeunload) — UX-M7. Los oyentes se retiran al salir.
  setLeaveGuard(() => !isDirty()
    || confirm('Tienes cambios sin guardar en la cinta.\n\n¿Salir sin guardarlos?'));
  const onBeforeUnload = event => { if (here() && isDirty()) { event.preventDefault(); event.returnValue = ''; } };
  const unhook = () => {
    if (here()) return;
    removeEventListener('beforeunload', onBeforeUnload);
    removeEventListener('hashchange', unhook);
  };
  addEventListener('beforeunload', onBeforeUnload);
  addEventListener('hashchange', unhook);

  let previewLang = 'es';
  let busy = false;               // Guardar y Restaurar comparten el candado
  const openTitles = new Set();   // entradas con el editor de idiomas desplegado

  // ── Render ─────────────────────────────────────────────────────────────────────

  // Vista previa en `previewLang`: candidatas de ESE idioma (etiquetas + orden
  // natural del locale) con los overrides locales — el mismo cómputo del servidor.
  // Sin facetas del idioma aún cargadas se usan las de ES (llegan y se repinta).
  function previewChips() {
    const facets = facetsByLang.get(previewLang) || facetsByLang.get('es');
    let { list } = candidatesFrom(facets);
    if (hasReorder()) {
      const pos = new Map(display.map((c, i) => [c.lower, i]));
      list = list.sort((a, b) => ((pos.get(a.lower) ?? BIG) - (pos.get(b.lower) ?? BIG)) || (a.natural - b.natural));
    }
    const chips = [`<span class="rb-chip on">${esc(ALL_LABEL[previewLang])}</span>`];
    let lastGroup = 'family';
    for (const c of list) {
      const st = overrides.get(c.lower);
      if (st?.hidden) continue;
      // Separador en cada cambio de plano (familias, atributo A, atributo B): la misma
      // regla que aplica el portal, para que la vista previa no mienta.
      const group = c.kind === 'family' ? 'family' : `attr:${c.group}`;
      if (group !== lastGroup) { chips.push('<span class="rb-psep" aria-hidden="true"></span>'); lastGroup = group; }
      chips.push(`<span class="rb-chip">${esc(st?.titles?.[previewLang] || c.label)}${
        c.count ? `<span class="rb-pcount">${c.count}</span>` : ''}</span>`);
    }
    return chips.join('');
  }

  function entryRow(c, index) {
    const st = overrides.get(c.lower);
    const hidden = st?.hidden === true;
    const custom = st?.titles?.es;
    const name = custom || c.label;
    const titleValues = LANGS.map(([lang, tag]) => `
      <label><span>${tag}</span>
        <input type="text" data-rb-title="${esc(lang)}" value="${esc(st?.titles?.[lang] || '')}"
          placeholder="${esc(c.label)}" aria-label="Título en ${tag} de ${esc(c.label)}"></label>`).join('');
    return `
    <div class="rb-entry${hidden ? ' is-hidden' : ''}" data-key="${esc(c.lower)}" data-index="${index}">
      <div class="rb-row">
        <div class="rb-move">
          <button type="button" data-rb-up ${index === 0 ? 'disabled' : ''}
            aria-label="Subir ${esc(name)}">${icons.up(13)}</button>
          <button type="button" data-rb-down ${index === display.length - 1 ? 'disabled' : ''}
            aria-label="Bajar ${esc(name)}">${icons.down(13)}</button>
        </div>
        <div class="rb-main">
          <b class="rb-name">${esc(name)}</b>
          <span class="rb-meta">
            <span class="grid-chip">${esc(c.group)}</span>
            <span>${c.count} modelo${c.count === 1 ? '' : 's'}</span>
            <span class="rb-orig"${custom ? '' : ' hidden'}>título propio · original «${esc(c.label)}»</span>
          </span>
        </div>
        <div class="rb-tools">
          <button type="button" class="btn-ghost rb-langbtn${openTitles.has(c.lower) ? ' on' : ''}" data-rb-titles
            aria-expanded="${openTitles.has(c.lower)}">${icons.chat(14)} Títulos</button>
          <button type="button" class="rb-vis${hidden ? ' off' : ''}" data-rb-vis role="switch"
            aria-checked="${!hidden}" aria-label="Mostrar ${esc(name)} en la cinta">
            ${hidden ? icons.eyeOff(15) : icons.eye(15)}<span>${hidden ? 'Oculta' : 'Visible'}</span></button>
        </div>
      </div>
      ${openTitles.has(c.lower) ? `
      <div class="rb-titles">
        <div class="rb-titles-grid">${titleValues}</div>
        <span class="acc-hint">Vacío = etiqueta original del catálogo («${esc(c.label)}»).</span>
      </div>` : ''}
    </div>`;
  }

  function orphanRows() {
    const list = orphans();
    if (!list.length) return '';
    // Clave legible ("Silueta → Fantasma"); la técnica se queda al lado, para soporte.
    // El slug conserva los ':' (Slug solo mapea espacio, / \ _ . y -), así que un valor
    // como "Talla: L" deja la clave "attr:talla:talla:-l": se parte por los DOS primeros
    // separadores y el resto es el valor entero.
    const pretty = key => {
      const parts = String(key).split(':');
      const kind = parts[0];
      const a = parts[1] ?? '';
      const v = parts.slice(2).join(':');
      if (kind === 'family') return `Familia → ${vocab.family.values.get(visSlug(parts.slice(1).join(':'))) || parts.slice(1).join(':')}`;
      const attr = vocab.attrs.get(visSlug(a));
      return `${attr?.label || a} → ${attr?.values.get(visSlug(v)) || v}`;
    };
    const summary = st => [
      st.hidden ? 'oculta' : '',
      ...LANGS.filter(([lang]) => st.titles?.[lang]).map(([lang, tag]) => `${tag} «${esc(st.titles[lang])}»`),
    ].filter(Boolean).join(' · ');
    return `
      <div class="rb-orphans">
        <p class="mng-subhead">Entradas sin modelos ahora mismo (${list.length})</p>
        <p class="acc-hint">Tienen ajustes guardados pero hoy no están en el catálogo (valor sin
          stock, atributo desmarcado…). Se conservan por si vuelven; quítalas si ya no las quieres.</p>
        <div class="rb-orphan-list">${list.map(([lower, st]) => `
          <div class="rb-orphan" data-orphan="${esc(lower)}">
            <b>${esc(pretty(st.key))}</b><code>${esc(st.key)}</code><span>${summary(st)}</span>
            <button type="button" class="btn-ghost" data-rb-orphan-del aria-label="Quitar los ajustes de ${esc(st.key)}">
              ${icons.close(13)} Quitar</button>
          </div>`).join('')}
        </div>
      </div>`;
  }

  function attrChips() {
    // Vocabulario ∪ lo ya marcado (una config antigua puede traer un slug que hoy no
    // esté en el vocabulario: se enseña igual para poder desmarcarlo).
    const options = new Map([...vocab.attrs.entries()].map(([slug, a]) => [slug, a.label]));
    for (const slug of attrsSelected) if (!options.has(slug)) options.set(slug, slug);
    if (!options.size) return '<p class="mng-multi-empty">El catálogo aún no tiene atributos.</p>';
    return `<div class="mng-multi" data-rb-attrs>${[...options.entries()].map(([slug, label]) => `
      <label><input type="checkbox" value="${esc(slug)}"${attrsSelected.includes(slug) ? ' checked' : ''}> ${esc(label)}</label>`).join('')}
    </div>`;
  }

  function render() {
    const orphanCount = orphans().length;
    main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Catálogo</p>
        <h1 class="title">Cinta del catálogo</h1>
        <p class="lead">La banda de pestañas con la que se navega el catálogo del portal
          (bajo CATÁLOGO | LOOKBOOK). Elige qué la alimenta, su orden, su visibilidad y sus
          títulos por idioma; el portal la filtra en automático por la visibilidad de cada
          cliente o agente.</p>
      </div>
    </div>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.eye(20)}Vista previa</h2></header>
      <div class="biz-card rb-preview-card">
        <div class="rb-prevbar">
          <span class="rb-prevnote" id="rbLangNote">Idioma de la vista previa</span>
          <div class="rb-langs" role="group" aria-labelledby="rbLangNote">${LANGS.map(([lang, tag]) => `
            <button type="button" class="rb-lang${lang === previewLang ? ' on' : ''}" data-rb-lang="${lang}"
              aria-pressed="${lang === previewLang}">${tag}</button>`).join('')}
          </div>
        </div>
        <div class="rb-mock">
          <div class="rb-mock-top"><b>Catálogo</b><span>Lookbook</span></div>
          <div class="rb-band" id="rbBand" aria-label="Vista previa de la cinta">${previewChips()}</div>
        </div>
        <p class="acc-hint">Se calcula en vivo con los cambios de abajo, aún sin guardar. Es la
          cinta del <b>catálogo completo</b> (administrador): cada cliente o agente puede ver
          <b>menos entradas</b>, según su visibilidad. En cada idioma, las entradas sin título
          propio salen con la etiqueta (y el orden) del catálogo en ese idioma. «Todo» es fija.</p>
      </div>
    </section>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.tag(20)}Atributos que alimentan la cinta</h2></header>
      <div class="biz-card">
        <p class="biz-hint">${icons.alert(16)}<span>Las <b>familias</b> siempre forman la cinta.
          Además, cada valor de los atributos marcados se convierte en una pestaña
          (p. ej. marcar «Silueta» añade Melrose, One…).</span></p>
        ${attrChips()}
        ${missing.length ? `<p class="acc-hint">${missing.map(s => esc(s)).join(', ')}: sin valores en el
          catálogo ahora mismo, no añade${missing.length === 1 ? '' : 'n'} pestañas (si llegan modelos con ese atributo, entrarán solos).</p>` : ''}
        ${vocab.clippedNote ? `<p class="acc-hint">${esc(vocab.clippedNote)}</p>` : ''}
      </div>
    </section>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.list(20)}Entradas de la cinta</h2></header>
      <div class="biz-card">
        ${display.length ? `
          <p class="biz-hint">${icons.alert(16)}<span>Ordena con las flechas, oculta lo que no
            quieras enseñar y da un título propio por idioma. Nada se aplica hasta
            <b>Guardar la cinta</b>.${orphanCount ? ` Hay ${orphanCount} entrada${orphanCount === 1 ? '' : 's'}
            con ajustes que hoy no está${orphanCount === 1 ? '' : 'n'} en el catálogo (abajo).` : ''}</span></p>
          <div class="rb-list">${display.map(entryRow).join('')}</div>`
          : `<div class="vis-empty">${icons.ribbon(20)}<b>Sin entradas todavía</b>
             <span>El catálogo no tiene familias; la cinta aparecerá sola cuando las haya.</span></div>`}
        ${orphanRows()}
      </div>
    </section>

    <div class="rb-actions">
      <button type="button" class="btn-ghost nc-remove" data-rb-reset ${busy ? 'disabled' : ''}>${icons.trash(15)} Restaurar por defecto</button>
      <span class="spacer"></span>
      <span class="rb-dirty" id="rbDirty" role="status" hidden>${icons.alert(14)} Cambios sin guardar</span>
      <button type="button" class="btn-primary" data-rb-save ${busy ? 'disabled' : ''}>Guardar la cinta</button>
    </div>
    <p class="acc-hint rb-foot">Por defecto (sin configuración) la cinta enseña solo las familias
      con su etiqueta original.</p>`;
    wire();
    syncDirty();
  }

  function syncDirty() {
    const tag = main.querySelector('#rbDirty');
    if (tag) tag.hidden = !isDirty();
  }

  function updatePreview() {
    const band = main.querySelector('#rbBand');
    if (band) band.innerHTML = previewChips();
  }

  function setBusy(on) {
    busy = on;
    // El botón dice lo que está pasando (no solo se apaga) y el aviso de "sin guardar"
    // se retira mientras se guarda: si no, conviven "Guardando…" y "cambios sin guardar".
    const save = main.querySelector('[data-rb-save]');
    if (save) {
      save.disabled = on;
      save.setAttribute('aria-busy', String(on));
      save.textContent = on ? 'Guardando…' : 'Guardar la cinta';
    }
    const reset = main.querySelector('[data-rb-reset]');
    if (reset) reset.disabled = on;
    const tag = main.querySelector('#rbDirty');
    if (tag && on) tag.hidden = true;   // syncDirty() lo vuelve a valorar al terminar
  }

  // Facetas del idioma de la vista previa (una petición por idioma, cacheada).
  async function ensureFacets(lang) {
    if (facetsByLang.has(lang)) return;
    try {
      const data = await api.shopFacets(lang);
      facetsByLang.set(lang, data?.facets || {});
    } catch { facetsByLang.set(lang, facetsByLang.get('es')); }   // sin red: cae a ES
    if (here() && previewLang === lang) updatePreview();
  }

  // Textos de una fila que dependen del título ES (en vivo, sin repintar la fila):
  // nombre, aria-label de los botones y el chip "título propio · original".
  function refreshRowText(key) {
    const entry = display.find(c => c.lower === key);
    const row = main.querySelector(`.rb-entry[data-key="${CSS.escape(key)}"]`);
    if (!entry || !row) return;
    const st = overrides.get(key);
    const name = st?.titles?.es || entry.label;
    row.querySelector('.rb-name').textContent = name;
    row.querySelector('[data-rb-up]').setAttribute('aria-label', `Subir ${name}`);
    row.querySelector('[data-rb-down]').setAttribute('aria-label', `Bajar ${name}`);
    row.querySelector('[data-rb-vis]').setAttribute('aria-label', `Mostrar ${name} en la cinta`);
    row.querySelector('.rb-orig').hidden = !st?.titles?.es;
  }

  // ── Interacción ────────────────────────────────────────────────────────────────
  function wire() {
    // Idioma de la vista previa
    for (const btn of main.querySelectorAll('[data-rb-lang]'))
      btn.onclick = () => {
        previewLang = btn.dataset.rbLang;
        for (const b of main.querySelectorAll('[data-rb-lang]')) {
          b.classList.toggle('on', b === btn);
          b.setAttribute('aria-pressed', String(b === btn));
        }
        updatePreview();
        ensureFacets(previewLang);
      };

    // Selector de atributos
    const attrsHost = main.querySelector('[data-rb-attrs]');
    if (attrsHost) attrsHost.onchange = e => {
      const input = e.target.closest('input[type=checkbox]');
      if (!input) return;
      const slug = input.value;
      if (input.checked) { if (!attrsSelected.includes(slug)) attrsSelected.push(slug); }
      else attrsSelected = attrsSelected.filter(s => s !== slug);
      rebuild();
      render();
      main.querySelector(`[data-rb-attrs] input[value="${CSS.escape(slug)}"]`)?.focus();
    };

    // Filas: mover, ocultar, títulos
    for (const row of main.querySelectorAll('.rb-entry')) {
      const index = Number(row.dataset.index);
      const key = row.dataset.key;
      const move = delta => {
        const j = index + delta;
        if (j < 0 || j >= display.length) return;
        [display[index], display[j]] = [display[j], display[index]];
        render();
        // El foco sigue a la entrada movida; si llegó al extremo (botón deshabilitado)
        // pasa al botón opuesto, que sigue operable.
        const moved = main.querySelector(`.rb-entry[data-index="${j}"]`);
        const same = moved?.querySelector(delta < 0 ? '[data-rb-up]' : '[data-rb-down]');
        const other = moved?.querySelector(delta < 0 ? '[data-rb-down]' : '[data-rb-up]');
        (same && !same.disabled ? same : other)?.focus();
      };
      row.querySelector('[data-rb-up]').onclick = () => move(-1);
      row.querySelector('[data-rb-down]').onclick = () => move(1);
      row.querySelector('[data-rb-vis]').onclick = () => {
        const st = stateOf(key, display.find(c => c.lower === key)?.key || key);
        st.hidden = !st.hidden;
        render();
        main.querySelector(`.rb-entry[data-key="${CSS.escape(key)}"] [data-rb-vis]`)?.focus();
      };
      row.querySelector('[data-rb-titles]').onclick = () => {
        openTitles.has(key) ? openTitles.delete(key) : openTitles.add(key);
        render();
        main.querySelector(`.rb-entry[data-key="${CSS.escape(key)}"] ${openTitles.has(key) ? '[data-rb-title="es"]' : '[data-rb-titles]'}`)?.focus();
      };
      for (const input of row.querySelectorAll('[data-rb-title]'))
        input.oninput = () => {
          const st = stateOf(key, display.find(c => c.lower === key)?.key || key);
          const value = input.value.trim();
          if (value) st.titles[input.dataset.rbTitle] = value;
          else delete st.titles[input.dataset.rbTitle];
          // En vivo sin repintar (no se pierde el foco): banda + textos de la fila
          updatePreview();
          refreshRowText(key);
          syncDirty();
        };
    }

    // Huérfanas: quitar sus ajustes
    for (const block of main.querySelectorAll('[data-orphan]'))
      block.querySelector('[data-rb-orphan-del]').onclick = () => {
        overrides.delete(block.dataset.orphan);
        render();
        (main.querySelector('[data-rb-orphan-del]') || main.querySelector('[data-rb-save]'))?.focus();
      };

    // Guardar / restaurar
    main.querySelector('[data-rb-save]').onclick = save;
    main.querySelector('[data-rb-reset]').onclick = reset;
  }

  async function save() {
    if (busy) return;
    const config = buildConfig();
    setBusy(true);
    try {
      await api.putRibbon(config);
      if (!here()) return;
      baseline = JSON.stringify(config);
      flash(config ? 'Cinta guardada. El portal ya la enseña así.'
        : 'Cinta guardada: configuración por defecto (solo familias).');
    } catch (e) {
      if (!here()) return;
      flash(e.body?.error || e.message || 'No se pudo guardar la cinta.', 'err');
    } finally {
      busy = false;
      if (here()) { setBusy(false); syncDirty(); }
    }
  }

  async function reset() {
    if (busy) return;
    if (!confirm('¿Restaurar la cinta por defecto?\n\nSe borra la configuración guardada: solo familias, en su orden natural, con sus etiquetas originales.')) return;
    setBusy(true);
    try {
      await api.putRibbon(null);
      if (!here()) return;
      attrsSelected = [];
      overrides.clear();
      openTitles.clear();
      rebuild();
      display.sort((a, b) => a.natural - b.natural);
      baseline = JSON.stringify(buildConfig());   // = null
      busy = false;
      flash('Cinta restaurada: por defecto (solo familias).');
      render();
    } catch (e) {
      if (!here()) return;
      flash(e.body?.error || e.message || 'No se pudo restaurar la cinta.', 'err');
    } finally {
      busy = false;
      // syncDirty() también aquí: setBusy(true) escondió el aviso de "cambios sin
      // guardar" y, si la restauración falla, los cambios SIGUEN sin guardarse.
      if (here()) { setBusy(false); syncDirty(); }
    }
  }

  render();
}
