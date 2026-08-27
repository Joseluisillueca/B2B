# Despliegue de B2BNew en Railway (URL fija para el conector BC)

Objetivo: tener el portal + la API de ingesta en una **URL pública estable**
(`https://xxx.up.railway.app`) que Business Central pueda alcanzar 24/7.

El proyecto ya está listo: **migra la BD sola al arrancar** y **crea el usuario de
integración** a partir de variables de entorno. Solo hay que desplegar y configurar.

---

## 0. Requisitos
- Cuenta en https://railway.app (login con GitHub).
- El repo en GitHub con estos archivos ya commiteados: `Dockerfile`, `.dockerignore`.
  ```bash
  git add Dockerfile .dockerignore DEPLOY-RAILWAY.md
  git commit -m "Despliegue: Dockerfile de produccion para Railway"
  git push
  ```

## 1. Crear el proyecto y la base de datos
1. Railway → **New Project** → **Deploy PostgreSQL**. Se crea un servicio **Postgres**.
   (Railway le pone variables `PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD`, `PGDATABASE`.)

## 2. Añadir el servicio de la app
2. En el mismo proyecto → **New** → **GitHub Repo** → elige tu repo de B2BNew.
   Railway detecta el **Dockerfile** de la raíz y construye la imagen automáticamente.
   (Si el repo es privado, autoriza Railway en GitHub.)

## 3. Variables de entorno del servicio de la app
En el servicio de la app → pestaña **Variables** → añade:

| Variable | Valor |
|---|---|
| `ConnectionStrings__Default` | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};SSL Mode=Prefer;Trust Server Certificate=true` |
| `Jwt__SigningKey` | *(una clave larga y aleatoria, mín. 40 caracteres — ver abajo)* |
| `Jwt__Issuer` | `b2b-platform` |
| `Jwt__Audience` | `b2b-clients` |
| `Seed__UserEmail` | `integracion@lejan.app` *(usuario de integración del conector)* |
| `Seed__UserPassword` | *(contraseña fuerte — la misma irá en BC)* |
| `Seed__AdminEmail` | `admin@lejan.app` *(admin del CMS)* |
| `Seed__AdminPassword` | *(contraseña fuerte del CMS)* |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

> **Nota sobre `${{Postgres.*}}`**: son referencias a las variables del servicio Postgres.
> Si tu servicio de BD NO se llama exactamente `Postgres`, ajusta el nombre
> (p.ej. `${{postgres.PGHOST}}`).

**Generar la `Jwt__SigningKey`** (elige una):
- PowerShell: `[Convert]::ToBase64String((1..48 | %{Get-Random -Max 256}))`
- Bash: `openssl rand -base64 48`

`PORT` lo inyecta Railway solo — **no** lo pongas tú.

## 4. Generar la URL pública fija
En el servicio de la app → **Settings** → **Networking** → **Generate Domain**.
Obtienes algo como `https://b2bnew-production.up.railway.app`. **Esa es la URL fija**
que usará BC. (Opcional: puedes añadir un dominio propio tipo `b2b.lejan.com`.)

Añade además esta variable con esa URL (para los enlaces de los emails):
| Variable | Valor |
|---|---|
| `Portal__BaseUrl` | `https://<tu-dominio>.up.railway.app` |

## 5. Desplegar y verificar
1. Railway redespliega al guardar variables (o **Deploy** manual).
2. Mira los **Logs**: debe aparecer que aplica migraciones y arranca en el puerto.
3. Abre `https://<tu-dominio>.up.railway.app/es/es/login` → debe cargar el portal.
4. Prueba el login del conector (desde tu PC):
   ```bash
   curl -s -X POST https://<tu-dominio>.up.railway.app/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"integracion@lejan.app","password":"<TU_PASSWORD>","type":"global","longDuration":true}'
   ```
   Debe devolver `{ "token": "...", "tokenExpiresIn": "dd/MM/yyyy HH:mm:ss" }`.

## 6. Configurar Business Central
En **B2B Integration Setup** de BC:
- `Base Url` = `https://<tu-dominio>.up.railway.app`
- `Integration User` / `Integration Password` = los de `Seed__UserEmail` / `Seed__UserPassword`
- El resto de URLs de entidad según la tabla de mapeo (ver el chat / la guía de configuración).
- **Test Connection** → debe traer token. A partir de ahí, sincroniza maestros y stock.

---

## Notas y resolución de problemas
- **Base de datos vacía / usuario de integración no aparece**: el seed del usuario de
  integración solo corre **si la tabla Users está vacía** (primer arranque). Si ya
  arrancó sin las variables `Seed__*`, bórralas y vuelve a crear la BD, o crea el
  usuario a mano. Pon las variables **antes** del primer deploy.
- **Error de conexión SSL a Postgres**: si falla con `SSL Mode=Prefer`, prueba
  `SSL Mode=Require;Trust Server Certificate=true`, y viceversa.
- **Arranque falla por JWT**: si ves el error de "Jwt:SigningKey sigue siendo la clave
  de desarrollo", es que falta `Jwt__SigningKey`. Añádela.
- **Coste**: Railway da un crédito inicial gratis; luego ~5 €/mes el plan Hobby. Mantén
  el servicio **siempre activo** (no lo pauses) para que BC pueda empujar datos.
- **Pagos/Email**: por defecto van en modo `mock`/`log` (no cobran ni envían). Para la
  integración con BC no hacen falta. Si quieres pagos reales, configura `Payments__Mode=stripe`
  y las claves; para email real, `Email__Mode=smtp` y el SMTP.
