using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Contrato: docs/contrato-api/01 §3.4 (GET/DELETE de ofertas por reconciliación)
// y docs/contrato-api/04 §6 (búsqueda de pedidos GET con body).
public class OfferAndOrderQueryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OfferAndOrderQueryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpRequestMessage> Authenticated(HttpMethod method, string route, string? json = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        return request;
    }

    private async Task PutOffer(string id, string modelId) =>
        (await _client.SendAsync(await Authenticated(HttpMethod.Put, $"/api/catalog/offers/{id}",
            $$"""{"modelId":"{{modelId}}","price":10.5}"""))).EnsureSuccessStatusCode();

    [Fact]
    public async Task GetOffers_ConBodyModelId_DevuelveSoloLasOfertasDeEseModelo()
    {
        const string modelA = "AAAA1111-4F3B-4E2A-9D77-001122334455";
        const string modelB = "BBBB2222-4F3B-4E2A-9D77-001122334455";
        await PutOffer("OFERTA-A1-4E2A-9D77-001122334455", modelA);
        await PutOffer("OFERTA-A2-4E2A-9D77-001122334455", modelA);
        await PutOffer("OFERTA-B1-4E2A-9D77-001122334455", modelB);

        // El conector envía GET con body JSON (hallazgo contrato 01 §2.2)
        var response = await _client.SendAsync(await Authenticated(
            HttpMethod.Get, "/api/catalog/offers", $$"""{"modelId":"{{modelA}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Contains("OFERTA-A1-4E2A-9D77-001122334455", ids);
        Assert.Contains("OFERTA-A2-4E2A-9D77-001122334455", ids);
    }

    [Fact]
    public async Task GetOffers_SinToken_Devuelve401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/offers")
        {
            Content = new StringContent("""{"modelId":"X"}""", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOffer_EliminaYEsIdempotente()
    {
        const string offerId = "BORRAR11-4F3B-4E2A-9D77-001122334455";
        await PutOffer(offerId, "CCCC3333-4F3B-4E2A-9D77-001122334455");

        // El conector no envía body ni Content-Type en DELETE (contrato 01 §2.2)
        var first = await _client.SendAsync(await Authenticated(HttpMethod.Delete, $"/api/catalog/offers/{offerId}"));
        var second = await _client.SendAsync(await Authenticated(HttpMethod.Delete, $"/api/catalog/offers/{offerId}"));

        // 204 No Content (DELETE REST estándar sin cuerpo): el 200+JSON de antes rompía
        // el HttpClient del conector. Idempotente: el segundo DELETE también da 204.
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(db.SyncDocuments, d => d.EntityType == "offer" && d.ExternalId == offerId);
    }

    [Fact]
    public async Task SearchOrders_DevuelveLosPedidosRecibidos()
    {
        const string orderId = "PEDIDO11-4F3B-4E2A-9D77-001122334455";
        (await _client.SendAsync(await Authenticated(HttpMethod.Put, $"/api/orders/{orderId}",
            $$"""{"id":"{{orderId}}","status":"open"}"""))).EnsureSuccessStatusCode();

        // El adapter de pedidos usa GET con body {"search":[{"all":true}]} (contrato 04 §6)
        var response = await _client.SendAsync(await Authenticated(
            HttpMethod.Get, "/api/orders/search", """{"search":[{"all":true}]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetString() == orderId);
    }

    [Fact]
    public async Task SearchOrders_TambienAceptaPost()
    {
        // Doc 01 §2.3 lista las búsquedas bajo el Post Api Manager: aceptamos ambos métodos
        var response = await _client.SendAsync(await Authenticated(
            HttpMethod.Post, "/api/orders/search", """{"search":[{"all":true}]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
