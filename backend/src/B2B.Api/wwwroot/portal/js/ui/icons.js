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
  login: size => svg('<path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/><path d="M10 17l5-5-5-5"/><path d="M15 12H3"/>', size),
  pause: size => svg('<path d="M9 5v14M15 5v14"/>', size),
  play: size => svg('<path d="M7 4.8v14.4L19.5 12Z"/>', size),

  // Redes del footer: trazos simples, mismo peso visual que el resto
  facebook: size => svg('<path d="M14 8h3V4.5h-3A4 4 0 0 0 10 8.5V11H7.5v3.5H10V21h3.5v-6.5H16l.5-3.5H13.5V9a1 1 0 0 1 1-1Z"/>', size),
  instagram: size => svg('<rect x="3" y="3" width="18" height="18" rx="5"/><circle cx="12" cy="12" r="4"/><circle cx="17.2" cy="6.8" r=".8" fill="currentColor"/>', size),
  linkedin: size => svg('<rect x="3" y="3" width="18" height="18" rx="3"/><path d="M8 10.5V17M8 7.4v.1M12 17v-3.6a2.4 2.4 0 0 1 4.8 0V17"/>', size),
  youtube: size => svg('<rect x="2.5" y="5.5" width="19" height="13" rx="4"/><path d="m10.5 9.8 5 2.2-5 2.2Z"/>', size),
  tiktok: size => svg('<path d="M14 4v10.2a3.4 3.4 0 1 1-3-3.4"/><path d="M14 4a4.6 4.6 0 0 0 4.6 4.4"/>', size)
};
