# B2B Platform

Plataforma B2B para venta mayorista de moda (pedidos por artículo y talla), integrada con Microsoft Dynamics 365 Business Central a través del conector existente **MITO - Conector B2B**.

Este proyecto sustituirá al B2B actual implementando **la misma API** que hoy consume el conector de BC, de modo que BC no requiere cambios (solo URL base y credenciales en el Setup en el corte final).

## Estructura

| Carpeta | Contenido |
|---|---|
| `backend/` | API REST (.NET 8 + PostgreSQL): auth JWT, recepción de maestros desde BC, motor de precios, pedidos |
| `admin/` | CMS de administración y monitorización (logs de sincronización, catálogo, pedidos, clientes) |
| `front/` | Portal B2B de clientes: login, catálogo por segmento/tarifa, pedido por matriz de tallas, documentos |
| `docs/` | Contrato API extraído del conector BC (especificación ejecutable) |

## Stack

- **Backend:** .NET 8, PostgreSQL
- **Integración BC:** el conector AL empuja maestros/documentos vía REST (BC → B2B) y el B2B escribe pedidos/clientes vía API pages OData (B2B → BC)

## Plan de fases

1. **Fase 1** — Backend + auth + recepción de maestros desde BC (en paralelo al B2B actual, sin riesgo)
2. **Fase 2** — CMS de administración/monitorización
3. **Fase 3** — Front de pedidos (catálogo → carrito por tallas → pedido → OData a BC)
4. **Fase 4** — Documentos (albaranes/facturas/tracking), ofertas, case packs, corte final

## Documentación

El contrato completo de la API está en [docs/contrato-api/](docs/contrato-api/) — documenta todos los endpoints que el backend debe exponer (dirección BC → B2B) y las APIs OData de BC que debe consumir (dirección B2B → BC).
