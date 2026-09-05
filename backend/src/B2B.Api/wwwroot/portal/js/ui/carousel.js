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
const isMobile = () => matchMedia('(max-width:48rem)').matches;

// Un medio del CMS que ya no está en disco dejaba el icono de "imagen rota" en
// mitad del hero. Si la imagen no carga se retira y el slide se queda con su fondo
// y su rótulo, que es lo que el visitante necesita ver.
const isVideo = url => /\.(mp4|webm|mov)(\?|$)/i.test(url || '');

// Con datos escasos (ahorro de datos o 2G/3G declarados por el navegador) el vídeo no
// se descarga: el póster ES la portada. Safari no expone navigator.connection → false.
const lowData = () => {
  const c = navigator.connection;
  return !!(c && (c.saveData || /(^|-)2g$|^3g$/.test(c.effectiveType || '')));
};
// Sin vídeo cuando el usuario pide menos movimiento o va con datos: se pinta el póster.
const autoVideo = () => !reducedMotion() && !lowData();

// Vídeo del slide para ESTA pantalla: el recorte móvil (16:9) si el CMS lo trae; si no,
// el general; por compatibilidad, un imageUrl que apunte a .mp4/.webm.
const videoOf = slide =>
  (isMobile() && slide.videoUrlMobile) || slide.videoUrl || (isVideo(slide.imageUrl) ? slide.imageUrl : '');

const imageHtml = (slide, eager) => {
  const img = `<img src="${esc(slide.imageUrl)}" alt="${esc(slide.alt || '')}"
    loading="${eager ? 'eager' : 'lazy'}" ${eager ? 'fetchpriority="high"' : ''}
    decoding="async" draggable="false" onerror="this.remove()">`;
  return slide.imageUrlMobile
    ? `<picture><source media="(max-width:48rem)" srcset="${esc(slide.imageUrlMobile)}">${img}</picture>`
    : img;
};

// Un slide puede ser vídeo (videoUrl / videoUrlMobile, o un imageUrl que apunte a
// .mp4/.webm) o imagen. El vídeo va silenciado y en bucle, SIN autoplay en el atributo:
// lo arranca el carrusel (play()) solo cuando es el slide activo y está en pantalla; los
// demás quedan en preload="metadata" (con +faststart, decenas de KB) hasta que les toca.
// imageUrl es el póster (y el LCP: preloadFirst ya lo pide). Con "reducir movimiento"
// o ahorro de datos no se pinta <video>: se pinta el póster como imagen y punto.
// Un vídeo SIN póster (forma antigua: imageUrl apunta al .mp4, como el hero de ALMA) se
// pinta siempre: no hay otra cosa que enseñar y un hueco vacío sería peor que el vídeo.
const media = (slide, eager) => {
  const video = videoOf(slide);
  const poster = slide.imageUrl && !isVideo(slide.imageUrl) ? slide.imageUrl : '';
  if (video && (autoVideo() || !poster)) {
    const shown = (isMobile() && slide.imageUrlMobile) || poster;
    return `<video class="c-video" muted loop playsinline disablepictureinpicture disableremoteplayback
      preload="${eager ? 'auto' : 'metadata'}"${shown ? ` poster="${esc(shown)}"` : ''}
      src="${esc(video)}" aria-label="${esc(slide.alt || '')}"></video>`;
  }
  // Imagen (o el póster del vídeo que no se reproduce). Un vídeo sin póster no pinta nada.
  return poster ? imageHtml(slide, eager) : '';
};

/**
 * La primera imagen del hero es el LCP de la portada, pero su URL solo se conoce
 * cuando responde el CMS (no se puede poner en el <head> de index.html). Se pide
 * en cuanto llega el JSON, antes de montar el DOM, y con prioridad alta.
 */
const preloadFirst = slide => {
  const url = (matchMedia('(max-width:48rem)').matches && slide.imageUrlMobile) || slide.imageUrl;
  if (!url || isVideo(url) || document.head.querySelector(`link[rel="preload"][href="${CSS.escape(url)}"]`)) return;
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
  const inner = `${media(slide, index === 0)}${caption(slide)}`;
  return slide.ctaHref
    ? `<a class="c-slide" href="${esc(slide.ctaHref)}">${inner}</a>`
    : `<div class="c-slide">${inner}</div>`;
};

/**
 * Pinta el carrusel dentro de `host`. Se auto-detiene cuando el nodo sale del
 * documento (el router sustituye el contenido de la vista sin avisar).
 */
export function carousel(host, slides, { label = '' } = {}) {
  const items = slides.filter(slide => slide?.imageUrl || slide?.videoUrl);
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
          <button type="button" class="c-play" data-play
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
  // El botón ⏯ detiene TODO el movimiento: el pase y el vídeo (WCAG 2.2.2). Tocar un
  // punto o una flecha solo detiene el pase; el vídeo del slide elegido sigue.
  let stopped = false;
  let onScreen = true;

  // ── Vídeo: solo el del slide activo, con el carrusel a la vista y la pestaña visible ──
  // Sin slides de vídeo (las otras marcas) `videos` es [null, …]: nada de lo de abajo actúa.
  const videos = slideNodes.map(node => node.querySelector('video.c-video'));
  const hasVideo = videos.some(Boolean);
  // Último currentTime visto de cada vídeo: sirve para detectar el cierre del bucle.
  const lastTimes = videos.map(() => 0);

  // Si el mp4 falla (404, códec, red) el <video> se cambia por su póster: nunca un hueco negro.
  const fallbackToPoster = video => {
    if (!video.isConnected) return;
    if (!video.poster) return video.remove();
    const img = document.createElement('img');
    img.src = video.poster;
    img.alt = video.getAttribute('aria-label') || '';
    img.decoding = 'async';
    img.draggable = false;
    img.onerror = () => img.remove();
    video.replaceWith(img);
  };
  videos.forEach(video => {
    if (video) video.addEventListener('error', () => fallbackToPoster(video), { once: true });
  });

  const syncVideos = () => {
    if (!hasVideo) return;
    videos.forEach((video, i) => {
      if (!video || !video.isConnected) return;
      const shouldPlay = i === index && onScreen && !stopped && !document.hidden;
      if (shouldPlay) {
        if (video.preload !== 'auto') video.preload = 'auto';
        video.play().catch(() => {});   // autoplay bloqueado (ahorro de batería): queda el póster
      } else if (!video.paused) {
        video.pause();
        if (i !== index) { video.currentTime = 0; lastTimes[i] = 0; }   // al volver arranca en el fotograma del póster
      }
    });
  };

  if (hasVideo) {
    // Fuera de pantalla (el comprador ya está en las ventanas o los KPI) el vídeo se para;
    // al volver, sigue. Pestaña oculta: igual. El listener se suelta cuando el router quita el nodo.
    // La cabecera es pegajosa: lo que queda debajo de ella no cuenta como «en pantalla».
    if ('IntersectionObserver' in window) {
      const header = document.querySelector('#chrome-header');
      const io = new IntersectionObserver(([entry]) => { onScreen = entry.isIntersecting; syncVideos(); },
        { threshold: 0.25, rootMargin: `-${header ? header.offsetHeight : 0}px 0px 0px 0px` });
      io.observe(root);
    }
    const onVisibility = () => {
      if (!root.isConnected) return document.removeEventListener('visibilitychange', onVisibility);
      syncVideos();
    };
    document.addEventListener('visibilitychange', onVisibility);
  }

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
    syncVideos();
  };

  // ¿Se mueve algo? El pase (si no está en pausa) o el film de la diapositiva activa. El botón
  // enseña UN estado: ⏸ mientras algo se mueve, ▶ cuando todo está quieto. Un solo nombre
  // cambiante y sin aria-pressed (el patrón APG de reproducir/pausar).
  const moving = () => !paused || !!(videos[index] && videos[index].isConnected && !videos[index].paused);
  const paintPlay = () => {
    if (!playBtn) return;
    const on = moving();
    playBtn.setAttribute('aria-label', t(on ? (hasVideo ? 'carousel.pauseAll' : 'carousel.pause')
                                            : (hasVideo ? 'carousel.playAll' : 'carousel.play')));
    playBtn.innerHTML = on ? icons.pause(14) : icons.play(14);
  };

  const stop = () => { clearTimeout(timer); timer = 0; };

  // La siguiente diapositiva de imagen se pide en cuanto se arma el pase: con loading="lazy"
  // y a un ancho de distancia (translateX) el navegador no la pedía hasta empezar la
  // transición y pintaba ~1 s la caja vacía. No toca el DOM.
  const warmNext = () => {
    const slide = items[(index + 1) % items.length];
    if (!slide || videoOf(slide)) return;
    const url = (isMobile() && slide.imageUrlMobile) || slide.imageUrl;
    if (url && !isVideo(url)) new Image().src = url;
  };

  const goTo = (next, manual = false) => {
    index = (next + items.length) % items.length;
    paint();
    // Tocar el carrusel equivale a pausarlo: el botón lo dice
    if (manual) pause();
    else { stop(); start(); }   // reloj a cero: la nueva diapositiva tiene sus 6,5 s enteros
    paintPlay();
  };

  const pause = () => { paused = true; stop(); paintPlay(); };
  const play = () => { paused = false; start(); paintPlay(); };

  // Pase automático con setTimeout, no setInterval: cada diapositiva arranca su propio reloj.
  // En una diapositiva de vídeo el film manda: el pase avanza cuando el bucle se cierra (abajo,
  // 'timeupdate'), nunca por reloj a mitad de plano; el reloj queda de red por si el vídeo se
  // atasca (currentTime sin avanzar entre dos ticks) o no llegó a reproducirse (queda el póster).
  let tickTime = -1;
  const tick = () => {
    timer = 0;
    // El router puede haber cambiado de vista: aquí se acaba el carrusel
    if (!root.isConnected) return;
    const video = videos[index];
    if (video && video.isConnected && !video.paused && video.currentTime !== tickTime) {
      tickTime = video.currentTime;
      return start();
    }
    if (!document.hidden && !root.matches(':hover, :focus-within')) goTo(index + 1);
    else start();
  };
  const start = () => {
    if (!many || timer || paused) return;
    timer = setTimeout(tick, AUTOPLAY_MS);
    warmNext();
  };

  videos.forEach((video, i) => {
    if (!video) return;
    // Cierre del bucle (currentTime vuelve a 0): con el pase armado, es el momento de pasar.
    video.addEventListener('timeupdate', () => {
      const wrapped = video.currentTime + 1 < lastTimes[i];
      lastTimes[i] = video.currentTime;
      if (wrapped && i === index && timer && !paused && !document.hidden
          && !root.matches(':hover, :focus-within')) goTo(index + 1);
    });
    // El icono sigue al estado real del vídeo (autoplay bloqueado, pestaña oculta…)
    ['play', 'pause'].forEach(type => video.addEventListener(type, paintPlay));
  });

  if (many) {
    root.querySelector('.prev').onclick = () => goTo(index - 1, true);
    root.querySelector('.next').onclick = () => goTo(index + 1, true);
    dots.forEach((dot, i) => { dot.onclick = () => goTo(i, true); });
    playBtn.onclick = () => {
      // Una pulsación lo para TODO (pase y film); la siguiente lo reanuda todo
      if (moving()) { stopped = true; pause(); } else { stopped = false; play(); }
      syncVideos();
      paintPlay();
    };

    root.addEventListener('keydown', event => {
      if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
      const fromSlide = !!event.target.closest('.c-slide');
      goTo(index + (event.key === 'ArrowRight' ? 1 : -1), true);
      // El foco no se queda en una diapositiva oculta (aria-hidden): pasa a la nueva
      if (fromSlide) (slideNodes[index].tagName === 'A' ? slideNodes[index] : playBtn).focus({ preventScroll: true });
      event.preventDefault();
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
