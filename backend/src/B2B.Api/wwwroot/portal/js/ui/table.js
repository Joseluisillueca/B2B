// Tabla de listado del portal: cabecera negra en mayúsculas y el vacío literal
// "No se han encontrado resultados" centrado bajo ella, como en las capturas. El
// vacío va FUERA del scroller: dentro de la tabla se centraba respecto a las siete
// columnas y en móvil quedaba cortado por el borde de .grid-scroll.
//
// Las celdas llegan ya como HTML (una fila puede llevar enlace, chip o icono), así
// que quien las construye es responsable de escapar lo que venga de la API con
// esc(). Las cabeceras, en cambio, se escapan aquí.

import { esc } from '../format.js';

/**
 * columns: [{ label, className }]
 * rows: [{ id, cells: [htmlDeCadaCelda] }]
 */
export function gridTable({ columns, rows, empty }) {
  const head = columns
    .map(column => `<th${column.className ? ` class="${esc(column.className)}"` : ''}>${esc(column.label)}</th>`)
    .join('');

  const body = rows.map(row => `
        <tr${row.id ? ` data-id="${esc(row.id)}"` : ''}>
          ${row.cells.map((cell, index) => {
            const className = columns[index]?.className;
            return `<td${className ? ` class="${esc(className)}"` : ''}>${cell ?? ''}</td>`;
          }).join('')}
        </tr>`).join('');

  // Sin filas la cabecera se queda (dice qué habría) y el mensaje va debajo del
  // scroller, siempre a la vista aunque la tabla desborde.
  const emptyLine = rows.length ? '' : `
    <p class="grid-empty" style="text-align:center;color:var(--muted);padding:1.6rem 1rem;margin:0">${esc(empty)}</p>`;

  // .grid-scroll: en pantallas estrechas la tabla se desplaza dentro de su caja
  // en vez de estirar el documento entero
  return `
    <div class="grid-scroll">
      <table class="grid">
        <thead><tr>${head}</tr></thead>
        <tbody>${body}</tbody>
      </table>
    </div>${emptyLine}`;
}
