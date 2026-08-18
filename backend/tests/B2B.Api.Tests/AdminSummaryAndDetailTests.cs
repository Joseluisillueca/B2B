using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// CMS: resumen por tipo de entidad y detalle con payload de un documento
public class AdminSummaryAndDetailTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminSummaryAndDetailTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string? json = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetAdminTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Summary_AgrupaPorTipoConCuentaYUltimaRecepcion()
    {
        (await Send(HttpMethod.Put, "/api/core/warehouses/SUM1", """{"code":"SUM1"}""")).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Put, "/api/core/warehouses/SUM2", """{"code":"SUM2"}""")).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Put, "/api/agents/SUMAGENT-4F3B-4E2A-9D77-001122334455", """{"name":"Agente"}""")).EnsureSuccessStatusCode();

        var response = await Send(HttpMethod.Get, "/api/admin/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        var warehouses = items.Single(i => i.GetProperty("entityType").GetString() == "warehouse");
        Assert.True(warehouses.GetProperty("count").GetInt32() >= 2);
        Assert.True(warehouses.TryGetProperty("lastReceivedAt", out _));
    }

    [Fact]
    public async Task Detail_DevuelveElPayloadCompleto()
    {
        (await Send(HttpMethod.Put, "/api/core/warehouses/DETAIL1", """{"code":"DETAIL1","active":true}"""))
            .EnsureSuccessStatusCode();

        var response = await Send(HttpMethod.Get, "/api/admin/sync-documents/warehouse/DETAIL1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("warehouse", body.GetProperty("entityType").GetString());
        Assert.Equal("DETAIL1", body.GetProperty("externalId").GetString());
        Assert.Contains("DETAIL1", body.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task Detail_Inexistente_Devuelve404()
    {
        var response = await Send(HttpMethod.Get, "/api/admin/sync-documents/warehouse/NOEXISTE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
