# Catálogo modulable por cliente/agente + multiagente — Diseño

**Fecha:** 2026-09-03 · **Estado:** aprobado por el usuario (enfoque A, cinta opción C)
**Alcance:** conector NEW (`C:\BC_Projects\Mito - Conector B2B - NEW`) + portal B2B (este repo).

## Objetivo
La cinta superior del catálogo y el catálogo entero se modulan por cliente/agente:
- Un **agente** puede tener restringido lo que VE (p. ej. solo la marca ADIDAS).
- Un **cliente** puede tener restringido lo que VE Y COMPRA (p. ej. solo CALZADO).
- "Marca" y "categoría" son **atributos de producto de BC** (Item Attribute con `B2B Code`).
- Un cliente puede tener **N agentes** (multiagente), no solo el `Salesperson Code`.
- Config en BC → viaja por sync; en instancias sin BC se configura en /manage.

## Decisiones cerradas (con el usuario)
1. **Cinta = opción C**: banda nueva de chips/pestañas bajo CATÁLOGO|LOOKBOOK con las
   entradas permitidas; autogenerada por defecto y gestionable en /manage (orden,
   entradas, títulos por idioma).
2. **Suplantación = intersección** agente∩cliente (el agente jamás ve/vende fuera de
   su propia restricción ni de la del cliente).
3. **Agente adicional = cartera completa**: ve al cliente, suplanta y crea pedidos;
   cada pedido queda **atribuido al agente que lo creó**.
4. **Semántica de reglas**: lista blanca POR ATRIBUTO. Sin filas para un (sujeto,
   atributo) → sin restricción en ese atributo. Con filas → solo esos valores.
   Varios atributos con filas → intersección. Sin reglas → se ve todo.
5. Documentos históricos (pedidos/albaranes/facturas ya emitidos) NO se ocultan.

## 1 · BC (conector NEW)
Numeración libre verificada: tablas ≥80134, páginas ≥80147, codeunits ≥80181, enums ≥80120.

- **`Tab80134 "B2B Customer Agent"`** (N:M): PK `Customer No.` + `Salesperson Code`;
  FlowFields de nombres. El principal sigue siendo `Customer."Salesperson Code"`.
  - `Pag80147` ListPart en la ficha del cliente (grupo B2B Integration de PagExt80104).
  - `Pag80148` ListPart/acción en la ficha del comercial (PagExt80131).
- **`Enum80120 "B2B Visibility Subject Type"`**: Customer | Agent.
- **`Tab80135 "B2B Catalog Visibility"`**: PK SubjectType + SubjectCode + `Attribute ID`
  (TableRelation Item Attribute con Sync to B2B) + `Attribute Value ID` (TableRelation
  Item Attribute Value del atributo). FlowFields nombre/valor. `Pag80149` lista general
  + ListParts en fichas de cliente/comercial.
- **Adapters**:
  - `Cod80140` (agente): `BuildClientIdsArray` = UNIÓN (Salesperson Code = agente) ∪
    (`B2B Customer Agent`), filtrada por Sync to B2B, dedupe por SystemId
    (patrón List of [Guid]). + `visibleAttributes`.
  - `Cod80130` (cliente, codeunit 80161): + `visibleAttributes`.
  - `SanitizeId` se extrae de Cod80114 a `Cod80122.B2BUtils` y se reutiliza para los
    valueIds (identidad garantizada con los ids del catálogo de atributos).
- **Frescura**: `Cod80181 "B2B Agent Sync Job"` calcado de Cod80169 (campo
  `B2B Needs Sync` en TabExt80121, Job Queue 5 min, jerarquía maestro-primero
  reutilizando la lógica de Rep80104). Suscriptores: insert/modify/delete de Tab80134 y
  Tab80135 marcan el sujeto (cliente → `Customer."B2B Needs Sync"`; agente → flag nuevo);
  cambio de `Customer."Salesperson Code"` marca agente ANTERIOR y NUEVO. Cualquier
  Salesperson referenciado por Tab80134 se sincroniza aunque no esté en Tab80104.
- Sin cambios en Setup (no hay endpoint nuevo).

## 2 · Contrato de sync (estrictamente aditivo)
```json
// agent (añadido) y client (añadido)
"visibleAttributes": [
  { "attributeId": "marca",     "valueIds": ["adidas"] },
  { "attributeId": "categoria", "valueIds": ["calzado"] }
]
```
- `attributeId` = `Item Attribute."B2B Code"`; `valueIds` = SanitizeId(valor).
- `clientIds` del agente pasa a poder solapar entre agentes (ya era array).
- El resto del contrato NO cambia (`saleId` de pedidos single/nullable, `groupIds`, etc.).

## 3 · Portal — datos
- Tabla **`CatalogVisibility`**: Id, `SubjectType` ("client"|"agent"), `SubjectId`
  (ExternalId), `RulesJson` (jsonb `[{attributeId, valueIds[]}]`), `Source`
  ("bc"|"manual"), UpdatedAt. Índice (SubjectType, SubjectId).
- **Hook de ingesta** (junto a ClientIdentity.ApplyAsync): al upsert de un doc
  client/agent — `visibleAttributes` PRESENTE y no vacío → upsert de la fila `Source="bc"`;
  PRESENTE y vacío (`[]`) → **BC levanta la restricción: se BORRA la fila bc** (la resolución
  cae a la manual si existe); AUSENTE → no tocar (conector antiguo / payload parcial).
  Las filas `manual` nunca se pisan desde la ingesta. (El builder del conector NEW emite
  la clave SIEMPRE, aunque no haya reglas — ver §2/T12.)
  Regla de resolución en runtime: para un sujeto, **manda la fila `bc` si existe;
  si no, la `manual`** (BC es la fuente de verdad cuando está conectado).
- El multiagente NO necesita tabla: la cartera sigue en `clientIds` de los docs agent
  (verificar en la implementación que el upsert por agente no "roba" clientes —
  cada doc agent conserva su propia lista; riesgo 5.1 de la auditoría BC).

## 4 · Portal — enforcement
- **Predicado único** `CatalogScope.VisibleFor(model)`: evalúa `FamilyId` +
  `AttributesJson` del modelo contra el conjunto de valores permitidos del actor.
  El "scope" del actor se resuelve una vez por request (reglas del cliente ∩ reglas
  del agente cuando hay suplantación; agente navegando sin suplantar usa solo las suyas).
- Aplicación:
  1. `CatalogService.QueryAsync`: filtrar `models` ANTES del recorte por Ids y de
     Build → catálogo, cinta, facetas, búsqueda, ficha (q=), relacionados, PDFs
     (tech-sheet/line-sheet/catalog) y stock-export quedan cubiertos de una vez.
  2. **Checkout**: validación de líneas contra el predicado en AMBOS modos (portal y
     ERP; hoy el modo ERP no valida nada) → 400 con mensaje claro por línea.
  3. `GET /api/agent/catalog-models`: aplicar el mismo predicado (hoy lee
     CatalogModels directo).
- "Familia" se trata como **pseudo-atributo reservado `familyId`**: una regla con
  `attributeId="familyId"` restringe familias (el predicado la evalúa contra
  `CatalogModel.FamilyId`); cualquier otro `attributeId` se evalúa contra
  `AttributesJson` por su clave. Sin ambigüedad ni doble fuente.

## 5 · Cinta (opción C)
- Nueva banda en el portal (chrome/catálogo) con chips: entradas = valores permitidos
  de los atributos "de cinta" + familias visibles. Clic = aplica el filtro de facetas
  existente (`?family=` / `?a.{clave}=`). "TODO" como primera entrada.
- **Autogenerada** cuando no hay config. Config de instancia en /manage
  ("Catálogo → Cinta"): qué atributos alimentan la cinta, orden de entradas,
  ocultar/mostrar, título por idioma (locales es/en/fr/it). Persistencia:
  **columna `CatalogRibbonJson` en `IntegrationSettings`** (singleton por instancia,
  patrón branding/orders-mode; una migración, sin tabla nueva).
- La cinta NUNCA enseña entradas fuera del scope del actor (se alimenta de las facetas
  ya filtradas por el predicado).
- Diseño de altísima calidad: skills + bucle de crítica + auditor UX/diseño,
  desktop y móvil (scroll-x con snap en móvil).

## 6 · /manage
- **Ficha de cliente** y **ficha/schema de agente**: sección "Visibilidad de catálogo"
  — por atributo, chips de valores permitidos (patrón sales-rules/tr-chip). Origen
  visible: filas de BC en solo-lectura informativa (candado + "lo fija BC"); manual
  editable. Fuente de valores: entidades attribute ya sincronizadas.
- **Cinta**: gestor con vista previa (activar/ocultar/ordenar, títulos por idioma).
- **Agentes**: cartera multi-select existente + en la ficha del cliente, lista inversa
  de "sus agentes" (consulta sobre los docs agent).

## 7 · Pedidos multiagente
- El pedido creado por un agente suplantando lleva `saleId` = SystemId del agente
  creador (hoy viaja vacío) en el JSON saliente a BC; BC lo resuelve a Salesperson.
- El doc de pedido nativo del portal guarda también el agente creador (auditoría).

## 8 · Calidad y pruebas
- Tests de integración: predicado en todos los endpoints (actor restringido ve N,
  sin restricción ve todo, intersección en suplantación), checkout denegando líneas
  (ambos modos), ingesta bc-vs-manual (BC no pisa manual y viceversa), multiagente
  (dos agentes con el mismo cliente; el upsert no roba carteras).
- Playwright: cinta por actor, catálogo/ficha/búsqueda filtrados, /manage config.
- Cierre: auditor de código (portal + AL) + crítico de diseño + auditor UX en paralelo,
  bucle de arreglos, verificación con el usuario en localhost. Deploy SOLO a ALMA
  tras su OK. El conector NEW lo compila/publica el usuario.

## Fuera de alcance (explícito)
- Modo "denylist" ("todo menos X") — extensión futura si las listas blancas se hinchan.
- Visibilidad por grupo de cliente — extensión futura (el sujeto es extensible).
- Ocultar documentos históricos.
