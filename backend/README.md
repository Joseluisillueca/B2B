# B2B Backend

API REST en **.NET 10 + PostgreSQL** que implementa el contrato documentado en [docs/contrato-api/](../docs/contrato-api/) — la misma API que consume el conector de Business Central.

## Estructura

```
backend/
├── B2B.slnx                  Solución
├── src/B2B.Api/              API (minimal APIs, EF Core + Npgsql)
│   ├── Auth/                 Login compatible con el conector (POST /api/auth/login)
│   └── Data/                 DbContext y entidades
├── tests/B2B.Api.Tests/      Tests de contrato (xUnit + WebApplicationFactory)
└── docker-compose.yml        PostgreSQL 17 para desarrollo
```

## Desarrollo

```bash
# Base de datos
docker compose up -d

# Tests (usan EF InMemory, no necesitan la BD)
dotnet test

# Ejecutar la API
dotnet run --project src/B2B.Api
```

## Endpoints implementados

| Endpoint | Contrato | Estado |
|---|---|---|
| `POST /api/auth/login` | [01 §1](../docs/contrato-api/01-autenticacion-y-convenciones.md) | ✅ Con tests |
| `PUT` de ingesta (19 rutas: catálogo, stock, maestros, clientes, agentes, pedidos, documentos) | [01 §4.2](../docs/contrato-api/01-autenticacion-y-convenciones.md) | ✅ Con tests |
| `PUT /api/clients/{id}/users/admin` y `/shipping-addresses/{id}` (sufijos hardcodeados del conector) | [04](../docs/contrato-api/04-clientes-agentes.md) | ✅ Con tests |
| `GET /api/catalog/offers` (con body `{"modelId"}`) y `DELETE /api/catalog/offers/{id}` — ciclo GET-comparar-DELETE | [01 §3.4](../docs/contrato-api/01-autenticacion-y-convenciones.md) | ✅ Con tests |
| `GET|POST /api/orders/search` (con body `{"search":[{"all":true}]}`) | [04 §6](../docs/contrato-api/04-clientes-agentes.md) | ✅ Con tests |
| `GET /api/admin/sync-documents` (CMS: comunicación recibida, filtro y paginación) | — | ✅ Con tests |
| `GET /health` | — | ✅ |

Los PUT de modelos y productos además **normalizan** el payload a las tablas de dominio `catalog_models` y `catalog_products` (nombre es_ES, referencia, familia, segmentos, SKU/EAN, talla extraída de `attributes.tallas`, case packs con bundle) en la misma transacción que el crudo — son las tablas que consumirán el CMS y el front de pedidos.

El login acepta `{email, password, type, longDuration}` y devuelve `{token, tokenExpiresIn}` con la fecha absoluta en formato `dd/MM/yyyy HH:mm:ss` que el `Evaluate` de BC-es sabe parsear (ver hallazgo en el contrato §1.3).

Los PUT de sincronización exigen `Authorization: Bearer` y hacen upsert idempotente del payload crudo en la tabla `sync_documents` (JSONB) con tipo de entidad, id externo y timestamps — el conector puede sincronizar ya contra este backend sin perder ningún dato, y la normalización a tablas de dominio vendrá en iteraciones posteriores. Ver rutas en [SyncEndpoints.cs](src/B2B.Api/Sync/SyncEndpoints.cs); al hacer el corte se configuran estas URLs en el Setup del conector en BC.

## Configuración

`appsettings.json` trae valores de desarrollo. En producción, sobreescribir vía variables de entorno:

- `ConnectionStrings__Default` — cadena de conexión PostgreSQL
- `Jwt__SigningKey` — clave de firma HS256 (mínimo 32 bytes; la de appsettings es solo para desarrollo)
- `Jwt__LongDurationHours` / `Jwt__ShortDurationHours` — vida del token (debe superar el intervalo de refresh del conector: 60 min + margen de 10)
