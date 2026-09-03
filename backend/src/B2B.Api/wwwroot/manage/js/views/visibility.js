// Sección "Visibilidad de catálogo" de las fichas de CLIENTE y AGENTE.
// Lista blanca por atributo contra GET/PUT /api/admin/visibility/{type}/{id}:
// si la fila la manda BC (bcRules) aquí solo se ENSEÑA (candado, edición en BC);
// si no, se edita la fila manual y se guarda con su propio botón (la visibilidad
// vive en su tabla, aparte del documento de la ficha).
//
// Moneda del attributeId: el SLUG del conector (CatalogVocabulary.Slug — minúsculas;
// espacios, "/", "\", "_", "." y "-" a "-"; sin repetidos ni extremos). Es la clave
// con la que VisibilityScope compara los atributos del modelo y el KeySlug de las
// facetas del catálogo, así que casa 1:1 con lo que filtra el portal. El vocabulario
// (atributos y valores elegibles) se construye con los maestros `attribute`/`family`
// sincronizados UNIDOS a lo realmente observado en los modelos: si BC aún no ha
// publicado el maestro, lo que existe en el catálogo sigue siendo elegible.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash, loadRows } from '../util.js';

export const FAMILY_ATTR = 'familyid';   // pseudo-atributo "familyId" ya en slug

// Réplica exacta de CatalogVocabulary.Slug (backend): NO usar util.slugify, que
// destruye los acentos ("Colección" → "colecci-n" ≠ "colección" del runtime).
export const visSlug = value => String(value ?? '').toLowerCase()
  .replace(/[ /\\_.-]+/g, '-').replace(/^-+|-+$/g, '');

const label = name => {
  if (typeof name === 'string') return name;
  if (name && typeof name === 'object') return name.es_ES || Object.values(name)[0] || '';
  return '';
};
const cap = s => s ? s.charAt(0).toUpperCase() + s.slice(1) : s;

// ── Vocabulario: attrSlug → { label, values: Map(valueSlug → etiqueta) } ─────────
async function loadVocabulary() {
  const [models, attrDocs, famDocs] = await Promise.all([
    loadRows('model').catch(() => []),
    loadRows('attribute').catch(() => []),
    loadRows('family').catch(() => []),
  ]);

  const attrs = new Map();
  const ensure = (slugId, text) => {
    if (!attrs.has(slugId)) attrs.set(slugId, { label: text, values: new Map() });
    else if (text && !attrs.get(slugId).label) attrs.get(slugId).label = text;
    return attrs.get(slugId);
  };

  // Familia (Líneas): siempre presente, valores = docs family ∪ familyId de los modelos
  const family = { label: 'Familia (Líneas)', values: new Map() };
  for (const f of famDocs) {
    const id = visSlug(f.payload?.code || f.id);
    if (id) family.values.set(id, label(f.payload?.name) || cap(id));
  }

  // Maestros attribute (si el conector los ha publicado): code = B2B Code
  for (const a of attrDocs) {
    const id = visSlug(a.payload?.code || a.id);
    if (!id) continue;
    const entry = ensure(id, label(a.payload?.name) || a.payload?.code || a.id);
    for (const v of (Array.isArray(a.payload?.values) ? a.payload.values : [])) {
      const rawId = typeof v === 'object' ? (v.id ?? '') : v;
      const vid = visSlug(rawId);
      if (vid && !entry.values.has(vid))
        entry.values.set(vid, (typeof v === 'object' && label(v.name)) || String(rawId));
    }
  }

  // Lo observado en el catálogo real: claves/valores de model.attributes + familyId
  for (const m of models) {
    const fid = visSlug(m.payload?.familyId || '');
    if (fid && !family.values.has(fid)) family.values.set(fid, cap(fid));
    const bag = m.payload?.attributes;
    if (!bag || typeof bag !== 'object' || Array.isArray(bag)) continue;
    for (const [key, value] of Object.entries(bag)) {
      if (typeof value !== 'string') continue;
      const id = visSlug(key);
      if (!id) continue;
      const entry = ensure(id, key);
      const vid = visSlug(value);
      if (vid && !entry.values.has(vid)) entry.values.set(vid, value);
    }
  }

  const sorted = new Map([...attrs.entries()].sort((x, y) => x[1].label.localeCompare(y[1].label, 'es')));
  return { family, attrs: sorted };
}

const attrLabel = (vocab, attributeId) =>
  attributeId === FAMILY_ATTR ? vocab.family.label : (vocab.attrs.get(attributeId)?.label || attributeId);
const valueLabel = (vocab, attributeId, valueId) => {
  const source = attributeId === FAMILY_ATTR ? vocab.family : vocab.attrs.get(attributeId);
  return source?.values.get(valueId) || valueId;
};

// ── Sección completa. host = contenedor vacío dentro del <form> de la ficha ─────
// subjectNoun solo cambia los textos ("el cliente" / "el agente").
export async function mountVisibility(host, type, id, subjectNoun = 'el cliente') {
  if (!host || !id) return;
  let data, vocab;
  try { [data, vocab] = await Promise.all([api.getVisibility(type, id), loadVocabulary()]); }
  catch (e) {
    host.innerHTML = sectionShell(`
      <div class="notice notice-error" role="alert">${icons.alert(18)}<div><span>
        No se pudo cargar la visibilidad: ${esc(e.body?.error || e.message)}</span></div></div>`);
    return;
  }

  // Reglas en edición (copia mutable de las manuales). Los valores desconocidos del
  // vocabulario se conservan: se pintan con su slug y siguen guardándose.
  let rules = (Array.isArray(data.manualRules) ? data.manualRules : [])
    .map(r => ({ attributeId: visSlug(r.attributeId), valueIds: (r.valueIds || []).map(visSlug).filter(Boolean) }))
    .filter(r => r.attributeId);
  const locked = Array.isArray(data.bcRules);   // hay fila bc → manda BC

  const render = () => {
    host.innerHTML = sectionShell(locked ? bcHtml() : manualHtml());
    if (!locked) wire();
  };

  function sectionShell(inner) {
    return `
    <section class="biz-section" data-vis-section>
      <header class="acc-head biz-head"><h2>${icons.eye(20)}Visibilidad de catálogo</h2></header>
      <div class="biz-card">${inner}</div>
    </section>`;
  }

  // ── Modo BC: solo lectura ──
  function bcHtml() {
    const chips = (data.bcRules || []).map(r => ruleChip(visSlug(r.attributeId), (r.valueIds || []).map(visSlug))).join('');
    return `
      <div class="vis-bc" role="status">
        <div class="vis-bc-head">${icons.lock(16)}<b>Lo fija Business Central</b></div>
        <p>Estas reglas llegan del conector con la ficha (<code>visibleAttributes</code>).
           Para cambiarlas, edítalas en BC: aquí son de solo lectura.</p>
      </div>
      <div class="vis-chips" role="list" aria-label="Reglas fijadas por Business Central">${chips}</div>
      ${Array.isArray(data.manualRules) ? `
        <p class="vis-manual-note">${icons.alert(14)}<span>Hay una restricción manual guardada en el portal,
          pero <b>no aplica</b> mientras BC fije la suya. Volverá a regir si BC retira sus reglas.</span></p>` : ''}`;
  }

  function ruleChip(attributeId, valueIds) {
    const values = valueIds.length
      ? valueIds.map(v => `<span class="vis-val">${esc(valueLabel(vocab, attributeId, v))}</span>`).join('')
      : '<span class="vis-val is-none">sin valores</span>';
    return `<div role="listitem" class="vis-chip">
      <b>${esc(attrLabel(vocab, attributeId))}</b><span class="vis-arrow">→</span>${values}</div>`;
  }

  // ── Modo manual: editor ──
  function manualHtml() {
    return `
      <p class="biz-hint">${icons.alert(16)}<span>Lista blanca: ${esc(subjectNoun)} solo
        <b>${type === 'agent' ? 've y vende' : 've y compra'}</b> lo permitido.
        Sin restricciones = catálogo completo.</span></p>
      <div data-vis-rules>${rules.length
        ? rules.map((r, i) => ruleEditor(r, i)).join('')
        : `<div class="vis-empty">${icons.eye(20)}<b>Sin restricciones</b>
             <span>${esc(cap(subjectNoun))} ve el catálogo completo.</span></div>`}</div>
      <div class="vis-actions">
        <button type="button" class="btn-ghost" data-vis-add>${icons.plus(15)} Añadir restricción</button>
        <button type="button" class="btn-primary" data-vis-save>Guardar visibilidad</button>
      </div>
      <p class="acc-hint">La visibilidad se guarda aparte del resto de la ficha, con su propio botón.</p>`;
  }

  function ruleEditor(rule, index) {
    const used = new Set(rules.filter((_, i) => i !== index).map(r => r.attributeId));
    const options = [[FAMILY_ATTR, vocab.family.label],
      ...[...vocab.attrs.entries()].map(([k, v]) => [k, v.label])]
      .filter(([k]) => !used.has(k) || k === rule.attributeId);
    // Valores elegibles = vocabulario ∪ los ya marcados en la regla (nada se pierde)
    const source = rule.attributeId === FAMILY_ATTR ? vocab.family : vocab.attrs.get(rule.attributeId);
    const values = new Map(source ? source.values : []);
    for (const v of rule.valueIds) if (!values.has(v)) values.set(v, v);

    return `
      <div class="vis-rule" data-rule="${index}">
        <div class="vis-rule-head">
          <label class="vis-rule-attr"><span>Restringir por</span>
            <select data-vis-attr>${options.map(([k, l]) =>
              `<option value="${esc(k)}"${k === rule.attributeId ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select>
          </label>
          <button type="button" class="btn-ghost nc-remove" data-vis-del>${icons.close(14)} Quitar</button>
        </div>
        ${values.size ? `
          <div class="mng-multi" data-vis-values>${[...values.entries()].map(([vid, text]) =>
            `<label><input type="checkbox" value="${esc(vid)}"${rule.valueIds.includes(vid) ? ' checked' : ''}> ${esc(text)}</label>`).join('')}
          </div>
          <span class="acc-hint">${rule.attributeId === FAMILY_ATTR
            ? 'Valores permitidos. Solo se verán los modelos de las familias marcadas.'
            : `Valores permitidos. Solo se verán los modelos cuyo atributo
               «${esc(attrLabel(vocab, rule.attributeId))}» tenga uno de los valores marcados.`}</span>`
          : `<p class="mng-multi-empty">El catálogo no tiene todavía valores para este atributo.</p>`}
      </div>`;
  }

  function wire() {
    host.querySelector('[data-vis-add]').onclick = () => {
      const used = new Set(rules.map(r => r.attributeId));
      const free = [FAMILY_ATTR, ...vocab.attrs.keys()].find(k => !used.has(k));
      if (!free) return flash('Ya hay una restricción por cada atributo disponible.', 'err');
      rules.push({ attributeId: free, valueIds: [] });
      render();
    };
    host.querySelector('[data-vis-save]').onclick = save;
    for (const block of host.querySelectorAll('.vis-rule')) {
      const index = Number(block.dataset.rule);
      block.querySelector('[data-vis-del]').onclick = () => { rules.splice(index, 1); render(); };
      block.querySelector('[data-vis-attr]').onchange = e => {
        rules[index] = { attributeId: e.target.value, valueIds: [] };
        render();
      };
      for (const input of block.querySelectorAll('[data-vis-values] input'))
        input.onchange = () => {
          rules[index].valueIds = [...block.querySelectorAll('[data-vis-values] input:checked')].map(i => i.value);
        };
    }
  }

  async function save() {
    const withoutValues = rules.find(r => !r.valueIds.length);
    if (withoutValues)
      return flash(`Marca al menos un valor en «${attrLabel(vocab, withoutValues.attributeId)}» o quita esa restricción.`, 'err');
    const btn = host.querySelector('[data-vis-save]');
    btn.disabled = true;
    try {
      data = await api.putVisibility(type, id, rules);
      rules = (Array.isArray(data.manualRules) ? data.manualRules : [])
        .map(r => ({ attributeId: visSlug(r.attributeId), valueIds: (r.valueIds || []).map(visSlug).filter(Boolean) }))
        .filter(r => r.attributeId);
      flash(rules.length ? 'Visibilidad guardada.' : 'Restricciones retiradas: catálogo completo.');
      render();
    } catch (e) {
      btn.disabled = false;
      flash(e.body?.error || e.message || 'No se pudo guardar la visibilidad.', 'err');
    }
  }

  render();
}

// ── "Agentes de este cliente" (solo ficha de cliente): informativo, con enlace ──
export async function mountClientAgents(host, clientId) {
  if (!host || !clientId) return;
  let agents = [];
  try { agents = await loadRows('agent'); } catch { agents = []; }
  const mine = agents.filter(a => (Array.isArray(a.payload?.clientIds) ? a.payload.clientIds : [])
    .some(cid => String(cid).toLowerCase() === String(clientId).toLowerCase()));

  host.innerHTML = `
    <section class="biz-section" data-vis-agents>
      <header class="acc-head biz-head"><h2>${icons.user(20)}Agentes de este cliente</h2></header>
      <div class="biz-card">
        <p class="mng-rel-note">Informativo: la cartera se asigna en la ficha de cada agente
          («Cartera de clientes»). Estos agentes pueden operar en nombre de este cliente.</p>
        ${mine.length ? `
          <div class="mng-rel-chips" role="list">${mine.map(a => `
            <div role="listitem"><a class="mng-rel-chip" href="#/agents/edit/${encodeURIComponent(a.id)}"
              title="Abrir la ficha de este agente">
              <b>${esc(a.payload?.name || a.id)}</b>${a.payload?.email ? `<span>${esc(a.payload.email)}</span>` : ''}</a></div>`).join('')}
          </div>`
          : '<p class="mng-rel-none">Ningún agente lleva este cliente en su cartera.</p>'}
      </div>
    </section>`;
}
