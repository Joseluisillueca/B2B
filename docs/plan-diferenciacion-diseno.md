# Plan — Diferenciación de diseño (menú CMS + filtros de catálogo)

Objetivo: que el back-office `/manage` y los filtros del catálogo **no se parezcan tanto al
portal de referencia**, con **mejor UX**, manteniendo la identidad de marca (Archivo, rojo
`#ec3013`, radio 0, filetes) pero con un lenguaje propio de "herramienta de gestión" y de
"filtros de tienda" más cuidado. Validado con subagentes (diseño + funcionalidad) en bucle.

## Tarea A · Menú lateral del CMS (`/manage`)
Hoy: barra lateral estilo portal (texto plano, grupos en mayúsculas, activo con filete rojo
a la izquierda). Se quiere un aspecto DISTINTO y más "app de back-office".
Puntos:
- A1. Tratamiento del ítem activo distinto (p. ej. pastilla/fondo en vez de solo filete izq.).
- A2. Densidad e iconografía más de herramienta (iconos con caja/ް estados, secciones claras).
- A3. Fondo/superficie de la barra diferenciada del portal (sin romper tokens de marca).
- A4. Marca/cabecera de la barra propia (no idéntica al header del portal).
- A5. Responsive: colapso limpio en móvil (mejor que la fila horizontal actual).
- A6. Accesibilidad: foco visible, `aria-current`, contraste AA, área de toque.

## Tarea B · Filtros del catálogo (portal, `.rail`)
Hoy: rail fino a la IZQUIERDA con líneas de texto, checkboxes simples, "ver más". Se quiere
aspecto propio y mejor UX, y que **no se parezca al original**.
> **Dirección preferida por el usuario:** llevar los filtros a una **barra SUPERIOR con
> lookups/desplegables** (menús de filtro) en vez del rail lateral, o el patrón que mejor UX
> dé. Esto libera ancho para la cuadrícula de productos y diferencia claramente del original.
Puntos:
- B1. Agrupación clara de filtros (familias, líneas, tallas, etc.) con jerarquía visual.
- B2. Checkboxes/controles con un tratamiento propio (no el genérico del portal ref.).
- B3. Chips de "filtros activos" + acción "Limpiar filtros".
- B4. Contadores por opción legibles; "ver más/menos" pulido.
- B5. Comportamiento sticky + scroll fino ya existente, revisado.
- B6. Responsive: en móvil, filtros en panel/acordeón desplegable (no rail fijo).
- B7. Accesibilidad: labels, foco, roles.

## Método
1. Capturar estado ACTUAL (antes) de `/manage` (menú) y del catálogo (rail) — desktop+móvil.
2. Rediseñar A y B con los tokens de marca, aportando lenguaje propio.
3. Capturar DESPUÉS; autocrítica.
4. **Subagente crítico de DISEÑO/UX** (Playwright + heurísticas) → iterar.
5. **Subagente crítico de FUNCIONALIDAD** (que los filtros siguen filtrando, el menú navega,
   0 errores de consola, responsive) → iterar.
6. Repetir loops hasta 0 altos/medios en ambos.

## Estado
- **Parte A (menú CMS): HECHA** — iconos en cajita, grupos con filete separador, ítem activo
  como bloque sólido rojo (icono/badge en negativo). Diferenciado del portal, misma marca.
- **Parte B (filtros catálogo): HECHA** — rail lateral → **barra superior de lookups**
  (`details.cat-lookup`: Líneas [única], Disponibilidad y atributos [multi]) + buscador +
  chips de filtros activos + "Limpiar". Catálogo a ancho completo. Estado en URL intacto.
  Verificado por Playwright: filtra (8→6), chips, 0 errores de consola.
- Auditoría diseño+funcionalidad (A+B) en curso; correcciones y commit final después.
