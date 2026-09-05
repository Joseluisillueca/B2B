using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace B2B.Api.Tests;

// El PDF del catálogo salía con tres fallos de multi-instancia: el fichero se llamaba
// siempre "catalogo-lejan.pdf" (la marca antigua, en TODAS las instancias), los marcos
// de foto salían vacíos cuando la imagen la aloja el propio portal, porque se buscaba
// como fichero en disco y en realidad es un binario guardado en la base de datos, y la
// paleta (verde y crema) iba escrita a fuego: de la marca de la instancia solo se leía
// el nombre, así que un portal rojo sobre blanco descargaba un PDF verde sobre crema.
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

    /// Marca de la instancia: nombre, color de acento (#rrggbb) y tokens de diseño (JSON
    /// plano o null). Con todo a null la instancia vuelve a la marca por defecto del producto.
    private async Task Marca(string? nombre, string? color = null, string? tokens = null)
    {
        static string Json(string? s) => s is null ? "null" : $"\"{s}\"";
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/integration/branding")
        {
            Content = new StringContent(
                $$"""{"name":{{Json(nombre)}},"color":{{Json(color)}},"tokens":{{tokens ?? "null"}} }""",
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

    // ── 3. La paleta del PDF sale de la marca de la instancia ──────────────────
    // Que los bytes difieran es necesario pero no suficiente (el PDF lleva fecha de
    // creación). La prueba honesta: los streams del PDF van comprimidos (Flate), así que
    // se inflan y se busca el operador de color PDF ("r g b rg") del acento de cada marca:
    // el verde tiene que estar en el PDF verde y NO en el azul, y al revés.

    [Fact]
    public async Task LaPaletaDelPdfSaleDelColorDeMarcaDeLaInstancia()
    {
        const string modelId = "PDFPAL01-0000-4000-9000-000000000001";
        await Connector($"/api/catalog/models/{modelId}",
            $$$"""{"name":{"es_ES":"PDFPALETA UNO"},"active":true,"externalReference":"PDFPALETA-1","familyId":"calzado","productSegments":["A"]}""");

        const string verde = "#1f5c46";
        const string azul = "#0044cc";
        try
        {
            await Marca("MARCA VERDE", verde);
            var catalogoVerde = await BytesAsync(await PdfAsync("&q=PDFPALETA"));
            var fichaVerde = await BytesAsync(await FichaAsync("PDFPALETA-1"));

            await Marca("MARCA AZUL", azul);
            var catalogoAzul = await BytesAsync(await PdfAsync("&q=PDFPALETA"));
            var fichaAzul = await BytesAsync(await FichaAsync("PDFPALETA-1"));

            foreach (var pdf in new[] { catalogoVerde, fichaVerde, catalogoAzul, fichaAzul })
                Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

            Assert.False(catalogoVerde.AsSpan().SequenceEqual(catalogoAzul),
                "El catálogo PDF sale idéntico con dos colores de marca distintos: la paleta sigue clavada.");
            Assert.False(fichaVerde.AsSpan().SequenceEqual(fichaAzul),
                "La ficha técnica sale idéntica con dos colores de marca distintos: la paleta sigue clavada.");

            Assert.True(TieneColor(catalogoVerde, verde), "El catálogo con marca verde no pinta nada en verde.");
            Assert.False(TieneColor(catalogoVerde, azul), "El catálogo con marca verde pinta en azul.");
            Assert.True(TieneColor(catalogoAzul, azul), "El catálogo con marca azul no pinta nada en azul.");
            Assert.False(TieneColor(catalogoAzul, verde), "El catálogo con marca azul pinta en verde.");
            Assert.True(TieneColor(fichaVerde, verde), "La ficha con marca verde no pinta nada en verde.");
            Assert.True(TieneColor(fichaAzul, azul), "La ficha con marca azul no pinta nada en azul.");
        }
        finally { await Marca(null); }
    }

    // ── 4. Papel, tinta y superficie salen de los tokens de diseño ─────────────

    [Fact]
    public async Task ElPapelLaTintaYLaSuperficieDelPdfSalenDeLosTokensDeMarca()
    {
        const string modelId = "PDFTOK01-0000-4000-9000-000000000001";
        await Connector($"/api/catalog/models/{modelId}",
            $$$"""{"name":{"es_ES":"PDFTOKENS UNO"},"active":true,"externalReference":"PDFTOKENS-1","familyId":"calzado","productSegments":["A"]}""");

        const string papel = "#fff4e5";
        const string tinta = "#123456";
        const string superficie = "#e0e7ff";
        try
        {
            await Marca(null, null, $$"""{"paper":"{{papel}}","ink":"{{tinta}}","surface":"{{superficie}}"}""");
            var conTokens = await BytesAsync(await PdfAsync("&q=PDFTOKENS"));
            Assert.True(TieneColor(conTokens, papel), "El PDF no pinta el papel del token `paper`.");
            Assert.True(TieneColor(conTokens, tinta), "El PDF no escribe con la tinta del token `ink`.");
            Assert.True(TieneColor(conTokens, superficie), "El PDF no usa la superficie del token `surface`.");

            // Sin tokens, ninguno de esos colores aparece: el papel vuelve al blanco neutro.
            await Marca(null);
            var sinTokens = await BytesAsync(await PdfAsync("&q=PDFTOKENS"));
            Assert.False(TieneColor(sinTokens, papel), "Sin tokens el PDF sigue pintando el papel de la instancia anterior.");
            Assert.False(TieneColor(sinTokens, tinta), "Sin tokens el PDF sigue escribiendo con la tinta de la instancia anterior.");
        }
        finally { await Marca(null); }
    }

    // ── Utilidades de inspección del PDF (sin abrirlo a mano) ──────────────────

    private async Task<HttpResponseMessage> FichaAsync(string referencia)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/portal/product/{referencia}/tech-sheet.pdf?locale=es");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.GetTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private static async Task<byte[]> BytesAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// Operador de color PDF: "r g b rg" (relleno) o "RG" (trazo), con componentes 0..1.
    private static readonly Regex OperadorColor = new(
        @"(?<r>-?\d*\.?\d+)\s+(?<g>-?\d*\.?\d+)\s+(?<b>-?\d*\.?\d+)\s+(rg|RG)\b", RegexOptions.Compiled);

    /// ¿Pinta el PDF con este color (#rrggbb)? Infla cada stream Flate del fichero y busca
    /// el operador de color con las componentes del hex (Skia las escribe con 4 decimales).
    private static bool TieneColor(byte[] pdf, string hex)
    {
        var esperado = new[]
        {
            Convert.ToInt32(hex[1..3], 16) / 255.0,
            Convert.ToInt32(hex[3..5], 16) / 255.0,
            Convert.ToInt32(hex[5..7], 16) / 255.0,
        };
        foreach (Match m in OperadorColor.Matches(ContenidoInflado(pdf)))
        {
            var visto = new[] { Numero(m.Groups["r"].Value), Numero(m.Groups["g"].Value), Numero(m.Groups["b"].Value) };
            if (Math.Abs(visto[0] - esperado[0]) < 0.002
                && Math.Abs(visto[1] - esperado[1]) < 0.002
                && Math.Abs(visto[2] - esperado[2]) < 0.002)
                return true;
        }
        return false;

        static double Numero(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }

    /// Texto de todos los streams del PDF ya inflados (los que no sean Flate se dejan tal cual).
    private static string ContenidoInflado(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var contenido = new StringBuilder();
        var i = 0;
        while ((i = raw.IndexOf("stream", i, StringComparison.Ordinal)) >= 0)
        {
            var inicio = i + "stream".Length;
            if (inicio < raw.Length && raw[inicio] == '\r') inicio++;
            if (inicio < raw.Length && raw[inicio] == '\n') inicio++;
            var fin = raw.IndexOf("endstream", inicio, StringComparison.Ordinal);
            if (fin < 0) break;
            try
            {
                using var zlib = new ZLibStream(new MemoryStream(pdf, inicio, fin - inicio), CompressionMode.Decompress);
                using var salida = new MemoryStream();
                zlib.CopyTo(salida);
                contenido.Append(Encoding.Latin1.GetString(salida.ToArray())).Append('\n');
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                contenido.Append(raw, inicio, fin - inicio).Append('\n');
            }
            i = fin + "endstream".Length;
        }
        return contenido.ToString();
    }
}
