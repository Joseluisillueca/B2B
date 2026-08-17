// Tabla de listado del portal: cabecera negra en mayúsculas y el vacío literal
// "No se han encontrado resultados" centrado bajo ella, como en las capturas.
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

  const body = rows.length
    ? rows.map(row => `
        <tr${row.id ? ` data-id="${esc(row.id)}"` : ''}>
          ${row.cells.map((cell, index) => {
            const className = columns[index]?.className;
            return `<td${className ? ` class="${esc(className)}"` : ''}>${cell ?? ''}</td>`;
          }).join('')}
        </tr>`).join('')
    : `<tr class="grid-empty"><td colspan="${columns.length}">${esc(empty)}</td></tr>`;

  // .grid-scroll: en pantallas estrechas la tabla se desplaza dentro de su caja
  // en vez de estirar el documento entero
  return `
    <div class="grid-scroll">
      <table class="grid">
        <thead><tr>${head}</tr></thead>
        <tbody>${body}</tbody>
      </table>
    </div>`;
}
