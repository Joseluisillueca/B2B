using B2B.Api.Data;

namespace B2B.Api.Integration;

// Resultado de evaluar las reglas de transporte para un pedido.
public readonly record struct TransportResult(decimal Cost, string? IncotermId, Guid? RuleId, string? RuleName)
{
    public bool Matched => RuleId is not null;
    public static readonly TransportResult None = new(0m, null, null, null);
}

// Motor de reglas de transporte (portes). Evalúa una lista de reglas contra los datos de un
// pedido y devuelve el transporte a aplicar. Determinista: gana la PRIMERA regla activa (por
// Priority asc, luego Name) cuyas condiciones TODAS casen. Si ninguna casa → portes gratis (0),
// que es el comportamiento por defecto (igual que antes de las reglas).
public static class TransportRules
{
    public static TransportResult Evaluate(
        IEnumerable<TransportRule> rules,
        string? clientId, string? countryIsoId, string? orderType, int units, decimal amount)
    {
        foreach (var r in rules.Where(x => x.Active).OrderBy(x => x.Priority).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!MatchText(r.ClientExternalId, clientId)) continue;
            if (!MatchText(r.CountryIsoId, countryIsoId)) continue;
            if (!MatchText(r.OrderType, orderType)) continue;
            if (r.MinUnits is { } minUnits && units < minUnits) continue;
            if (r.MinAmount is { } minAmount && amount < minAmount) continue;

            var cost = r.PerUnit ? r.Cost * units : r.Cost;
            if (cost < 0) cost = 0;
            return new TransportResult(
                decimal.Round(cost, 2, MidpointRounding.AwayFromZero),
                string.IsNullOrWhiteSpace(r.IncotermId) ? null : r.IncotermId.Trim(),
                r.Id, r.Name);
        }
        return TransportResult.None;
    }

    // Una condición vacía casa con todo; con valor, debe coincidir (ignora mayúsculas y espacios).
    private static bool MatchText(string? condition, string? value) =>
        string.IsNullOrWhiteSpace(condition) ||
        string.Equals(condition.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);
}
