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
