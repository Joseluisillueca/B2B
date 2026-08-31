using B2B.Api.Data;
using B2B.Api.Integration;
using Xunit;

namespace B2B.Api.Tests;

// Tests del motor de "Condiciones de venta / promos" (SalesRules.Evaluate). Puros, sin servidor.
public class SalesRulesEngineTests
{
    private static SalesRule Rule(string conditions, string actions, string name = "R", int priority = 0, bool active = true)
        => new() { Id = Guid.NewGuid(), Name = name, Priority = priority, Active = active, ConditionsJson = conditions, ActionsJson = actions };

    private static SalesContext Ctx(
        string? client = null, string? group = null, string? market = null, string? country = null,
        string? orderType = null, int units = 0, decimal amount = 0, bool agent = false,
        DateOnly? date = null, string? rate = null,
        string[]? models = null, string[]? products = null, string[]? families = null, string[]? brands = null)
        => new()
        {
            ClientId = client, GroupId = group, Market = market, CountryIsoId = country,
            OrderType = orderType, Units = units, Amount = amount, CreatedByAgent = agent,
            Date = date ?? new DateOnly(2026, 6, 15), RateId = rate,
            ModelIds = models ?? [], ProductIds = products ?? [], FamilyIds = families ?? [], BrandIds = brands ?? [],
        };

    // ── Condiciones ──────────────────────────────────────────────────────────────
    [Fact]
    public void TodasLasCondiciones_DebenCumplirse_AND()
    {
        var r = Rule("""[{"type":"order_type","value":"REPLENISHMENT"},{"type":"units_lt","value":10}]""",
                     """[{"type":"fixed_transport","amount":30}]""");
        Assert.Equal(30m, SalesRules.Evaluate([r], Ctx(orderType: "REPLENISHMENT", units: 5)).TransportCost);
        Assert.Null(SalesRules.Evaluate([r], Ctx(orderType: "REPLENISHMENT", units: 20)).TransportCost); // una condición falla
        Assert.Null(SalesRules.Evaluate([r], Ctx(orderType: "SCHEDULED", units: 5)).TransportCost);       // otra falla
    }

    [Fact]
    public void UnitsLt_Y_MinUnits()
    {
        Assert.True(SalesRules.Evaluate([Rule("""[{"type":"units_lt","value":10}]""", Free)], Ctx(units: 9)).FreeShipping);
        Assert.False(SalesRules.Evaluate([Rule("""[{"type":"units_lt","value":10}]""", Free)], Ctx(units: 10)).FreeShipping);
        Assert.True(SalesRules.Evaluate([Rule("""[{"type":"min_units","value":10}]""", Free)], Ctx(units: 10)).FreeShipping); // inclusivo
        Assert.False(SalesRules.Evaluate([Rule("""[{"type":"min_units","value":10}]""", Free)], Ctx(units: 9)).FreeShipping);
    }

    [Theory]
    [InlineData("lt", 100, 99, true)]
    [InlineData("lt", 100, 100, false)]
    [InlineData("gte", 300, 300, true)]
    [InlineData("gt", 300, 300, false)]
    [InlineData("eq", 50, 50, true)]
    public void CartTotal_ConOperador(string op, int threshold, int amount, bool expected)
    {
        var json = "[{\"type\":\"cart_total\",\"op\":\"" + op + "\",\"value\":" + threshold + "}]";
        Assert.Equal(expected, SalesRules.Evaluate([Rule(json, Free)], Ctx(amount: amount)).FreeShipping);
    }

    [Fact]
    public void Country_Client_Group_EnLista_CaseInsensitive()
    {
        Assert.True(SalesRules.Evaluate([Rule("""[{"type":"country","values":["ES","FR"]}]""", Free)], Ctx(country: "fr")).FreeShipping);
        Assert.False(SalesRules.Evaluate([Rule("""[{"type":"country","values":["ES","FR"]}]""", Free)], Ctx(country: "GB")).FreeShipping);
        Assert.True(SalesRules.Evaluate([Rule("""[{"type":"client","values":["C1","C2"]}]""", Free)], Ctx(client: "c2")).FreeShipping);
        Assert.True(SalesRules.Evaluate([Rule("""[{"type":"client_group","values":["G1"]}]""", Free)], Ctx(group: "g1")).FreeShipping);
    }

    [Fact]
    public void Models_Products_Intersecan_ConElCarrito()
    {
        var r = Rule("""[{"type":"models","values":["M1","M3"]}]""", Free);
        Assert.True(SalesRules.Evaluate([r], Ctx(models: ["m3", "m9"])).FreeShipping);   // m3 está
        Assert.False(SalesRules.Evaluate([r], Ctx(models: ["m9"])).FreeShipping);
    }

    [Fact]
    public void AgentCart_Y_DateBetween()
    {
        Assert.True(SalesRules.Evaluate([Rule("""[{"type":"agent_cart","value":true}]""", Free)], Ctx(agent: true)).FreeShipping);
        Assert.False(SalesRules.Evaluate([Rule("""[{"type":"agent_cart","value":true}]""", Free)], Ctx(agent: false)).FreeShipping);
        var between = Rule("""[{"type":"date_between","from":"2026-01-01","to":"2026-12-31"}]""", Free);
        Assert.True(SalesRules.Evaluate([between], Ctx(date: new DateOnly(2026, 6, 15))).FreeShipping);
        Assert.False(SalesRules.Evaluate([between], Ctx(date: new DateOnly(2025, 6, 15))).FreeShipping);
    }

    // ── Acciones (agregación) ───────────────────────────────────────────────────
    [Fact]
    public void Denegar_Carrito()
    {
        var r = Rule(AnyCond, """[{"type":"deny","message":"Bloqueado"}]""");
        var res = SalesRules.Evaluate([r], Ctx(units: 1));
        Assert.True(res.Denied);
        Assert.Equal("Bloqueado", res.DeniedReason);
    }

    [Fact]
    public void PortesGratis_Ganan_AlImporteFijo()
    {
        var free = Rule(AnyCond, Free, priority: 1);
        var fixed30 = Rule(AnyCond, """[{"type":"fixed_transport","amount":30}]""", priority: 2);
        var res = SalesRules.Evaluate([free, fixed30], Ctx(units: 1));
        Assert.True(res.FreeShipping);
        Assert.Equal(0m, res.TransportCost);   // portes gratis → 0 aunque haya importe fijo
    }

    [Fact]
    public void ImporteFijo_LaPrimeraQueCasaGana_PorPrioridad()
    {
        var a = Rule(AnyCond, """[{"type":"fixed_transport","amount":10}]""", priority: 1);
        var b = Rule(AnyCond, """[{"type":"fixed_transport","amount":30}]""", priority: 2);
        Assert.Equal(10m, SalesRules.Evaluate([b, a], Ctx(units: 1)).TransportCost);   // gana priority 1 = 10
    }

    [Fact]
    public void Descuentos_SeAcumulan()
    {
        var pct = Rule(AnyCond, """[{"type":"line_discount_percent","percent":10}]""", priority: 1);
        var pct2 = Rule(AnyCond, """[{"type":"line_discount_percent","percent":5}]""", priority: 2);
        var fixed5 = Rule(AnyCond, """[{"type":"line_discount_fixed","amount":5}]""", priority: 3);
        var res = SalesRules.Evaluate([pct, pct2, fixed5], Ctx(units: 1));
        Assert.Equal(15m, res.LineDiscountPercent);   // 10 + 5
        Assert.Equal(5m, res.LineDiscountFixed);
    }

    [Fact]
    public void ReglasInactivas_SeIgnoran_Y_SinCondiciones_NoAplica()
    {
        Assert.False(SalesRules.Evaluate([Rule(AnyCond, Free, active: false)], Ctx(units: 1)).FreeShipping);
        Assert.False(SalesRules.Evaluate([Rule("[]", Free)], Ctx(units: 1)).FreeShipping); // sin condiciones no aplica
    }

    [Fact]
    public void NingunaCasa_ResultadoVacio()
    {
        var r = Rule("""[{"type":"country","values":["FR"]}]""", Free);
        var res = SalesRules.Evaluate([r], Ctx(country: "ES"));
        Assert.False(res.Denied);
        Assert.False(res.FreeShipping);
        Assert.Null(res.TransportCost);
        Assert.Empty(res.MatchedRuleIds);
    }

    // Condición "siempre cierta" para tests de acciones (unidades >= 0) y acción portes gratis.
    private const string AnyCond = """[{"type":"min_units","value":0}]""";
    private const string Free = """[{"type":"free_shipping"}]""";
}
