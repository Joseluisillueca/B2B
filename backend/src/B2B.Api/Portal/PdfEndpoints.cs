using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using B2B.Api.Admin;
using B2B.Api.Data;
using B2B.Api.Shop;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace B2B.Api.Portal;

// PDFs comerciales con la marca de la instancia: ficha técnica de un producto y line-sheet de
// varios, siempre con la TARIFA DEL CLIENTE (o del cliente suplantado por el agente).
// El precio sale de CatalogService con el contexto del actor, igual que el catálogo.
// La paleta NO va clavada: sale de la marca de la instancia (PdfPalette, más abajo).
public static class PdfEndpoints
{
    // Tipografía: "Lato" es la ÚNICA familia que QuestPDF garantiza en cualquier entorno,
    // porque viaja dentro del propio paquete (carpeta LatoFont/ copiada al output por su
    // .targets y registrada al arrancar, sin tocar el sistema). El contenedor de producción
    // (aspnet:10.0 + libfontconfig1) no instala NINGUNA fuente del sistema: la antigua
    // "Arial" no existía allí y QuestPDF caía en silencio a Lato, mientras que en Windows
    // sí resolvía Arial. Declararla explícita hace que dev y producción rendericen el mismo
    // PDF. No se carga ninguna fuente web ni de la instancia (fontUrl es solo del portal).
    private const string Font = "Lato";

    public static void MapPdfEndpoints(this IEndpointRouteBuilder app)
    {
        // Ficha técnica de un producto (enciende el botón "Descargar ficha técnica").
        app.MapGet("/api/portal/product/{reference}/tech-sheet.pdf", async (
            string reference, HttpRequest request, ClaimsPrincipal principal,
            AppDbContext db, IWebHostEnvironment env, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var query = CatalogQuery.From(request.Query) with { Search = reference, Skip = 0, Take = 60, Locale = locale };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow, visibility);

            var row = page.Rows.FirstOrDefault(r =>
                          string.Equals(r.Model.ExternalReference, reference, StringComparison.OrdinalIgnoreCase))
                      ?? page.Rows.FirstOrDefault();
            if (row is null) return Results.NotFound();

            var clientName = await ClientNameAsync(db, actor);
            var image = await LoadImageAsync(row.ImageUri, env, httpFactory, db, config);

            var pdf = new TechSheetDocument(row, clientName, image, await BrandAsync(db)).GeneratePdf();
            var safeRef = string.Concat((row.Model.ExternalReference ?? "producto")
                .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            return Results.File(pdf, "application/pdf", $"ficha-{safeRef}.pdf");
        }).RequireAuthorization();

        // Line-sheet: catálogo comercial de VARIOS productos (preselección/carrito),
        // con la tarifa del cliente. Refs por query para que api.download (GET) baje
        // el PDF con el token.
        app.MapGet("/api/portal/line-sheet.pdf", async (
            HttpRequest request, ClaimsPrincipal principal,
            AppDbContext db, IWebHostEnvironment env, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var refs = request.Query["refs"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList();
            if (refs.Count == 0) return Results.BadRequest(new { error = "Indica al menos un producto (refs)." });

            var query = CatalogQuery.From(request.Query) with { Search = "", Skip = 0, Take = 300, Locale = locale };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow, visibility);
            var byRef = page.Rows
                .GroupBy(r => r.Model.ExternalReference ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = refs.Select(r => byRef.GetValueOrDefault(r)).OfType<CatalogRow>().ToList();
            if (rows.Count == 0) return Results.NotFound();

            var clientName = await ClientNameAsync(db, actor);
            var images = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                images[r.Model.ExternalReference ?? ""] = await LoadImageAsync(r.ImageUri, env, httpFactory, db, config);

            var heading = clientName.Length > 0 ? $"Selección para {clientName}" : "Selección de productos";
            var brand = await BrandAsync(db);
            var pdf = new LineSheetDocument(rows, images, clientName, heading, "LINE-SHEET", brand).GeneratePdf();
            return Results.File(pdf, "application/pdf", FileName("line-sheet", brand.Name));
        }).RequireAuthorization();

        // Catálogo completo (o filtrado) en PDF con marca y tarifa del cliente. Respeta
        // los mismos filtros que la barra del catálogo (línea, silueta, disponibilidad…).
        app.MapGet("/api/portal/catalog.pdf", async (
            HttpRequest request, ClaimsPrincipal principal,
            AppDbContext db, IWebHostEnvironment env, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var visibility = await VisibilityStore.ScopeForAsync(db, actor?.ClientId, actor?.User.AgentExternalId);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var query = CatalogQuery.From(request.Query) with { Skip = 0, Take = 300, Locale = locale };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow, visibility);
            var rows = page.Rows;
            if (rows.Count == 0) return Results.NotFound();

            var clientName = await ClientNameAsync(db, actor);
            var images = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                images[r.Model.ExternalReference ?? ""] = await LoadImageAsync(r.ImageUri, env, httpFactory, db, config);

            var heading = clientName.Length > 0 ? $"Catálogo · tarifa de {clientName}" : "Catálogo";
            var brand = await BrandAsync(db);
            var pdf = new LineSheetDocument(rows, images, clientName, heading, "CATÁLOGO", brand).GeneratePdf();
            return Results.File(pdf, "application/pdf", FileName("catalogo", brand.Name));
        }).RequireAuthorization();
    }

    private static PortalActorPrices Prices(PortalActor? actor) =>
        actor is null ? PortalActorPrices.Anonymous : new PortalActorPrices(actor.ClientId, actor.GroupIds);

    // ── Marca de la instancia: nombre + paleta ─────────────────────────────────
    // Los PDF llevaban una paleta escrita a fuego (verde y crema de la marca antigua) y de
    // la instancia solo leían el NOMBRE: un cliente con portal rojo sobre blanco recibía un
    // PDF verde sobre crema. Ahora el acento es el BrandColor de la instancia y papel, tinta
    // y superficie salen de sus tokens de diseño (BrandTokensJson) cuando los tiene.
    private sealed record PdfBrand(string Name, PdfPalette Palette);

    private static async Task<PdfBrand> BrandAsync(AppDbContext db)
    {
        var settings = await db.IntegrationSettings.FindAsync(1);
        // La misma lectura del JSON de tokens que hacen los endpoints de marca (objeto plano
        // {"paper":"#ffffff",...}); null cuando la instancia no tiene tokens.
        var tokens = VisibilityEndpoints.ParseNode(settings?.BrandTokensJson) as JsonObject;
        return new PdfBrand(
            settings?.BrandNameOrDefault ?? "MITO PROJECTS",
            PdfPalette.From(settings?.BrandColorOrDefault, tokens));
    }

    /// Paleta de un PDF derivada de la marca de la instancia. Todos los valores son "#rrggbb"
    /// listos para QuestPDF. Solo cambia el ORIGEN de los colores: los documentos conservan
    /// su estructura y reparten los mismos papeles (acento, papel, tinta, superficie, filete…).
    private sealed record PdfPalette(
        string Accent,         // color de marca: filete de cabecera y bloque del precio
        string Paper,          // fondo de página
        string Ink,            // texto principal
        string Surface,        // marcos de foto y cabeceras de tabla
        string Muted,          // texto secundario: tinta rebajada hacia el papel
        string Line,           // filetes finos de tablas y tarjetas
        string AccentText,     // texto PEQUEÑO de acento (precio de tarjeta, título "Tallas")
        string AccentDisplay,  // texto GRANDE de acento (nombre de marca en la cabecera)
        string OnAccent)       // texto encima del bloque de acento
    {
        // Defectos neutros (instancia sin tokens): blanco, casi negro y gris muy claro. El
        // rojo del producto es el mismo defecto que publica GET /api/portal/branding.
        private const string DefaultAccent = "#ec3013";
        private const string DefaultPaper = "#ffffff";
        private const string DefaultInk = "#1a1a1a";
        private const string DefaultSurface = "#f4f4f4";
        private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

        public static PdfPalette From(string? brandColor, JsonObject? tokens)
        {
            var accent = HexOrNull(brandColor) ?? DefaultAccent;
            var paper = Token(tokens, "paper") ?? DefaultPaper;
            var ink = Token(tokens, "ink") ?? DefaultInk;
            var surface = Token(tokens, "surface") ?? DefaultSurface;
            // Secundarios derivados de tinta y papel, para que sigan leyéndose sobre CUALQUIER
            // papel (un gris fijo desaparecería sobre un papel oscuro). El token `rule`, si la
            // instancia lo define, manda sobre el filete derivado.
            var muted = Mix(ink, paper, 0.36);
            var line = Token(tokens, "rule") ?? Mix(ink, paper, 0.86);

            // Contraste WCAG del acento sobre el papel. Un acento que no llega a 4,5:1 (el rojo
            // por defecto #ec3013 sobre blanco se queda en 4,2:1) NO sirve para texto pequeño:
            // ahí se usa solo en filetes y en el bloque del precio (con texto claro encima), y
            // los textos pequeños de acento (precio de tarjeta, título "Tallas") van en tinta.
            // El nombre de marca de la cabecera es texto grande y en negrita (20-22 pt), al que
            // WCAG pide 3:1: conserva el acento salvo que ni eso alcance.
            var accentOnPaper = Contrast(accent, paper);
            var accentText = accentOnPaper >= 4.5 ? accent : ink;
            var accentDisplay = accentOnPaper >= 3.0 ? accent : ink;
            // Sobre el bloque de acento va texto blanco; solo si el acento es tan claro que la
            // tinta contrasta más (un amarillo, un pastel) se usa tinta, para no perder el precio.
            var onAccent = Contrast("#ffffff", accent) >= Contrast(ink, accent) ? "#ffffff" : ink;

            return new PdfPalette(accent, paper, ink, surface, muted, line, accentText, accentDisplay, onAccent);
        }

        private static string? Token(JsonObject? tokens, string key) =>
            tokens?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? HexOrNull(text) : null;

        // Los tokens ya llegan normalizados por IntegrationEndpoints (#rrggbb en minúsculas);
        // esto NO revalida la marca, solo garantiza que lo que entra a QuestPDF y a las
        // cuentas de contraste es parseable: un valor raro heredado degrada al defecto en
        // vez de tumbar la descarga del PDF.
        private static string? HexOrNull(string? text)
        {
            text = text?.Trim();
            return text is not null && Hex.IsMatch(text) ? text.ToLowerInvariant() : null;
        }

        private static (int R, int G, int B) Rgb(string hex) =>
            (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

        /// Color a la fracción `t` del camino de `from` a `to` (0 = from, 1 = to).
        private static string Mix(string from, string to, double t)
        {
            var (r1, g1, b1) = Rgb(from);
            var (r2, g2, b2) = Rgb(to);
            static int Step(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
            return $"#{Step(r1, r2, t):x2}{Step(g1, g2, t):x2}{Step(b1, b2, t):x2}";
        }

        // Luminancia relativa y ratio de contraste según WCAG 2.x (sRGB linealizado).
        private static double Luminance(string hex)
        {
            var (r, g, b) = Rgb(hex);
            static double Linear(int channel)
            {
                var s = channel / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
        }

        private static double Contrast(string a, string b)
        {
            var (la, lb) = (Luminance(a), Luminance(b));
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }

    /// Nombre del fichero que descarga el cliente, con SU marca: "catalogo-alma-en-pena.pdf".
    /// Estaba escrito a mano con la marca antigua, así que todas las instancias descargaban
    /// un PDF con el nombre de otra empresa.
    private static string FileName(string prefix, string brand)
    {
        var slug = new string([.. brand.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length > 0 ? $"{prefix}-{slug}.pdf" : $"{prefix}.pdf";
    }

    private static async Task<string> ClientNameAsync(AppDbContext db, PortalActor? actor)
    {
        if (actor?.ClientId is not { Length: > 0 } clientId) return "";
        var doc = await db.SyncDocuments
            .FirstOrDefaultAsync(d => d.EntityType == "client" && d.ExternalId == clientId);
        if (doc is null || JsonNode.Parse(doc.Payload) is not JsonObject payload) return "";
        return DocumentProjections.Text(payload["name"]);
    }

    // Bytes de la imagen del producto: fichero local de /media o URL http(s). Si falla,
    // el PDF se genera igual sin foto (nunca revienta por una imagen).
    private static async Task<byte[]?> LoadImageAsync(
        string? uri, IWebHostEnvironment env, IHttpClientFactory httpFactory, AppDbContext db, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        // Medios del CMS (/media/portal/…): pueden estar en la base de datos (subidas), en
        // la carpeta de medios (subidas antiguas en disco) o dentro de la imagen (demo).
        // El mismo resolvedor que usa el endpoint que los sirve.
        if (uri.StartsWith(MediaEndpoints.UrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var leido = await MediaEndpoints.ReadAsync(uri[MediaEndpoints.UrlPrefix.Length..].Split('?')[0], db, config, env);
            if (leido is { } medio && !medio.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
                return medio.Bytes;
            return null;
        }
        // Foto ALOJADA por el portal: /media/models/{id}.jpg no es un fichero en disco,
        // es el binario que guardamos cuando el conector la manda en base64. Buscarla con
        // File.Exists devolvía null y el PDF salía con todos los marcos vacíos, que es
        // justo lo que pasa cuando el ERP manda las fotos embebidas y no por URL.
        const string alojadas = "/media/models/";
        if (uri.StartsWith(alojadas, StringComparison.OrdinalIgnoreCase))
        {
            var id = uri[alojadas.Length..].Split('?')[0];
            if (id.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
            var asset = await db.MediaAssets.SingleOrDefaultAsync(a => a.ExternalId == id);
            return asset?.Bytes is { Length: > 0 } bytes ? bytes : null;
        }
        // QuestPDF no decodifica SVG (ni otros vectoriales): se ignora y el PDF sale
        // con el marco vacío en vez de reventar.
        if (uri.Split('?')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (uri.StartsWith('/'))
            {
                var relative = uri.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(env.WebRootPath ?? "", relative);
                return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
            }
            if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(6);
                return await http.GetByteArrayAsync(uri);
            }
        }
        catch { /* sin foto, pero con PDF */ }
        return null;
    }

    // ── Documento: ficha técnica ────────────────────────────────────────────────
    private sealed class TechSheetDocument(CatalogRow row, string clientName, byte[]? image, PdfBrand brand) : IDocument
    {
        private PdfPalette Pal => brand.Palette;

        public void Compose(IDocumentContainer container) => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.PageColor(Pal.Paper);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Pal.Ink).FontFamily(Font));

            page.Header().Element(Header);
            page.Content().PaddingVertical(14).Element(Body);
            page.Footer().Element(Footer);
        });

        private void Header(IContainer c) => c.BorderBottom(1.5f).BorderColor(Pal.Accent).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Text($"{brand.Name}™").FontSize(22).Bold().FontColor(Pal.AccentDisplay);
            row.RelativeItem().AlignRight().AlignBottom().Text("FICHA TÉCNICA")
                .FontSize(10).FontColor(Pal.Muted).LetterSpacing(0.1f);
        });

        private void Body(IContainer c) => c.Column(col =>
        {
            col.Spacing(16);

            col.Item().Column(head =>
            {
                head.Item().Text(row.Name ?? "").FontSize(20).Bold();
                head.Item().Text($"Referencia {row.Model.ExternalReference}").FontSize(10).FontColor(Pal.Muted);
            });

            col.Item().Row(main =>
            {
                main.Spacing(18);

                // Foto (o marco vacío si no hay)
                main.ConstantItem(200).Height(240).Background(Pal.Surface).Border(1).BorderColor(Pal.Line)
                    .AlignCenter().AlignMiddle().Element(box =>
                    {
                        if (image is not null) box.Padding(6).Image(image).FitArea();
                        else box.Text("Sin imagen").FontColor(Pal.Muted).FontSize(9);
                    });

                main.RelativeItem().Column(info =>
                {
                    info.Spacing(10);

                    var price = MainPrice();
                    if (price is not null)
                        info.Item().Background(Pal.Accent).PaddingVertical(8).PaddingHorizontal(12).Row(p =>
                        {
                            p.RelativeItem().Text(price.Value.label).FontColor(Pal.OnAccent).FontSize(9);
                            p.AutoItem().Text(price.Value.text).FontColor(Pal.OnAccent).FontSize(15).Bold();
                        });

                    var attrs = row.AttributeList;
                    if (attrs.Count > 0)
                        info.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(1.4f); });
                            foreach (var a in attrs)
                            {
                                t.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Pal.Line)
                                    .Text(a.Label).FontColor(Pal.Muted).FontSize(9);
                                t.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Pal.Line)
                                    .Text(a.Value).FontSize(10);
                            }
                        });
                });
            });

            // Tabla de tallas
            var variants = row.Variants;
            if (variants.Count > 0)
            {
                col.Item().PaddingTop(4).Text("Tallas").FontSize(12).Bold().FontColor(Pal.AccentText);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(2); cd.RelativeColumn(1.2f); });

                    void H(string s) => t.Cell().Background(Pal.Surface).PaddingVertical(5).PaddingHorizontal(8)
                        .Text(s).FontColor(Pal.Muted).FontSize(9).Bold();
                    H("TALLA"); H("EAN"); H("PVD");

                    foreach (var v in variants)
                    {
                        t.Cell().PaddingVertical(4).PaddingHorizontal(8).BorderBottom(1).BorderColor(Pal.Line)
                            .Text(v.Product.Size ?? "").FontSize(10);
                        t.Cell().PaddingVertical(4).PaddingHorizontal(8).BorderBottom(1).BorderColor(Pal.Line)
                            .Text(v.Product.Ean ?? "").FontSize(9).FontColor(Pal.Muted);
                        t.Cell().PaddingVertical(4).PaddingHorizontal(8).BorderBottom(1).BorderColor(Pal.Line)
                            .Text(Eur(v.Pvd ?? row.Pvd)).FontSize(10);
                    }
                });
            }
        });

        private void Footer(IContainer c) => c.BorderTop(1).BorderColor(Pal.Line).PaddingTop(6).Row(row =>
        {
            var forWhom = clientName.Length > 0 ? $"Precios para: {clientName}" : "Precios de tarifa";
            row.RelativeItem().Text(forWhom).FontSize(8).FontColor(Pal.Muted);
            row.RelativeItem().AlignRight().Text($"{brand.Name} · {DateTime.Now:dd/MM/yyyy}").FontSize(8).FontColor(Pal.Muted);
        });

        private (string label, string text)? MainPrice()
        {
            if (row.Pvd is { } pvd) return ("PVD", Eur(pvd));
            if (row.Pvp is { } pvp) return ("PVP", Eur(pvp));
            return null;
        }

        private string Eur(decimal? value) =>
            value is null ? "—" : $"{value.Value:#,##0.00} {(string.IsNullOrEmpty(row.Currency) ? "EUR" : row.Currency)}";
    }

    // ── Documento: line-sheet (varios productos) ────────────────────────────────
    private sealed class LineSheetDocument(
        IReadOnlyList<CatalogRow> rows, Dictionary<string, byte[]?> images,
        string clientName, string heading, string label, PdfBrand brand) : IDocument
    {
        private PdfPalette Pal => brand.Palette;

        public void Compose(IDocumentContainer container) => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.PageColor(Pal.Paper);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Pal.Ink).FontFamily(Font));

            page.Header().Element(Header);
            page.Content().PaddingVertical(12).Element(Grid);
            page.Footer().Element(Footer);
        });

        private void Header(IContainer c) => c.BorderBottom(1.5f).BorderColor(Pal.Accent).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text($"{brand.Name}™").FontSize(20).Bold().FontColor(Pal.AccentDisplay);
                col.Item().Text(heading).FontSize(9).FontColor(Pal.Muted);
            });
            row.RelativeItem().AlignRight().AlignBottom().Text($"{label} · {rows.Count} modelos")
                .FontSize(10).FontColor(Pal.Muted);
        });

        private void Grid(IContainer c) => c.Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); });
            foreach (var row in rows)
                t.Cell().Padding(4).Element(cell => Card(cell, row));
        });

        private void Card(IContainer c, CatalogRow row) => c.Border(1).BorderColor(Pal.Line).Padding(8).Row(r =>
        {
            r.Spacing(8);
            r.ConstantItem(74).Height(88).Background(Pal.Surface).AlignCenter().AlignMiddle().Element(box =>
            {
                var img = images.GetValueOrDefault(row.Model.ExternalReference ?? "");
                if (img is not null) box.Image(img).FitArea();
                else box.Text("—").FontColor(Pal.Muted);
            });
            r.RelativeItem().Column(col =>
            {
                col.Spacing(3);
                col.Item().Text(row.Name ?? "").Bold().FontSize(10);
                col.Item().Text($"Ref. {row.Model.ExternalReference}").FontColor(Pal.Muted).FontSize(8);
                var sizes = SizeRange(row);
                if (sizes.Length > 0) col.Item().Text(sizes).FontColor(Pal.Muted).FontSize(8);
                if ((row.Pvd ?? row.Pvp) is { } price)
                    col.Item().PaddingTop(2).Text($"{price:#,##0.00} {Currency(row)}").FontColor(Pal.AccentText).Bold().FontSize(12);
            });
        });

        private void Footer(IContainer c) => c.BorderTop(1).BorderColor(Pal.Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(clientName.Length > 0 ? $"Precios para: {clientName}" : "Precios de tarifa")
                .FontSize(8).FontColor(Pal.Muted);
            row.RelativeItem().AlignRight().Text(txt =>
            {
                txt.DefaultTextStyle(x => x.FontSize(8).FontColor(Pal.Muted));
                txt.Span($"{brand.Name} · {DateTime.Now:dd/MM/yyyy} · ");
                txt.CurrentPageNumber();
                txt.Span(" / ");
                txt.TotalPages();
            });
        });

        private static string Currency(CatalogRow row) => string.IsNullOrEmpty(row.Currency) ? "EUR" : row.Currency;

        // "Tallas 36–46" a partir de las variantes numéricas
        private static string SizeRange(CatalogRow row)
        {
            var sizes = row.Variants
                .Select(v => int.TryParse(v.Product.Size, out var n) ? n : (int?)null)
                .OfType<int>().ToList();
            return sizes.Count == 0 ? "" : $"Tallas {sizes.Min()}–{sizes.Max()}";
        }
    }
}
