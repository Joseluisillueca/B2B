using System.Net.Http.Headers;
using System.Text;

namespace B2B.Api.Tests;

// El PDF del catálogo salía con dos fallos de multi-instancia: el fichero se llamaba
// siempre "catalogo-lejan.pdf" (la marca antigua, en TODAS las instancias) y los marcos
// de foto salían vacíos cuando la imagen la aloja el propio portal, porque se buscaba
// como fichero en disco y en realidad es un binario guardado en la base de datos.
public class CatalogPdfBrandTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Jpeg1x1 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a"
        + "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA"
        + "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogPdfBrandTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task Connector(string route, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetConnectorTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task Marca(string? nombre)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/integration/branding")
        {
            Content = new StringContent($$"""{"name":{{(nombre is null ? "null" : $"\"{nombre}\"")}} }""",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetAdminTokenAsync(_client));
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PdfAsync(string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/catalog.pdf?locale=es" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    // ── 1. El fichero lleva la marca de la instancia, no una escrita a mano ────

    [Fact]
    public async Task ElNombreDelFicheroSaleDeLaMarcaDeLaInstancia()
    {
        const string modelId = "PDFBR001-0000-4000-9000-000000000001";
        await Connector($"/api/catalog/models/{modelId}",
            $$$"""{"name":{"es_ES":"PDFMARCA UNO"},"active":true,"externalReference":"PDFMARCA-1","familyId":"calzado","productSegments":["A"]}""");

        try
        {
            await Marca("ALMA EN PENA");
            var conMarca = await PdfAsync("&q=PDFMARCA");
            conMarca.EnsureSuccessStatusCode();
            Assert.Equal("catalogo-alma-en-pena.pdf", conMarca.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

            // Sin marca propia, el nombre sale de la marca por defecto del producto
            await Marca(null);
            var porDefecto = await PdfAsync("&q=PDFMARCA");
            porDefecto.EnsureSuccessStatusCode();
            Assert.Equal("catalogo-mito-projects.pdf", porDefecto.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

            // Y en ningún caso el nombre de una marca ajena
            Assert.DoesNotContain("lejan", conMarca.Content.Headers.ContentDisposition?.FileName ?? "",
                StringComparison.OrdinalIgnoreCase);
        }
        finally { await Marca(null); }
    }

    // ── 2. La foto alojada por el portal entra en el PDF ───────────────────────
    // El PDF con foto pesa sensiblemente más que el mismo PDF sin ella: es la forma
    // honesta de comprobar que la imagen viaja dentro, sin abrir el PDF.

    [Fact]
    public async Task LaFotoAlojadaPorElPortalEntraEnElPdf()
    {
        const string sinFoto = "PDFIMG01-0000-4000-9000-000000000001";
        const string conFoto = "PDFIMG02-0000-4000-9000-000000000002";

        await Connector($"/api/catalog/models/{sinFoto}",
            $$$"""{"name":{"es_ES":"PDFIMGA SIN FOTO"},"active":true,"externalReference":"PDFIMGA-1","familyId":"calzado","productSegments":["A"]}""");
        await Connector($"/api/catalog/models/{conFoto}",
            $$$"""{"name":{"es_ES":"PDFIMGB CON FOTO"},"active":true,"externalReference":"PDFIMGB-1","familyId":"calzado","productSegments":["A"]}""");
        // La foto llega en base64 y el documento con la uri vacía: la aloja el portal
        await Connector($"/api/catalog/model-images/{conFoto}",
            $$$"""{"images":[{"id":"{{{conFoto}}}","image":{"uri":"","order":0,"base64":"{{{Jpeg1x1}}}","contentType":"image/jpeg"}}]}""");

        var pdfSinFoto = await PdfAsync("&q=PDFIMGA");
        var pdfConFoto = await PdfAsync("&q=PDFIMGB");
        pdfSinFoto.EnsureSuccessStatusCode();
        pdfConFoto.EnsureSuccessStatusCode();

        var vacio = (await pdfSinFoto.Content.ReadAsByteArrayAsync()).Length;
        var conImagen = (await pdfConFoto.Content.ReadAsByteArrayAsync()).Length;
        Assert.True(conImagen > vacio,
            $"El PDF con foto ({conImagen} bytes) debería pesar más que el mismo sin foto ({vacio} bytes): la imagen alojada no ha entrado.");
    }
}
