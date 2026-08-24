// Visor multi-ángulo del producto: varias fotos del mismo modelo que el comprador
// gira arrastrando (efecto giro 360) y amplía en un lightbox. Se alimenta de la
// galería del catálogo (item.images); si solo hay una foto, degrada a imagen simple.
import { esc } from '../format.js';
import { t } from '../i18n.js';
import { icons } from './icons.js';

// Monta el visor dentro de `host`. images: array de urls. opts.name: alt/título.
export function createViewer(host, images, opts = {}) {
  const frames = (images || []).filter(Boolean);
  const name = opts.name || '';

  if (!frames.length) {
    host.innerHTML = `<div class="viewer viewer-empty"><span class="item-art" aria-hidden="true">${icons.shoe(90)}</span></div>`;
    return;
  }

  const many = frames.length > 1;
  let idx = 0;

  host.innerHTML = `
    <div class="viewer${many ? ' viewer-spin' : ''}"${many
        ? ` tabindex="0" role="group" aria-roledescription="${esc(t('viewer.spinRole'))}" aria-label="${esc(name)}"` : ''}>
      <div class="viewer-stage" data-stage>
        ${frames.map((src, i) => `<img src="${esc(src)}" class="viewer-frame${i === 0 ? ' on' : ''}"
            alt="${esc(name)}${many ? ` — ${i + 1}/${frames.length}` : ''}" draggable="false"
            decoding="async"${i === 0 ? '' : ' loading="lazy"'}>`).join('')}
        <button type="button" class="viewer-zoom" data-zoom aria-label="${esc(t('viewer.zoom'))}">${icons.search(16)}</button>
        ${many
          ? `<span class="viewer-badge">${icons.spin(14)} ${esc(t('viewer.badge'))}</span>
             <span class="viewer-hint" data-hint>${esc(t('viewer.dragHint'))}</span>`
          : `<span class="viewer-hint viewer-zoomhint">${esc(t('viewer.zoomHint'))}</span>`}
      </div>
      ${many ? `
        <div class="viewer-controls">
          <button type="button" class="viewer-nav" data-nav="-1" aria-label="${esc(t('viewer.prev'))}">${icons.left(18)}</button>
          <div class="viewer-thumbs" data-thumbs>
            ${frames.map((src, i) => `<button type="button" class="viewer-thumb${i === 0 ? ' on' : ''}"
                data-thumb="${i}" aria-label="${esc(t('viewer.frame', { n: i + 1 }))}"><img src="${esc(src)}" alt="" loading="lazy"></button>`).join('')}
          </div>
          <button type="button" class="viewer-nav" data-nav="1" aria-label="${esc(t('viewer.next'))}">${icons.right(18)}</button>
        </div>` : ''}
    </div>`;

  const root = host.querySelector('.viewer');
  const stage = host.querySelector('[data-stage]');
  const framesEls = [...host.querySelectorAll('.viewer-frame')];
  const thumbsEls = [...host.querySelectorAll('.viewer-thumb')];
  const hint = host.querySelector('[data-hint]');

  const show = i => {
    idx = (i % frames.length + frames.length) % frames.length;
    framesEls.forEach((el, k) => el.classList.toggle('on', k === idx));
    thumbsEls.forEach((el, k) => el.classList.toggle('on', k === idx));
    thumbsEls[idx]?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  };

  // moved: distingue un giro (arrastre) de un toque (abre el zoom)
  let dragging = false, moved = false, startX = 0, startIdx = 0;
  const PX_PER_FRAME = 22;

  if (many) {
    const startDrag = x => {
      dragging = true; moved = false; startX = x; startIdx = idx;
      root.classList.add('dragging'); hint?.classList.add('gone');
    };
    const onMove = x => {
      if (!dragging) return;
      if (Math.abs(x - startX) > 4) moved = true;
      show(startIdx - Math.round((x - startX) / PX_PER_FRAME));
    };
    const endDrag = () => { dragging = false; root.classList.remove('dragging'); };

    // Ratón: los listeners de window viven SOLO durante el arrastre; así no quedan
    // colgados al cambiar de vista del SPA (sin fugas).
    const mouseMove = e => onMove(e.clientX);
    const mouseUp = () => {
      endDrag();
      window.removeEventListener('mousemove', mouseMove);
      window.removeEventListener('mouseup', mouseUp);
    };
    stage.addEventListener('mousedown', e => {
      startDrag(e.clientX);
      window.addEventListener('mousemove', mouseMove);
      window.addEventListener('mouseup', mouseUp);
    });

    // Táctil: listeners en el propio stage; se liberan con el elemento al repintar.
    stage.addEventListener('touchstart', e => startDrag(e.touches[0].clientX), { passive: true });
    stage.addEventListener('touchmove', e => {
      onMove(e.touches[0].clientX);
      if (e.cancelable) e.preventDefault();
    }, { passive: false });
    stage.addEventListener('touchend', endDrag);

    root.addEventListener('click', event => {
      const nav = event.target.closest('[data-nav]');
      if (nav) { show(idx + Number(nav.dataset.nav)); return; }
      const thumb = event.target.closest('[data-thumb]');
      if (thumb) show(Number(thumb.dataset.thumb));
    });
    root.addEventListener('keydown', event => {
      if (event.key === 'ArrowRight') { show(idx + 1); event.preventDefault(); }
      else if (event.key === 'ArrowLeft') { show(idx - 1); event.preventDefault(); }
    });
  }

  // Botón de lupa: afordancia explícita de zoom (teclado y táctil), sin depender del hover
  host.querySelector('[data-zoom]').addEventListener('click', event => {
    event.stopPropagation();
    openZoom(frames[idx], name);
  });

  // Toque en la foto (no arrastre) → lightbox con la imagen grande
  stage.addEventListener('click', event => {
    if (event.target.closest('[data-nav], [data-thumb], [data-zoom]')) return;
    if (moved) { moved = false; return; }
    openZoom(frames[idx], name);
  });
}

// Mueve el foco al diálogo, lo atrapa mientras está abierto y lo restaura al cerrar.
function trapFocus(container, initial) {
  const previous = document.activeElement;
  (initial || container).focus?.();
  const onKey = event => {
    if (event.key !== 'Tab') return;
    const f = [...container.querySelectorAll('button, [href], input, [tabindex]:not([tabindex="-1"])')]
      .filter(el => !el.disabled && el.offsetParent !== null);
    if (!f.length) return;
    const first = f[0], last = f[f.length - 1];
    if (event.shiftKey && document.activeElement === first) { last.focus(); event.preventDefault(); }
    else if (!event.shiftKey && document.activeElement === last) { first.focus(); event.preventDefault(); }
  };
  container.addEventListener('keydown', onKey);
  return () => { container.removeEventListener('keydown', onKey); previous?.focus?.(); };
}

// Pila de overlays (quick-view + lightbox). Escape cierra SOLO la capa superior, así
// ampliar sobre el quick-view y pulsar Escape cierra el zoom y no el modal de debajo.
const openOverlays = [];
function pushOverlay(close) {
  openOverlays.push(close);
  const onKey = event => {
    if (event.key === 'Escape' && openOverlays[openOverlays.length - 1] === close) {
      event.stopPropagation();
      close();
    }
  };
  document.addEventListener('keydown', onKey);
  return () => {
    const i = openOverlays.indexOf(close);
    if (i >= 0) openOverlays.splice(i, 1);
    document.removeEventListener('keydown', onKey);
  };
}

// Quick-view: abre el visor completo (giro + miniaturas) en un modal, sin salir de
// la página. Lo usa el lookbook desde el badge 360° de las tarjetas.
export function openViewerModal(images, name) {
  const box = document.createElement('div');
  box.className = 'viewer-modal';
  box.innerHTML = `<div class="viewer-modal-box" role="dialog" aria-modal="true" aria-label="${esc(name)}">
      <button type="button" class="viewer-modal-close" aria-label="${esc(t('viewer.close'))}">✕</button>
      <div class="viewer-modal-name">${esc(name)}</div>
      <div class="viewer-modal-mount"></div>
    </div>`;
  let release, popKey;
  const close = () => { release?.(); popKey?.(); box.remove(); };
  box.addEventListener('click', e => { if (e.target === box || e.target.closest('.viewer-modal-close')) close(); });
  document.body.appendChild(box);
  createViewer(box.querySelector('.viewer-modal-mount'), images, { name });
  release = trapFocus(box.querySelector('.viewer-modal-box'), box.querySelector('.viewer-modal-close'));
  popKey = pushOverlay(close);
  requestAnimationFrame(() => box.classList.add('on'));
}

// Lightbox de zoom: la foto a pantalla completa, cerrar con clic o Escape.
function openZoom(src, name) {
  const box = document.createElement('div');
  box.className = 'viewer-lightbox';
  box.setAttribute('role', 'dialog');
  box.setAttribute('aria-modal', 'true');
  box.setAttribute('aria-label', name);
  box.innerHTML = `<button type="button" class="viewer-lightbox-close" aria-label="${esc(t('viewer.close'))}">✕</button>
    <img src="${esc(src)}" alt="${esc(name)}">`;
  let release, popKey;
  const close = () => { release?.(); popKey?.(); box.remove(); };
  box.addEventListener('click', close);
  document.body.appendChild(box);
  release = trapFocus(box, box.querySelector('.viewer-lightbox-close'));
  popKey = pushOverlay(close);
  requestAnimationFrame(() => box.classList.add('on'));
}
