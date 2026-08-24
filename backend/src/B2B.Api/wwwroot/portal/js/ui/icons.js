// Iconos SVG inline: sin CDN, sin fuente de iconos, heredan currentColor.

const svg = (body, size = 18, extra = '') =>
  `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor"
     stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" ${extra}>${body}</svg>`;

export const icons = {
  search: size => svg('<circle cx="11" cy="11" r="7"/><path d="m20 20-3.6-3.6"/>', size),
  eye: size => svg('<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>', size),
  eyeOff: size => svg('<path d="M10.6 6.2A9.8 9.8 0 0 1 12 6c6.4 0 10 6 10 6a17 17 0 0 1-3 3.6M6.3 6.4A17 17 0 0 0 2 12s3.6 6 10 6a9.8 9.8 0 0 0 4-.9"/><path d="m3 3 18 18"/>', size),
  chevron: size => svg('<path d="m6 9 6 6 6-6"/>', size),
  cart: size => svg('<path d="M6 7h14l-1.5 9.5a2 2 0 0 1-2 1.5H9a2 2 0 0 1-2-1.6L5 3H2"/><circle cx="10" cy="21" r="1"/><circle cx="17" cy="21" r="1"/>', size),
  close: size => svg('<path d="M6 6l12 12M18 6 6 18"/>', size),
  chat: size => svg('<path d="M21 11.5a8.5 8.5 0 0 1-12.3 7.6L3 21l1.9-5.7A8.5 8.5 0 1 1 21 11.5Z"/>', size),
  sparkles: size => svg('<path d="M12 3l1.6 4.4L18 9l-4.4 1.6L12 15l-1.6-4.4L6 9l4.4-1.6L12 3Z"/><path d="M19 15l.7 1.8L21.5 17.5l-1.8.7L19 20l-.7-1.8L16.5 17.5l1.8-.7L19 15Z"/>', size),
  login: size => svg('<path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/><path d="M10 17l5-5-5-5"/><path d="M15 12H3"/>', size),
  pause: size => svg('<path d="M9 5v14M15 5v14"/>', size),
  play: size => svg('<path d="M7 4.8v14.4L19.5 12Z"/>', size),

  // Catálogo, checkout y carritos (Fase 2)
  heart: size => svg('<path d="M12 20s-7.5-4.6-7.5-9.7A4.3 4.3 0 0 1 12 7.4a4.3 4.3 0 0 1 7.5 2.9C19.5 15.4 12 20 12 20Z"/>', size),
  heartOn: size => svg('<path d="M12 20s-7.5-4.6-7.5-9.7A4.3 4.3 0 0 1 12 7.4a4.3 4.3 0 0 1 7.5 2.9C19.5 15.4 12 20 12 20Z" fill="currentColor"/>', size),
  download: size => svg('<path d="M12 4v10m0 0 4-4m-4 4-4-4"/><path d="M5 18h14"/>', size),
  list: size => svg('<path d="M4 6h16M4 12h16M4 18h16"/>', size),
  grid: size => svg('<rect x="4" y="4" width="7" height="7" rx="1"/><rect x="13" y="4" width="7" height="7" rx="1"/><rect x="4" y="13" width="7" height="7" rx="1"/><rect x="13" y="13" width="7" height="7" rx="1"/>', size),
  trash: size => svg('<path d="M4 7h16M9 7V5h6v2M6 7l1 12.2A1.8 1.8 0 0 0 8.8 21h6.4a1.8 1.8 0 0 0 1.8-1.8L18 7"/>', size),
  pencil: size => svg('<path d="M4 20h4L20 8a2.1 2.1 0 0 0-3-3L5 17Z"/><path d="M15 5.5 18.5 9"/>', size),
  fileDown: size => svg('<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8Z"/><path d="M14 3v5h5"/><path d="M12 11v5m0 0 2-2m-2 2-2-2"/>', size),
  alert: size => svg('<circle cx="12" cy="12" r="9"/><path d="M12 7.5v5M12 16v.5"/>', size),
  check: size => svg('<path d="m5 12.5 4.5 4.5L19 7.5"/>', size),
  card: size => svg('<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 10h18"/><path d="M7 15h4"/>', size),
  truck: size => svg('<path d="M3 7h11v9H3zM14 10h4l3 3v3h-7z"/><circle cx="7" cy="18" r="1.6"/><circle cx="17.5" cy="18" r="1.6"/>', size),
  left: size => svg('<path d="m14 6-6 6 6 6"/>', size),
  right: size => svg('<path d="m10 6 6 6-6 6"/>', size),
  shoe: size => svg('<path d="M3 16c3-1 4-4 6-6s4-3 7-3c2.5 0 5 1.5 5 4 0 2-1.5 3-4 3H3Z"/><path d="M3 16v2.5h18V16"/>', size),

  // Cuenta, empresa, contacto y devoluciones (Fase 4)
  building: size => svg('<path d="M4 21V4.5A1.5 1.5 0 0 1 5.5 3h7A1.5 1.5 0 0 1 14 4.5V21"/><path d="M14 10h4.5A1.5 1.5 0 0 1 20 11.5V21"/><path d="M3 21h18"/><path d="M7 7h3M7 11h3M7 15h3M17 14h0M17 17.5h0"/>', size),
  coin: size => svg('<circle cx="12" cy="12" r="9"/><path d="M14.5 9.2A2.6 2.6 0 0 0 12 8c-1.4 0-2.5.8-2.5 1.9s1.1 1.6 2.5 1.9 2.5.8 2.5 1.9S13.4 16 12 16a2.6 2.6 0 0 1-2.5-1.2"/><path d="M12 6.6v10.8"/>', size),
  upload: size => svg('<path d="M12 16V6m0 0-4 4m4-4 4 4"/><path d="M5 19h14"/>', size),
  send: size => svg('<path d="M21 3 3 10.5l7 3 3 7Z"/><path d="m10 13.5 4-4"/>', size),
  plus: size => svg('<circle cx="12" cy="12" r="9"/><path d="M12 8v8M8 12h8"/>', size),
  image: size => svg('<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.5"/><path d="m4 18 5-5 3.5 3.5L16 13l4 4"/>', size),
  chart: size => svg('<path d="M4 4v16h16"/><path d="M8 16v-4M12.5 16V8M17 16v-6"/>', size),
  user: size => svg('<circle cx="12" cy="8.5" r="3.8"/><path d="M4.5 20a7.5 7.5 0 0 1 15 0"/>', size),
  lock: size => svg('<rect x="4.5" y="10" width="15" height="10" rx="2"/><path d="M8.5 10V7.5a3.5 3.5 0 0 1 7 0V10"/>', size),

  // Redes del footer: trazos simples, mismo peso visual que el resto
  facebook: size => svg('<path d="M14 8h3V4.5h-3A4 4 0 0 0 10 8.5V11H7.5v3.5H10V21h3.5v-6.5H16l.5-3.5H13.5V9a1 1 0 0 1 1-1Z"/>', size),
  instagram: size => svg('<rect x="3" y="3" width="18" height="18" rx="5"/><circle cx="12" cy="12" r="4"/><circle cx="17.2" cy="6.8" r=".8" fill="currentColor"/>', size),
  linkedin: size => svg('<rect x="3" y="3" width="18" height="18" rx="3"/><path d="M8 10.5V17M8 7.4v.1M12 17v-3.6a2.4 2.4 0 0 1 4.8 0V17"/>', size),
  youtube: size => svg('<rect x="2.5" y="5.5" width="19" height="13" rx="4"/><path d="m10.5 9.8 5 2.2-5 2.2Z"/>', size),
  tiktok: size => svg('<path d="M14 4v10.2a3.4 3.4 0 1 1-3-3.4"/><path d="M14 4a4.6 4.6 0 0 0 4.6 4.4"/>', size)
};
