# Estado del proyecto — B2B Platform (lejan)

> Actualizado: 2026-08-18. Este documento es la foto de estado para retomar el
> trabajo en cualquier sesión. La historia fina está en `git log` y el plan en
> [plan-portal.md](plan-portal.md).

## Qué es

Plataforma B2B que sustituirá al portal actual de lejan (mygo2b). Backend .NET 10 +
PostgreSQL que implementa el mismo contrato API que consume el conector MITO de
Business Central (BC no se toca: solo URLs y credenciales de su Setup en el corte).

**Evolución del /goal:**
1. Primero se replicó el portal lejan con **paridad** visual/funcional (auditoría
   CUMPLE, tag `v1-paridad-lejan`).
2. Ahora el /goal es un **portal moderno con las MISMAS funcionalidades pero un
   diseño nuevo y potente que no parezca una copia**. En marcha el rediseño
   "Premium editorial" (ver sección abajo). Punto de retorno: `git checkout v1-paridad-lejan`.

## Rediseño "Premium editorial" (2026-08-18, en curso)

Dirección elegida por el cliente: lienzo **crema cálido**, display serif **Fraunces**
+ **Inter**, **verde profundo** de marca (#1f5c46, reutiliza los tokens `--blue*`) +
**terracota** de acento (#c4633a), tablas silenciosas, layout contenido. Todo el
sistema cuelga de los tokens de `app.css`.

- **Método**: sistema base construido a mano → **3 subagentes críticos de diseño en
  paralelo** (entrada, compra, datos) con las skills de `.claude/skills/` (66 skills
  del repo *awesome-design-skills* instaladas: editorial, refined, premium, bento…)
  + `web-design-guidelines` → aplicación de los hallazgos P1 en bucle. Informes en
  `scratchpad/critica/{entrada,compra,datos}.md`.
- **Hecho** (commits `a6d07a5`, `a1fcdee` + el del checkout): tokens/tipografía/chrome
  verde/footer/login; tablas silenciosas; **portada rediseñada** con saludo editorial
  + **bento de KPIs** (datos reales del cliente) + tarjetas díptico verde/terracota con
  line-art (assets SVG de demo reescritos on-brand); escala de estados sin azul; matriz
  de tallas "silenciosa"; contraste AA de los grises; checkout con panel crema y flujo
  reordenado (desglose → condiciones → CTA).
- **Backlog de pulido pendiente** (de las 3 críticas, no bloqueante):
  1. **Móvil**: rail de facetas del catálogo → drawer "Filtrar"; listados de documentos
     (orders/sat) → tarjetas por debajo de ~640px (hoy ocultan columnas).
  2. **Estadísticas**: etiquetas de valor en las barras, altura en móvil, banda de KPIs.
  3. Matriz: ocultar el "0" vacío (placeholder) para quitar el "mar de ceros".
  4. Pantalla de credenciales más rica; wordmark como SVG real; chips truncados
     ("Pendiente De…"); microinteracción de count-up en el botón del carrito.
- El MCP de consultas está en `mcp/` (ver sección propia abajo).

## Hecho (330/330 tests; todo commiteado y pusheado)

| Pieza | Dónde |
|---|---|
| Contrato API completo extraído del conector | `docs/contrato-api/` (6 documentos) |
| Ingesta BC→B2B: login JWT + 19 PUT upsert + ofertas en array + GET/DELETE reconciliación + búsqueda pedidos | `backend/src/B2B.Api/{Auth,Sync}/` |
| Normalización a dominio: catálogo, productos/tallas, ofertas, stock por ventana, ventanas | `Sync/CatalogNormalizer.cs`, `Data/` |
| CMS admin (réplica del CMS lejan): dashboard, vistas por entidad, comunicación BC, editor de portada, medios | `wwwroot/admin.html`, `Admin/` |
| **Portal de clientes completo** (réplica del portal lejan) | `wwwroot/portal/` + `Portal/`, `Shop/` |
| — Fase 0: rutas reales /{market}/{lang}/{vista}, login + selección de credenciales, chrome, i18n es/en/fr/it | commit `c0ec529` |
| — Fase 1: portada configurable (portal_content + /admin Web→Portada + subida de medios) + 12 correcciones de diseño | `3ba7d8d` |
| — Fase 2: catálogo (facetas, tarifas por cliente, precio por talla, favoritos, Desc. Stock), checkout, carritos guardados + 12 correcciones (móvil reconstruido, AA) | `2df95ca` |
| — Fase 3: pedidos/albaranes/facturas con rail de estados, detalle, CSV, aislamiento por clientId del token | `b50f7af` |
| — Fase 4: perfil (prefs PVD/PVP), empresa (solicitudes de cambio), estadísticas (gráfico SVG), contacto, SAT (return_requests) | `6e23315` |
| Referencias de diseño capturadas del portal y CMS reales | `docs/front-referencia/`, `docs/cms-referencia/` |

Método usado (pedido por el cliente): subagentes Opus por fase
(implementador → revisor de diseño con skills `web-design-guidelines`/
`frontend-design`/`impeccable` → corrector), auditoría propia por fase en bucle.

## Pendiente

1. **Auditoría integral final del /goal** — **CERRADA: CUMPLE en las tres
   dimensiones** (visual R3, funcional R3, técnica R2 tras corregir B-2), commit
   `e91580b`. Historia del bucle (visual: 14 vistas vs capturas; funcional:
   recorridos completos; técnica: tests/consola/i18n/aislamiento), tres rondas
   (2026-08-17/18):
   - **Ronda 1** (NO CUMPLE): 1 bloqueante (B-1 CMS sin rol), 15 mayores, ~33 menores.
     Corregido: B-1 (rol `admin`), 9 mayores visuales (footer, buscador, contenedor a
     sangre, literales EN/IT, checkout, estadísticas, empresa/AÑADIR, carritos),
     F-01 (clic del carrito), F-02 (`Consultar`), M-1 (locale catálogo), M-4 (claves
     de rol), m-1/m-4/m-5/m-6/m-8, F-07 (SVG) — 26/27 en front + 8 de accesibilidad.
   - **Ronda 2** (NO CUMPLE): las 11 correcciones encargadas verificadas OK, pero
     aparece **B-2** (bloqueante nuevo: `/api/sync/*` y `/api/query/*` sin rol,
     alcanzables con token `client-admin`) + M5 parcial (nombre del método de pago
     corrupto) + menores nuevos. **Todo corregido en esta sesión**: B-2 (política
     `bc-connector` = rol `integration`+`admin` en sync/query, verificada en vivo
     403/401/200; ver `Auth/ConnectorPolicy.cs`), M5 (dato de dev `payment-method`
     con U+FFFD reparado en BD; el código de proyección era correcto), n-1 (hero de
     dev restaurado a las imágenes de demo), R2 (proporción del gráfico de
     estadísticas a ~3.49:1) y n-4 (PVD/PVP por i18n con `catalog.price.*`).
   - **Ronda 3** (CUMPLE): verificación visual (M5, R2, n-1, n-4 + regresión, 0
     errores de consola) y funcional (F-01, F-02, M8, compra completa, permisos del
     CMS en navegador y regresión de las 14 vistas) — ambas CUMPLE, sin fallos nuevos.
     Detalles cosméticos anotados y no accionados: el aviso "sin alias" de M8 es la
     burbuja de validación nativa del navegador (no localizable sin validación propia);
     `TRASNPORTE` del CSV de albaranes es la errata literal del portal original (paridad).
   - **Menores no bloqueantes aplazados** (hardening/contrato, anotados en
     `scratchpad/auditoria/tecnica-r2.md`): n-2 (el rate-limit cuenta también logins
     correctos y particiona por IP sin `ForwardedHeaders`), n-3 (`keySlug` del catálogo
     conserva acentos), m-7/m-9 (el catálogo y los documentos se cargan enteros en
     memoria por petición — validar con volumen real antes del corte), autoalojar
     Google Fonts (m-10). Ninguno afecta a la paridad del /goal.
   - Aislamiento multi-cliente: probado con **un** cliente en la BD de dev; **repetir
     con dos clientes sembrados** antes del corte (es donde se manifestarían B-1/B-2).
2. **Fase BC** (`plan-portal.md`): envío real del pedido a BC vía sus API OData
   (contrato `docs/contrato-api/06-api-odata-bc.md`), PDFs vía `salesDocuments`,
   estados. Requiere del cliente: tenant + client id/secret OAuth del sandbox.
3. Cosas menores anotadas: SVG real del wordmark lejan (interino: texto),
   preload completo del hero (requiere inlinar primer slide), modo Grid del
   catálogo (select deshabilitado a propósito).

## MCP "lejan-b2b" — consulta en lenguaje natural (carpeta `mcp/`)

Servidor MCP en Python (FastMCP) para conectar un chat (Claude Desktop, etc.) con los
datos del cliente y preguntar "¿cuánto he vendido este mes?", "¿qué pedidos he hecho?",
"¿cuánto debo?". Se autentica en la API del portal con `B2B_EMAIL`/`B2B_PASSWORD` (el
token filtra por cliente; solo lectura). Herramientas: `resumen_ventas`, `pedidos`,
`facturas`, `ventas_por_mes`, `mi_cuenta`. Instalación y config para Claude Desktop en
`mcp/README.md`. Verificado en vivo contra el portal.

## Cómo arrancar todo (dev)

```bash
docker compose up -d                      # en backend/ (PostgreSQL 17)
cd backend && dotnet test                 # 330/330
# Servidor para navegar (carpeta publicada para no bloquear builds):
dotnet publish src/B2B.Api -c Release -o /tmp/portal-run
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 /tmp/portal-run/B2B.Api.exe
```

- Portal: http://localhost:5199/es/es/dashboard — `integracion@dev.local` / `dev-password` → SELECCIONAR
- **CMS: http://localhost:5199/admin — `admin@dev.local` / `dev-password`** (rol `admin`).
  Desde la corrección de la auditoría (B-1) el CMS es exclusivo del rol `admin`: el
  usuario del portal y el de integración reciben **403** en `/api/admin/*` y el CMS
  lo dice con un mensaje de permiso insuficiente. El administrador se siembra al
  arrancar desde `Seed:AdminEmail` / `Seed:AdminPassword`
  (`appsettings.Development.json`); en producción va por secretos del despliegue.
- **Sync/Query del conector** (`/api/sync/*`, `/api/query/*`) exigen rol de servicio
  desde la corrección de B-2: solo `integration` (la cuenta del Setup del conector) y
  `admin`. Un token de cliente del portal (`client-admin`) recibe **403**. En dev,
  `admin@dev.local` sirve para probar el sync a mano; el conector real usa su Integration
  User. Ojo: en dev `integracion@dev.local` es el usuario de navegación del portal
  (`client-admin`), no el del conector — son cuentas distintas por diseño.
- API docs: http://localhost:5199/docs — **solo en Development**; fuera de ese entorno
  `/docs` y `/openapi` devuelven 404 (m-4).
- Fuera de Development el arranque **falla** si `Jwt:SigningKey` sigue siendo la clave
  de desarrollo de `appsettings.json` (m-6): pásala por `Jwt__SigningKey`.
- `POST /api/auth/login` está limitado a 10 intentos por minuto y por IP (m-5),
  configurable con `Auth:Login:PermitLimit` / `Auth:Login:WindowSeconds`.
- Los agentes de desarrollo usan el puerto **5198** para no chocar con el 5199 del usuario.
- Tras `dotnet run`/tests, matar todo `B2B.Api.exe` (bloquea el build) — ojo: mata también el 5199.

## Prueba contra BC real (cuando se retome)

Túnel: `devtunnel host -p 5199 --allow-anonymous` (login devtunnel ya hecho).
La URL cambia en cada arranque. Tabla de valores del Setup del conector para el
sandbox: está en la conversación del 2026-08-03 y se reconstruye desde
`docs/contrato-api/01-autenticacion-y-convenciones.md` §4.2 + las rutas de
`Sync/SyncEndpoints.cs`. Solo sandbox, nunca producción.

## Credenciales de referencia (portal/CMS actuales de lejan, para capturas)

- CMS actual: https://admin-b2b.lejanbrand.com — admin@b2b.com / 123
- Portal actual: https://b2b.lejanbrand.com — jilluecasaus@gmail.com / Kr987solutions. (cliente de prueba TEST 5)
