using System.Net.Http.Headers;
using System.Text;
using B2B.Api.Data;
using B2B.Api.Shop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Hook de ingesta (proyección bc): el sync de client/agent proyecta el campo
// visibleAttributes del payload a CatalogVisibility (Source="bc"). En runtime,
// para un sujeto manda la fila "bc" si existe; si no, la "manual" (/manage).
// El scope de un actor (VisibilityStore.ScopeForAsync) intersecta cliente y agente.
public class VisibilityIngestTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VisibilityIngestTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task Put(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    // ── 1. Ingesta de cliente proyecta una fila bc ─────────────────────────────

    [Fact]
    public async Task IngestaCliente_ProyectaFilaBc()
    {
        const string clientId = "VISCLI0A-0000-4000-9000-000000000001";

        await Put($"/api/clients/{clientId}",
            """{"name":"Cliente visible","visibleAttributes":[{"attributeId":"marca","valueIds":["adidas"]}]}""");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.CatalogVisibilities
            .Where(v => v.SubjectType == "client" && v.SubjectId == clientId)
            .ToList();

        var row = Assert.Single(rows);
        Assert.Equal("bc", row.Source);
        Assert.Contains("adidas", row.RulesJson);
    }

    // ── 2. Sin el campo, la ingesta no toca nada (ni bc ni manual) ─────────────

    [Fact]
    public async Task IngestaSinCampo_NoTocaNada()
    {
        const string clientId = "VISCLI0B-0000-4000-9000-000000000002";

        await Put($"/api/clients/{clientId}",
            """{"name":"Cliente con reglas","visibleAttributes":[{"attributeId":"marca","valueIds":["nike"]}]}""");

        // Fila manual pre-sembrada directamente en BD (como si viniera de /manage)
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.CatalogVisibilities.Add(new CatalogVisibility
            {
                SubjectType = "client",
                SubjectId = clientId,
                RulesJson = """[{"attributeId":"marca","valueIds":["manual-brand"]}]""",
                Source = "manual"
            });
            await seedDb.SaveChangesAsync();
        }

        // Re-PUT del mismo cliente SIN visibleAttributes (payload normal de BC)
        await Put($"/api/clients/{clientId}", """{"name":"Cliente con reglas (renombrado)"}""");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.CatalogVisibilities
            .Where(v => v.SubjectType == "client" && v.SubjectId == clientId)
            .ToList();

        Assert.Equal(2, rows.Count);
        var bcRow = Assert.Single(rows, r => r.Source == "bc");
        Assert.Contains("nike", bcRow.RulesJson);
        var manualRow = Assert.Single(rows, r => r.Source == "manual");
        Assert.Contains("manual-brand", manualRow.RulesJson);
    }

    // ── 3. Ingesta de agente proyecta una fila bc con SubjectType="agent" ──────

    [Fact]
    public async Task IngestaAgente_ProyectaFilaBc()
    {
        const string agentId = "VISAGE0A-0000-4000-9000-000000000003";

        await Put($"/api/agents/{agentId}",
            """{"name":"Agente visible","visibleAttributes":[{"attributeId":"marca","valueIds":["puma"]}]}""");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.CatalogVisibilities
            .Where(v => v.SubjectType == "agent" && v.SubjectId == agentId)
            .ToList();

        var row = Assert.Single(rows);
        Assert.Equal("bc", row.Source);
        Assert.Contains("puma", row.RulesJson);
    }

    // ── 4. bc manda sobre manual; ScopeForAsync intersecta cliente y agente ────

    [Fact]
    public async Task BcMandaSobreManual()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        const string onlyBc = "VISRES0A-0000-4000-9000-000000000004";
        const string onlyManual = "VISRES0B-0000-4000-9000-000000000005";
        const string neither = "VISRES0C-0000-4000-9000-000000000006";
        const string both = "VISRES0D-0000-4000-9000-000000000007";

        db.CatalogVisibilities.AddRange(
            new CatalogVisibility { SubjectType = "client", SubjectId = onlyBc, Source = "bc", RulesJson = """[{"attributeId":"marca","valueIds":["bc-brand"]}]""" },
            new CatalogVisibility { SubjectType = "client", SubjectId = onlyManual, Source = "manual", RulesJson = """[{"attributeId":"marca","valueIds":["manual-brand"]}]""" },
            new CatalogVisibility { SubjectType = "client", SubjectId = both, Source = "bc", RulesJson = """[{"attributeId":"marca","valueIds":["bc-wins"]}]""" },
            new CatalogVisibility { SubjectType = "client", SubjectId = both, Source = "manual", RulesJson = """[{"attributeId":"marca","valueIds":["manual-loses"]}]""" });
        await db.SaveChangesAsync();

        Assert.Contains("bc-brand", await VisibilityStore.RulesForAsync(db, "client", onlyBc));
        Assert.Contains("manual-brand", await VisibilityStore.RulesForAsync(db, "client", onlyManual));
        Assert.Null(await VisibilityStore.RulesForAsync(db, "client", neither));
        var resolved = await VisibilityStore.RulesForAsync(db, "client", both);
        Assert.Contains("bc-wins", resolved);
        Assert.DoesNotContain("manual-loses", resolved);

        // ScopeForAsync intersecta cliente + agente: solo la marca común pasa
        const string clientId = "VISSCO0A-0000-4000-9000-000000000008";
        const string agentId = "VISSCO0B-0000-4000-9000-000000000009";
        db.CatalogVisibilities.AddRange(
            new CatalogVisibility { SubjectType = "client", SubjectId = clientId, Source = "bc", RulesJson = """[{"attributeId":"marca","valueIds":["adidas","nike"]}]""" },
            new CatalogVisibility { SubjectType = "agent", SubjectId = agentId, Source = "bc", RulesJson = """[{"attributeId":"marca","valueIds":["nike","puma"]}]""" });
        await db.SaveChangesAsync();

        var actorScope = await VisibilityStore.ScopeForAsync(db, clientId, agentId);

        var nikeModel = new CatalogModel { ExternalId = "m-nike", AttributesJson = """{"marca":"nike"}""" };
        var adidasModel = new CatalogModel { ExternalId = "m-adidas", AttributesJson = """{"marca":"adidas"}""" };
        var pumaModel = new CatalogModel { ExternalId = "m-puma", AttributesJson = """{"marca":"puma"}""" };

        Assert.True(actorScope.Visible(nikeModel));    // intersección: en ambos
        Assert.False(actorScope.Visible(adidasModel));  // solo en el cliente
        Assert.False(actorScope.Visible(pumaModel));    // solo en el agente
    }
}
