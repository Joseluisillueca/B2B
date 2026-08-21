// Crear pedido de selección — /agent/model-selection/add (Fase 3, réplica del real).
// Tres secciones: (1) nombrar + seleccionar modelos (modal con rejilla de tarjetas),
// (2) seleccionar clientes de la cartera (tabla + panel de seleccionados),
// (3) realizar envío (ENVIAR CORREO / GUARDAR SIN ENVIAR / CANCELAR) con validaciones.
// ENVIAR CORREO y GUARDAR requieren las 3 condiciones: nombre + ≥1 modelo + ≥1 cliente.

import { api, ApiError } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc } from '../format.js';
import { go, href } from '../router.js';
import { pageHead } from '../ui/chrome.js';
import { icons } from '../ui/icons.js';

const trash = () => (icons.trash ? icons.trash(15) : icons.close(14));

export default async function modelSelectionAdd(host) {
  const model = { name: '', models: [], clients: [], notice: null };
  let portfolio = [];   // cartera completa para el selector de clientes

  host.innerHTML = `
    <div class="page ms-add">
      ${pageHead(t('selection.addTitle'), [t('clients.crumb'), t('selection.crumb'), t('selection.addCrumb')])}

      <p class="acc-field ms-name"><label>
        <span>${esc(t('selection.name'))}</span>
        <input type="text" id="msName" maxlength="200" autocomplete="off">
      </label></p>

      <section class="ms-section">
        <div class="ms-shead">
          <h2 id="msModelsHead"></h2>
          <button type="button" class="ms-link" id="msAddModels">${esc(t('selection.addModels'))}</button>
        </div>
        <div id="msModels" class="ms-models"></div>
      </section>

      <section class="ms-section">
        <h2>${esc(t('selection.clientsTitle'))}</h2>
        <div class="ms-clients">
          <div class="ms-clients-main">
            <div class="doc-tools ms-cfilters">
              <form class="doc-search" role="search">
                <input type="search" id="msClientSearch" placeholder="${esc(t('clients.search'))}" aria-label="${esc(t('clients.searchLabel'))}">
                <button type="submit" aria-label="${esc(t('clients.searchLabel'))}">${icons.search(17)}</button>
              </form>
              <label class="acc-field ms-seg"><span>${esc(t('clients.filter.segment'))}</span>
                <select id="msSegment"><option value="">${esc(t('clients.filter.all'))}</option></select></label>
              <button type="button" class="btn-ghost" id="msSelectAll">${esc(t('selection.selectAll'))}</button>
              <button type="button" class="btn-ghost" id="msDeselectAll">${esc(t('selection.deselectAll'))}</button>
            </div>
            <div id="msClientsTable"></div>
          </div>
          <aside class="ms-selected panel">
            <div class="ms-selected-head">
              <b id="msSelectedCount"></b>
              <button type="button" class="ms-link" id="msClearClients">${esc(t('selection.clearAll'))}</button>
            </div>
            <div id="msSelectedList"></div>
          </aside>
        </div>
      </section>

      <section class="ms-section ms-send">
        <h2>${esc(t('selection.sendTitle'))}</h2>
        <div id="msSend"></div>
      </section>
    </div>`;

  const nameInput = host.querySelector('#msName');
  const modelsHost = host.querySelector('#msModels');
  const modelsHead = host.querySelector('#msModelsHead');
  const clientsTable = host.querySelector('#msClientsTable');
  const selectedList = host.querySelector('#msSelectedList');
  const selectedCount = host.querySelector('#msSelectedCount');
  const segment = host.querySelector('#msSegment');
  const sendHost = host.querySelector('#msSend');
  let clientQuery = '';

  nameInput.oninput = () => { model.name = nameInput.value.trim(); renderSend(); };
  host.querySelector('#msAddModels').onclick = openModelPicker;

  const searchForm = host.querySelector('.ms-cfilters .doc-search');
  searchForm.onsubmit = e => { e.preventDefault(); clientQuery = host.querySelector('#msClientSearch').value.trim(); renderClients(); };
  segment.onchange = renderClients;
  host.querySelector('#msSelectAll').onclick = () => { visibleClients().forEach(addClient); renderClients(); renderSelected(); renderSend(); };
  host.querySelector('#msDeselectAll').onclick = () => { model.clients = []; renderClients(); renderSelected(); renderSend(); };
  host.querySelector('#msClearClients').onclick = () => { model.clients = []; renderClients(); renderSelected(); renderSend(); };

  // ── Modelos ──────────────────────────────────────────────────────────────────
  function renderModels() {
    modelsHead.textContent = t('selection.modelsHead', { n: model.models.length });
    if (!model.models.length) {
      modelsHost.innerHTML = `<p class="ms-empty">${esc(t('selection.noModels'))}</p>`;
      return;
    }
    modelsHost.innerHTML = model.models.map(m => `
      <div class="ms-model" data-id="${esc(m.id)}">
        <div class="ms-model-img">${m.image
          ? `<img src="${esc(m.image)}" alt="" loading="lazy" decoding="async">`
          : `<span class="ms-model-mono">${esc((m.name || '?')[0])}</span>`}</div>
        <div class="ms-model-info"><b>${esc(m.name || '')}</b>
          ${m.reference ? `<span>${esc(m.reference)}</span>` : ''}</div>
        <button type="button" class="ms-del" data-remove aria-label="${esc(t('selection.remove'))}">${trash()}</button>
      </div>`).join('');
    modelsHost.querySelectorAll('[data-remove]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.closest('.ms-model').dataset.id;
        model.models = model.models.filter(x => x.id !== id);
        renderModels(); renderSend();
      };
    });
  }

  // ── Clientes ─────────────────────────────────────────────────────────────────
  function visibleClients() {
    const seg = segment.value;
    const q = clientQuery.toLowerCase();
    return portfolio.filter(c =>
      (!seg || (c.segments || []).includes(seg)) &&
      (!q || (c.name || '').toLowerCase().includes(q) || (c.number || '').toLowerCase().includes(q)));
  }

  const isSelected = id => model.clients.some(c => c.id === id);
  function addClient(c) { if (!isSelected(c.clientId ?? c.id)) model.clients.push({ id: c.clientId ?? c.id, name: c.name, number: c.number }); }

  function renderClients() {
    const rows = visibleClients();
    clientsTable.innerHTML = `
      <div class="grid-scroll">
        <table class="grid ms-ctable">
          <thead><tr>
            <th class="ms-add-col"></th>
            <th>${esc(t('clients.col.name'))}</th>
            <th>${esc(t('selection.lastSent'))}</th>
          </tr></thead>
          <tbody>
            ${rows.length ? rows.map(c => {
              const id = c.clientId;
              const on = isSelected(id);
              return `<tr class="${on ? 'ms-on' : ''}">
                <td class="ms-add-col">
                  <button type="button" class="ms-plus${on ? ' on' : ''}" data-add="${esc(id)}"
                    aria-label="${esc(on ? t('selection.remove') : t('clients.select'))}">
                    ${on ? icons.check(16) : icons.plus(16)}</button></td>
                <td><b>${esc(c.name || '')}</b>${c.number ? `<span class="ag-cnum">${esc(c.number)}</span>` : ''}</td>
                <td><span class="cl-no">${esc(t('selection.never'))}</span></td>
              </tr>`;
            }).join('') : `<tr><td colspan="3" class="grid-empty">${esc(t('clients.none'))}</td></tr>`}
          </tbody>
        </table>
      </div>`;
    clientsTable.querySelectorAll('[data-add]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.dataset.add;
        if (isSelected(id)) model.clients = model.clients.filter(c => c.id !== id);
        else { const c = portfolio.find(x => x.clientId === id); if (c) addClient(c); }
        renderClients(); renderSelected(); renderSend();
      };
    });
  }

  function renderSelected() {
    selectedCount.textContent = t('selection.selectedCount', { n: model.clients.length });
    selectedList.innerHTML = model.clients.length
      ? model.clients.map(c => `
          <div class="ms-chip" data-id="${esc(c.id)}">
            <span>${esc(c.name || c.number || c.id)}</span>
            <button type="button" data-remove aria-label="${esc(t('selection.remove'))}">${icons.close(13)}</button>
          </div>`).join('')
      : `<p class="ms-empty">${esc(t('selection.noClients'))}</p>`;
    selectedList.querySelectorAll('[data-remove]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.closest('.ms-chip').dataset.id;
        model.clients = model.clients.filter(c => c.id !== id);
        renderClients(); renderSelected(); renderSend();
      };
    });
  }

  // ── Envío ────────────────────────────────────────────────────────────────────
  function renderSend() {
    const errs = [];
    if (!model.models.length) errs.push(t('selection.errModel'));
    if (!model.clients.length) errs.push(t('selection.errClient'));
    if (!model.name) errs.push(t('selection.errName'));
    const ready = errs.length === 0;

    sendHost.innerHTML = `
      <p class="ms-sendtext">${esc(t('selection.sendText', { n: model.clients.length }))}</p>
      ${model.notice ? `<div class="notice notice-${model.notice.tone}" ${model.notice.tone === 'error' ? 'role="alert"' : ''}>
        ${model.notice.tone === 'ok' ? icons.check(18) : icons.alert(18)}<div><span>${esc(model.notice.text)}</span></div></div>` : ''}
      <div class="ms-send-actions">
        <button type="button" class="btn-primary" id="msSendMail"${ready ? '' : ' disabled'}>${esc(t('selection.send'))}</button>
        <button type="button" class="btn-ghost btn-strong" id="msSave"${ready ? '' : ' disabled'}>${esc(t('selection.saveDraft'))}</button>
        <a class="btn-ghost" href="${href('agent/model-selection')}">${esc(t('selection.cancel'))}</a>
      </div>
      ${errs.length ? `<ul class="ms-errors">${errs.map(e => `<li>${esc(e)}</li>`).join('')}</ul>` : ''}`;

    sendHost.querySelector('#msSendMail').onclick = () => submit(true);
    sendHost.querySelector('#msSave').onclick = () => submit(false);
  }

  async function submit(send) {
    model.notice = null;
    const buttons = sendHost.querySelectorAll('button');
    buttons.forEach(b => { b.disabled = true; });
    try {
      await api.createModelSelection({
        name: model.name,
        modelIds: model.models.map(m => m.id),
        clientIds: model.clients.map(c => c.id),
        send
      });
      go('agent/model-selection');
    } catch (failure) {
      model.notice = { tone: 'error', text: failure instanceof ApiError && failure.body?.error ? failure.body.error : t('selection.errorBody') };
      renderSend();
    }
  }

  // ── Modal selector de modelos ──────────────────────────────────────────────────
  function openModelPicker() {
    const chosen = new Map(model.models.map(m => [m.id, m]));   // copia editable

    const dialog = document.createElement('dialog');
    dialog.className = 'dlg dlg-models';
    dialog.innerHTML = `
      <div class="panel mp-panel">
        <header class="mp-head">
          <h2>${esc(t('selection.pickTitle'))}</h2>
          <form class="doc-search mp-search" role="search">
            <input type="search" name="q" placeholder="${esc(t('selection.searchModel'))}" aria-label="${esc(t('selection.searchModel'))}">
            <button type="submit" aria-label="${esc(t('selection.searchModel'))}">${icons.search(17)}</button>
          </form>
        </header>
        <div class="mp-grid" id="mpGrid"><div class="skeleton"></div></div>
        <footer class="mp-foot">
          <span id="mpCount"></span>
          <span class="mp-foot-actions">
            <button type="button" class="btn-ghost" data-close>${esc(t('selection.cancel'))}</button>
            <button type="button" class="btn-primary" id="mpAccept"></button>
          </span>
        </footer>
      </div>`;
    document.body.append(dialog);
    dialog.addEventListener('close', () => dialog.remove());
    dialog.querySelector('[data-close]').onclick = () => dialog.close();

    const grid = dialog.querySelector('#mpGrid');
    const countEl = dialog.querySelector('#mpCount');
    const acceptBtn = dialog.querySelector('#mpAccept');

    const refreshFooter = () => {
      countEl.textContent = t('selection.modelsPicked', { n: chosen.size });
      acceptBtn.textContent = t('selection.accept', { n: chosen.size });
    };

    async function loadGrid(q = '') {
      grid.innerHTML = '<div class="skeleton"></div>';
      let data;
      try {
        const params = new URLSearchParams({ take: '48', locale: lang() });
        if (q) params.set('search', q);
        data = await api.catalogModels(params);
      } catch { grid.innerHTML = `<p class="mp-error">${esc(t('selection.errorBody'))}</p>`; return; }

      const items = data.items || [];
      grid.innerHTML = items.length ? items.map(m => `
        <article class="mp-card${chosen.has(m.id) ? ' on' : ''}" data-id="${esc(m.id)}">
          <div class="mp-img">${m.image
            ? `<img src="${esc(m.image)}" alt="" loading="lazy" decoding="async">`
            : `<span class="ms-model-mono">${esc((m.name || '?')[0])}</span>`}</div>
          <div class="mp-info"><b>${esc(m.name || '')}</b>${m.reference ? `<span>${esc(m.reference)}</span>` : ''}</div>
          <button type="button" class="mp-pick" data-pick>${esc(chosen.has(m.id) ? t('selection.picked') : t('selection.pick'))}</button>
        </article>`).join('') : `<p class="mp-empty">${esc(t('selection.noModelsFound'))}</p>`;

      grid.querySelectorAll('.mp-card').forEach(card => {
        const id = card.dataset.id;
        const item = items.find(x => x.id === id);
        card.querySelector('[data-pick]').onclick = () => {
          if (chosen.has(id)) chosen.delete(id); else chosen.set(id, { id, name: item.name, reference: item.reference, image: item.image });
          card.classList.toggle('on', chosen.has(id));
          card.querySelector('[data-pick]').textContent = chosen.has(id) ? t('selection.picked') : t('selection.pick');
          refreshFooter();
        };
      });
    }

    dialog.querySelector('.mp-search').onsubmit = e => {
      e.preventDefault();
      loadGrid(dialog.querySelector('.mp-search input').value.trim());
    };
    acceptBtn.onclick = () => {
      model.models = [...chosen.values()];
      dialog.close();
      renderModels(); renderSend();
    };

    refreshFooter();
    loadGrid();
    dialog.showModal();
  }

  // ── Carga inicial ──────────────────────────────────────────────────────────────
  try {
    const data = await api.agentClients(new URLSearchParams({ take: '500' }));
    portfolio = data.items || [];
    // Poblar el filtro de segmentos con los de la cartera
    const segs = [...new Set(portfolio.flatMap(c => c.segments || []).filter(Boolean))].sort();
    for (const s of segs) segment.insertAdjacentHTML('beforeend', `<option value="${esc(s)}">${esc(s)}</option>`);
  } catch { portfolio = []; }

  renderModels();
  renderClients();
  renderSelected();
  renderSend();
}
