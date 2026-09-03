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
        Assert.True(s.Visible(Model(attrsJson: """{"Marca":"ADIDAS"}""")));
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
            """[{"attributeId":"marca","valueIds":["adidas","nike"]}]""",
            """[{"attributeId":"marca","valueIds":["adidas","puma"]}]"""
        ]);
        Assert.True(s.Visible(Model(attrsJson: """{"Marca":"ADIDAS"}""")));
        Assert.False(s.Visible(Model(attrsJson: """{"Marca":"NIKE"}""")));
        Assert.False(s.Visible(Model(attrsJson: """{"Marca":"PUMA"}""")));
    }

    [Fact] public void ValoresConEspacios_CasanPorSlug()
    {
        var s = Scope("""[{"attributeId":"grupo-de-edad","valueIds":["adulto-joven"]}]""");
        Assert.True(s.Visible(Model(attrsJson: """{"Grupo de edad":"Adulto Joven"}""")));
    }

    [Fact] public void ReglasRotas_NoRestringen()
    {
        var s = VisibilityScope.FromRules(["esto-no-es-json"]);
        Assert.True(s.Visible(Model()));
    }
}
