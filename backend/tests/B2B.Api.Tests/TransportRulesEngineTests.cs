using B2B.Api.Data;
using B2B.Api.Integration;

namespace B2B.Api.Tests;

// Motor puro de REGLAS DE TRANSPORTE (portes): TransportRules.Evaluate(...). Sin servidor
// ni BD: se comprueba el algoritmo de casación (condiciones combinables, case-insensitive y
// con trim), la prioridad (1ª que casa gana), el cálculo del coste (fijo / por unidad,
// redondeo, no-negativo), que las reglas inactivas se ignoran y el "sin coincidencia" → None.
public class TransportRulesEngineTests
{
    // Fábrica de reglas: por defecto activa, sin condiciones (casa con todo) y coste fijo.
    private static TransportRule Rule(
        string name = "R", bool active = true, int priority = 0,
        string? client = null, string? country = null, string? orderType = null,
        int? minUnits = null, decimal? minAmount = null,
        decimal cost = 0, bool perUnit = false, string? incoterm = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Active = active,
        Priority = priority,
        ClientExternalId = client,
        CountryIsoId = country,
        OrderType = orderType,
        MinUnits = minUnits,
        MinAmount = minAmount,
        Cost = cost,
        PerUnit = perUnit,
        IncotermId = incoterm,
    };

    private static TransportResult Eval(
        IEnumerable<TransportRule> rules, string? client = null, string? country = null,
        string? orderType = null, int units = 0, decimal amount = 0) =>
        TransportRules.Evaluate(rules, client, country, orderType, units, amount);

    // ── Condiciones vacías / null casan con todo ──────────────────────────────────
    [Fact]
    public void ReglaSinCondiciones_CasaConCualquierPedido()
    {
        var res = Eval([Rule(cost: 9)], client: "CUALQUIERA", country: "ES", orderType: "SCHEDULED", units: 1, amount: 5);
        Assert.True(res.Matched);
        Assert.Equal(9m, res.Cost);
    }

    // ── País como LISTA (varios países en una misma regla) ────────────────────────
    [Fact]
    public void Pais_ListaSeparadaPorComas_CasaSiEstaEnLaLista()
    {
        var rule = Rule(cost: 12, country: "ES,FR,PT");
        Assert.True(Eval([rule], country: "FR").Matched);    // FR está en la lista
        Assert.True(Eval([rule], country: "es").Matched);     // case-insensitive
        Assert.Equal(12m, Eval([rule], country: " PT ").Cost); // con espacios
        Assert.False(Eval([rule], country: "GB").Matched);    // GB no está en la lista
        Assert.False(Eval([rule], country: null).Matched);    // sin país no casa una regla con país
    }

    [Fact]
    public void ReglaConCondicionVacia_NoRestringe()
    {
        // "" y "   " en las condiciones equivalen a "cualquiera" (no exigen nada).
        var res = Eval([Rule(client: "", country: "   ", orderType: "", cost: 4)], client: "X", country: "FR");
        Assert.True(res.Matched);
        Assert.Equal(4m, res.Cost);
    }

    // ── Casación por cada condición (case-insensitive + trim) ─────────────────────
    [Fact]
    public void CasaPorPais_IgnoraMayusculasYEspacios()
    {
        var rules = new[] { Rule(name: "ES", country: "ES", cost: 7) };
        Assert.True(Eval(rules, country: "  es  ").Matched);      // trim + minúsculas
        Assert.Equal(7m, Eval(rules, country: "es").Cost);
        Assert.False(Eval(rules, country: "FR").Matched);          // otro país no casa
    }

    [Fact]
    public void CasaPorCliente_IgnoraMayusculasYEspacios()
    {
        var rules = new[] { Rule(name: "CLI", client: "C-100", cost: 3) };
        Assert.True(Eval(rules, client: " c-100 ").Matched);
        Assert.False(Eval(rules, client: "C-999").Matched);
    }

    [Theory]
    [InlineData("REPLENISHMENT")]
    [InlineData("SCHEDULED")]
    public void CasaPorTipoDePedido(string type)
    {
        var rules = new[] { Rule(name: "T", orderType: type, cost: 2) };
        Assert.True(Eval(rules, orderType: type.ToLowerInvariant()).Matched);   // case-insensitive
        Assert.False(Eval(rules, orderType: "OTRO").Matched);
    }

    // ── Mínimos de unidades e importe ─────────────────────────────────────────────
    [Fact]
    public void CasaPorMinUnits_UmbralInclusivo()
    {
        var rules = new[] { Rule(name: "U", minUnits: 10, cost: 5) };
        Assert.False(Eval(rules, units: 9).Matched);    // por debajo del mínimo → no casa
        Assert.True(Eval(rules, units: 10).Matched);    // el umbral SÍ casa (>=)
        Assert.True(Eval(rules, units: 11).Matched);
    }

    [Fact]
    public void CasaPorMinAmount_UmbralInclusivo()
    {
        var rules = new[] { Rule(name: "A", minAmount: 100m, cost: 5) };
        Assert.False(Eval(rules, amount: 99.99m).Matched);
        Assert.True(Eval(rules, amount: 100m).Matched);
        Assert.True(Eval(rules, amount: 250m).Matched);
    }

    [Fact]
    public void Combinacion_TodasLasCondicionesDebenCumplirse()
    {
        var rule = Rule(name: "COMBO", client: "C-1", country: "ES", orderType: "SCHEDULED",
            minUnits: 5, minAmount: 50m, cost: 12);
        var rules = new[] { rule };

        // Todas se cumplen → casa
        Assert.True(Eval(rules, client: "C-1", country: "ES", orderType: "SCHEDULED", units: 5, amount: 50m).Matched);

        // Falla una cualquiera → no casa
        Assert.False(Eval(rules, client: "OTRO", country: "ES", orderType: "SCHEDULED", units: 5, amount: 50m).Matched);
        Assert.False(Eval(rules, client: "C-1", country: "FR", orderType: "SCHEDULED", units: 5, amount: 50m).Matched);
        Assert.False(Eval(rules, client: "C-1", country: "ES", orderType: "REPLENISHMENT", units: 5, amount: 50m).Matched);
        Assert.False(Eval(rules, client: "C-1", country: "ES", orderType: "SCHEDULED", units: 4, amount: 50m).Matched);
        Assert.False(Eval(rules, client: "C-1", country: "ES", orderType: "SCHEDULED", units: 5, amount: 49m).Matched);
    }

    // ── Prioridad ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Prioridad_GanaLaDeMenorPriority()
    {
        var rules = new[]
        {
            Rule(name: "cara", priority: 10, cost: 100),
            Rule(name: "barata", priority: 1, cost: 5),
        };
        var res = Eval(rules, units: 1);
        Assert.Equal("barata", res.RuleName);
        Assert.Equal(5m, res.Cost);
    }

    [Fact]
    public void Prioridad_AIgualdad_DesempataPorNombre()
    {
        // Misma Priority; gana el Name menor (OrdinalIgnoreCase): "Alpha" < "bravo".
        var rules = new[]
        {
            Rule(name: "bravo", priority: 3, cost: 20),
            Rule(name: "Alpha", priority: 3, cost: 8),
        };
        var res = Eval(rules, units: 1);
        Assert.Equal("Alpha", res.RuleName);
        Assert.Equal(8m, res.Cost);
    }

    [Fact]
    public void Prioridad_PrimeraQueCasaGana_AunqueOtraPosteriorTambienCasaria()
    {
        // La de prioridad 0 NO casa (país FR); la siguiente que casa (prioridad 5, match-all)
        // gana, aunque exista otra posterior (prioridad 9) que también casaría.
        var rules = new[]
        {
            Rule(name: "solo-FR", priority: 0, country: "FR", cost: 1),
            Rule(name: "gana", priority: 5, cost: 30),
            Rule(name: "tambien-casaria", priority: 9, cost: 40),
        };
        var res = Eval(rules, country: "ES", units: 1);
        Assert.Equal("gana", res.RuleName);
        Assert.Equal(30m, res.Cost);
    }

    // ── Cálculo del coste ─────────────────────────────────────────────────────────
    [Fact]
    public void CosteFijo_NoDependeDeLasUnidades()
    {
        var res = Eval([Rule(cost: 15, perUnit: false)], units: 7);
        Assert.Equal(15m, res.Cost);
    }

    [Fact]
    public void PerUnit_MultiplicaCostePorUnidades()
    {
        var res = Eval([Rule(cost: 2.5m, perUnit: true)], units: 3);
        Assert.Equal(7.50m, res.Cost);
    }

    [Fact]
    public void Coste_SeRedondeaA2Decimales_AwayFromZero()
    {
        // 0.333 × 3 = 0.999 → 1.00
        Assert.Equal(1.00m, Eval([Rule(cost: 0.333m, perUnit: true)], units: 3).Cost);
        // 10.005 (fijo) → 10.01
        Assert.Equal(10.01m, Eval([Rule(cost: 10.005m)]).Cost);
    }

    [Fact]
    public void CosteNegativo_SeSaneaA0()
    {
        Assert.Equal(0m, Eval([Rule(cost: -5m)]).Cost);
        Assert.Equal(0m, Eval([Rule(cost: -1m, perUnit: true)], units: 4).Cost);
    }

    [Fact]
    public void Incoterm_SeDevuelveTrimeado_YVacioEsNull()
    {
        Assert.Equal("fob", Eval([Rule(cost: 1, incoterm: "  fob  ")]).IncotermId);
        Assert.Null(Eval([Rule(cost: 1, incoterm: "   ")]).IncotermId);
        Assert.Null(Eval([Rule(cost: 1, incoterm: null)]).IncotermId);
    }

    // ── Reglas inactivas ──────────────────────────────────────────────────────────
    [Fact]
    public void ReglasInactivas_SeIgnoran()
    {
        // La regla que casaría está inactiva → no cuenta; gana la activa siguiente.
        var rules = new[]
        {
            Rule(name: "inactiva-prioritaria", active: false, priority: 0, cost: 99),
            Rule(name: "activa", active: true, priority: 5, cost: 6),
        };
        var res = Eval(rules, units: 1);
        Assert.Equal("activa", res.RuleName);
        Assert.Equal(6m, res.Cost);
    }

    [Fact]
    public void TodasInactivas_NoCasaNinguna()
    {
        var res = Eval([Rule(active: false, cost: 10)], units: 1);
        Assert.False(res.Matched);
    }

    // ── Sin coincidencia → None ───────────────────────────────────────────────────
    [Fact]
    public void SinReglas_DevuelveNone()
    {
        var res = Eval([]);
        Assert.False(res.Matched);
        Assert.Equal(0m, res.Cost);
        Assert.Null(res.RuleId);
        Assert.Null(res.RuleName);
        Assert.Null(res.IncotermId);
        Assert.Equal(TransportResult.None, res);
    }

    [Fact]
    public void NingunaCasa_DevuelveNone()
    {
        var rules = new[] { Rule(name: "solo-ES", country: "ES", cost: 8) };
        var res = Eval(rules, country: "PT", units: 1);
        Assert.False(res.Matched);
        Assert.Equal(0m, res.Cost);
        Assert.Null(res.RuleId);
    }

    [Fact]
    public void ReglaQueCasa_LlevaSuIdYNombre()
    {
        var rule = Rule(name: "Con-Id", cost: 5);
        var res = Eval([rule], units: 1);
        Assert.True(res.Matched);
        Assert.Equal(rule.Id, res.RuleId);
        Assert.Equal("Con-Id", res.RuleName);
    }
}
