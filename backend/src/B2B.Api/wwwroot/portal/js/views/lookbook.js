// Lookbook "Colecciones lejan" — /{market}/{lang}/lookbook.
// Entorno editorial de marca (barefoot) donde cada historia termina en un raíl
// "Compra el look" con productos reales del catálogo. Cada producto se puede marcar
// FAVORITO (♥, reutiliza el mecanismo del catálogo) o AÑADIR como PRESELECCIÓN (sin
// tallas); las cantidades se ponen después en la ficha, como pide el flujo.
//
// Reutiliza: contenido del CMS (portal_content lookbook.*), el carrusel de la portada,
// la tarjeta .pcard del catálogo, favoritos y la preselección de state.js. No inventa
// embudo: alimenta favoritos → (preselección) → carrito → pedido.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state } from '../state.js';
import { go, href } from '../router.js';
import { carousel } from '../ui/carousel.js';
import { icons } from '../ui/icons.js';
import { openViewerModal } from '../ui/viewer.js';

const preferred = () => (state.me?.prefs?.showPrices === 'pvp' ? 'pvp' : 'pvd');
const priceOf = (item, kind) =>
  item?.[kind] == null ? null : { label: t(`catalog.price.${kind}`), value: item[kind] };
const main = item => priceOf(item, preferred()) ?? priceOf(item, preferred() === 'pvd' ? 'pvp' : 'pvd');
const productHref = item => `${href('product')}/${encodeURIComponent(item.reference || item.modelId)}`;
const favLabel = on => t(on ? 'catalog.favoriteOff' : 'catalog.favorite');

async function content(key) {
  try {
    const data = await api.get(`/api/portal/content/${key}?locale=${encodeURIComponent(lang())}`);
    return Array.isArray(data) ? data : (data?.items || []);
  } catch { return []; }
}

export default async function lookbook(host) {
  host.innerHTML = `
    <div class="page lookbook">
      <div class="lb-hero" id="lbHero"><div class="hero-skeleton"></div></div>
      <div id="lbBody" aria-live="polite"><div class="skeleton"></div></div>
    </div>
    <div id="lbSelBar"></div>`;

  const heroHost = host.querySelector('#lbHero');
  const body = host.querySelector('#lbBody');
  const selBar = host.querySelector('#lbSelBar');

  const [hero, stories, catalog] = await Promise.all([
    content('lookbook.hero'), content('lookbook.stories'), loadCatalog()
  ]);
  const byId = new Map(catalog.map(p => [String(p.modelId), p]));

  // Limpia de la preselección lo que ya se ha tallado (tiene líneas en el carrito)
  const carted = new Set(state.cartLines().map(l => String(l.modelId)));
  for (const p of state.preselections()) if (carted.has(String(p.modelId))) state.unpreselect(p.modelId);

  // ── Portada (carrusel reutilizado) ─────────────────────────────────────────
  if (hero.length) {
    heroHost.innerHTML = '';
    carousel(heroHost, hero, { label: t('lookbook.heroLabel') });
  } else {
    heroHost.innerHTML = `<div class="lb-hero-fallback"><h1>${esc(t('lookbook.title'))}</h1></div>`;
  }

  // ── Historias ───────────────────────────────────────────────────────────────
  if (!stories.length) {
    body.innerHTML = `<div class="page"><p class="lb-empty">${esc(t('lookbook.empty'))}</p></div>`;
    return;
  }
  body.innerHTML = `<div class="lb-stories">${stories.map(story => storyBlock(story, byId)).join('')}
    <div class="lb-close">
      <p>${esc(t('lookbook.closeLead'))}</p>
      <a class="btn-primary" href="${href('catalog/catalog')}">${esc(t('lookbook.toCatalog'))} ${icons.right(15)}</a>
    </div></div>`;

  bindProducts();
  renderSelBar();

  // ── Bloques de historia ───────────────────────────────────────────────────
  function storyBlock(story, map) {
    const refs = (story.refs || []).map(id => map.get(String(id))).filter(Boolean);
    const side = story.layout === 'left' ? 'lb-left' : 'lb-right';
    const accent = /^#[0-9a-fA-F]{3,8}$/.test(story.accent || '') ? story.accent : 'var(--accent)';
    // Si el CMS no trae imagen editorial, no dejamos una caja vacía: caemos a la
    // foto del primer producto del raíl (evita el efecto "imagen rota").
    const media = story.imageUrl || (refs[0] && refs[0].imageUri) || '';
    return `
      <section class="lb-story ${side}" style="--lb-accent:${esc(accent)}">
        <div class="lb-story-media">
          ${media ? `<img src="${esc(media)}" alt="${esc(story.alt || story.title || '')}" loading="lazy" decoding="async">` : ''}
        </div>
        <div class="lb-story-text">
          ${story.kicker ? `<span class="lb-kicker">${esc(story.kicker)}</span>` : ''}
          <h2>${esc(story.title || '')}</h2>
          ${story.body ? `<p>${esc(story.body)}</p>` : ''}
        </div>
        ${refs.length ? `
          <div class="lb-shop">
            <h3 class="lb-shop-title">${esc(t('lookbook.shopTheLook'))}</h3>
            <div class="lb-rail">${refs.map(pcard).join('')}</div>
          </div>` : ''}
      </section>`;
  }

  // Tarjeta de producto (reutiliza .pcard del catálogo) + preselección
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
                 title="${esc(t('viewer.spinRole'))}" aria-label="${esc(t('viewer.spinRole'))}">↻ 360°</button>` : ''}
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

  async function loadCatalog() {
    try {
      const data = await api.get(`/api/shop/catalog?take=200&locale=${encodeURIComponent(lang())}`);
      return data?.items || data?.models || [];
    } catch { return []; }
  }
}
