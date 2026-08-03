using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// El CMS pinta columnas por entidad (modelo, pedido, oferta...) leyendo el payload
// directamente en el listado: includePayload=true lo incluye por fila.
public class AdminIncludePayloadTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminIncludePayloadTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListConIncludePayload_DevuelveElPayloadPorFila()
    {
        var token = await _factory.GetTokenAsync(_client);
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/core/warehouses/PAYLOAD1")
        {
            Content = new StringContent("""{"code":"PAYLOAD1","active":true}""", Encoding.UTF8, "application/json")
        };
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(put)).EnsureSuccessStatusCode();

        var get = new HttpRequestMessage(HttpMethod.Get,
            "/api/admin/sync-documents?entityType=warehouse&includePayload=true");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("externalId").GetString() == "PAYLOAD1");
        Assert.Contains("PAYLOAD1", row.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task ListSinIncludePayload_NoIncluyePayload()
    {
        var token = await _factory.GetTokenAsync(_client);
        var get = new HttpRequestMessage(HttpMethod.Get, "/api/admin/sync-documents?take=1");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(get);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.GetProperty("items").EnumerateArray())
            Assert.False(item.TryGetProperty("payload", out _));
    }
}
