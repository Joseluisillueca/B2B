// Lookbook "Colecciones lejan" — /{market}/{lang}/lookbook.
// Entorno editorial de marca (barefoot) donde cada historia termina en un raíl
// "Compra el look" con productos reales del catálogo. Cada producto se puede marcar
// FAVORITO (♥, reutiliza el mecanismo del catálogo) o AÑADIR como PRESELECCIÓN (sin
// tallas); las cantidades se ponen después en la ficha, como pide el flujo.
//
// Reutiliza: contenido del CMS (portal_content lookbook.*), el carrusel de la portada,
// la tarjeta .pcard del catálogo, favoritos y la preselección de state.js. No inventa
// embudo: alimenta favoritos → (preselección) → carrito → pedido.
//
// Portada: carrusel con las diapositivas del CMS que traen imagen o vídeo; sobre papel
// (heroStyle = paper) un ÍNDICE tipográfico sin fotografía (ver indexBlock).

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state } from '../state.js';
import { go, href } from '../router.js';
import { getTokens } from '../branding.js';
import { carousel } from '../ui/carousel.js';
import { icons } from '../ui/icons.js';
import { openViewerModal } from '../ui/viewer.js';
import { bindRail } from '../ui/related.js';

const preferred = () => (state.me?.prefs?.showPrices === 'pvp' ? 'pvp' : 'pvd');
const priceOf = (item, kind) =>
  item?.[kind] == null ? null : { label: t(`catalog.price.${kind}`), value: item[kind] };
const main = item => priceOf(item, preferred()) ?? priceOf(item, preferred() === 'pvd' ? 'pvp' : 'pvd');
const productHref = item => `${href('product')}/${encodeURIComponent(item.reference || item.modelId)}`;
const favLabel = on => t(on ? 'catalog.favoriteOff' : 'catalog.favorite');

// ¿Marca sobre papel? El mismo atributo del que cuelga el bloque CSS de la marca, para que
// JS y hoja de estilo no discrepen nunca. branding.js lo pone antes de que arranque el
// router (boot.js espera a initBranding), así que aquí ya es fiable.
const onPaper = () => document.documentElement.dataset.heroStyle === 'paper';
// Ancla de cada historia por posición (lb-s01…): el índice de la apertura salta a ellas.
const storyId = n => `lb-s${String(n).padStart(2, '0')}`;

async function content(key) {
  try {
    const data = await api.get(`/api/portal/content/${key}?locale=${encodeURIComponent(lang())}`);
    return Array.isArray(data) ? data : (data?.items || []);
  } catch { return []; }
}

export default async function lookbook(host) {
  // Sobre papel la apertura es el índice tipográfico: la clase va en el cascarón para que el
  // esqueleto de carga ya tenga su altura y su papel, sin placa gris ni salto al pintar.
  const paper = onPaper();
  host.innerHTML = `
    <div class="page lookbook">
      <div class="lb-hero${paper ? ' lb-hero-index' : ''}" id="lbHero"><div class="hero-skeleton"></div></div>
      <div id="lbBody" aria-live="polite"><div class="skeleton"></div></div>
    </div>
    <div id="lbSelBar"></div>`;

  const heroHost = host.querySelector('#lbHero');
  const body = host.querySelector('#lbBody');
  const selBar = host.querySelector('#lbSelBar');

  // Ventanas de servicio que trae el catálogo: "Ver todo el catálogo" las necesita
  let windows = [];
  const [hero, stories] = await Promise.all([content('lookbook.hero'), content('lookbook.stories')]);
  // Los productos del raíl los elige el CMS uno a uno, así que se piden POR ID. Antes se
  // traía una PÁGINA del catálogo y se buscaban dentro: con más de 100 artículos, los que
  // caían fuera de esa página desaparecían del raíl sin ningún aviso.
  const catalog = await loadCatalog([...new Set(stories.flatMap(story => story.refs || []).map(String))]);
  const byId = new Map(catalog.map(p => [String(p.modelId), p]));

  // Limpia de la preselección lo que ya se ha tallado (tiene líneas en el carrito)
  const carted = new Set(state.cartLines().map(l => String(l.modelId)));
  for (const p of state.preselections()) if (carted.has(String(p.modelId))) state.unpreselect(p.modelId);

  // ── Portada ─────────────────────────────────────────────────────────────────
  // Sobre papel (BLOCCO 5) el lookbook abre como su índice impreso (p02): sin fotos, seis
  // entradas numeradas que anclan a cada historia y la palabra gigante. Los textos salen del
  // primer bloque lookbook.hero del CMS (title = la palabra, subtitle = línea de estado,
  // kicker = cabecera de página, ctaText/ctaHref = la acción); su imagen, si la hay, se
  // ignora a propósito. Las otras marcas conservan el carrusel con las diapositivas que
  // traen imagen o vídeo; sin ninguna (el bloque ya admite elementos solo de texto) cae al
  // titular de siempre en vez de dejar el hueco vacío.
  const slides = hero.filter(slide => slide?.imageUrl || slide?.videoUrl);
  if (paper) {
    heroHost.innerHTML = indexBlock(hero[0] || {}, stories);
    bindIndex();
  } else if (slides.length) {
    heroHost.innerHTML = '';
    carousel(heroHost, slides, { label: t('lookbook.heroLabel') });
  } else {
    heroHost.innerHTML = `<div class="lb-hero-fallback"><h1>${esc(t('lookbook.title'))}</h1></div>`;
  }

  // "Ver todo el catálogo" (cierre y, sobre papel, la acción del índice) abre el catálogo
  // en la ventana de PROGRAMACIÓN si la instancia la tiene: el lookbook es la campaña, y
  // abrirlo en reposición —donde la colección nueva aún dice "Consultar"— era un callejón
  // sin salida. La preferencia se escribe ANTES de navegar (el router intercepta el clic
  // después). Sin ventana programada no se toca nada.
  const toScheduled = () => {
    if (windows.some(w => w.orderType === 'SCHEDULED') && state.prefs.window !== 'scheduled')
      state.prefs = { ...state.prefs, window: 'scheduled' };
  };
  // La acción del índice solo si lleva al catálogo (el CMS puede apuntar a otro sitio)
  const indexCta = heroHost.querySelector('[data-to-catalog]');
  if (indexCta?.getAttribute('href') === href('catalog/catalog')) indexCta.addEventListener('click', toScheduled);

  // ── Historias ───────────────────────────────────────────────────────────────
  if (!stories.length) {
    body.innerHTML = `<div class="page"><p class="lb-empty">${esc(t('lookbook.empty'))}</p></div>`;
    return;
  }
  body.innerHTML = `<div class="lb-stories">${stories.map((story, i) => storyBlock(story, byId, i + 1)).join('')}
    <div class="lb-close">
      <p>${esc(t('lookbook.closeLead'))}</p>
      <a class="btn-primary" href="${href('catalog/catalog')}">${esc(t('lookbook.toCatalog'))} ${icons.right(15)}</a>
    </div></div>`;

  bindProducts();
  bindRails();
  renderSelBar();
  body.querySelector('.lb-close a')?.addEventListener('click', toScheduled);

  // ── Bloques de historia ───────────────────────────────────────────────────
  // n = posición (1…): da el id al que salta el índice de la apertura. El id y el tabindex
  // del h2 van sin gatear: son anclas inertes, sin efecto visual en las otras marcas.
  function storyBlock(story, map, n) {
    const refs = (story.refs || []).map(id => map.get(String(id))).filter(Boolean);
    const side = story.layout === 'left' ? 'lb-left' : 'lb-right';
    const accent = /^#[0-9a-fA-F]{3,8}$/.test(story.accent || '') ? story.accent : 'var(--accent)';
    // Si el CMS no trae imagen editorial, no dejamos una caja vacía: caemos a la
    // foto del primer producto del raíl (evita el efecto "imagen rota").
    const media = story.imageUrl || (refs[0] && refs[0].imageUri) || '';
    return `
      <section class="lb-story ${side}" id="${storyId(n)}" style="--lb-accent:${esc(accent)}">
        <div class="lb-story-media">
          ${media ? `<img src="${esc(media)}" alt="${esc(story.alt || story.title || '')}" loading="lazy" decoding="async">` : ''}
        </div>
        <div class="lb-story-text">
          ${story.kicker ? `<span class="lb-kicker">${esc(story.kicker)}</span>` : ''}
          <h2 tabindex="-1">${esc(story.title || '')}</h2>
          ${story.body ? `<p>${esc(story.body)}</p>` : ''}
        </div>
        ${refs.length ? `
          <div class="lb-shop">
            <!-- Cabecera con las flechas del raíl (mismo patrón y mismas .related-arrow
                 que "Completa la gama"): sin ellas la cuarta tarjeta quedaba cortada a
                 media palabra y solo la barra del sistema, invisible en Mac y táctil,
                 decía que había más. Se enseñan solo si el raíl desborda (JS). -->
            <div class="lb-shop-head" style="display:flex;align-items:center;justify-content:space-between;gap:1rem;margin:0 0 1rem">
              <h3 class="lb-shop-title" style="margin:0">${esc(t('lookbook.shopTheLook'))}</h3>
              <div class="lb-shop-nav" style="display:flex;gap:.5rem;flex:none" hidden>
                <button type="button" class="related-arrow lb-prev" aria-label="${esc(t('lookbook.prev'))}">${icons.left(18)}</button>
                <button type="button" class="related-arrow lb-next" aria-label="${esc(t('lookbook.next'))}">${icons.right(18)}</button>
              </div>
            </div>
            <div class="lb-rail">${refs.map(pcard).join('')}</div>
          </div>` : ''}
      </section>`;
  }

  // ── Índice de la apertura (solo sobre papel) ────────────────────────────────
  // Orden del DOM = orden de LECTURA (cabecera, h1 = la palabra, entradas, pie); el CSS lo
  // recoloca con grid-template-areas para que la palabra quede la última y cortada por el
  // filete. Cada entrada enlaza con href="#id" por semántica, pero el salto lo hace JS
  // (bindIndex): un cambio de hash dispara popstate y el router re-resolvería la ruta
  // (repinta la vista y vuelve arriba).
  function indexBlock(cover, list) {
    const norm = s => String(s || '').replace(/[.\s]+$/, '').trim().toLowerCase();
    const tagline = String(getTokens().tagline || '').trim();
    const lookbookWord = t('nav.lookbook');
    const word = String(cover.title || '').trim() || lookbookWord;
    // El claim (token tagline) va a la derecha de la cabecera salvo que ya esté en la
    // palabra o en la línea de estado: nada se dice dos veces en la misma pantalla.
    const showTag = tagline && norm(word) !== norm(tagline) && !norm(cover.subtitle).includes(norm(tagline));
    const ctaHref = cover.ctaHref || href('catalog/catalog');
    const ctaText = cover.ctaText || t('lookbook.toCatalog');
    // El h1 accesible dice «Lookbook SS27», no solo la cifra: el prefijo va solo para lectores
    // (sr-only) y se omite si la palabra ya es «Lookbook». Los «01…06» visibles van aria-hidden:
    // el <ol> ya numera para el lector de pantalla.
    return `
      <section class="lb-index" aria-labelledby="lbIndexWord">
        <div class="lb-ix-head">
          <p class="lb-ix-kicker">${esc(cover.kicker || t('lookbook.indexLabel'))}</p>
          ${showTag ? `<p class="lb-ix-tag">${esc(tagline)}</p>` : ''}
        </div>
        <div class="lb-ix-wordbox"><h1 class="lb-ix-word" id="lbIndexWord">${
          norm(word) === norm(lookbookWord) ? '' : `<span class="sr-only">${esc(lookbookWord)} </span>`
        }<span class="lb-ix-glyphs">${esc(word)}</span></h1></div>
        ${list.length ? `
        <nav class="lb-ix-nav" aria-label="${esc(t('lookbook.indexLabel'))}">
          <ol class="lb-ix-list">
            ${list.map((story, i) => `
              <li><a href="#${storyId(i + 1)}" data-jump="${storyId(i + 1)}">
                <span class="lb-ix-num" aria-hidden="true">${String(i + 1).padStart(2, '0')}</span>
                <span class="lb-ix-name">${esc(story.kicker || story.title || '')}</span>
                ${story.kicker && story.title ? `<span class="lb-ix-title">${esc(story.title)}</span>` : ''}
              </a></li>`).join('')}
          </ol>
        </nav>` : ''}
        <div class="lb-ix-foot">
          ${cover.subtitle ? `<p class="lb-ix-sub">${esc(cover.subtitle)}</p>` : ''}
          <a class="lb-ix-cta" href="${esc(ctaHref)}" data-to-catalog>${esc(ctaText)} ${icons.right(14)}</a>
        </div>
      </section>`;
  }

  // Salto a la historia sin tocar la URL (ver indexBlock). Respeta «reducir movimiento» y
  // lleva el foco al título de la historia (teclado y lectores), no a toda la sección.
  // Se enlaza antes de pintar las historias: da igual, el querySelector se hace en el clic.
  function bindIndex() {
    const behavior = matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth';
    heroHost.querySelectorAll('[data-jump]').forEach(link => {
      link.addEventListener('click', event => {
        const target = body.querySelector(`#${link.dataset.jump}`);
        if (!target) return;
        event.preventDefault();
        target.scrollIntoView({ behavior, block: 'start' });
        target.querySelector('h2')?.focus({ preventScroll: true });
      });
    });
    fitWord(heroHost.querySelector('.lb-ix-word'));
  }

  // ── Tarjeta de producto (reutiliza .pcard del catálogo) + preselección ──────
  function pcard(item) {
    const price = main(item);
    const on = state.isPreselected(item.modelId);
    return `
      <article class="pcard lb-pcard" data-model="${esc(item.modelId)}">
        <div class="pcard-media">
          ${item.imageUri
            ? `<img src="${esc(item.imageUri)}" alt="" loading="lazy" decoding="async">`
            : `<span class="item-art" aria-hidden="true">${icons.shoe(52)}</span>`}
          ${(item.images && item.images.length > 1)
            ? `<button type="button" class="pcard-360" data-spin="${esc(item.modelId)}"
                 title="${esc(t('viewer.spinRole'))}" aria-label="${esc(t('viewer.spinRole'))}">${icons.spin(13)} ${esc(t('viewer.badge'))}</button>` : ''}
          <button type="button" class="item-fav pcard-fav" data-fav="${esc(item.modelId)}"
            aria-pressed="${item.favorite ? 'true' : 'false'}"
            aria-label="${esc(favLabel(item.favorite))}" title="${esc(favLabel(item.favorite))}">
            ${item.favorite ? icons.heartOn(22) : icons.heart(22)}
          </button>
        </div>
        <div class="pcard-body">
          <h3 class="pcard-name"><a class="pcard-link" href="${esc(productHref(item))}">${esc(item.name)}</a></h3>
          <p class="pcard-ref">${esc(t('catalog.reference'))} <b>${esc(item.reference || '')}</b></p>
          ${price ? `<p class="pcard-price"><span>${esc(price.label)}</span> <b>${esc(eur(price.value))}</b></p>` : ''}
          <button type="button" class="lb-add${on ? ' on' : ''}" data-add="${esc(item.modelId)}">
            ${on ? `${icons.check(15)} ${esc(t('lookbook.inSelection'))}` : `${icons.plus(15)} ${esc(t('lookbook.add'))}`}
          </button>
        </div>
      </article>`;
  }

  // ── Interacción de producto ─────────────────────────────────────────────────
  function bindProducts() {
    // Favorito (reutiliza el endpoint del catálogo, toggle optimista con rollback)
    body.querySelectorAll('[data-fav]').forEach(button => {
      button.onclick = async () => {
        const modelId = button.dataset.fav;
        const on = button.getAttribute('aria-pressed') !== 'true';
        paintFav(button, on);
        const item = byId.get(String(modelId)); if (item) item.favorite = on;
        try {
          await (on ? api.put(`/api/portal/favorites/${encodeURIComponent(modelId)}`)
                    : api.del(`/api/portal/favorites/${encodeURIComponent(modelId)}`));
        } catch {
          paintFav(button, !on);   // revierte si falla la red
          if (item) item.favorite = !on;
        }
      };
    });

    // Badge 360°: abre el visor multi-ángulo en un quick-view sin salir del lookbook
    body.querySelectorAll('[data-spin]').forEach(button => {
      button.onclick = event => {
        event.preventDefault();
        const item = byId.get(String(button.dataset.spin));
        if (item?.images?.length) openViewerModal(item.images, item.name || '');
      };
    });

    // Añadir = preselección (sin tallas)
    body.querySelectorAll('[data-add]').forEach(button => {
      button.onclick = () => {
        const modelId = button.dataset.add;
        const item = byId.get(String(modelId));
        if (!item) return;
        if (state.isPreselected(modelId)) state.unpreselect(modelId);
        else state.preselect(item);
        paintAdd(button, state.isPreselected(modelId));
        renderSelBar();
      };
    });
  }

  // Flechas de "Compra el look": el paso es un número ENTERO de tarjetas (las que
  // caben a la vista), así ninguna queda cortada tras el clic.
  function bindRails() {
    body.querySelectorAll('.lb-shop').forEach(shop => bindRail({
      host: shop,
      rail: shop.querySelector('.lb-rail'),
      prev: shop.querySelector('.lb-prev'),
      next: shop.querySelector('.lb-next'),
      nav: shop.querySelector('.lb-shop-nav'),
      step: rail => {
        const card = rail.querySelector('.pcard');
        if (!card) return 0;
        const pitch = card.getBoundingClientRect().width + (parseFloat(getComputedStyle(rail).columnGap) || 0);
        return Math.max(1, Math.floor(rail.clientWidth / pitch)) * pitch;
      }
    }));
  }

  function paintFav(button, on) {
    button.setAttribute('aria-pressed', on ? 'true' : 'false');
    button.setAttribute('aria-label', favLabel(on));
    button.setAttribute('title', favLabel(on));
    button.innerHTML = on ? icons.heartOn(22) : icons.heart(22);
  }

  function paintAdd(button, on) {
    button.classList.toggle('on', on);
    button.innerHTML = on
      ? `${icons.check(15)} ${esc(t('lookbook.inSelection'))}`
      : `${icons.plus(15)} ${esc(t('lookbook.add'))}`;
  }

  // ── Barra flotante "Tu selección" ───────────────────────────────────────────
  function renderSelBar() {
    const items = state.preselections();
    if (!items.length) { selBar.innerHTML = ''; return; }
    selBar.innerHTML = `
      <div class="lb-selbar">
        <button type="button" class="lb-selbar-toggle" id="lbSelToggle" aria-expanded="false">
          ${icons.card(18)} ${esc(t('lookbook.yourSelection', { n: items.length }))}
        </button>
        <div class="lb-selpanel" id="lbSelPanel" hidden>
          <div class="lb-selpanel-head">
            <b>${esc(t('lookbook.yourSelection', { n: items.length }))}</b>
            <button type="button" class="ms-link" id="lbSelClear">${esc(t('lookbook.clear'))}</button>
          </div>
          <ul class="lb-sellist">
            ${items.map(p => `
              <li data-sel="${esc(p.modelId)}">
                <span class="lb-selthumb">${p.imageUri ? `<img src="${esc(p.imageUri)}" alt="">` : ''}</span>
                <span class="lb-selinfo"><b>${esc(p.name || '')}</b><span>${esc(p.reference || '')}</span></span>
                <span class="lb-selacts">
                  <button type="button" class="btn-primary lb-sizes" data-sizes="${esc(p.modelId)}">${esc(t('lookbook.putSizes'))}</button>
                  <button type="button" class="lb-selremove" data-remove="${esc(p.modelId)}" aria-label="${esc(t('lookbook.remove'))}">${icons.close(14)}</button>
                </span>
              </li>`).join('')}
          </ul>
          <p class="lb-selhint">${esc(t('lookbook.selectionHint'))}</p>
          <button type="button" class="btn-primary lb-selpdf" id="lbSelPdf">
            ${icons.fileDown(15)} ${esc(t('lookbook.downloadPdf'))}</button>
        </div>
      </div>`;

    const toggle = selBar.querySelector('#lbSelToggle');
    const panel = selBar.querySelector('#lbSelPanel');
    toggle.onclick = () => {
      const openNow = panel.hidden;
      panel.hidden = !openNow;
      toggle.setAttribute('aria-expanded', String(openNow));
    };
    selBar.querySelector('#lbSelClear').onclick = () => {
      for (const p of state.preselections()) state.unpreselect(p.modelId);
      refreshAddButtons();
      renderSelBar();
    };
    selBar.querySelectorAll('[data-remove]').forEach(b => {
      b.onclick = () => { state.unpreselect(b.dataset.remove); refreshAddButtons(); renderSelBar(); };
    });
    // Line-sheet PDF: catálogo comercial de la selección, con la tarifa del cliente
    selBar.querySelector('#lbSelPdf').onclick = async event => {
      const button = event.currentTarget;
      const refs = state.preselections().map(p => p.reference).filter(Boolean).join(',');
      if (!refs) return;
      button.disabled = true;
      try {
        await api.download(`/api/portal/line-sheet.pdf?refs=${encodeURIComponent(refs)}&locale=${lang()}`, 'line-sheet.pdf');
      } catch { /* api.download gestiona errores */ }
      button.disabled = false;
    };
    // "Poner tallas" lleva a la ficha del producto, donde vive la matriz de tallas
    selBar.querySelectorAll('[data-sizes]').forEach(b => {
      b.onclick = () => {
        const item = byId.get(String(b.dataset.sizes));
        if (item) go(productHref(item));   // ficha del producto (con su matriz de tallas)
      };
    });
  }

  function refreshAddButtons() {
    body.querySelectorAll('[data-add]').forEach(b => paintAdd(b, state.isPreselected(b.dataset.add)));
  }

  async function loadCatalog(ids) {
    if (!ids.length) return [];
    try {
      const data = await api.get(`/api/shop/catalog?locale=${encodeURIComponent(lang())}`
        + `&ids=${encodeURIComponent(ids.join(','))}`);
      windows = data?.windows || [];
      return data?.items || data?.models || [];
    } catch { return []; }
  }
}

// La palabra gigante del índice llena EXACTAMENTE la columna, sea «SS27» o «Lookbook»: se
// mide su anchura natural a 100px y se escala (tope 22rem; suelo 2.25rem, con el que un título
// de 13 caracteres como «Own your code» aún cabe entero a 390px; por debajo el CSS la recorta
// por la derecha antes que romper la línea). El .99 deja un pelo de margen para que
// el último glifo no roce el recorte. Se repite al cargar la webfont (Archivo llega después
// del primer pintado y es más ancha que el respaldo) y al cambiar el ancho; el listener se
// retira solo cuando el router sustituye la vista (nodo desconectado). A nivel de módulo,
// como content(): no depende de nada de la vista.
function fitWord(word) {
  const glyphs = word?.querySelector('.lb-ix-glyphs');
  if (!glyphs) return;
  const rem = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
  let raf = 0;
  const fit = () => {
    if (!word.isConnected) { removeEventListener('resize', onResize); return; }
    word.style.fontSize = '100px';
    const natural = glyphs.getBoundingClientRect().width || 1;
    const size = Math.min(22 * rem, Math.max(2.25 * rem, 100 * word.clientWidth / natural * .99));
    word.style.fontSize = `${size}px`;
  };
  const onResize = () => { cancelAnimationFrame(raf); raf = requestAnimationFrame(fit); };
  addEventListener('resize', onResize);
  fit();
  document.fonts?.ready.then(fit);
}
