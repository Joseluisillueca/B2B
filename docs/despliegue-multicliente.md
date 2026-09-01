# Despliegue multi-cliente (una instancia por cliente)

El portal es un producto **single-tenant**: cada cliente (marca) corre en su propia instancia
con su propia base de datos. El código es ÚNICO (este repo); lo que cambia por cliente es la
configuración: variables de entorno, marca (Conexiones → Marca), contenido (Portada/Lookbook)
y su conexión a Business Central.

```
GitHub (main) ──► Railway proyecto "b2b mito"
                  ├─ b2b-api          + Postgres        ← Way2Growth (MITO PROJECTS)
                  └─ b2b-almaenpena   + Postgres-gsZN   ← ALMA EN PENA
                  └─ (siguiente cliente…)
```

## Alta de un cliente nuevo (checklist)

### 1) Railway (≈10 min)
```bash
railway add -d postgres -s postgres-<cliente>     # BD propia
railway add -s b2b-<cliente>                      # servicio de la app
railway domain --service b2b-<cliente>            # URL pública
railway up --service b2b-<cliente>                # deploy (Dockerfile del repo)
```

Variables del servicio (patrón; generar secretos NUEVOS por cliente):

| Variable | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Default` | `Host=${{postgres-<cliente>.PGHOST}};Port=5432;Database=${{postgres-<cliente>.PGDATABASE}};Username=${{postgres-<cliente>.PGUSER}};Password=${{postgres-<cliente>.PGPASSWORD}}` |
| `Seed__AdminEmail` / `Seed__AdminPassword` | admin del back-office (contraseña nueva) |
| `Seed__UserEmail` / `Seed__UserPassword` | usuario de integración del conector BC (nueva) |
| `Jwt__SigningKey` | clave aleatoria larga NUEVA (no compartir entre clientes) |
| `Email__Mode` + `Email__Brevo__ApiKey` + `Email__From` + `Email__Smtp__*` | correo (Brevo); idealmente remitente del cliente |
| `Payments__Mode` + `Payments__Stripe__SecretKey` | pagos (si aplica) |
| `Portal__BaseUrl` | la URL pública del servicio (para los enlaces de los emails) |

### 2) Marca y contenido (en el propio portal)
- `/manage → Integración → Conexiones → **Marca del portal**`: nombre, color de acento y logo.
  Se aplica a portal, back-office, emails y PDFs. (API: `PUT /api/admin/integration/branding`.)
- `/manage → Contenido`: Portada y Lookbook del cliente (imágenes, textos, locales).
- `/manage → Integración → Conexiones → Modo de pedidos`: `portal` (comunica pedidos a BC)
  o `erp` según el cliente.

### 3) Business Central del cliente
Seguir `docs/manual-configuracion-bc.md`: App registration en SU tenant de Azure (admin
consent + redirect `https://businesscentral.dynamics.com/OAuthLanding.htm`), instalar el
conector, permission set B2BINTEGRATION, Job Queues B2BINT, plantillas B2B (países, IVA,
formas de pago), y rellenar `Conexiones` en /manage (URL base con SU tenant/companyId +
client id/secret). El conector del cliente apunta a la URL de SU instancia con SU usuario
de integración.

### 4) Dominio propio (opcional)
Railway → service → Settings → Custom Domain (p.ej. `b2b.cliente.com`) + CNAME en su DNS.
Actualizar `Portal__BaseUrl` al dominio final.

## Actualizar TODAS las instancias
Hoy: `railway up --service <cada-servicio>` tras cada push (el asistente lo hace).
Recomendado al crecer: conectar los servicios al repo de GitHub (auto-deploy de `main`)
para que un push actualice todas las instancias a la vez.

## Instancias actuales

| Cliente | Servicio | URL | BD |
|---|---|---|---|
| Way2Growth (MITO PROJECTS) | `b2b-api` | https://b2b-api-production-9b41.up.railway.app | `Postgres` |
| ALMA EN PENA | `b2b-almaenpena` | https://b2b-almaenpena-production.up.railway.app | `Postgres-gsZN` |

Las credenciales de cada instancia viven en las variables de su servicio en Railway
(`Seed__AdminPassword`, `Seed__UserPassword`) — no se guardan en el repo.
