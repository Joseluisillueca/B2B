// Imágenes de producto: cada modelo del catálogo con su foto. Subir un fichero (lo
// aloja el portal) o pegar una URL, y quitarla. Escribe el documento `model-image`
// que ya lee el catálogo comprable — no depende del ERP.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, flash } from '../util.js';

export default async function images(main) {
  let items = [];
  try { items = (await api.modelImages()).items || []; } catch (e) { main.innerHTML = `<div class="notice notice-error">${esc(e.message)}</div>`; return; }
  const withImg = items.filter(m => m.imageUri).length;

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Catálogo</p>
        <h1 class="title">Imágenes</h1>
        <p class="lead">La foto de cada modelo del catálogo. Súbela o pega una URL; la aloja el propio portal.</p>
      </div>
    </div>
    <div class="mng-tools">
      <div class="mng-search">${icons.search(16)}<input type="search" id="q" placeholder="Buscar modelo…" aria-label="Buscar"></div>
      <span class="spacer"></span>
      <span class="mng-count">${withImg} de ${items.length} con imagen</span>
    </div>
    ${items.length ? '<div class="mng-imgs" id="grid"></div>'
      : `<div class="mng-empty">${icons.image(30)}<b>No hay modelos en el catálogo</b>
         <p>Crea un modelo primero y luego añade su imagen.</p>
         <a class="btn-primary" href="#/models/new">${icons.plus(16)} Nuevo modelo</a></div>`}`;

  if (!items.length) return;
  const grid = main.querySelector('#grid');
  const q = main.querySelector('#q');

  const card = m => `
    <figure class="mng-img-card" data-id="${esc(m.externalId)}" data-name="${esc((m.name || '') + ' ' + (m.reference || ''))}">
      <div class="mng-img-thumb">${m.imageUri
        ? `<img src="${esc(m.imageUri)}" alt="" loading="lazy">`
        : `<span class="mng-img-none">${icons.image(26)}<span>Sin imagen</span></span>`}</div>
      <figcaption>
        <b>${esc(m.name || m.externalId)}</b>
        <span>${esc(m.reference || '')}</span>
        <div class="mng-img-actions">
          <button class="btn-ghost" data-act="upload">${icons.upload(15)} Subir</button>
          <button class="btn-ghost" data-act="url">${icons.image(15)} URL</button>
          ${m.imageUri ? `<button class="btn-ghost" data-act="remove" style="color:var(--out)">${icons.trash(15)}</button>` : ''}
        </div>
      </figcaption>
    </figure>`;

  const paint = () => {
    const term = (q.value || '').toLowerCase().trim();
    const shown = items.filter(m => !term || (m.name || '').toLowerCase().includes(term) || (m.reference || '').toLowerCase().includes(term) || m.externalId.toLowerCase().includes(term));
    grid.innerHTML = shown.map(card).join('') || '<p class="mng-multi-empty">Sin resultados.</p>';
    grid.querySelectorAll('.mng-img-card').forEach(fig => wire(fig));
  };

  const refreshCard = (id, uri) => {
    const m = items.find(x => x.externalId === id);
    if (m) m.imageUri = uri;
    paint();
    main.querySelector('.mng-count').textContent = `${items.filter(x => x.imageUri).length} de ${items.length} con imagen`;
  };

  function wire(fig) {
    const id = fig.dataset.id;
    fig.querySelector('[data-act=upload]').onclick = () => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = 'image/png,image/jpeg,image/webp,image/avif,image/gif';
      input.onchange = async () => {
        const file = input.files[0]; if (!file) return;
        try {
          const up = await api.uploadMedia(file);
          await api.setModelImage(id, up.url);
          refreshCard(id, up.url + '?t=' + Date.now());
          flash(`Imagen subida: ${up.name}`);
        } catch (e) { flash(e.body?.error || e.message, 'err'); }
      };
      input.click();
    };
    fig.querySelector('[data-act=url]').onclick = async () => {
      const url = prompt('URL de la imagen (https://… o /media/…):');
      if (!url) return;
      try { await api.setModelImage(id, url.trim()); refreshCard(id, url.trim()); flash('Imagen asignada.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
    const rm = fig.querySelector('[data-act=remove]');
    if (rm) rm.onclick = async () => {
      if (!confirm('¿Quitar la imagen de este modelo?')) return;
      try { await api.delModelImage(id); refreshCard(id, null); flash('Imagen quitada.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
  }

  q.oninput = paint;
  paint();
}
