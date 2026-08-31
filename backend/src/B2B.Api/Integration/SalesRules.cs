using System.Text.Json.Nodes;
using B2B.Api.Data;

namespace B2B.Api.Integration;

// Contexto de evaluación de una regla de venta: todo lo que las condiciones pueden mirar del
// carrito/pedido/cliente. Los que no apliquen quedan a null/vacío (una condición sobre un dato
// ausente no casa).
public sealed class SalesContext
{
    public string? ClientId { get; init; }
    public string? GroupId { get; init; }            // grupo del cliente
    public string? Market { get; init; }             // mercado (es, fr…)
    public string? CountryIsoId { get; init; }       // país de la dirección de envío
    public string? OrderType { get; init; }          // REPLENISHMENT | SCHEDULED
    public int Units { get; init; }                  // unidades del carrito
    public decimal Amount { get; init; }             // subtotal del carrito (sin IVA)
    public bool CreatedByAgent { get; init; }        // el carrito lo creó un comercial
    public DateOnly Date { get; init; }              // fecha del pedido
    public string? RateId { get; init; }             // tarifa del cliente
    public IReadOnlyCollection<string> ModelIds { get; init; } = [];   // modelos en el carrito
    public IReadOnlyCollection<string> ProductIds { get; init; } = []; // variantes en el carrito
    public IReadOnlyCollection<string> FamilyIds { get; init; } = [];  // familias en el carrito
    public IReadOnlyCollection<string> BrandIds { get; init; } = [];   // marcas en el carrito
}

// Resultado agregado de aplicar todas las reglas que casan (en orden de prioridad).
public sealed class SalesResult
{
    public bool Denied { get; set; }                 // "Denegar carrito"
    public string? DeniedReason { get; set; }
    public bool FreeShipping { get; set; }           // "Portes gratis"
    public decimal? FixedTransport { get; set; }     // "Importe fijo transporte" (la 1ª que casa gana)
    public decimal LineDiscountPercent { get; set; } // "Descuento porcentual por línea" (acumulado, cap 100)
    public decimal LineDiscountFixed { get; set; }   // "Descuento fijo por línea" (acumulado)
    public List<Guid> MatchedRuleIds { get; } = [];

    // Coste de transporte resultante: 0 si portes gratis; el importe fijo si aplica; null si
    // ninguna regla toca el transporte (→ el checkout deja el transporte como estuviera).
    public decimal? TransportCost => FreeShipping ? 0m : FixedTransport;
}

// Motor de "Condiciones de venta / promos". Evalúa las reglas activas por prioridad ascendente;
// una regla casa si TODAS sus condiciones se cumplen (AND). Las acciones de las reglas que casan
// se agregan en el SalesResult. Determinista y sin excepciones al llamante (parseo defensivo).
public static class SalesRules
{
    public static SalesResult Evaluate(IEnumerable<SalesRule> rules, SalesContext ctx)
    {
        var result = new SalesResult();
        foreach (var rule in rules.Where(r => r.Active).OrderBy(r => r.Priority).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var conditions = ParseArray(rule.ConditionsJson);
            if (conditions.Count == 0) continue;                    // una regla sin condiciones no aplica
            if (!conditions.All(c => MatchCondition(c, ctx))) continue;

            result.MatchedRuleIds.Add(rule.Id);
            foreach (var action in ParseArray(rule.ActionsJson))
                ApplyAction(action, result);
        }
        return result;
    }

    // ── Condiciones ───────────────────────────────────────────────────────────────
    private static bool MatchCondition(JsonObject c, SalesContext ctx)
    {
        var type = Str(c["type"])?.ToLowerInvariant();
        return type switch
        {
            "market"        => InList(c["values"], ctx.Market),
            "country"       => InList(c["values"], ctx.CountryIsoId),
            "client"        => InList(c["values"], ctx.ClientId),
            "client_group"  => InList(c["values"], ctx.GroupId),
            "rate"          => InList(c["values"], ctx.RateId),
            "order_type"    => Eq(Str(c["value"]), ctx.OrderType),
            "models"        => Intersects(c["values"], ctx.ModelIds),
            "products"      => Intersects(c["values"], ctx.ProductIds),
            "families"      => Intersects(c["values"], ctx.FamilyIds),
            "brands"        => Intersects(c["values"], ctx.BrandIds),
            "agent_cart"    => (Bool(c["value"]) ?? true) == ctx.CreatedByAgent,
            "units_lt"      => Num(c["value"]) is { } x && ctx.Units < x,          // "Menos de X unidades"
            "min_units"     => Num(c["value"]) is { } m && ctx.Units >= m,          // "Unidades mínimas elegibles"
            "cart_total"    => CompareNum(ctx.Amount, Str(c["op"]), Num(c["value"])),
            "date_between"  => DateBetween(ctx.Date, Str(c["from"]), Str(c["to"])),
            _ => false,                                                              // tipo desconocido → no casa
        };
    }

    // ── Acciones (agregación) ──────────────────────────────────────────────────────
    private static void ApplyAction(JsonObject a, SalesResult r)
    {
        switch (Str(a["type"])?.ToLowerInvariant())
        {
            case "deny":
                r.Denied = true;
                r.DeniedReason ??= Str(a["message"]);
                break;
            case "free_shipping":
                r.FreeShipping = true;
                break;
            case "fixed_transport":
                r.FixedTransport ??= Money(Num(a["amount"]));    // la primera que casa fija el importe
                break;
            case "line_discount_percent":
                r.LineDiscountPercent = Math.Min(100m, r.LineDiscountPercent + Money(Num(a["percent"])));
                break;
            case "line_discount_fixed":
                r.LineDiscountFixed += Money(Num(a["amount"]));
                break;
        }
    }

    // ── Utilidades ─────────────────────────────────────────────────────────────────
    private static List<JsonObject> ParseArray(string? json)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json) is JsonArray arr
                ? arr.OfType<JsonObject>().ToList() : [];
        }
        catch { return []; }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : null;

    private static decimal? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<decimal>(out var d)) return d;
        if (v.TryGetValue<string>(out var s) && decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ds)) return ds;
        return null;
    }

    private static bool? Bool(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<string>(out var s)) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return null;
    }

    private static decimal Money(decimal? d) => Math.Max(0m, decimal.Round(d ?? 0m, 2, MidpointRounding.AwayFromZero));

    private static bool Eq(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    // El valor del contexto debe estar en la lista de la condición (case-insensitive).
    private static bool InList(JsonNode? values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || values is not JsonArray arr) return false;
        var v = value.Trim();
        return arr.Any(n => string.Equals(Str(n), v, StringComparison.OrdinalIgnoreCase));
    }

    // Alguno de los ids del carrito está en la lista de la condición.
    private static bool Intersects(JsonNode? values, IReadOnlyCollection<string> ctxIds)
    {
        if (values is not JsonArray arr || ctxIds.Count == 0) return false;
        var set = new HashSet<string>(arr.Select(n => Str(n) ?? "").Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
        return ctxIds.Any(id => set.Contains(id));
    }

    private static bool CompareNum(decimal actual, string? op, decimal? threshold)
    {
        if (threshold is not { } t) return false;
        return (op?.ToLowerInvariant()) switch
        {
            "lt" => actual < t,
            "lte" => actual <= t,
            "gt" => actual > t,
            "gte" => actual >= t,
            "eq" => actual == t,
            _ => actual >= t,     // por defecto, "a partir de" (>=)
        };
    }

    private static bool DateBetween(DateOnly date, string? from, string? to)
    {
        var okFrom = !DateOnly.TryParse(from, System.Globalization.CultureInfo.InvariantCulture, out var f) || date >= f;
        var okTo = !DateOnly.TryParse(to, System.Globalization.CultureInfo.InvariantCulture, out var t) || date <= t;
        return okFrom && okTo;
    }
}
