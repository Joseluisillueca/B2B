using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// UX-A3 (14a-5): nombre del comercial que creó un pedido (saleId → doc `agent`.name).
// Se resuelve UNA vez por petición para todos los ids presentes (una consulta), nunca
// por fila. Sin documento de agente el nombre queda a null (el front no inventa nada).
public static class AgentNames
{
    public static async Task<Dictionary<string, string>> ResolveAsync(AppDbContext db, IEnumerable<string> agentIds)
    {
        var ids = agentIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return names;

        var docs = await db.SyncDocuments
            .Where(d => d.EntityType == "agent" && ids.Contains(d.ExternalId))
            .ToListAsync();
        foreach (var doc in docs)
            if (ClientIdentity.Text(ClientIdentity.Parse(doc.Payload)?["name"]) is { Length: > 0 } name)
                names[doc.ExternalId] = name;
        return names;
    }

    /// Las filas con el nombre del agente ya puesto (las de cliente normal siguen a null).
    public static async Task<List<OrderRow>> AttachAsync(AppDbContext db, IEnumerable<OrderRow> rows)
    {
        var list = rows.ToList();
        var names = await ResolveAsync(db, list.Select(r => r.AgentId));
        return [.. list.Select(r => r with { AgentName = names.GetValueOrDefault(r.AgentId) })];
    }
}
