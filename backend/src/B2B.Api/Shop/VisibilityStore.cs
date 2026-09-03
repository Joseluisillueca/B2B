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
        var (bc, manual) = await RowsForAsync(db, subjectType, subjectId);
        return (bc ?? manual)?.RulesJson;
    }

    // Las dos filas de un sujeto, con la precedencia escrita UNA sola vez (bc manda
    // sobre manual): de aquí derivan RulesForAsync (runtime) y el GET/PUT del admin
    // (VisibilityEndpoints.ProjectAsync).
    internal static async Task<(CatalogVisibility? Bc, CatalogVisibility? Manual)> RowsForAsync(
        AppDbContext db, string subjectType, string subjectId)
    {
        var rows = await db.CatalogVisibilities
            .Where(v => v.SubjectType == subjectType && v.SubjectId == subjectId)
            .ToListAsync();
        return (rows.FirstOrDefault(r => r.Source == "bc"), rows.FirstOrDefault(r => r.Source == "manual"));
    }

    public static async Task<VisibilityScope> ScopeForAsync(AppDbContext db, string? clientId, string? agentId)
        => VisibilityScope.FromRules([
            await RulesForAsync(db, "client", clientId),
            await RulesForAsync(db, "agent", agentId)]);

    // Hook de ingesta: proyecta visibleAttributes del payload de un doc client/agent.
    // AUSENTE (clave no presente o no-array) → no tocar nada. PRESENTE y no vacío →
    // upsert de la fila bc. PRESENTE y vacío ([]) → BORRAR la fila bc si existe: BC
    // levanta la restricción y la resolución cae a la manual si la hay (sin este
    // trinquete, BC jamás podría deshacer una restricción ya proyectada). Las filas
    // manual nunca se escriben ni se borran desde aquí.
    public static async Task ProjectFromPayloadAsync(
        AppDbContext db, string entityType, string externalId, System.Text.Json.Nodes.JsonNode? payload)
    {
        if (entityType is not ("client" or "agent")) return;
        if (payload?["visibleAttributes"] is not System.Text.Json.Nodes.JsonArray arr) return;

        var row = await db.CatalogVisibilities.FirstOrDefaultAsync(v =>
            v.SubjectType == entityType && v.SubjectId == externalId && v.Source == "bc");

        if (arr.Count == 0)
        {
            if (row is not null) db.CatalogVisibilities.Remove(row);
            return;
        }

        var rules = arr.ToJsonString();
        if (row is null)
            db.CatalogVisibilities.Add(new CatalogVisibility
                { SubjectType = entityType, SubjectId = externalId, RulesJson = rules, Source = "bc" });
        else { row.RulesJson = rules; row.UpdatedAt = DateTime.UtcNow; }
    }
}
