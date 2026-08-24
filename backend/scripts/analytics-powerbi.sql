-- ============================================================================
--  Feed de analítica de solo lectura para Power BI / Microsoft Fabric
-- ----------------------------------------------------------------------------
--  El portal no tiene tablas de ventas: pedidos, albaranes y facturas llegan
--  crudos como jsonb en "SyncDocuments". Este script crea un esquema `analytics`
--  con vistas que DESANIDAN ese jsonb en tablas planas (modelo dimensional) y
--  10 informes agregados, más un rol de SOLO LECTURA que solo ve estas vistas
--  (no las tablas base: las vistas se ejecutan con los permisos de su dueño).
--
--  Idempotente: se puede volver a ejecutar. Aplicar con:
--    docker exec -i b2b-postgres psql -U b2b -d b2b < analytics-powerbi.sql
-- ============================================================================

create schema if not exists analytics;

-- ────────────────────────────────────────────────────────────────────────────
--  MODELO DIMENSIONAL (hechos + dimensiones)
-- ────────────────────────────────────────────────────────────────────────────

-- Hecho: una fila por LÍNEA de factura (grano de la mayoría de informes).
-- La talla vive dentro del SKU (nº de artículo + variante); se obtiene quitando
-- el prefijo del modelo, igual que DocumentProjections.Size.
create or replace view analytics.fact_invoice_line as
select
    d."ExternalId"                                                        as invoice_id,
    d."Payload"->>'number'                                                as invoice_number,
    (d."Payload"->>'issueDate')::timestamptz                              as issue_date,
    to_char((d."Payload"->>'issueDate')::timestamptz, 'YYYY-MM')          as month,
    d."ParentId"                                                          as client_id,
    d."Payload"->>'status'                                                as invoice_status,
    line->'productInfo'->>'modelExternalReference'                        as model_ref,
    line->'productInfo'->>'modelId'                                       as model_id,
    coalesce(line->'productName'->>'es_ES',
             line->'productInfo'->'name'->>'es_ES')                       as product_name,
    line->'productInfo'->>'sku'                                           as sku,
    nullif(
      case
        when line->'productInfo'->>'sku' like (line->'productInfo'->>'modelExternalReference') || '%'
          then substr(line->'productInfo'->>'sku',
                      length(line->'productInfo'->>'modelExternalReference') + 1)
        else ''
      end, '')                                                            as size,
    (line->'transactionInfo'->'info'->>'quantity')::numeric              as quantity,
    (line->'transactionInfo'->'info'->'price'->>'value')::numeric        as unit_price,
    (line->'transactionInfo'->'info'->'amount'->>'value')::numeric       as amount,
    (line->'transactionInfo'->'taxes'->0->>'percent')::numeric           as tax_percent,
    coalesce(line->'transactionInfo'->'info'->'amount'->>'code', 'EUR')  as currency
from "SyncDocuments" d
cross join lateral jsonb_array_elements(d."Payload"->'lines') as line
where d."EntityType" = 'invoice';

-- Hecho: cabecera de pedido (para embudo de estados, temporada y devoluciones).
create or replace view analytics.fact_order as
select
    d."ExternalId"                                                        as order_id,
    d."Payload"->>'externalReference'                                     as order_number,
    (d."Payload"->>'orderedDate')::timestamptz                           as ordered_date,
    to_char((d."Payload"->>'orderedDate')::timestamptz, 'YYYY-MM')        as month,
    d."ParentId"                                                          as client_id,
    coalesce(nullif(d."Payload"->>'type', ''), 'STANDARD')               as order_type,
    lower(coalesce(nullif(d."Payload"->>'status', ''), 'open'))          as order_status,
    d."Payload"->>'seasonId'                                              as season,
    (d."Payload"#>>'{totals,total,value}')::numeric                       as total,
    coalesce(d."Payload"#>>'{totals,total,code}', 'EUR')                  as currency
from "SyncDocuments" d
where d."EntityType" = 'order';

-- Dimensión: cliente.
create or replace view analytics.dim_client as
select
    d."ExternalId"                                as client_id,
    d."Payload"->>'name'                          as client_name,
    d."Payload"->'groupIds'->>0                   as client_group,
    d."Payload"->'markets'->>0                    as country,
    d."Payload"#>>'{fiscalInfo,address,city}'     as city,
    (d."Payload"#>>'{creditInfo,value}')::numeric as credit_limit
from "SyncDocuments" d
where d."EntityType" = 'client';

-- Dimensión: agente ↔ cliente (una fila por cliente de la cartera del agente).
create or replace view analytics.dim_agent_client as
select
    d."Payload"->>'name'   as agent_name,
    d."ExternalId"         as agent_id,
    cid                    as client_id
from "SyncDocuments" d
cross join lateral jsonb_array_elements_text(d."Payload"->'clientIds') as cid
where d."EntityType" = 'agent';

-- ────────────────────────────────────────────────────────────────────────────
--  10 INFORMES
-- ────────────────────────────────────────────────────────────────────────────

-- 01 · Ventas por mes (facturación e unidades)
create or replace view analytics.rpt_01_sales_by_month as
select month,
       sum(amount)              as invoiced_amount,
       sum(quantity)            as units,
       count(distinct invoice_id) as invoices
from analytics.fact_invoice_line
group by month
order by month;

-- 02 · Top modelos (más vendidos)
create or replace view analytics.rpt_02_top_models as
select l.model_ref,
       coalesce(m."Name", l.model_ref) as model_name,
       sum(l.quantity)                 as units,
       sum(l.amount)                   as amount
from analytics.fact_invoice_line l
left join "CatalogModels" m on m."ExternalReference" = l.model_ref
group by l.model_ref, coalesce(m."Name", l.model_ref)
order by amount desc;

-- 03 · Ventas por cliente
create or replace view analytics.rpt_03_sales_by_client as
select l.client_id,
       c.client_name,
       c.client_group,
       c.country,
       sum(l.amount)              as amount,
       sum(l.quantity)            as units,
       count(distinct l.invoice_id) as invoices
from analytics.fact_invoice_line l
left join analytics.dim_client c on c.client_id = l.client_id
group by l.client_id, c.client_name, c.client_group, c.country
order by amount desc;

-- 04 · Ventas por agente comercial
create or replace view analytics.rpt_04_sales_by_agent as
select a.agent_name,
       sum(l.amount)              as amount,
       sum(l.quantity)            as units,
       count(distinct l.client_id) as clients,
       count(distinct l.invoice_id) as invoices
from analytics.fact_invoice_line l
join analytics.dim_agent_client a on a.client_id = l.client_id
group by a.agent_name
order by amount desc;

-- 05 · Curva de tallas (unidades e importe por talla)
create or replace view analytics.rpt_05_size_curve as
select size,
       sum(quantity) as units,
       sum(amount)   as amount
from analytics.fact_invoice_line
where size is not null
group by size
order by case when size ~ '^[0-9]+$' then size::int else 9999 end, size;

-- 06 · Ventas por familia / colección (une con el catálogo)
create or replace view analytics.rpt_06_sales_by_family as
select coalesce(nullif(m."FamilyId", ''), '(sin familia)') as family,
       sum(l.quantity) as units,
       sum(l.amount)   as amount
from analytics.fact_invoice_line l
left join "CatalogModels" m on m."ExternalReference" = l.model_ref
group by coalesce(nullif(m."FamilyId", ''), '(sin familia)')
order by amount desc;

-- 07 · Ventas por país / mercado
create or replace view analytics.rpt_07_sales_by_country as
select coalesce(c.country, '(sin país)') as country,
       sum(l.amount)              as amount,
       sum(l.quantity)            as units,
       count(distinct l.client_id) as clients
from analytics.fact_invoice_line l
left join analytics.dim_client c on c.client_id = l.client_id
group by coalesce(c.country, '(sin país)')
order by amount desc;

-- 08 · Pedidos por temporada y tipo (reposición / programación)
create or replace view analytics.rpt_08_orders_by_season as
select coalesce(nullif(season, ''), '(sin temporada)') as season,
       order_type,
       count(*)     as orders,
       sum(total)   as amount
from analytics.fact_order
group by coalesce(nullif(season, ''), '(sin temporada)'), order_type
order by amount desc;

-- 09 · Embudo de pedidos (por estado)
create or replace view analytics.rpt_09_order_funnel as
select order_status as status,
       count(*)   as orders,
       sum(total) as amount
from analytics.fact_order
group by order_status
order by amount desc;

-- 10 · Devoluciones / SAT (pedidos de devolución: tipo NOT_DEFINED o importe < 0)
create or replace view analytics.rpt_10_returns as
select coalesce(month, '(sin fecha)') as month,
       count(*)   as return_docs,
       sum(total) as amount
from analytics.fact_order
where upper(coalesce(order_type, '')) = 'NOT_DEFINED' or total < 0
group by coalesce(month, '(sin fecha)')
order by month;

-- ────────────────────────────────────────────────────────────────────────────
--  ROL DE SOLO LECTURA para Power BI / Fabric
--  Solo ve el esquema `analytics`; NO puede leer las tablas base (las vistas se
--  ejecutan con los permisos de su dueño). CAMBIA la contraseña antes de usar.
-- ────────────────────────────────────────────────────────────────────────────
do $$
begin
    if not exists (select 1 from pg_roles where rolname = 'analytics_ro') then
        create role analytics_ro login password 'CAMBIA_ESTA_PASSWORD';
    end if;
end $$;

grant usage on schema analytics to analytics_ro;
grant select on all tables in schema analytics to analytics_ro;
alter default privileges in schema analytics grant select on tables to analytics_ro;

-- Que NO pueda ver otros esquemas ni tablas de la aplicación
revoke all on schema public from analytics_ro;
