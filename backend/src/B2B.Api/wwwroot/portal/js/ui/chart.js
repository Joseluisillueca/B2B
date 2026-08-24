// Gráficos SVG sin librerías ni CDN: barras verticales, barras horizontales y donut.
// Heredan los colores del sistema (var(--blue), var(--accent)…) y escalan con el ancho.
import { esc } from '../format.js';

const money = v => (Number(v) || 0).toLocaleString('es-ES', { maximumFractionDigits: 0 }) + ' €';
const int = v => (Number(v) || 0).toLocaleString('es-ES');

// Techo "redondo" para que las guías caigan en cifras legibles
function niceMax(value) {
  if (!(value > 0)) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const scaled = value / magnitude;
  const step = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 2.5 ? 2.5 : scaled <= 5 ? 5 : 10;
  return step * magnitude;
}

// Barras verticales — data: [{ label, value }]
export function bars(data, opts = {}) {
  const fmt = opts.format || money;
  const W = 1000, H = 300, PAD = { top: 18, right: 16, bottom: 40, left: 70 };
  const values = data.map(d => Number(d.value) || 0);
  const top = niceMax(Math.max(0, ...values));
  const plotW = W - PAD.left - PAD.right, plotH = H - PAD.top - PAD.bottom;
  const y = v => PAD.top + plotH * (1 - v / (top || 1));
  const slot = plotW / Math.max(1, data.length);
  const barW = Math.min(46, Math.max(6, slot * 0.6));
  const ticks = Array.from({ length: 5 }, (_, i) => (top / 4) * i);
  const empty = !data.length || values.every(v => v === 0);

  return `<svg viewBox="0 0 ${W} ${H}" class="ch" role="img" preserveAspectRatio="xMidYMid meet"
      aria-label="${esc(opts.label || '')}">
    ${ticks.map(tk => `<g class="ch-tick">
      <line x1="${PAD.left}" x2="${W - PAD.right}" y1="${y(tk).toFixed(1)}" y2="${y(tk).toFixed(1)}"></line>
      <text x="${PAD.left - 10}" y="${(y(tk) + 4).toFixed(1)}" text-anchor="end">${esc(fmt(tk))}</text></g>`).join('')}
    ${empty ? `<text class="ch-empty" x="${W / 2}" y="${H / 2}" text-anchor="middle">${esc(opts.empty || 'Sin datos')}</text>`
      : data.map((d, i) => {
        const v = Number(d.value) || 0;
        const x = PAD.left + slot * i + (slot - barW) / 2;
        const h = Math.max(v === 0 ? 0 : 1, PAD.top + plotH - y(v));
        return `<g class="ch-bar"><title>${esc(`${d.label} · ${fmt(v)}`)}</title>
          <rect x="${x.toFixed(1)}" y="${y(v).toFixed(1)}" width="${barW.toFixed(1)}" height="${h.toFixed(1)}" rx="3"></rect>
          <text class="ch-xlab" x="${(x + barW / 2).toFixed(1)}" y="${H - PAD.bottom + 22}" text-anchor="middle">${esc(d.label)}</text></g>`;
      }).join('')}
  </svg>`;
}

// Barras horizontales — data: [{ label, value, sub }]. Para rankings (top modelos, familia).
export function hbars(data, opts = {}) {
  const fmt = opts.format || money;
  if (!data.length) return `<div class="ch-na">${esc(opts.empty || 'Sin datos')}</div>`;
  const max = Math.max(1, ...data.map(d => Number(d.value) || 0));
  return `<ul class="ch-hbars">${data.map(d => {
    const v = Number(d.value) || 0;
    const pct = Math.max(2, (v / max) * 100);
    return `<li>
      <div class="ch-hbar-top"><span class="ch-hbar-label">${esc(d.label)}${d.sub ? ` <em>${esc(d.sub)}</em>` : ''}</span>
        <b class="ch-hbar-val">${esc(fmt(v))}</b></div>
      <div class="ch-hbar-track"><span style="width:${pct.toFixed(1)}%"></span></div>
    </li>`;
  }).join('')}</ul>`;
}

// Donut — data: [{ label, value }]. Para reparto (familia).
export function donut(data, opts = {}) {
  const fmt = opts.format || money;
  const items = data.filter(d => (Number(d.value) || 0) > 0);
  const total = items.reduce((s, d) => s + (Number(d.value) || 0), 0);
  if (!total) return `<div class="ch-na">${esc(opts.empty || 'Sin datos')}</div>`;
  const palette = ['var(--blue)', 'var(--accent)', '#c98a1e', '#6b8f7a', '#a4502c', '#8c7b64', '#3f6b57'];
  const R = 80, C = 2 * Math.PI * R;
  let offset = 0;
  const rings = items.map((d, i) => {
    const frac = (Number(d.value) || 0) / total;
    const seg = `<circle r="${R}" cx="100" cy="100" fill="none" stroke="${palette[i % palette.length]}"
      stroke-width="30" stroke-dasharray="${(frac * C).toFixed(2)} ${C.toFixed(2)}"
      stroke-dashoffset="${(-offset * C).toFixed(2)}" transform="rotate(-90 100 100)"><title>${esc(`${d.label} · ${fmt(d.value)}`)}</title></circle>`;
    offset += frac;
    return seg;
  }).join('');
  const legend = items.map((d, i) => `<li><span class="ch-dot" style="background:${palette[i % palette.length]}"></span>
    ${esc(d.label)} <b>${esc(fmt(d.value))}</b></li>`).join('');
  return `<div class="ch-donut">
    <svg viewBox="0 0 200 200" class="ch-donut-svg" role="img" aria-label="${esc(opts.label || '')}">${rings}
      <text x="100" y="96" text-anchor="middle" class="ch-donut-total">${esc(fmt(total))}</text>
      <text x="100" y="116" text-anchor="middle" class="ch-donut-cap">${esc(opts.caption || '')}</text></svg>
    <ul class="ch-legend">${legend}</ul></div>`;
}

export const chartFormat = { money, int };
