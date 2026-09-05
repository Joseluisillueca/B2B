// Ficha de producto — /{market}/{lang}/product/{referencia} (estilo thehoffbrand.com:
// foto protagonista a la izquierda, ficha aireada a la derecha). El pedido se hace
// aquí igual que en el catálogo: la MISMA matriz de tallas mete cantidades en el
// carrito de la ventana de servicio activa. Se reutilizan sizeMatrix/bindMatrix.

import { api } from '../api.js';
import { t, lang } from '../i18n.js';
import { esc, eur } from '../format.js';
import { state } from '../state.js';
import { href } from '../router.js';
import { icons } from '../ui/icons.js';
import { sizeMatrix, bindMatrix, rowState } from '../ui/size-matrix.js';
import { createViewer } from '../ui/viewer.js';
import { fetchRelated, relatedSectionHtml, bindRelatedRail } from '../ui/related.js';

// Precio de la ficha: una sola línea, la de la preferencia MOSTRAR PRECIOS del perfil
// (PVD por defecto). Si el artículo no trae ese precio se enseña el otro (mismo criterio
// que el listado, m7).
const preferred = () => (state.me?.prefs?.showPrices === 'pvp' ? 'pvp' : 'pvd');
const priceOf = (item, kind) =>
  item?.[kind] == null ? null : { label: t(`catalog.price.${kind}`), value: item[kind] };
const mainPrice = item =>
  priceOf(item, preferred()) ?? priceOf(item, preferred() === 'pvd' ? 'pvp' : 'pvd');

// Vocabulario traducido de los atributos (mismo criterio que el catálogo): la clave
// estable se busca en el diccionario; si no está, se cae en la etiqueta del servidor.
const slugKey = value => String(value ?? '').normalize('NFD')
  .replace(/[̀-ͯ]/g, '').toLowerCase()
  .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

const vocab = (prefix, slug, label, raw) => {
  const key = slugKey(slug || raw);
  if (key) {
    const translated = t(`catalog.${prefix}.${key}`);
    if (translated !== `catalog.${prefix}.${key}`) return translated;
  }
  return label || raw || '';
};

// ¿El valor ya está escrito en el nombre del artículo? "ELAN Aegean" lleva LINE=ELAN y
// COLOR NAME=Aegean: repetirlos en chips no es información. Palabras normalizadas y
// límite de palabra (misma regla que el listado).
const inName = (name, value) => {
  const needle = slugKey(value);
  return needle.length > 1 && `-${slugKey(name)}-`.includes(`-${needle}-`);
};

// La ficha muestra los atributos DESCRIPTIVOS (silueta, colección, corte, forro,
// suela…) con su etiqueta a la vista: el comprador tiene que distinguir corte de forro
// sin pasar el ratón por el chip. Fuera quedan los CÓDIGOS del ERP —el style code
// identifica, así que va en la línea de referencia; el color code no dice nada— y los
// valores que el nombre ya dice. Devuelve { chips, styleCode }.
const attrsOf = item => {
  const list = Array.isArray(item.attributeList) && item.attributeList.length
    ? item.attributeList.map(entry => ({
        slug: slugKey(entry.keySlug || entry.key),
        raw: entry.value,
        label: vocab('attr', entry.keySlug, entry.label, entry.key),
        value: vocab('attrValue', entry.valueSlug, entry.valueLabel, entry.value)
      }))
    : Object.entries(item.attributes || {}).map(([key, value]) => ({
        slug: slugKey(key),
        raw: value,
        label: vocab('attr', key, '', key),
        value: vocab('attrValue', value, '', value)
      }));
  return {
    styleCode: String(list.find(entry => entry.slug === 'style-code')?.raw ?? '').trim(),
    chips: list.filter(entry => !/-code$/.test(entry.slug) && !inName(item.name, entry.raw))
  };
};

const favLabel = on => t(on ? 'catalog.favoriteOff' : 'catalog.favorite');

export default async function product(host, route) {
  const reference = decodeURIComponent(route?.param || '').trim();

  host.innerHTML = `
    <div class="page product">
      <div class="product-grid">
        <div class="skeleton product-skeleton"></div>
        <div><div class="skeleton short"></div><div class="skeleton short"></div></div>
      </div>
    </div>`;

  // Ventana de servicio activa: el tipo lo elige la portada, el id lo trae el catálogo.
  // Como el listado, la primera llamada aún no conoce el id, así que se repite una vez
  // si el servidor resolvió una ventana distinta a la activa.
  let windows = [];
  const windowId = () => {
    const type = state.prefs.window === 'scheduled' ? 'SCHEDULED' : 'REPLENISHMENT';
    return (windows.find(w => w.orderType === type) || windows[0])?.id || '';
  };
  async function loadData() {
    const params = new URLSearchParams();
    params.set('q', reference);
    params.set('take', '50');
    params.set('window', windowId());
    params.set('locale', lang());
    const data = await api.get(`/api/shop/catalog?${params}`);
    const knew = windows.length > 0;
    windows = data.windows || [];
    // Si la instancia no publica el tipo preferido, la preferencia se realinea al que
    // existe (la misma regla que catálogo y chrome): así carrito, header y CTA
    // coinciden con la ventana cuyo stock y precio se están mostrando.
    state.alignWindow(windows);
    if (!knew && windowId() && data.window !== windowId()) return loadData();
    return data;
  }

  let data;
  try {
    data = await loadData();
  } catch {
    host.querySelector('.product-grid').outerHTML = `
      <div class="panel">
        <b>${esc(t('product.errorTitle'))}</b>${esc(t('product.errorBody'))}
        <div><a class="cta" href="${href('catalog/catalog')}">${esc(t('product.backToCatalog'))}</a></div>
      </div>`;
    return;
  }

  const items = data.items || [];
  const item = items.find(entry => (entry.reference || '') === reference) || items[0];

  if (!item) {
    host.innerHTML = `
      <div class="page product">
        <nav class="crumbs" aria-label="${esc(t('nav.catalog'))}">
          <a href="${href('dashboard')}">${esc(t('nav.home'))}</a>
          <span class="crumb"> / <a href="${href('catalog/catalog')}">${esc(t('nav.catalog'))}</a></span>
        </nav>
        <div class="panel">
          <b>${esc(t('product.notFoundTitle'))}</b>${esc(t('product.notFoundBody'))}
          <div><a class="cta" href="${href('catalog/catalog')}">${esc(t('product.backToCatalog'))}</a></div>
        </div>
      </div>`;
    return;
  }

  const { chips: attributes, styleCode } = attrsOf(item);
  const price = mainPrice(item);
  const available = rowState(item, data.window) !== 'out';
  const windowType = state.prefs.window === 'scheduled' ? 'scheduled' : 'replenishment';
  // Nombre propio de la ventana con la que se está comprando ("Programación SS27"):
  // lo trae el catálogo; si no lo hay, el genérico del diccionario.
  const windowName = windows.find(w => w.id === data.window)?.name || t(`window.${windowType}`);
  const lines = Object.fromEntries(
    state.cartLines().map(line => [`${line.modelId}|${line.size}`, line]));

  host.innerHTML = `
    <div class="page product">
      <nav class="crumbs" aria-label="${esc(t('nav.catalog'))}">
        <a href="${href('dashboard')}">${esc(t('nav.home'))}</a>
        <span class="crumb"> / <a href="${href('catalog/catalog')}">${esc(t('nav.catalog'))}</a></span>
        <span class="crumb"> / <span>${esc(item.name || reference)}</span></span>
      </nav>

      <div class="product-grid">
        <figure class="product-media" id="media"></figure>

        <div class="product-panel">
          <div class="product-head">
            <h1 class="product-name">${esc(item.name || reference)}</h1>
            <button type="button" class="item-fav product-fav" id="fav"
              aria-pressed="${item.favorite ? 'true' : 'false'}"
              aria-label="${esc(favLabel(item.favorite))}" title="${esc(favLabel(item.favorite))}">
              ${item.favorite ? icons.heartOn(24) : icons.heart(24)}
            </button>
          </div>

          <!-- "Referencia: 1040 · R1ABY150A3C08": el style code del ERP identifica, así que
               acompaña a la referencia en vez de flotar como chip sin etiqueta. -->
          <p class="product-ref">${esc(t('catalog.reference'))} <b>${esc(item.reference || '')}</b>${
            styleCode && styleCode !== item.reference ? ` · <span>${esc(styleCode)}</span>` : ''}</p>
          <!-- La ventana con la que se compra, visible donde se compra: hasta ahora solo
               la contaban el tile de la portada y el botón del carrito. -->
          <p class="product-ref product-window" style="margin-top:.25rem">${esc(t('product.orderWindow'))} <b>${esc(windowName)}</b></p>

          <!-- Slot RESERVADO del "También en:" — existe desde el primer render con la
               altura de la fila (esqueleto), así los relacionados llegan y se rellenan
               DENTRO sin empujar el precio ni la matriz (con red lenta el precio caía
               157px). Sin gama, el slot se pliega con una transición suave. -->
          <div class="product-alsoin-slot" data-pending>
            <div class="alsoin-skel" aria-hidden="true">
              <span class="alsoin-skel-bar"></span>
              <span class="alsoin-skel-pics">
                <span class="alsoin-skel-pic"></span>
                <span class="alsoin-skel-pic"></span>
              </span>
            </div>
          </div>

          ${attributes.length ? `<div class="product-attrs">${attributes.map(attribute => `
            <span class="tag"><small style="font-weight:500;letter-spacing:.04em;color:var(--ink-2);margin-right:.45em">${esc(attribute.label)}</small>${esc(attribute.value)}</span>`).join('')}</div>` : ''}

          <div class="product-price">
            ${item.pvd != null ? `<div class="pp-col"><span>${esc(t('catalog.price.pvd'))}</span>
              <b>${esc(eur(item.pvd))}</b></div>` : ''}
            ${item.pvp != null ? `<div class="pp-col"><span>${esc(t('product.pvpRec'))}</span>
              <b>${esc(eur(item.pvp))}</b></div>` : ''}
          </div>

          <div class="product-buy">
            <!-- DISPONIBLE es el estado de COMPRA, no un atributo del modelo: va en la
                 cabecera de la matriz, donde se elige la cantidad, y no mezclado con
                 corte, forro y suela. El rótulo conserva su clase (rasgos y filete) y el
                 h2 hereda la fuente. El chip va SOLO con sus clases: .tag-avail vive en
                 app.css con el grosor de filete de la marca (--rule-w); el estilo en
                 línea que llevaba fijaba 2px y se saltaba ese token. -->
            <div class="product-sizes-h product-sizes-head" style="display:flex;align-items:center;justify-content:space-between;gap:.8rem">
              <h2 style="font:inherit;letter-spacing:inherit;margin:0">${esc(t('product.sizesTitle'))}</h2>
              ${available ? `<span class="tag tag-avail">${esc(t('product.available'))}</span>` : ''}
            </div>
            <div id="buy">${sizeMatrix(item, { windowKey: data.window, lines })}</div>
            <!-- Sin ninguna talla pedible en esta ventana el botón rojo no lleva a ninguna
                 parte (su handler busca una celda habilitada que no existe): se apaga y
                 dice CONSULTAR; la acción real es el enlace "Consultar" de la matriz,
                 convertido abajo en botón fantasma a lo ancho.
                 Con stock, el botón NO añade nada (el pedido se hace al teclear en la
                 matriz): decía «AÑADIR A REPOSICIÓN» y el comprador creía añadir dos veces
                 o ninguna. Ahora dice lo que hace su clic y paintTotal() lo mantiene por
                 estado: sin unidades, fantasma y «Pon cantidades por talla»; con unidades,
                 primario y «Ver en el pedido» con pares e importe. Arranca en fantasma. -->
            <div class="product-order">
              <button type="button" class="${available ? 'btn-ghost block' : 'btn-primary'}" id="add" ${available ? '' : 'disabled'}>
                <span id="add-label">${esc(available ? t('product.startSizes') : t('catalog.availability.consult'))}</span>
                <span class="product-total" id="total"></span>
              </button>
            </div>
          </div>

          <!-- Solo la ficha técnica: el botón "Descargar galería" volverá con su endpoint;
               un "Disponible próximamente" apagado en producción no es una función. -->
          <div class="product-docs">
            <button type="button" class="product-doc" id="dl-tech" title="${esc(t('product.downloadTech'))}">
              ${icons.fileDown(17)} ${esc(t('product.downloadTech'))}
            </button>
          </div>
        </div>
      </div>
    </div>`;

  // ── "Completa la gama": cross/up-selling que fija BC, cargado en paralelo ──
  // La ficha ya está pintada; los relacionados llegan después y la sección aparece
  // con una transición suave. La fila "También en:" se rellena DENTRO de su slot
  // reservado (cero salto de layout); sin gama (o con error) el slot se pliega.
  (async () => {
    // El centinela se captura ANTES del await: si el usuario navega a OTRA ficha
    // mientras el fetch está en vuelo, este nodo concreto ya no está conectado y
    // se aborta (evita inyectar los relacionados de A en la ficha de B).
    const page = host.querySelector('.page.product');
    const slot = page?.querySelector('.product-alsoin-slot');
    const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Plegar el hueco reservado: transición de altura suave (~.25s); con "reducir
    // movimiento" desaparece al instante. El nodo se retira al terminar.
    const collapseSlot = () => {
      if (!slot || !slot.isConnected) return;
      if (reduced()) { slot.remove(); return; }
      slot.style.height = `${slot.offsetHeight}px`;
      void slot.offsetHeight;   // reflow: la transición parte de la altura real
      slot.classList.add('alsoin-collapse');
      setTimeout(() => slot.remove(), 320);
    };

    let related = [];
    try { related = await fetchRelated([item.modelId], data.window); }
    catch { related = []; }
    if (!page || !page.isConnected) return;

    // ── "También en:": la gama, visible donde se decide ──
    // Patrón de swatches de PDP: el color del PROPIO artículo abre la fila (marcado,
    // no clicable: "estás viendo BLACK") seguido de los HERMANOS de gama (solo cross;
    // los artículos de la colección se quedan en el raíl). Máximo 6 swatches en
    // escritorio, 5 en móvil, y "+N" que lleva con scroll suave al raíl completo.
    const cross = related.filter(entry => entry.relation === 'cross');

    // Pie de color. Primero el ATRIBUTO de color del artículo, si el ERP lo manda
    // ("COLOR", "COLOR NAME", "COLOUR"...): es el dato de verdad y no depende de cómo
    // esté escrito el nombre. Si no hay atributo, la convención "MODELO — COLOR" (raya
    // em — o en –, con espacios); sin ninguna de las dos no hay pie. Un ERP que nombra
    // "BUND RETRO Field Yellow", sin raya, dejaba la carta de colores sin nombres.
    const colorAttribute = attributes => {
      const entries = Object.entries(attributes || {});
      const hit = entries.find(([key]) => /^colou?r(\s|_|-)?(name)?$/i.test(String(key).trim()))
        || entries.find(([key]) => /colou?r/i.test(String(key)) && !/code/i.test(String(key)));
      const value = hit ? hit[1] : '';
      return Array.isArray(value) ? String(value[0] || '') : String(value || '');
    };
    const colorOf = (name, attributes) => {
      const fromAttribute = colorAttribute(attributes).trim();
      if (fromAttribute) return fromAttribute;
      const parts = String(name || '').split(/\s[—–]\s/);
      return parts.length > 1 ? parts.pop().trim() : '';
    };
    // Miniatura ligera: un marco de 116px no necesita el JPEG de 1400px del visor
    const thumbSrc = uri => (uri || '').replace(/([?&]width=)\d+/, '$1300');
    const pic = uri => uri
      ? `<img src="${esc(thumbSrc(uri))}" alt="" loading="lazy" decoding="async">`
      : `<span class="alsoin-art" aria-hidden="true">${icons.shoe(32)}</span>`;

    const swatch = ({ card }) => {
      const color = colorOf(card.name, card.attributes);
      // Sin stock en ninguna talla (availability solo "consult"): foto atenuada + AGOTADO
      const out = (card.availability || []).includes('consult');
      // El PVD del hermano SOLO cuando difiere del artículo abierto (igual es ruido)
      const price = !out && card.pvd != null && card.pvd !== item.pvd ? eur(card.pvd) : '';
      const label = [card.name, card.reference && `${t('catalog.reference')} ${card.reference}`,
        out ? t('related.out') : ''].filter(Boolean).join(' — ');
      const url = `${href('product')}/${encodeURIComponent(card.reference || card.modelId)}`;
      return `
        <a class="alsoin-thumb${out ? ' is-out' : ''}" href="${esc(url)}"
          title="${esc(label)}" aria-label="${esc(label)}">
          <span class="alsoin-pic">${pic(card.imageUri)}</span>
          ${color ? `<span class="alsoin-color" aria-hidden="true">${esc(color)}</span>` : ''}
          ${out ? `<span class="alsoin-sub alsoin-outlbl" aria-hidden="true">${esc(t('related.out'))}</span>`
            : price ? `<span class="alsoin-sub" aria-hidden="true">${esc(price)}</span>` : ''}
        </a>`;
    };

    // Color actual primero; si el nombre no lleva raya, la fila queda como antes
    const ownColor = cross.length ? colorOf(item.name, item.attributes) : '';
    const current = ownColor ? `
      <span class="alsoin-thumb alsoin-current" aria-current="true" title="${esc(item.name || '')}">
        <span class="alsoin-pic">${pic(item.imageUri)}</span>
        <span class="alsoin-color">${esc(ownColor)}</span>
      </span>` : '';

    // Tope de la fila (el swatch actual cuenta): 6 escritorio / 5 móvil. Se calcula
    // ANTES del raíl porque decide qué hermanos quedan para él.
    const max = matchMedia('(max-width:48rem)').matches ? 5 : 6;
    const shown = Math.max(1, max - (current ? 1 : 0));

    // ── "Completa la gama": el RESTO, no toda la lista ──
    // Los hermanos que la fila de arriba ya enseña no se repiten en el raíl (antes
    // arrancaba por los mismos cuatro colores del "También en:"): quedan los de más allá
    // del tope —a los que lleva el "+N"— y los artículos de la colección. Sin resto, sin
    // sección: ni hueco ni título.
    const rest = [...cross.slice(shown), ...related.filter(entry => entry.relation !== 'cross')];
    let section = null;
    if (rest.length) {
      page.insertAdjacentHTML('beforeend', relatedSectionHtml(rest, {
        title: t('related.title'),
        sub: t('related.sub')
      }));
      section = page.querySelector('.related');
      bindRelatedRail(section);
    }

    if (cross.length && slot) {
      slot.innerHTML = `
        <div class="product-alsoin" role="group" aria-labelledby="alsoin-label">
          <span class="alsoin-label" id="alsoin-label">${esc(t('related.alsoIn'))}</span>
          ${current}
          ${cross.slice(0, shown).map(swatch).join('')}
          ${cross.length > shown ? `
            <button type="button" class="alsoin-more" title="${esc(t('related.next'))}"
              aria-label="${esc(t('related.next'))}">+${cross.length - shown}</button>` : ''}
        </div>`;
      slot.removeAttribute('data-pending');
      // "+N": el resto de la gama vive en el raíl "Completa la gama" de abajo
      slot.querySelector('.alsoin-more')?.addEventListener('click', () => {
        section?.scrollIntoView({ behavior: reduced() ? 'auto' : 'smooth', block: 'start' });
      });
    } else {
      collapseSlot();
    }

    const alsoIn = page.querySelector('.product-alsoin');
    if (section) {
      void section.offsetWidth;   // reflow: la transición de entrada arranca desde el estado inicial
      section.classList.add('on');
    } else {
      void alsoIn?.offsetWidth;
    }
    alsoIn?.classList.add('on');
  })();

  // ── Visor multi-ángulo (giro 360 + zoom); degrada a la portada o a placeholder ──
  createViewer(host.querySelector('#media'),
    item.images?.length ? item.images : (item.imageUri ? [item.imageUri] : []),
    { name: item.name || reference });

  // Descargar ficha técnica: PDF con marca y la TARIFA del cliente (baja con el token)
  host.querySelector('#dl-tech').onclick = async event => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await api.download(
        `/api/portal/product/${encodeURIComponent(item.reference || reference)}/tech-sheet.pdf?locale=${lang()}`,
        `ficha-${item.reference || reference}.pdf`);
    } catch { /* api.download ya gestiona el 401/errores */ }
    button.disabled = false;
  };

  // ── Matriz de tallas: el mismo componente del catálogo mete en el carrito ──
  const buy = host.querySelector('#buy');
  const add = host.querySelector('#add');
  const addLabel = host.querySelector('#add-label');
  const total = host.querySelector('#total');
  const itemsById = { [item.modelId]: item };

  // Etiqueta y rango del botón por ESTADO (ver el handler de #add más abajo): sin
  // unidades, fantasma con «Pon cantidades por talla» y el total vacío (el hint «Elige
  // tallas…» dentro de un botón rojo que decía AÑADIR era el origen de la confusión);
  // con unidades, primario con «Ver en el pedido» y «{n} pares · {importe}». La clase
  // .block acompaña al fantasma porque app.css solo da ancho completo a
  // .product-order .btn-primary.
  const paintTotal = () => {
    if (!available) {
      // No hay total que calcular: el botón apagado explica por qué lo está
      total.innerHTML = `<span class="product-hint">${esc(t('product.noStock'))}</span>`;
      return;
    }
    let units = 0, amount = 0;
    for (const input of buy.querySelectorAll('.sz-qty')) {
      const qty = Number(input.value) || 0;
      units += qty;
      amount += qty * (Number(input.dataset.price) || 0);
    }
    const hasUnits = units > 0;
    add.classList.toggle('btn-primary', hasUnits);
    add.classList.toggle('btn-ghost', !hasUnits);
    add.classList.toggle('block', !hasUnits);
    addLabel.textContent = t(hasUnits ? 'product.reviewOrder' : 'product.startSizes');
    total.innerHTML = hasUnits
      ? `${esc(t('product.pairs', { n: units }))} · <b>${esc(eur(amount))}</b>`
      : '';
  };

  bindMatrix(buy, itemsById, { onChange: paintTotal });
  paintTotal();

  // Sin stock en ninguna talla, el "Consultar" de la matriz es LA acción de la ficha:
  // botón fantasma a lo ancho en el sitio del CTA (el rojo queda apagado encima).
  const consult = buy.querySelector('.matrix-consult a');
  if (consult && !available) {
    consult.classList.add('btn-ghost', 'block');
    consult.style.textDecoration = 'none';
  }

  // El pedido ya se hace al teclear en la matriz; el botón hace lo que dice su etiqueta
  // (paintTotal): sin unidades, «Pon cantidades por talla» lleva el foco a la primera
  // talla disponible; con unidades, «Ver en el pedido» abre el carrito para revisarlas
  // (reutiliza el botón del header).
  host.querySelector('#add').onclick = () => {
    const units = [...buy.querySelectorAll('.sz-qty')].reduce((sum, i) => sum + (Number(i.value) || 0), 0);
    if (units > 0) {
      document.getElementById('cartBtn')?.click();
      return;
    }
    const first = buy.querySelector('.sz-qty:not([disabled])');
    first?.focus();
    first?.select?.();
  };

  // ── Favorito: mismo comportamiento y accesibilidad que el catálogo ──
  const setFav = (button, on) => {
    button.setAttribute('aria-pressed', String(on));
    button.setAttribute('aria-label', favLabel(on));
    button.setAttribute('title', favLabel(on));
    button.innerHTML = on ? icons.heartOn(24) : icons.heart(24);
  };

  host.querySelector('#fav').onclick = async event => {
    const button = event.currentTarget;
    const on = button.getAttribute('aria-pressed') !== 'true';
    setFav(button, on);
    item.favorite = on;
    try {
      await (on ? api.put(`/api/portal/favorites/${encodeURIComponent(item.modelId)}`)
                : api.del(`/api/portal/favorites/${encodeURIComponent(item.modelId)}`));
    } catch {
      setFav(button, !on);
      item.favorite = !on;
    }
  };
}
