import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc } from '../util.js';

export default async function dashboard(main) {
  let counts = {};
  try {
    const data = await api.summary();
    counts = Object.fromEntries((data.items || []).map(i => [i.entityType, i.count]));
  } catch { /* seguimos con ceros */ }
  const n = t => counts[t] || 0;

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Gestión</p>
        <h1 class="title">Resumen</h1>
        <p class="lead">Da de alta y edita todo lo que ve el portal: catálogo, clientes y pedidos.</p>
      </div>
    </div>

    <div class="kpis">
      <a class="kpi" href="#/models"><span class="kpi-label">Modelos</span><span class="kpi-value">${n('model')}</span><span class="kpi-sub">del catálogo</span></a>
      <a class="kpi" href="#/products"><span class="kpi-label">Variantes</span><span class="kpi-value">${n('product')}</span><span class="kpi-sub">tallas con precio y stock</span></a>
      <a class="kpi" href="#/clients"><span class="kpi-label">Clientes</span><span class="kpi-value">${n('client')}</span><span class="kpi-sub">dados de alta</span></a>
      <a class="kpi" href="#/orders"><span class="kpi-label">Pedidos</span><span class="kpi-value">${n('order')}</span><span class="kpi-sub">en el portal</span></a>
    </div>

    <h2 class="dash-greet">¿Qué quieres crear?</h2>
    <div class="mng-quick" id="quick"></div>`;

  const quick = [
    ['clients/new', 'Nuevo cliente', 'building', 'Datos, fiscal y direcciones de envío'],
    ['models/new', 'Nuevo modelo', 'box', 'Con su referencia y familia'],
    ['offers/new', 'Nuevo precio', 'coin', 'Tarifa por modelo, grupo o cliente'],
    ['users/', 'Dar acceso', 'key', 'Crear un login de cliente o admin'],
  ];
  main.querySelector('#quick').innerHTML = quick.map(([href, label, icon, sub]) => `
    <a class="qa" href="#/${href.replace(/\/$/, '')}">
      <span class="qa-ic">${icons[icon] ? icons[icon](22) : ''}</span>
      <span class="qa-txt"><b>${esc(label)}</b><span>${esc(sub)}</span></span>
      ${icons.right(18)}
    </a>`).join('');
}
