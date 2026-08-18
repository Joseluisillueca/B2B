// Asistente del portal: chat flotante para preguntar en lenguaje natural sobre la
// actividad del cliente ("¿qué artículo he comprado más?", "¿cuánto he comprado de la
// talla 40?"). Habla con POST /api/portal/assistant, que responde de forma determinista
// o con el modelo si hay clave configurada. Se monta una sola vez sobre <body>.

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc } from '../format.js';
import { icons } from './icons.js';

let mounted = false;
const history = [];   // { role: 'user'|'assistant', content }

export function mountAssistant() {
  if (mounted || document.getElementById('assistant-fab')) return;
  mounted = true;

  const fab = document.createElement('button');
  fab.id = 'assistant-fab';
  fab.type = 'button';
  fab.setAttribute('aria-label', t('assistant.open'));
  fab.innerHTML = icons.chat(24);

  const panel = document.createElement('section');
  panel.id = 'assistant-panel';
  panel.hidden = true;
  panel.setAttribute('aria-label', t('assistant.title'));
  panel.innerHTML = `
    <header class="as-head">
      <span class="as-title">${icons.sparkles(18)} ${esc(t('assistant.title'))}</span>
      <button type="button" class="as-close" aria-label="${esc(t('assistant.close'))}">${icons.close(18)}</button>
      <p class="as-sub">${esc(t('assistant.subtitle'))}</p>
    </header>
    <div class="as-log" id="as-log" role="log" aria-live="polite"></div>
    <div class="as-suggest" id="as-suggest">
      ${['s1', 's2', 's3'].map(s => `<button type="button" class="as-chip" data-q="${esc(t(`assistant.${s}`))}">${esc(t(`assistant.${s}`))}</button>`).join('')}
    </div>
    <form class="as-form" id="as-form">
      <textarea id="as-input" rows="1" placeholder="${esc(t('assistant.placeholder'))}"
        aria-label="${esc(t('assistant.placeholder'))}"></textarea>
      <button type="submit" class="as-send" aria-label="${esc(t('assistant.send'))}">${icons.send(18)}</button>
    </form>`;

  document.body.append(fab, panel);

  const log = panel.querySelector('#as-log');
  const input = panel.querySelector('#as-input');
  const suggest = panel.querySelector('#as-suggest');

  const open = () => {
    panel.hidden = false;
    fab.setAttribute('aria-expanded', 'true');
    if (!log.childElementCount) addMessage('assistant', t('assistant.hello'));
    setTimeout(() => input.focus(), 50);
  };
  const close = () => { panel.hidden = true; fab.setAttribute('aria-expanded', 'false'); fab.focus(); };

  fab.onclick = () => (panel.hidden ? open() : close());
  panel.querySelector('.as-close').onclick = close;
  addEventListener('keydown', e => { if (e.key === 'Escape' && !panel.hidden) close(); });

  // Auto-alto del textarea y envío con Enter (Shift+Enter = salto de línea)
  input.addEventListener('input', () => {
    input.style.height = 'auto';
    input.style.height = Math.min(input.scrollHeight, 120) + 'px';
  });
  input.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); panel.querySelector('#as-form').requestSubmit(); }
  });

  suggest.addEventListener('click', e => {
    const chip = e.target.closest('.as-chip');
    if (chip) ask(chip.dataset.q);
  });

  panel.querySelector('#as-form').addEventListener('submit', e => {
    e.preventDefault();
    const q = input.value.trim();
    if (q) ask(q);
  });

  function addMessage(role, text) {
    const el = document.createElement('div');
    el.className = `as-msg as-${role}`;
    el.innerHTML = render(text);
    log.append(el);
    log.scrollTop = log.scrollHeight;
    return el;
  }

  async function ask(question) {
    suggest.hidden = true;
    input.value = '';
    input.style.height = 'auto';
    addMessage('user', question);
    history.push({ role: 'user', content: question });

    const typing = addMessage('assistant', '<span class="as-typing"><i></i><i></i><i></i></span>');
    try {
      const res = await api.post('/api/portal/assistant', { question, history: history.slice(0, -1) });
      typing.innerHTML = render(res.answer || t('assistant.error'));
      history.push({ role: 'assistant', content: res.answer || '' });
    } catch {
      typing.innerHTML = render(t('assistant.error'));
    }
    log.scrollTop = log.scrollHeight;
  }

  // Markdown mínimo y seguro: **negrita** y saltos de línea, sobre texto ya escapado
  function render(text) {
    return esc(String(text))
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/\n/g, '<br>');
  }
}
