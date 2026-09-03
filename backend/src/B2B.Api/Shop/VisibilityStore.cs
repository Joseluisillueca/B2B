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
        var rules = arr.ToJsonString();
        var row = await db.CatalogVisibilities.FirstOrDefaultAsync(v =>
            v.SubjectType == entityType && v.SubjectId == externalId && v.Source == "bc");
        if (row is null)
            db.CatalogVisibilities.Add(new CatalogVisibility
                { SubjectType = entityType, SubjectId = externalId, RulesJson = rules, Source = "bc" });
        else { row.RulesJson = rules; row.UpdatedAt = DateTime.UtcNow; }
    }
}
