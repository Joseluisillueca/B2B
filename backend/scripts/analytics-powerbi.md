# Feed de analítica para Power BI / Microsoft Fabric

Feed de **solo lectura** para conectar Power BI o Microsoft Fabric a las ventas del
portal. No expone las tablas de la aplicación: solo un esquema `analytics` con 10
informes ya agregados, leídos por un rol que **no puede ver nada más**.

## Qué instala `analytics-powerbi.sql`

- Esquema **`analytics`** con:
  - Modelo dimensional: `fact_invoice_line`, `fact_order`, `dim_client`, `dim_agent_client`.
  - **10 informes** (`rpt_01`…`rpt_10`).
- Rol de solo lectura **`analytics_ro`** con acceso únicamente a ese esquema
  (las vistas se ejecutan con los permisos de su dueño, así que el rol **no puede
  leer `SyncDocuments`** ni el resto de la base).

### Los 10 informes
| Vista | Informe |
|---|---|
| `analytics.rpt_01_sales_by_month` | Ventas por mes (facturación + unidades) |
| `analytics.rpt_02_top_models` | Top modelos más vendidos |
| `analytics.rpt_03_sales_by_client` | Ventas por cliente |
| `analytics.rpt_04_sales_by_agent` | Ventas por agente comercial |
| `analytics.rpt_05_size_curve` | Curva de tallas (unidades por talla) |
| `analytics.rpt_06_sales_by_family` | Ventas por familia / colección |
| `analytics.rpt_07_sales_by_country` | Ventas por país / mercado |
| `analytics.rpt_08_orders_by_season` | Pedidos por temporada y tipo (reposición/programación) |
| `analytics.rpt_09_order_funnel` | Embudo de pedidos por estado |
| `analytics.rpt_10_returns` | Devoluciones / SAT |

El detalle línea a línea (`fact_invoice_line`) también es consultable, por si se quieren
construir informes propios en Power BI/Fabric.

## Instalación

```bash
docker exec -i b2b-postgres psql -U b2b -d b2b < analytics-powerbi.sql
```

Es idempotente (se puede reejecutar tras cambios). **Antes de dar acceso, cambia la
contraseña** del rol:

```sql
ALTER ROLE analytics_ro PASSWORD 'una-password-fuerte';
```

## Conexión — Power BI Desktop

1. **Obtener datos → Base de datos PostgreSQL.**
2. Servidor `host:5432` (el de vuestro Postgres), Base de datos `b2b`.
3. Modo: **Import** (recomendado para informes) o **DirectQuery** (si queréis en vivo).
4. Usuario `analytics_ro` / la contraseña que hayáis puesto.
5. En el navegador, marca las vistas del esquema **`analytics`** (los `rpt_*`).
6. **Actualización incremental** (opcional): usa la columna `issue_date` de
   `fact_invoice_line` como clave de rango.

## Conexión — Microsoft Fabric

Cualquiera de estas vías (todas sobre el mismo esquema `analytics`):

- **Dataflow Gen2 / Data pipeline** → conector **PostgreSQL** → cargar las vistas a un
  Lakehouse/Warehouse. Usa `issue_date` para refresco incremental.
- **Database Mirroring de PostgreSQL** (casi en tiempo real) hacia Fabric.
- **Power BI dentro de Fabric** → conector PostgreSQL directo (igual que arriba).

## Nota de red

Si el Postgres **no es accesible desde la nube** (está on-prem o en la red interna),
Power BI/Fabric necesitan un **On-premises Data Gateway** instalado en una máquina que
sí vea la base. Si el Postgres está en un servidor accesible, se conecta directo.

## Seguridad

- `analytics_ro` es de **solo lectura** y **solo ve el esquema `analytics`**. No puede
  leer `SyncDocuments` ni las tablas de la app (verificado: `permission denied`).
- Da al equipo de BI únicamente esas credenciales; nunca las de la aplicación.
