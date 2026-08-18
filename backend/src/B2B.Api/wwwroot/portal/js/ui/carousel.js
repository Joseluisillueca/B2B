// Carrusel del hero de la portada (01-dashboard.png): imágenes a ancho completo,
// dots centrados abajo y avance automático. Sin librerías: track con translateX.
//
// El contenido lo pone el CMS (portal_content → dashboard.hero), así que cada
// diapositiva puede traer texto y enlace opcionales; sin ellos es solo imagen,
// como en el portal actual.

import { esc } from '../format.js';
import { t } from '../i18n.js';
import { icons } from './icons.js';

const AUTOPLAY_MS = 6500;
const SWIPE_PX = 40;

const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

// Un medio del CMS que ya no está en disco dejaba el icono de "imagen rota" en
// mitad del hero. Si la imagen no carga se retira y el slide se queda con su fondo
// y su rótulo, que es lo que el visitante necesita ver.
const picture = (slide, eager) => {
  const img = `<img src="${esc(slide.imageUrl)}" alt="${esc(slide.alt || '')}"
    loading="${eager ? 'eager' : 'lazy'}" ${eager ? 'fetchpriority="high"' : ''}
    decoding="async" draggable="false" onerror="this.remove()">`;
  return slide.imageUrlMobile
    ? `<picture><source media="(max-width:48rem)" srcset="${esc(slide.imageUrlMobile)}">${img}</picture>`
    : img;
};

/**
 * La primera imagen del hero es el LCP de la portada, pero su URL solo se conoce
 * cuando responde el CMS (no se puede poner en el <head> de index.html). Se pide
 * en cuanto llega el JSON, antes de montar el DOM, y con prioridad alta.
 */
const preloadFirst = slide => {
  const url = (matchMedia('(max-width:48rem)').matches && slide.imageUrlMobile) || slide.imageUrl;
  if (!url || document.head.querySelector(`link[rel="preload"][href="${CSS.escape(url)}"]`)) return;
  const link = document.createElement('link');
  link.rel = 'preload';
  link.as = 'image';
  link.href = url;
  link.fetchPriority = 'high';
  document.head.append(link);
};

const caption = slide => {
  if (!slide.title && !slide.subtitle && !slide.ctaText) return '';
  return `
    <div class="c-caption">
      ${slide.title ? `<strong>${esc(slide.title)}</strong>` : ''}
      ${slide.subtitle ? `<span>${esc(slide.subtitle)}</span>` : ''}
      ${slide.ctaText ? `<span class="c-cta">${esc(slide.ctaText)}</span>` : ''}
    </div>`;
};

const slideHtml = (slide, index) => {
  const inner = `${picture(slide, index === 0)}${caption(slide)}`;
  return slide.ctaHref
    ? `<a class="c-slide" href="${esc(slide.ctaHref)}">${inner}</a>`
    : `<div class="c-slide">${inner}</div>`;
};

/**
 * Pinta el carrusel dentro de `host`. Se auto-detiene cuando el nodo sale del
 * documento (el router sustituye el contenido de la vista sin avisar).
 */
export function carousel(host, slides, { label = '' } = {}) {
  const items = slides.filter(slide => slide?.imageUrl);
  if (!items.length) return;

  const many = items.length > 1;
  preloadFirst(items[0]);
  host.innerHTML = `
    <div class="carousel" role="region" aria-roledescription="${esc(t('carousel.role'))}"
         aria-label="${esc(label)}">
      <div class="c-track">${items.map(slideHtml).join('')}</div>
      ${many ? `
        <button type="button" class="c-arrow prev" aria-label="${esc(t('carousel.prev'))}">${icons.chevron(20)}</button>
        <button type="button" class="c-arrow next" aria-label="${esc(t('carousel.next'))}">${icons.chevron(20)}</button>
        <div class="c-dots">
          ${items.map((_, i) => `<button type="button" data-go="${i}"
            aria-label="${esc(t('carousel.goTo', { n: i + 1 }))}"></button>`).join('')}
          <button type="button" class="c-play" data-play aria-pressed="false"
            aria-label="${esc(t('carousel.pause'))}">${icons.pause(14)}</button>
        </div>` : ''}
    </div>`;

  const root = host.querySelector('.carousel');
  const track = root.querySelector('.c-track');
  const dots = [...root.querySelectorAll('[data-go]')];
  const playBtn = root.querySelector('[data-play]');
  const slideNodes = [...track.children];
  if (reducedMotion()) track.style.transition = 'none';

  let index = 0;
  let timer = 0;
  // Con "reducir movimiento" el pase automático arranca detenido, pero el botón
  // sigue ahí por si el usuario lo quiere (sin transición, que es lo que pidió)
  let paused = reducedMotion();

  const paint = () => {
    track.style.transform = `translateX(-${index * 100}%)`;
    slideNodes.forEach((node, i) => {
      node.setAttribute('aria-hidden', String(i !== index));
      // Nada enfocable fuera de la diapositiva visible
      if (node.tagName === 'A') node.tabIndex = i === index ? 0 : -1;
    });
    dots.forEach((dot, i) => {
      // Son botones normales, no pestañas: todos son tabulables
      dot.setAttribute('aria-current', String(i === index));
      dot.tabIndex = 0;
    });
  };

  const paintPlay = () => {
    if (!playBtn) return;
    playBtn.setAttribute('aria-pressed', String(paused));
    playBtn.setAttribute('aria-label', t(paused ? 'carousel.play' : 'carousel.pause'));
    playBtn.innerHTML = paused ? icons.play(14) : icons.pause(14);
  };

  const goTo = (next, manual = false) => {
    index = (next + items.length) % items.length;
    paint();
    // Tocar el carrusel equivale a pausarlo: el botón lo dice
    if (manual) pause();
  };

  const stop = () => { clearInterval(timer); timer = 0; };

  const pause = () => { paused = true; stop(); paintPlay(); };
  const play = () => { paused = false; start(); paintPlay(); };

  const start = () => {
    if (!many || timer || paused) return;
    timer = setInterval(() => {
      // El router puede haber cambiado de vista: aquí se acaba el carrusel
      if (!root.isConnected) return stop();
      if (!document.hidden && !root.matches(':hover, :focus-within')) goTo(index + 1);
    }, AUTOPLAY_MS);
  };

  if (many) {
    root.querySelector('.prev').onclick = () => goTo(index - 1, true);
    root.querySelector('.next').onclick = () => goTo(index + 1, true);
    dots.forEach((dot, i) => { dot.onclick = () => goTo(i, true); });
    playBtn.onclick = () => (paused ? play() : pause());

    root.addEventListener('keydown', event => {
      if (event.key === 'ArrowRight') goTo(index + 1, true);
      else if (event.key === 'ArrowLeft') goTo(index - 1, true);
    });

    // Swipe en tableta, que es donde se compra en tienda
    let startX = null;
    root.addEventListener('pointerdown', event => { startX = event.clientX; });
    root.addEventListener('pointerup', event => {
      if (startX === null) return;
      const delta = event.clientX - startX;
      startX = null;
      if (Math.abs(delta) > SWIPE_PX) goTo(index + (delta < 0 ? 1 : -1), true);
    });
  }

  paint();
  paintPlay();
  start();
}
