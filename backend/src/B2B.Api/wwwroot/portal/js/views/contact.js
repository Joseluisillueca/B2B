// Vista 14 — /{market}/{lang}/contact (07-contact.png).
//
// "Puedes enviarnos este formulario o escribirnos vía tiendas@lejanbrand.com" y el
// formulario de cuatro campos: Asunto*, Email de contacto, Adjuntar archivo
// (SELECCIONAR FICHERO) y Mensaje*, con el botón azul ENVIAR SOLICITUD.
//
// La solicitud se guarda en el backend (queda constancia aunque el correo falle) y
// va dirigida al buzón de tiendas.

import { api } from '../api.js';
import { t } from '../i18n.js';
import { esc } from '../format.js';
import { state } from '../state.js';
import { icons } from '../ui/icons.js';
import { pageHead } from '../ui/chrome.js';

const INBOX = 'tiendas@lejanbrand.com';

export default function contact(host) {
  let notice = null;   // { tone: 'ok' | 'error', text }
  let file = null;

  // El "Consultar" del catálogo (F-02) llega aquí con la referencia del artículo:
  // el asunto viene escrito para que el usuario solo tenga que redactar el mensaje.
  const ref = new URLSearchParams(location.search).get('ref') || '';
  let prefillSubject = ref ? t('contact.stockSubject', { ref }) : '';

  render();

  function render() {
    host.innerHTML = `
      <div class="page contact">
        ${pageHead(t('nav.contact'), [t('nav.contact')])}

        <p class="ct-lead">${t('contact.lead', {
          email: `<a class="link" href="mailto:${INBOX}">${INBOX}</a>`
        })}</p>

        ${notice ? `
          <div class="notice notice-${notice.tone}"${notice.tone === 'error' ? ' role="alert"' : ''}>
            ${notice.tone === 'ok' ? icons.check(18) : icons.alert(18)}
            <div><span>${esc(notice.text)}</span></div>
          </div>` : ''}

        <form class="ct-form" novalidate>
          <div class="ct-row">
            <p class="ct-field">
              <label for="subject">${esc(t('contact.subject'))}<b class="ct-req">*</b></label>
              <input type="text" id="subject" name="subject" maxlength="200" required
                value="${esc(prefillSubject)}">
            </p>
            <p class="ct-field">
              <label for="email">${esc(t('contact.email'))}</label>
              <input type="email" id="email" name="email" maxlength="320"
                placeholder="${esc(t('contact.emailPlaceholder'))}"
                value="${esc(state.me?.email || '')}">
            </p>
            <p class="ct-field">
              <span class="ct-label">${esc(t('contact.attach'))}</span>
              <label class="ct-file" for="file">${icons.upload(17)}${esc(t('contact.pickFile'))}</label>
              <input type="file" id="file" name="file" class="sr-only"
                accept=".jpg,.jpeg,.png,.webp,.gif,.pdf,.txt,.csv,.xlsx,.xls,.docx,.doc,.zip">
              <span class="ct-filename" id="fileName">${esc(file ? file.name : t('contact.noFile'))}
                ${file ? `<button type="button" id="dropFile" class="ct-file-drop"
                  aria-label="${esc(t('contact.removeFile'))}">${icons.close(14)}</button>` : ''}</span>
            </p>
          </div>

          <p class="ct-field ct-message">
            <label for="message">${esc(t('contact.message'))}<b class="ct-req">*</b></label>
            <textarea id="message" name="message" rows="5" maxlength="4000" required></textarea>
          </p>

          <div class="ct-actions">
            <button type="submit" class="btn-primary">${icons.send(16)}${esc(t('contact.submit'))}</button>
          </div>
        </form>
      </div>`;

    bind();
  }

  function bind() {
    const form = host.querySelector('.ct-form');
    const input = host.querySelector('#file');

    // El <input type=file> nativo no se puede vestir: se oculta y el botón negro de
    // la referencia es su <label>, que ya lo abre sin JavaScript.
    input.onchange = () => {
      file = input.files?.[0] || null;
      const name = host.querySelector('#fileName');
      name.textContent = file ? file.name : t('contact.noFile');
      if (file) {
        const drop = document.createElement('button');
        drop.type = 'button';
        drop.className = 'ct-file-drop';
        drop.setAttribute('aria-label', t('contact.removeFile'));
        drop.innerHTML = icons.close(14);
        drop.onclick = () => { file = null; input.value = ''; render(); };
        name.append(drop);
      }
    };

    host.querySelector('#dropFile')?.addEventListener('click', () => {
      file = null;
      input.value = '';
      render();
    });

    form.onsubmit = async event => {
      event.preventDefault();
      const subject = form.elements.subject.value.trim();
      const message = form.elements.message.value.trim();

      if (!subject || !message) {
        notice = { tone: 'error', text: t('contact.required') };
        const draft = { subject, email: form.elements.email.value, message };
        render();
        restore(draft);
        return;
      }

      const body = new FormData();
      body.set('subject', subject);
      body.set('email', form.elements.email.value.trim());
      body.set('message', message);
      if (file) body.set('file', file);

      const submit = form.querySelector('button[type=submit]');
      submit.disabled = true;
      try {
        // multipart: no pasa por api.post(), que serializa JSON
        const response = await fetch('/api/portal/contact', {
          method: 'POST',
          headers: state.token ? { Authorization: `Bearer ${state.token}` } : {},
          body
        });
        if (!response.ok) throw new Error(String(response.status));
        file = null;
        prefillSubject = '';   // enviada la consulta, el formulario queda limpio
        notice = { tone: 'ok', text: t('contact.sent') };
        render();
      } catch {
        submit.disabled = false;
        notice = { tone: 'error', text: t('contact.error') };
        const draft = { subject, email: form.elements.email.value, message };
        render();
        restore(draft);
      }
    };
  }

  // Un fallo no puede costarle al cliente el mensaje que acaba de escribir
  function restore({ subject, email, message }) {
    const form = host.querySelector('.ct-form');
    form.elements.subject.value = subject;
    form.elements.email.value = email;
    form.elements.message.value = message;
  }
}
