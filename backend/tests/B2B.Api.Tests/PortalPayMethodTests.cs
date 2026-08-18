using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace B2B.Api.Tests;

// Auditoría M5 (checkout, MÉTODO DE PAGO). El cliente llega del sync con sus formas de
// pago en códigos ("transf30") y el nombre legible vive en otro documento del sync
// (payment-method, contrato 03 §5). En el navegador no había forma de casar código y
// nombre: /invoices trae el nombre sin código y /orders el código sin nombre. Ahora
// /api/portal/me sirve las dos caras.
public class PortalPayMethodTests : IClassFixture<PortalPayMethodTests.Factory>, IAsyncLifetime
{
    public class Factory : TestWebApplicationFactory { }

    private const string Client = "PAYM0001-1111-4111-8111-000000000001";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PortalPayMethodTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task PutAsync(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> MeAsync(string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/me" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.GetTokenAsync(_client));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static bool _seeded;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task SeedAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            // Maestro de formas de pago: solo "transf30" está publicado. "contado" no,
            // que es el caso que obliga a tener un fallback.
            await PutAsync("/api/core/payment-methods/transf30", """
            {"name":{"es_ES":"Transferencia 30 días","en_EN":"Bank transfer 30 days",
              "fr_FR":"Virement 30 jours","it_IT":"Bonifico 30 giorni"},
             "description":{"es_ES":"Transferencia 30 días"},
             "order":10,"allowCredit":true,"requiredForConfirm":false,"requiresStock":false,
             "externalReference":"TRANSF30"}
            """);

            await PutAsync($"/api/clients/{Client}", """
            {"name":"TEST PAGO","externalReference":"C100077","canShop":true,
             "groupIds":["mayorista"],"productSegments":["A+"],
             "payMethods":["transf30","contado"],"markets":["es"]}
            """);
            await PutAsync($"/api/clients/{Client}/users/admin",
                $$"""{"email":"{{TestWebApplicationFactory.SeededEmail}}","name":"Test","culture":"es_ES"}""");

            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private static JsonElement PayMethods(JsonElement body) =>
        body.GetProperty("client").GetProperty("payMethods");

    [Fact]
    public async Task Me_LasFormasDePagoTraenCodigoYNombreLegible()
    {
        var methods = PayMethods(await MeAsync()).EnumerateArray().ToList();

        var transferencia = methods.Single(m => m.GetProperty("id").GetString() == "transf30");
        Assert.Equal("Transferencia 30 días", transferencia.GetProperty("name").GetString());
    }

    // Sin ficha en el maestro no se inventa nombre: se enseña el código, que es
    // exactamente lo que se veía antes. Nunca una celda vacía en el checkout.
    [Fact]
    public async Task Me_SinFichaEnElMaestro_ElNombreEsElCodigo()
    {
        var methods = PayMethods(await MeAsync()).EnumerateArray().ToList();

        var contado = methods.Single(m => m.GetProperty("id").GetString() == "contado");
        Assert.Equal("contado", contado.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("en", "Bank transfer 30 days")]
    [InlineData("fr", "Virement 30 jours")]
    [InlineData("it", "Bonifico 30 giorni")]
    [InlineData("de", "Transferencia 30 días")]   // idioma desconocido: español
    public async Task Me_ConLocale_DevuelveElNombreEnEseIdioma(string locale, string expected)
    {
        var methods = PayMethods(await MeAsync($"?locale={locale}")).EnumerateArray().ToList();

        Assert.Equal(expected, methods.Single(m => m.GetProperty("id").GetString() == "transf30")
            .GetProperty("name").GetString());
    }

    // Compatibilidad: quien solo quiera los códigos los sigue teniendo en una lista
    // de cadenas, sin tener que conocer la forma nueva.
    [Fact]
    public async Task Me_MantieneLaListaDeCodigosSueltos()
    {
        var body = await MeAsync();

        var ids = body.GetProperty("client").GetProperty("payMethodIds")
            .EnumerateArray().Select(i => i.GetString()).ToList();
        Assert.Equal(["transf30", "contado"], ids);
    }

    [Fact]
    public async Task Me_ConservaElOrdenDelSync()
    {
        var ids = PayMethods(await MeAsync()).EnumerateArray()
            .Select(m => m.GetProperty("id").GetString()).ToList();

        Assert.Equal(["transf30", "contado"], ids);
    }
}
