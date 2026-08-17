# PRODUCT.md — B2B Platform (lejan)

## Qué es
Plataforma B2B de venta mayorista de calzado (marca **lejan™**) que sustituirá al portal actual (mygo2b). Los clientes (tiendas) se logean y hacen pedidos **por artículo y talla** contra ventanas de servicio (REPOSICIÓN = stock físico; PROGRAMACIÓN = campaña/pre-venta). Backend .NET 10 + PostgreSQL alimentado desde Business Central vía el conector MITO.

## Usuarios
- **Cliente B2B** (tienda/distribuidor): compra por curva de tallas, consulta pedidos, albaranes con tracking y facturas. Perfil profesional, compra rápida y repetitiva; a menudo desde tablet o portátil en la tienda.
- **Agente comercial**: hace pedidos en nombre de sus clientes (multi-cliente).
- **Administrador (interno)**: usa el CMS `/admin` (ya construido) para monitorizar catálogo, precios, stock y comunicación con BC.

## Superficie actual en construcción
**Catálogo comprable** (`/shop`): réplica funcional del catálogo del portal actual pero moderna. Verdad de referencia: `docs/front-referencia/17-catalog-catalog.png` — el pedido se hace EN el listado: cada artículo muestra foto, nombre, referencia, PVD y la curva de tallas (36–46) con casillas de cantidad y disponibilidad por talla (semáforo). Filtros laterales (línea, disponibilidad, edad, silueta, colección). Carrito por ventana de servicio (REPOSICIÓN (n) en el header).

## Identidad visual (pinned por la marca)
- Logo/espíritu **lejan™**: tipografía negra ultra-bold, blanco y negro dominante, acento azul eléctrico (#2f2fff aprox del portal actual), fotografía de producto sobre fondo neutro claro.
- El portal actual es funcional pero plano; el encargo del cliente es literalmente: **"igual que ese, pero más bonito/moderno"**. Mantener la lógica de uso idéntica (matriz en el listado), elevar acabado: jerarquía, espaciado, estados, microinteracciones sobrias.
- Modo de superficie: **Operate** (herramienta de compra profesional; escaneabilidad y velocidad mandan).

## Datos
API propia: `POST /api/auth/login` (JWT) y datos normalizados del conector (`catalog_models`, `catalog_products` con talla, `offers` PVD/PVP, `stock_levels` por ventana, `service_windows`). Idiomas del dominio: es (principal), en, fr, it.

## Supuestos (inferidos del brief, no preguntados)
- El front nuevo se sirve desde el backend (`wwwroot/`) en esta fase; si crece, se migrará a `front/` con tooling propio.
- Las imágenes de producto llegarán vía `model-image` (URI); mientras no existan se usa placeholder neutro.
- Checkout final contra BC (OData) queda para una fase posterior; el carrito de esta fase es local.
