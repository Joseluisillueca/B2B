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
| `POST /api/auth/login` | [01-autenticacion §1](../docs/contrato-api/01-autenticacion-y-convenciones.md) | ✅ Con tests |
| `GET /health` | — | ✅ |

El login acepta `{email, password, type, longDuration}` y devuelve `{token, tokenExpiresIn}` con la fecha absoluta en formato `dd/MM/yyyy HH:mm:ss` que el `Evaluate` de BC-es sabe parsear (ver hallazgo en el contrato §1.3).

## Configuración

`appsettings.json` trae valores de desarrollo. En producción, sobreescribir vía variables de entorno:

- `ConnectionStrings__Default` — cadena de conexión PostgreSQL
- `Jwt__SigningKey` — clave de firma HS256 (mínimo 32 bytes; la de appsettings es solo para desarrollo)
- `Jwt__LongDurationHours` / `Jwt__ShortDurationHours` — vida del token (debe superar el intervalo de refresh del conector: 60 min + margen de 10)
