// Iconos del back-office: reutiliza los del portal y añade los que faltan para el
// menú de maestros. Mismo trazo (24×24, currentColor, stroke 2) para coherencia.
import { icons as portalIcons } from '/portal/js/ui/icons.js';

const svg = (body, size = 18) =>
  `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor"
     stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;

export const icons = {
  ...portalIcons,
  home: size => svg('<path d="M4 11 12 4l8 7"/><path d="M6 10v9h12v-9"/><path d="M10 19v-5h4v5"/>', size),
  box: size => svg('<path d="M3.5 7.5 12 3l8.5 4.5v9L12 21l-8.5-4.5Z"/><path d="M3.5 7.5 12 12l8.5-4.5M12 12v9"/>', size),
  tag: size => svg('<path d="M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9Z"/><circle cx="7.5" cy="7.5" r="1.4"/>', size),
  layers: size => svg('<path d="m12 3 9 5-9 5-9-5Z"/><path d="m3 13 9 5 9-5"/>', size),
  calendar: size => svg('<rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 9h16M8 3v4M16 3v4"/>', size),
  folder: size => svg('<path d="M3 6.5A1.5 1.5 0 0 1 4.5 5h4l2 2.5h7A1.5 1.5 0 0 1 19 9v8.5A1.5 1.5 0 0 1 17.5 19h-13A1.5 1.5 0 0 1 3 17.5Z"/>', size),
  users: size => svg('<circle cx="9" cy="8.5" r="3.3"/><path d="M3.5 19a5.5 5.5 0 0 1 11 0"/><path d="M16 5.5a3.3 3.3 0 0 1 0 6.4M20.5 19a5.5 5.5 0 0 0-4-5.3"/>', size),
  key: size => svg('<circle cx="8" cy="15" r="4"/><path d="m11 12 8-8M17 4l3 3M15 6l2 2"/>', size),
  layout: size => svg('<rect x="3" y="4" width="18" height="16"/><path d="M3 9h18M9 9v11"/>', size),
  book: size => svg('<path d="M5 4h11a2 2 0 0 1 2 2v14H7a2 2 0 0 1-2-2Z"/><path d="M18 6H8"/>', size),
  activity: size => svg('<path d="M3 12h4l2.5 7 4-14 2.5 7H21"/>', size),
  percent: size => svg('<path d="M19 5 5 19"/><circle cx="7.5" cy="7.5" r="2.2"/><circle cx="16.5" cy="16.5" r="2.2"/>', size),
};
