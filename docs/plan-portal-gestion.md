# Plan — Portal de GESTIÓN de maestros (`/manage`)

Objetivo: sustituir los formularios feos del CMS (`admin.html`) por un **back-office
propio, bien diseñado**, para que un cliente sin ERP cree los maestros con formularios
cuidados. **Diseño idéntico al portal B2B** (se reutiliza `/portal/app.css`; NO se
instala ningún sistema de diseño externo — rompería la coherencia). Loops de agentes
que critican diseño Y funcionalidad hasta dejarlo perfecto.

## Decisión de diseño
- Reutilizar el sistema del portal: tokens Modernist (Archivo, rojo `#ec3013`, radio 0,
  reglas 2px), componentes `.btn-*`, `table.grid`, `.biz-section/.biz-card/.biz-grid/
  .acc-field`, `.dlg`, `.notice`, `.pager`. Referencia de formulario: `views/new-client.js`.
- El back-office vive en `/manage` (SPA con hash-router), reutiliza `icons.js`/`format.js`
  del portal y consume los endpoints ya existentes (`/api/admin/*`, auditados y con tests).

## Checklist

### A · Armazón
- [x] Mapear el sistema de diseño del portal (app.css + patrones de formulario)
- [x] `index.html` + `manage.css` (reutiliza `/portal/app.css`)
- [x] `api.js` (auth admin + endpoints)
- [x] `schemas.js` (14 maestros + navegación)
- [x] `shell.js` (cabecera negra + barra lateral) · `router.js` (hash) · `boot.js`
- [x] `login.js` (login-split del portal, solo rol admin)

### B · Vistas
- [x] Listado genérico (`table.grid`, búsqueda, "+ Nuevo", vacío elegante, paginación)
- [x] Formulario genérico por secciones (FK selects, i18n, multi-fichas, valuelist)
- [x] **Formulario RICO de Cliente**: básico · fiscal + dirección · **varias direcciones
      de envío** · comercial (grupo, formas de pago) → guarda `client` + N `shipping-address`
- [x] Accesos (usuarios): lista + alta (contraseña/activación) + reset + borrar
- [x] Pedidos: lista + detalle + gestión de estado + eliminar
- [x] Resumen (dashboard con KPIs)

### C · Integración
- [x] Servir `/manage` (`/manage`→`/manage/index.html`; SPA hash routing)
- [x] Enlace desde `admin.html` (nav "Portal de gestión →") + token compartido (reusa `b2b_token`)
- [x] Retirados los formularios embebidos de `admin.html`: sus maestros redirigen a `/manage`

### D · Loops de calidad (agentes)
- [x] Crítico **diseño** v1 → 2 altos, 6 medios, nits → **corregidos**
- [x] Crítico **funcionalidad** v1 → 1 alto, 1 medio, 2 bajos → **corregidos**
- [x] Crítico **diseño** v2 → 8 arreglos confirmados; 1 medio (hints) → **corregido**; 0 altos
- [x] Crítico **funcionalidad** v2 → **0 críticos/altos/medios**, sin regresiones
- [x] Batería backend 434/434 verde

## Estado — CERRADO ✅
Back-office `/manage` completo, verificado por doble loop de críticos (diseño+funcionalidad,
ambos a 0 altos/medios) y batería 434/434. Diseño idéntico al portal (Archivo, rojo #ec3013,
radios 0). Pendiente menor (no bloqueante): borrar un agente deja su usuario de login huérfano
(se gestiona aparte en «Accesos»); tabla de Precios algo apretada a 390px (estético).

