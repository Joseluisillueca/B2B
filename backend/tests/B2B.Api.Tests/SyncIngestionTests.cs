using System.Net;
using System.Net.Http.Headers;
using System.Text;
using B2B.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Contrato: docs/contrato-api/01-autenticacion-y-convenciones.md §2
// Todos los PUT del conector son upserts idempotentes con Bearer token y body JSON
// (objeto o array). Respuesta 2xx SIEMPRE con body JSON (nunca texto plano) o vacío.
public class SyncIngestionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SyncIngestionTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Una ruta PUT por cada campo "... URL" del Setup del conector (contrato 01 §4.2)
    public static TheoryData<string, string> UpsertRoutes => new()
    {
        { "/api/catalog/models/08A9C2D1-4F3B-4E2A-9D77-001122334455", "model" },
        { "/api/catalog/products/18A9C2D1-4F3B-4E2A-9D77-001122334455", "product" },
        { "/api/catalog/attributes/tallas", "attribute" },
        { "/api/catalog/model-images/28A9C2D1-4F3B-4E2A-9D77-001122334455", "model-image" },
        { "/api/catalog/categories/catalog.mujer.zapatos", "category" },
        { "/api/catalog/families/sandalias", "family" },
        { "/api/catalog/case-packs/38A9C2D1-4F3B-4E2A-9D77-001122334455", "case-pack" },
        { "/api/catalog/offers/48A9C2D1-4F3B-4E2A-9D77-001122334455", "offer" },
        { "/api/stock/inventory/58A9C2D1-4F3B-4E2A-9D77-001122334455", "inventory" },
        { "/api/core/service-windows/VENTANA01", "service-window" },
        { "/api/core/warehouses/REPOSIC", "warehouse" },
        { "/api/core/payment-methods/CONTADO", "payment-method" },
        { "/api/core/b2binfo", "company" },
        { "/api/clients/68A9C2D1-4F3B-4E2A-9D77-001122334455", "client" },
        { "/api/clients/groups/MAYORISTA", "client-group" },
        { "/api/agents/78A9C2D1-4F3B-4E2A-9D77-001122334455", "agent" },
        { "/api/orders/88A9C2D1-4F3B-4E2A-9D77-001122334455", "order" },
        { "/api/documents/delivery-notes/98A9C2D1-4F3B-4E2A-9D77-001122334455", "delivery-note" },
        { "/api/documents/invoices/A8A9C2D1-4F3B-4E2A-9D77-001122334455", "invoice" },
    };

    private async Task<HttpRequestMessage> AuthenticatedPut(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return request;
    }

    [Theory]
    [MemberData(nameof(UpsertRoutes))]
    public async Task Put_SinToken_Devuelve401(string route, string _)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent("""{"x":1}""", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(UpsertRoutes))]
    public async Task Put_ConToken_Devuelve200JsonYPersisteElPayload(string route, string entityType)
    {
        var payload = $$"""{"name":"prueba {{entityType}}","values":[1,2,3]}""";

        var response = await _client.SendAsync(await AuthenticatedPut(route, payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = db.SyncDocuments.Single(d => d.EntityType == entityType);
        Assert.Equal(payload, doc.Payload);
        Assert.NotEqual(default, doc.LastReceivedAt);
    }

    [Fact]
    public async Task Put_DosVeces_ActualizaSinDuplicar()
    {
        const string route = "/api/catalog/models/UPSERT-4F3B-4E2A-9D77-001122334455";

        await _client.SendAsync(await AuthenticatedPut(route, """{"version":1}"""));
        var second = await _client.SendAsync(await AuthenticatedPut(route, """{"version":2}"""));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var docs = db.SyncDocuments.Where(d => d.ExternalId == "UPSERT-4F3B-4E2A-9D77-001122334455").ToList();
        var doc = Assert.Single(docs);
        Assert.Equal("""{"version":2}""", doc.Payload);
        Assert.True(doc.LastReceivedAt >= doc.FirstReceivedAt);
    }

    [Fact]
    public async Task Put_ConBodyArray_SeAcepta()
    {
        // El manager PUT de BC soporta arrays explícitamente (contrato 01 §2.4)
        var response = await _client.SendAsync(
            await AuthenticatedPut("/api/catalog/offers/ARRAY-4F3B-4E2A-9D77-001122334455", """[{"a":1},{"a":2}]"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_ConBodyNoJson_Devuelve400()
    {
        var response = await _client.SendAsync(
            await AuthenticatedPut("/api/catalog/models/BAD-4F3B-4E2A-9D77-001122334455", "esto no es json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UsuarioAdminDeCliente_GuardaConParentId()
    {
        // El conector deriva esta ruta de la de clientes + "/users/admin" (contrato 04)
        const string clientId = "C8A9C2D1-4F3B-4E2A-9D77-001122334455";

        var response = await _client.SendAsync(
            await AuthenticatedPut($"/api/clients/{clientId}/users/admin", """{"email":"cliente@test.com"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = db.SyncDocuments.Single(d => d.EntityType == "client-user");
        Assert.Equal(clientId, doc.ExternalId);
    }

    [Fact]
    public async Task Put_DireccionDeEnvio_GuardaConParentId()
    {
        // Ruta derivada: clientes + "/shipping-addresses/{addressId}" (contrato 04)
        const string clientId = "D8A9C2D1-4F3B-4E2A-9D77-001122334455";
        const string addressId = "E8A9C2D1-4F3B-4E2A-9D77-001122334455";

        var response = await _client.SendAsync(
            await AuthenticatedPut($"/api/clients/{clientId}/shipping-addresses/{addressId}", """{"city":"Elche"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = db.SyncDocuments.Single(d => d.EntityType == "shipping-address");
        Assert.Equal(addressId, doc.ExternalId);
        Assert.Equal(clientId, doc.ParentId);
    }
}
