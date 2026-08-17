# Estado del proyecto — B2B Platform (lejan)

> Actualizado: 2026-08-17. Este documento es la foto de estado para retomar el
> trabajo en cualquier sesión. La historia fina está en `git log` y el plan en
> [plan-portal.md](plan-portal.md).

## Qué es

Plataforma B2B que sustituirá al portal actual de lejan (mygo2b), con el
objetivo declarado por el cliente (**/goal**): portal **idéntico** al actual en
menú, apariencia y funciones — con la portada configurable desde el CMS — pero
con acabado más moderno. Backend .NET 10 + PostgreSQL que implementa el mismo
contrato API que consume el conector MITO de Business Central (BC no se toca:
solo URLs y credenciales de su Setup en el corte).

## Hecho (todo commiteado, 213/213 tests)

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

1. **Auditoría integral final del /goal** (visual: 14 vistas vs capturas;
   funcional: recorridos completos; técnica: tests/consola/i18n/aislamiento).
   Se lanzó y se canceló por saldo — relanzar y corregir en bucle hasta CUMPLE.
2. **Fase BC** (`plan-portal.md`): envío real del pedido a BC vía sus API OData
   (contrato `docs/contrato-api/06-api-odata-bc.md`), PDFs vía `salesDocuments`,
   estados. Requiere del cliente: tenant + client id/secret OAuth del sandbox.
3. Cosas menores anotadas: SVG real del wordmark lejan (interino: texto),
   preload completo del hero (requiere inlinar primer slide), modo Grid del
   catálogo (select deshabilitado a propósito).

## Cómo arrancar todo (dev)

```bash
docker compose up -d                      # en backend/ (PostgreSQL 17)
cd backend && dotnet test                 # 213/213
# Servidor para navegar (carpeta publicada para no bloquear builds):
dotnet publish src/B2B.Api -c Release -o /tmp/portal-run
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 /tmp/portal-run/B2B.Api.exe
```

- Portal: http://localhost:5199/es/es/dashboard — `integracion@dev.local` / `dev-password` → SELECCIONAR
- CMS: http://localhost:5199/admin (mismas credenciales) — portada en Web → Portada
- API docs: http://localhost:5199/docs
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
