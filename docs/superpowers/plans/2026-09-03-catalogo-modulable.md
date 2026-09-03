# Catálogo modulable por cliente/agente — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Visibilidad de catálogo por cliente/agente basada en valores de atributo (lista blanca, intersección en suplantación), cinta superior configurable (opción C) y multiagente por cliente, según `docs/superpowers/specs/2026-09-03-catalogo-modulable-design.md`.

**Architecture:** Portal: tabla `CatalogVisibility` (Source bc|manual) + hook de ingesta + un predicado único (`VisibilityScope`) enchufado al pipeline del catálogo y a 3 costuras (checkout ambos modos, agent/catalog-models, saleId). Cinta servida por `/api/shop/ribbon` (computada server-side sobre facetas ya filtradas + config `CatalogRibbonJson`). Conector NEW: `B2B Customer Agent` (unión en clientIds), `B2B Catalog Visibility` → `visibleAttributes` embebido, job de agentes.

**Tech Stack:** .NET 10 minimal APIs + EF Core/Npgsql (jsonb), xUnit + WebApplicationFactory (InMemory), vanilla JS (portal/manage), AL BC22+ (repo aparte, compila el usuario).

**Convenciones de ejecución de ESTE repo** (el ejecutor las sigue SIEMPRE):
- Antes de compilar/testear: `powershell -NoProfile -Command "Get-Process -Name B2B.Api -ErrorAction SilentlyContinue | Stop-Process -Force"` (el server local bloquea el exe).
- Tests: `dotnet test backend/tests/B2B.Api.Tests/B2B.Api.Tests.csproj -v q --nologo` (suite actual: 468 verdes; nunca dejarla en rojo al commitear).
- Tras `dotnet ef migrations add X --project backend/src/B2B.Api/B2B.Api.csproj`, compilar SIEMPRE antes de arrancar con `--no-build`.
- Relanzar server para las tareas de UI: `ASPNETCORE_ENVIRONMENT=Development Portal__OrdersMode=portal nohup dotnet run --project backend/src/B2B.Api --no-build --urls http://localhost:5300 &`.
- Commits pequeños por tarea, mensaje en castellano, `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Decisión de spec que aplica a varios pasos: **whitelist estricta** — si hay regla para un atributo, el modelo debe TENER ese atributo con valor permitido (modelo sin el atributo → oculto). "familyId" es pseudo-atributo contra `CatalogModel.FamilyId`.
- Normalización compartida: usar SIEMPRE **`CatalogVocabulary.Slug`** (ya existe en
  `Shop/CatalogVocabulary.cs:80-93`, paridad completa con SanitizeId de BC: minúsculas,
  espacios y `/ \ _ .` → `-`, colapso de `--` y recorte de `-` en extremos). PROHIBIDO
  definir otro slug. Claves y valores se comparan SIEMPRE en slug.
- Semántica de regla con `valueIds: []` CONFIGURADA: se ignora (no restringe) — protege de
  guardados a medias en /manage. Una intersección COMPUTADA que quede vacía SÍ bloquea.

---

## PARTE A — Portal backend (TDD)

### Task 1: Entidad CatalogVisibility + migración

**Files:**
- Create: `backend/src/B2B.Api/Data/CatalogVisibility.cs`
- Modify: `backend/src/B2B.Api/Data/AppDbContext.cs` (DbSet + config, junto a SalesRule)

- [ ] **Step 1: Crear la entidad**

```csharp
namespace B2B.Api.Data;

// Visibilidad de catálogo por sujeto (cliente o agente): lista blanca POR ATRIBUTO.
// RulesJson: [{"attributeId":"marca","valueIds":["adidas"]}] — attributeId y valueIds
// en slug (la misma moneda que emite BC). Source: "bc" (proyectada del sync, BC la
// pisa en cada re-envío) | "manual" (editada en /manage, el sync NUNCA la toca).
// En runtime, para un sujeto manda la fila "bc" si existe; si no, la "manual".
public class CatalogVisibility
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SubjectType { get; set; } = "";   // "client" | "agent"
    public string SubjectId { get; set; } = "";     // ExternalId (SystemId de BC)
    public string RulesJson { get; set; } = "[]";
    public string Source { get; set; } = "manual";  // "bc" | "manual"
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: DbSet + configuración en AppDbContext** (patrón SalesRule)

```csharp
public DbSet<CatalogVisibility> CatalogVisibilities => Set<CatalogVisibility>();
// en OnModelCreating:
modelBuilder.Entity<CatalogVisibility>(v =>
{
    v.ToTable("catalog_visibility");
    v.HasKey(x => x.Id);
    v.Property(x => x.SubjectType).HasMaxLength(20);
    v.Property(x => x.SubjectId).HasMaxLength(120);
    v.Property(x => x.Source).HasMaxLength(10);
    v.Property(x => x.RulesJson).HasColumnType("jsonb");
    v.HasIndex(x => new { x.SubjectType, x.SubjectId, x.Source }).IsUnique();
});
```

- [ ] **Step 3: Migración y build**

Run: `dotnet ef migrations add AddCatalogVisibility --project backend/src/B2B.Api/B2B.Api.csproj && dotnet build backend/src/B2B.Api/B2B.Api.csproj -v q --nologo`
Expected: `0 Errores`.

- [ ] **Step 4: Suite en verde y commit**

Run: `dotnet test ...` → 468 verdes. `git add backend && git commit -m "Visibilidad de catálogo (1): entidad CatalogVisibility (bc|manual)"`.

### Task 2: VisibilityScope — el predicado único (TDD puro)

**Files:**
- Create: `backend/src/B2B.Api/Shop/VisibilityScope.cs`
- Test: `backend/tests/B2B.Api.Tests/VisibilityScopeTests.cs`

- [ ] **Step 1: Tests unitarios que FALLAN** (escribir todos, sin server)

```csharp
using B2B.Api.Data;
using B2B.Api.Shop;

public class VisibilityScopeTests
{
    private static CatalogModel Model(string family = "calzado", string attrsJson = "{}") =>
        new() { ExternalId = "m1", FamilyId = family, AttributesJson = attrsJson, Active = true };

    private static VisibilityScope Scope(string rulesJson) =>
        VisibilityScope.FromRules([rulesJson]);

    [Fact] public void SinReglas_TodoVisible() =>
        Assert.True(VisibilityScope.Unrestricted.Visible(Model()));

    [Fact] public void ReglaDeMarca_SoloEsaMarca()
    {
        var s = Scope("""[{"attributeId":"marca","valueIds":["adidas"]}]""");
        Assert.True(s.Visible(Model(attrsJson: """{"Marca":"ADIDAS"}""")));   // slug de clave y valor
        Assert.False(s.Visible(Model(attrsJson: """{"Marca":"NIKE"}""")));
    }

    [Fact] public void WhitelistEstricta_ModeloSinElAtributo_Oculto()
    {
        var s = Scope("""[{"attributeId":"marca","valueIds":["adidas"]}]""");
        Assert.False(s.Visible(Model(attrsJson: "{}")));
    }

    [Fact] public void FamilyId_EsPseudoAtributo()
    {
        var s = Scope("""[{"attributeId":"familyId","valueIds":["calzado"]}]""");
        Assert.True(s.Visible(Model(family: "calzado")));
        Assert.False(s.Visible(Model(family: "limpieza")));
    }

    [Fact] public void VariosAtributos_Interseccion()
    {
        var s = Scope("""[{"attributeId":"marca","valueIds":["adidas"]},{"attributeId":"categoria","valueIds":["calzado"]}]""");
        Assert.True(s.Visible(Model(attrsJson: """{"Marca":"Adidas","Categoria":"Calzado"}""")));
        Assert.False(s.Visible(Model(attrsJson: """{"Marca":"Adidas","Categoria":"Ropa"}""")));
    }

    [Fact] public void DosJuegosDeReglas_InterseccionAgenteCliente()
    {
        var s = VisibilityScope.FromRules([
            """[{"attributeId":"marca","valueIds":["adidas","nike"]}]""",     // agente
            """[{"attributeId":"marca","valueIds":["adidas","puma"]}]"""      // cliente
        ]);
        Assert.True(s.Visible(Model(attrsJson: """{"Marca":"ADIDAS"}""")));
        Assert.False(s.Visible(Model(attrsJson: """{"Marca":"NIKE"}""")));    // solo agente
        Assert.False(s.Visible(Model(attrsJson: """{"Marca":"PUMA"}""")));    // solo cliente
    }

    [Fact] public void ValoresConEspacios_CasanPorSlug()
    {
        var s = Scope("""[{"attributeId":"grupo-de-edad","valueIds":["adulto-joven"]}]""");
        Assert.True(s.Visible(Model(attrsJson: """{"Grupo de edad":"Adulto Joven"}""")));
    }

    [Fact] public void ReglasRotas_NoRestringen()   // jsonb corrupto jamás tumba el catálogo
    {
        var s = VisibilityScope.FromRules(["esto-no-es-json"]);
        Assert.True(s.Visible(Model()));
    }
}
```

- [ ] **Step 2: Run** `dotnet test ... --filter VisibilityScope` → FAIL (tipo no existe).

- [ ] **Step 3: Implementación mínima**

```csharp
using System.Text.Json.Nodes;
using B2B.Api.Data;

namespace B2B.Api.Shop;

// Predicado ÚNICO de visibilidad del catálogo. Lista blanca POR ATRIBUTO:
// si hay regla para un atributo, el modelo debe TENERLO con un valor permitido
// (whitelist estricta: sin el atributo → oculto). "familyId" es pseudo-atributo
// contra CatalogModel.FamilyId. Varias fuentes de reglas (agente + cliente en
// suplantación) = INTERSECCIÓN. Claves y valores comparados en slug (paridad
// con el SanitizeId del conector BC).
public sealed class VisibilityScope
{
    public static readonly VisibilityScope Unrestricted = new(null);

    // attributeId(slug) -> valores permitidos (slug). null = sin restricción.
    private readonly Dictionary<string, HashSet<string>>? _allowed;
    private VisibilityScope(Dictionary<string, HashSet<string>>? allowed) => _allowed = allowed;

    public bool IsRestricted => _allowed is { Count: > 0 };

    public static VisibilityScope FromRules(IEnumerable<string?> rulesJsonPerSubject)
    {
        Dictionary<string, HashSet<string>>? merged = null;
        foreach (var json in rulesJsonPerSubject)
        {
            var parsed = Parse(json);
            if (parsed is null) continue;                    // sin reglas / roto → no restringe
            if (merged is null) { merged = parsed; continue; }
            foreach (var (attr, values) in parsed)           // intersección por atributo
            {
                if (merged.TryGetValue(attr, out var mine)) mine.IntersectWith(values);
                else merged[attr] = values;
            }
        }
        return merged is { Count: > 0 } ? new VisibilityScope(merged) : Unrestricted;
    }

    public bool Visible(CatalogModel model)
    {
        if (_allowed is null) return true;
        foreach (var (attr, allowed) in _allowed)
        {
            var value = attr == "familyid" ? model.FamilyId : AttributeValue(model, attr);
            if (value is null || !allowed.Contains(Slug(value))) return false;
        }
        return true;
    }

    private static string? AttributeValue(CatalogModel model, string attrSlug)
    {
        try
        {
            if (JsonNode.Parse(model.AttributesJson ?? "{}") is not JsonObject obj) return null;
            foreach (var (key, node) in obj)
                if (Slug(key) == attrSlug) return node?.GetValue<string>();
        }
        catch { /* atributos rotos → como si no existieran */ }
        return null;
    }

    private static Dictionary<string, HashSet<string>>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return null;
            var result = new Dictionary<string, HashSet<string>>();
            foreach (var item in arr)
            {
                var attr = Slug(item?["attributeId"]?.GetValue<string>() ?? "");
                if (attr.Length == 0 || item?["valueIds"] is not JsonArray values) continue;
                var set = result.TryGetValue(attr, out var existing)
                    ? existing : result[attr] = new HashSet<string>(StringComparer.Ordinal);
                foreach (var v in values)
                    if (v?.GetValue<string>() is { Length: > 0 } s) set.Add(Slug(s));
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    // Paridad con SanitizeId del conector (Cod80114): minúsculas; espacio / \ _ . → '-'.
    public static string Slug(string text)
    {
        var chars = text.Trim().ToLowerInvariant().Select(c =>
            c is ' ' or '/' or '\\' or '_' or '.' ? '-' : c);
        return new string(chars.ToArray());
    }
}
```

- [ ] **Step 4: Run tests** → PASS todos. Suite completa verde.
- [ ] **Step 5: Commit** `"Visibilidad (2): VisibilityScope — whitelist por atributo con intersección (TDD)"`.

### Task 3: Resolución del scope por actor + hook de ingesta

**Files:**
- Create: `backend/src/B2B.Api/Shop/VisibilityStore.cs`
- Modify: `backend/src/B2B.Api/Sync/SyncEndpoints.cs` (donde se upsertea el doc, junto a `ClientIdentity.ApplyAsync` — SyncEndpoints.cs:269-270)
- Test: `backend/tests/B2B.Api.Tests/VisibilityIngestTests.cs`

- [ ] **Step 1: Tests de ingesta que FALLAN**

```csharp
// Con el harness estándar (WebApplicationFactory + token admin):
// 1) PUT /api/clients/{id} con visibleAttributes NO vacío → existe fila bc con esas reglas.
// 2) Re-PUT del mismo cliente SIN visibleAttributes → la fila bc anterior SE CONSERVA
//    (BC vacío no toca) y una fila manual previa nunca se pisa.
// 3) PUT /api/agents/{id} con visibleAttributes → fila bc de subject agent.
// 4) VisibilityStore.RulesForAsync devuelve bc cuando hay bc y manual (bc manda),
//    y manual cuando solo hay manual.
[Fact] public async Task IngestaCliente_ProyectaFilaBc() { /* PUT + assert fila via scope interno */ }
[Fact] public async Task IngestaSinCampo_NoTocaNada() { }
[Fact] public async Task BcMandaSobreManual() { }
```
(Implementar los asserts leyendo `db.CatalogVisibilities` vía un scope del factory, patrón de los tests existentes de ingesta.)

- [ ] **Step 2: VisibilityStore**

```csharp
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Shop;

// Lee/escribe las reglas de visibilidad. En runtime: para un sujeto manda la fila
// "bc" (proyectada del sync); si no hay, la "manual" (/manage). El scope del actor
// es la INTERSECCIÓN de las reglas del cliente y las del agente (si aplican).
public static class VisibilityStore
{
    public static async Task<string?> RulesForAsync(AppDbContext db, string subjectType, string? subjectId)
    {
        if (string.IsNullOrEmpty(subjectId)) return null;
        var rows = await db.CatalogVisibilities
            .Where(v => v.SubjectType == subjectType && v.SubjectId == subjectId)
            .ToListAsync();
        return (rows.FirstOrDefault(r => r.Source == "bc") ?? rows.FirstOrDefault())?.RulesJson;
    }

    public static async Task<VisibilityScope> ScopeForAsync(AppDbContext db, string? clientId, string? agentId)
        => VisibilityScope.FromRules([
            await RulesForAsync(db, "client", clientId),
            await RulesForAsync(db, "agent", agentId)]);

    // Hook de ingesta: proyecta visibleAttributes del payload de un doc client/agent.
    // Solo escribe si el array viene NO vacío (BC vacío/ausente no toca nada); las
    // filas manual nunca se pisan desde aquí.
    public static async Task ProjectFromPayloadAsync(
        AppDbContext db, string entityType, string externalId, System.Text.Json.Nodes.JsonNode? payload)
    {
        if (entityType is not ("client" or "agent")) return;
        if (payload?["visibleAttributes"] is not System.Text.Json.Nodes.JsonArray arr || arr.Count == 0) return;
        var subjectType = entityType;
        var rules = arr.ToJsonString();
        var row = await db.CatalogVisibilities.FirstOrDefaultAsync(v =>
            v.SubjectType == subjectType && v.SubjectId == externalId && v.Source == "bc");
        if (row is null)
            db.CatalogVisibilities.Add(new CatalogVisibility
                { SubjectType = subjectType, SubjectId = externalId, RulesJson = rules, Source = "bc" });
        else { row.RulesJson = rules; row.UpdatedAt = DateTime.UtcNow; }
    }
}
```

- [ ] **Step 3: Enchufar el hook en la ingesta** — en `SyncEndpoints`, en el mismo punto donde se llama a `ClientIdentity.ApplyAsync` tras el upsert del doc (mismo SaveChanges), añadir:

```csharp
await VisibilityStore.ProjectFromPayloadAsync(db, entityType, externalId, parsedPayload);
```
(usar el JsonNode ya parseado ahí; si solo hay string, `ClientIdentity.Parse(payload)`).

- [ ] **Step 4: Tests PASS + suite verde.**
- [ ] **Step 5: Commit** `"Visibilidad (3): hook de ingesta bc + resolución de scope (bc manda sobre manual)"`.

### Task 4: Enforcement en el pipeline del catálogo

**Files:**
- Modify: `backend/src/B2B.Api/Shop/CatalogQuery.cs` (QueryAsync ~línea 160-168; firma + filtro)
- Modify: `backend/src/B2B.Api/Shop/ShopEndpoints.cs` (los 3 endpoints: catalog, related, stock-export)
- Modify: `backend/src/B2B.Api/Portal/PdfEndpoints.cs` (3 endpoints)
- Test: `backend/tests/B2B.Api.Tests/VisibilityCatalogTests.cs`

- [ ] **Step 1: Tests de integración que FALLAN** — sembrar 2 modelos (marca ADIDAS calzado / marca NIKE ropa, con oferta) + cliente con regla `marca=[adidas]` (PUT del doc client con visibleAttributes) y afirmar:

```csharp
// 1) GET /api/shop/catalog como ese cliente → solo el modelo ADIDAS; total=1;
//    facets.families y facets.attributes NO contienen valores del oculto.
// 2) GET /api/shop/catalog?q=<ref del NIKE> → items vacío (la búsqueda no lo destapa).
// 3) GET /api/shop/related con A→cross=[NIKE] → items vacío (relacionado oculto no se sugiere).
// 4) Cliente SIN reglas → ve los 2.
// 5) stock-export.csv del restringido no contiene la ref oculta.
```

- [ ] **Step 2: QueryAsync acepta el scope** — añadir parámetro con default:

```csharp
public static async Task<CatalogPage> QueryAsync(
    AppDbContext db, PortalActorPrices prices, CatalogQuery query, DateTimeOffset now,
    VisibilityScope? visibility = null)
{
    var models = await db.CatalogModels.Where(m => m.Active).ToListAsync();
    // Visibilidad por actor ANTES de todo (incluido el recorte por Ids de relacionados):
    // así catálogo, facetas/cinta, búsqueda, ficha, relacionados, PDFs y CSV quedan
    // filtrados en un único punto.
    if (visibility is { IsRestricted: true })
        models = models.Where(visibility.Visible).ToList();
    ...
```

- [ ] **Step 3: Pasar el scope desde los 6 endpoints** — en cada uno (ShopEndpoints x3, PdfEndpoints x3), tras resolver `actor`:

```csharp
var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
// ... QueryAsync(db, Prices(actor), query, now, visibility)
```
(En PdfEndpoints el actor ya se resuelve; misma línea. `actor?.User.AgentExternalId` — para el agente suplantando aporta la intersección automáticamente porque ClientId también viene del claim.)

- [ ] **Step 4: Tests PASS + suite verde.**
- [ ] **Step 5: Commit** `"Visibilidad (4): predicado enchufado al pipeline — catálogo, facetas, búsqueda, related, PDFs y CSV filtrados"`.

### Task 5: Costura checkout (ambos modos) + costura agent/catalog-models

**Files:**
- Modify: `backend/src/B2B.Api/Portal/CartEndpoints.cs` (checkout ~línea 212+; validar líneas en modo portal Y ERP)
- Modify: `backend/src/B2B.Api/Portal/AgentEndpoints.cs:514-544` (catalog-models)
- Test: `backend/tests/B2B.Api.Tests/VisibilityCheckoutTests.cs`

- [ ] **Step 1: Tests que FALLAN**

```csharp
// 1) Cliente restringido a marca=[adidas] con carrito que incluye una línea NIKE →
//    POST /api/portal/orders → 400 con error que NOMBRA la referencia bloqueada;
//    no se crea pedido. Probar con Portal__OrdersMode=portal Y con "erp".
// 2) La misma compra solo-ADIDAS → 200.
// 3) GET /api/agent/catalog-models como agente restringido → solo sus modelos.
```

- [ ] **Step 2: Validación en checkout** — al inicio del procesado de líneas (ANTES de cualquier SaveChanges, común a ambos modos):

```csharp
var visibility = await VisibilityStore.ScopeForAsync(db, actor.ClientId, actor.User.AgentExternalId);
if (visibility.IsRestricted)
{
    var modelIds = lines.Select(l => l.ModelId ?? "").Where(s => s.Length > 0).Distinct().ToList();
    var models = await db.CatalogModels.Where(m => modelIds.Contains(m.ExternalId)).ToListAsync();
    var blocked = models.Where(m => !visibility.Visible(m))
        .Select(m => m.ExternalReference).ToList();
    // Un modelId sin fila en CatalogModels también se bloquea (no comprable a ciegas).
    var known = models.Select(m => m.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    blocked.AddRange(modelIds.Where(id => !known.Contains(id)));
    if (blocked.Count > 0)
        return Results.BadRequest(new { error =
            $"Estos artículos no están disponibles para tu cuenta: {string.Join(", ", blocked)}." });
}
```

- [ ] **Step 3: catalog-models** — aplicar el mismo scope del AGENTE (sin cliente):

```csharp
var visibility = await VisibilityStore.ScopeForAsync(db, null, actor.User.AgentExternalId);
var models = await db.CatalogModels.Where(m => m.Active).ToListAsync();
if (visibility.IsRestricted) models = models.Where(visibility.Visible).ToList();
```

- [ ] **Step 4: Tests PASS + suite verde.** **Step 5: Commit** `"Visibilidad (5): checkout bloquea líneas fuera de scope (portal y erp) + selector del agente filtrado"`.

### Task 6: Atribución del pedido al agente creador (saleId)

**Files:**
- Modify: `backend/src/B2B.Api/Portal/CartEndpoints.cs` (llamada a `Integration.SourceJson.Order`, `saleId: ""` actual)
- Test: añadir caso en `VisibilityCheckoutTests.cs`

- [ ] **Step 1: Test que FALLA** — pedido creado por agente suplantando → el doc de notificación saliente (`order.created` → InputJson) contiene `"saleId":"<AgentExternalId>"`; pedido de cliente normal → `saleId` vacío como hoy.
- [ ] **Step 2: Implementación** — en la llamada a SourceJson.Order: `saleId: actor.User.AgentExternalId ?? ""`.
- [ ] **Step 3: PASS + commit** `"Multiagente (6): el pedido saliente lleva saleId del agente que lo creó"`.

### Task 7: Endpoints admin de visibilidad + cinta backend

**Files:**
- Create: `backend/src/B2B.Api/Admin/VisibilityEndpoints.cs`
- Modify: `backend/src/B2B.Api/Data/Integration.cs` (+ `public string? CatalogRibbonJson { get; set; }`)
- Modify: `backend/src/B2B.Api/Program.cs` (Map nuevo)
- Migración: `AddCatalogRibbon`
- Test: `backend/tests/B2B.Api.Tests/VisibilityAdminTests.cs`

- [ ] **Step 1: Tests que FALLAN**

```csharp
// 1) GET /api/admin/visibility/client/{id} → {source, rules[], bcRules[]|null} (RequireAdmin).
// 2) PUT /api/admin/visibility/client/{id} body {rules:[{attributeId,valueIds[]}]} →
//    upsert fila manual; con fila bc presente, GET indica que manda bc (source:"bc").
// 3) PUT con rules:[] borra la fila manual.
// 4) GET/PUT /api/admin/integration/ribbon (CatalogRibbonJson crudo).
// 5) GET /api/shop/ribbon como cliente restringido → SOLO entradas permitidas
//    (se computa sobre las facetas filtradas: familias visibles + valores visibles
//    de los atributos configurados en la cinta), respetando orden/títulos/ocultos
//    de la config. Sin config → autogenerada (familias + nada más).
```

- [ ] **Step 2: Implementación**

```csharp
// VisibilityEndpoints.cs (RequireAdmin, patrón SalesRulesEndpoints):
//  GET  /api/admin/visibility/{type}/{id}   type ∈ client|agent
//  PUT  /api/admin/visibility/{type}/{id}   { rules: [...] } — valida attributeId no vacío
// Ribbon:
//  PUT /api/admin/integration/ribbon  { ribbon: {...} } → IntegrationSettings.CatalogRibbonJson
//  GET /api/shop/ribbon (RequireAuthorization) →
//    1) scope del actor (VisibilityStore.ScopeForAsync)
//    2) page = CatalogService.QueryAsync(..., Take=0? → usar RowsAsync/QueryAsync con Take máximo
//       SOLO para facetas; reutilizar la página con Take=1 y leer page.Families/AttributeFacets)
//    3) config = settings.CatalogRibbonJson: {"attributes":["marca","categoria"],
//       "entries":[{"key":"family:calzado","hidden":false,"order":1,"titles":{"es":"Calzado"}}]}
//    4) respuesta: { entries:[{key, kind:"family"|"attr", attributeId?, value?, label, count}] }
//       — solo entradas presentes en las facetas filtradas (jamás fuga), orden de config,
//       ocultas fuera, títulos por locale con fallback a la etiqueta de la faceta.
```
El JSON de config exacto queda así de simple a propósito (YAGNI); la UI de /manage lo edita entero.

- [ ] **Step 3: Migración `AddCatalogRibbon` + build + tests PASS + suite verde.**
- [ ] **Step 4: Commit** `"Visibilidad (7): admin endpoints (bc/manual) + /api/shop/ribbon computada server-side"`.

## PARTE B — Portal UI (agentes de diseño con skills + Playwright; el ejecutor los lanza como subagentes)

### Task 8: La CINTA en el portal (pantalla estrella)
- Agente de diseño (skill `refined`, bucle de autocrítica ≥3 rondas) sobre `wwwroot/portal` (catalog.js/chrome.js + app.css + i18n):
  - Banda de chips/pestañas bajo CATÁLOGO|LOOKBOOK alimentada por `GET /api/shop/ribbon`; "TODO" primero; clic aplica `?family=` o `?a.{clave}=` (mecanismo de facetas existente); estado activo visible; scroll-x con snap y sin flechas en móvil; desktop con desborde → flechas patrón related.js.
  - Sin entradas (ribbon vacío) → la banda NO existe ni deja hueco; slot reservado si carga async (lección CLS del /goal anterior).
  - Aceptación Playwright: cliente sin restricción ve todas; cliente restringido (sembrar reglas) ve solo lo suyo; clic filtra; 0 errores consola; capturas desktop+390.
- Commit al validar.

### Task 9: /manage — Visibilidad en ficha de cliente y agente
- Agente de diseño sobre `wwwroot/manage` (`views/client.js`, `schemas.js`/form del agente, api.js):
  - Sección "Visibilidad de catálogo": editor por atributo (selector de atributo de las entidades attribute + chips de valores permitidos, patrón sales-rules/tr-chip) → `PUT /api/admin/visibility/...`. Si hay fila BC: banner candado "Lo fija Business Central" con las reglas BC en solo lectura y la edición manual deshabilitada (source de la respuesta GET).
  - En la ficha del cliente, lista informativa "Agentes de este cliente" (consulta inversa de docs agent).
  - Aceptación Playwright + capturas.

### Task 10: /manage — Gestor de la cinta
- Agente de diseño: vista "Catálogo → Cinta" (router+nav): elegir atributos que alimentan la cinta, listar entradas detectadas (de las facetas globales), ocultar/mostrar, reordenar (botones ↑↓, no drag), títulos por idioma es/en/fr/it, **vista previa** de la banda. `GET/PUT /api/admin/integration/ribbon`.
- Aceptación Playwright + capturas.

## PARTE C — Conector NEW (AL; autoría con doble self-review, compila el usuario)

### Task 11: Multiagente — Tab80134 + páginas + unión en clientIds
- `Tab80134 "B2B Customer Agent"` (PK Customer No. + Salesperson Code, FlowFields), `Pag80147` ListPart en PagExt80104 (grupo B2B Integration), `Pag80148` en PagExt80131.
- `Cod80140.BuildClientIdsArray`: unión con Tab80134, filtro Sync to B2B, dedupe `List of [Guid]` (patrón Cod80112:226-242), exclusiones por SystemId en bucle (nunca SetFilter '<>%1').

### Task 12: Visibilidad — Enum80120 + Tab80135 + páginas + visibleAttributes
- `Enum80120` (Customer|Agent), `Tab80135 "B2B Catalog Visibility"` (PK 4 campos, TableRelations condicionales, FlowFields), `Pag80149` + ListParts en fichas.
- Extraer `SanitizeId` de Cod80114 a `Cod80122.B2BUtils` (mover, actualizar llamada) y builder compartido `BuildVisibleAttributesArray(subjectType, code)` usado por `Cod80130.BuildCustomerJson` (tras productSegments) y `Cod80140.InternalBuildModelJson` (tras markets). Agrupar filas por atributo → `{"attributeId": B2B Code, "valueIds":[SanitizeId(valor)]}`; atributo sin B2B Code → omitir.

### Task 13: Frescura — job de agentes + suscriptores
- `"B2B Needs Sync"` en TabExt80121 (field 50101). `Cod80181 "B2B Agent Sync Job"` calcado de Cod80169: Job Queue 5 min, procesa marcados jerarquía maestro-primero (lógica de Rep80104:67-84), sincroniza cualquier Salesperson referenciado por Tab80134 aunque no esté en Tab80104.
- Suscriptores: Tab80134 y Tab80135 (insert/modify/delete) marcan el sujeto (Customer."B2B Needs Sync" ya existente / flag nuevo del agente); modify de `Customer."Salesperson Code"` marca agente anterior Y nuevo; modify de Salesperson (Name/E-Mail/B2B Culture) marca.
- Self-review AL x2 (FieldRef/Variant, PKs, FindSet, sin variables muertas) + actualizar `docs/contrato-api/04-clientes-agentes.md` (visibleAttributes + unión clientIds).

## PARTE D — Cierre de calidad (mandato del usuario)

### Task 14: Verificación integral + auditores
- Sembrar en local un escenario completo (2 marcas × 2 categorías, cliente restringido, agente restringido, multiagente con 2 agentes al mismo cliente) vía PUTs de sync como haría BC.
- Playwright integral: cinta por actor, catálogo/búsqueda/ficha/related filtrados, suplantación=intersección, checkout denegando, /manage config, 0 errores consola.
- Lanzar en paralelo: **auditor de código** (portal+AL adversarial), **crítico de diseño**, **auditor UX** → aplicar TODOS los hallazgos (bucle) → re-suite + re-Playwright.
- Entrega al usuario: capturas de altísima calidad + manual paso a paso + accesos; verificación conjunta en localhost; deploy SOLO a ALMA tras su OK; recordatorio de compilar/publicar el conector NEW y configurar atributos/visibilidad en BC.

---

## Self-review del plan (hecho)
- Cobertura del spec: §1→T11-13, §2→T12, §3→T1/T3, §4→T2/T4/T5, §5→T7/T8/T10, §6→T9/T10, §7→T6, §8→T14. Sin huecos.
- Tipos coherentes: `VisibilityScope.FromRules(IEnumerable<string?>)`, `VisibilityStore.ScopeForAsync(db, clientId, agentId)`, `QueryAsync(..., VisibilityScope? visibility = null)` usados igual en T2-T7.
- Riesgo señalado por auditoría BC (upsert de agentes "roba" carteras): cubierto por test explícito en T14 (dos agentes con el mismo cliente conservan ambos su lista).
