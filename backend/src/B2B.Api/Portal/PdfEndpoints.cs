using System.Security.Claims;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using B2B.Api.Shop;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace B2B.Api.Portal;

// PDFs comerciales con marca lejan: ficha técnica de un producto y line-sheet de
// varios, siempre con la TARIFA DEL CLIENTE (o del cliente suplantado por el agente).
// El precio sale de CatalogService con el contexto del actor, igual que el catálogo.
public static class PdfEndpoints
{
    // Paleta de marca (premium editorial)
    private const string Green = "#1F5C46";
    private const string Terra = "#C4633A";
    private const string Cream = "#FAF6EF";
    private const string Ink = "#221D17";
    private const string Muted = "#6B6459";
    private const string Line = "#E7E0D5";
    private const string Font = "Arial";

    public static void MapPdfEndpoints(this IEndpointRouteBuilder app)
    {
        // Ficha técnica de un producto (enciende el botón "Descargar ficha técnica").
        app.MapGet("/api/portal/product/{reference}/tech-sheet.pdf", async (
            string reference, HttpRequest request, ClaimsPrincipal principal,
            AppDbContext db, IWebHostEnvironment env, IHttpClientFactory httpFactory) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var query = CatalogQuery.From(request.Query) with { Search = reference, Skip = 0, Take = 60, Locale = locale };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow);

            var row = page.Rows.FirstOrDefault(r =>
                          string.Equals(r.Model.ExternalReference, reference, StringComparison.OrdinalIgnoreCase))
                      ?? page.Rows.FirstOrDefault();
            if (row is null) return Results.NotFound();

            var clientName = await ClientNameAsync(db, actor);
            var image = await LoadImageAsync(row.ImageUri, env, httpFactory);

            var pdf = new TechSheetDocument(row, clientName, image).GeneratePdf();
            var safeRef = string.Concat((row.Model.ExternalReference ?? "producto")
                .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            return Results.File(pdf, "application/pdf", $"ficha-{safeRef}.pdf");
        }).RequireAuthorization();

        // Line-sheet: catálogo comercial de VARIOS productos (preselección/carrito),
        // con la tarifa del cliente. Refs por query para que api.download (GET) baje
        // el PDF con el token.
        app.MapGet("/api/portal/line-sheet.pdf", async (
            HttpRequest request, ClaimsPrincipal principal,
            AppDbContext db, IWebHostEnvironment env, IHttpClientFactory httpFactory) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var refs = request.Query["refs"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList();
            if (refs.Count == 0) return Results.BadRequest(new { error = "Indica al menos un producto (refs)." });

            var query = CatalogQuery.From(request.Query) with { Search = "", Skip = 0, Take = 300, Locale = locale };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow);
            var byRef = page.Rows
                .GroupBy(r => r.Model.ExternalReference ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = refs.Select(r => byRef.GetValueOrDefault(r)).OfType<CatalogRow>().ToList();
            if (rows.Count == 0) return Results.NotFound();

            var clientName = await ClientNameAsync(db, actor);
            var images = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                images[r.Model.ExternalReference ?? ""] = await LoadImageAsync(r.ImageUri, env, httpFactory);

            var heading = clientName.Length > 0 ? $"Selección para {clientName}" : "Selección de productos";
            var pdf = new LineSheetDocument(rows, images, clientName, heading, "LINE-SHEET").GeneratePdf();
            return Results.File(pdf, "application/pdf", "line-sheet-lejan.pdf");
        }).RequireAuthorization();

        // Catálogo completo (o filtrado) en PDF con marca y tarifa del cliente. Respeta
        // los mismos filtros que la barra del catálogo (línea, silueta, disponibilidad…).
        app.MapGet("/api/portal/catalog.pdf", async (
            HttpRequest request, ClaimsPrincipal principal,
            AppDbContext db, IWebHostEnvironment env, IHttpClientFactory httpFactory) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var query = CatalogQuery.From(request.Query) with { Skip = 0, Take = 300, Locale = locale };
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow);
            var rows = page.Rows;
            if (rows.Count == 0) return Results.NotFound();

            var clientName = await ClientNameAsync(db, actor);
            var images = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                images[r.Model.ExternalReference ?? ""] = await LoadImageAsync(r.ImageUri, env, httpFactory);

            var heading = clientName.Length > 0 ? $"Catálogo · tarifa de {clientName}" : "Catálogo";
            var pdf = new LineSheetDocument(rows, images, clientName, heading, "CATÁLOGO").GeneratePdf();
            return Results.File(pdf, "application/pdf", "catalogo-lejan.pdf");
        }).RequireAuthorization();
    }

    private static PortalActorPrices Prices(PortalActor? actor) =>
        actor is null ? PortalActorPrices.Anonymous : new PortalActorPrices(actor.ClientId, actor.GroupIds);

    // Nombre del cliente para el pie "Precios para: …" (o del suplantado por el agente)
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
    private static async Task<byte[]?> LoadImageAsync(string? uri, IWebHostEnvironment env, IHttpClientFactory httpFactory)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
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
    private sealed class TechSheetDocument(CatalogRow row, string clientName, byte[]? image) : IDocument
    {
        public void Compose(IDocumentContainer container) => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink).FontFamily(Font));

            page.Header().Element(Header);
            page.Content().PaddingVertical(14).Element(Body);
            page.Footer().Element(Footer);
        });

        private void Header(IContainer c) => c.BorderBottom(1.5f).BorderColor(Green).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Text("lejan™").FontSize(22).Bold().FontColor(Green);
            row.RelativeItem().AlignRight().AlignBottom().Text("FICHA TÉCNICA")
                .FontSize(10).FontColor(Muted).LetterSpacing(0.1f);
        });

        private void Body(IContainer c) => c.Column(col =>
        {
            col.Spacing(16);

            col.Item().Column(head =>
            {
                head.Item().Text(row.Name ?? "").FontSize(20).Bold();
                head.Item().Text($"Referencia {row.Model.ExternalReference}").FontSize(10).FontColor(Muted);
            });

            col.Item().Row(main =>
            {
                main.Spacing(18);

                // Foto (o marco vacío si no hay)
                main.ConstantItem(200).Height(240).Background(Cream).Border(1).BorderColor(Line)
                    .AlignCenter().AlignMiddle().Element(box =>
                    {
                        if (image is not null) box.Padding(6).Image(image).FitArea();
                        else box.Text("Sin imagen").FontColor(Muted).FontSize(9);
                    });

                main.RelativeItem().Column(info =>
                {
                    info.Spacing(10);

                    var price = MainPrice();
                    if (price is not null)
                        info.Item().Background(Green).PaddingVertical(8).PaddingHorizontal(12).Row(p =>
                        {
                            p.RelativeItem().Text(price.Value.label).FontColor(Cream).FontSize(9);
                            p.AutoItem().Text(price.Value.text).FontColor("#FFFFFF").FontSize(15).Bold();
                        });

                    var attrs = row.AttributeList;
                    if (attrs.Count > 0)
                        info.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(1.4f); });
                            foreach (var a in attrs)
                            {
                                t.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Line)
                                    .Text(a.Label).FontColor(Muted).FontSize(9);
                                t.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Line)
                                    .Text(a.Value).FontSize(10);
                            }
                        });
                });
            });

            // Tabla de tallas
            var variants = row.Variants;
            if (variants.Count > 0)
            {
                col.Item().PaddingTop(4).Text("Tallas").FontSize(12).Bold().FontColor(Green);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(2); cd.RelativeColumn(1.2f); });

                    void H(string s) => t.Cell().Background(Cream).PaddingVertical(5).PaddingHorizontal(8)
                        .Text(s).FontColor(Muted).FontSize(9).Bold();
                    H("TALLA"); H("EAN"); H("PVD");

                    foreach (var v in variants)
                    {
                        t.Cell().PaddingVertical(4).PaddingHorizontal(8).BorderBottom(1).BorderColor(Line)
                            .Text(v.Product.Size ?? "").FontSize(10);
                        t.Cell().PaddingVertical(4).PaddingHorizontal(8).BorderBottom(1).BorderColor(Line)
                            .Text(v.Product.Ean ?? "").FontSize(9).FontColor(Muted);
                        t.Cell().PaddingVertical(4).PaddingHorizontal(8).BorderBottom(1).BorderColor(Line)
                            .Text(Eur(v.Pvd ?? row.Pvd)).FontSize(10);
                    }
                });
            }
        });

        private void Footer(IContainer c) => c.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            var forWhom = clientName.Length > 0 ? $"Precios para: {clientName}" : "Precios de tarifa";
            row.RelativeItem().Text(forWhom).FontSize(8).FontColor(Muted);
            row.RelativeItem().AlignRight().Text($"lejan · {DateTime.Now:dd/MM/yyyy}").FontSize(8).FontColor(Muted);
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
        string clientName, string heading, string label) : IDocument
    {
        public void Compose(IDocumentContainer container) => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Ink).FontFamily(Font));

            page.Header().Element(Header);
            page.Content().PaddingVertical(12).Element(Grid);
            page.Footer().Element(Footer);
        });

        private void Header(IContainer c) => c.BorderBottom(1.5f).BorderColor(Green).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("lejan™").FontSize(20).Bold().FontColor(Green);
                col.Item().Text(heading).FontSize(9).FontColor(Muted);
            });
            row.RelativeItem().AlignRight().AlignBottom().Text($"{label} · {rows.Count} modelos")
                .FontSize(10).FontColor(Muted);
        });

        private void Grid(IContainer c) => c.Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); });
            foreach (var row in rows)
                t.Cell().Padding(4).Element(cell => Card(cell, row));
        });

        private void Card(IContainer c, CatalogRow row) => c.Border(1).BorderColor(Line).Padding(8).Row(r =>
        {
            r.Spacing(8);
            r.ConstantItem(74).Height(88).Background(Cream).AlignCenter().AlignMiddle().Element(box =>
            {
                var img = images.GetValueOrDefault(row.Model.ExternalReference ?? "");
                if (img is not null) box.Image(img).FitArea();
                else box.Text("—").FontColor(Muted);
            });
            r.RelativeItem().Column(col =>
            {
                col.Spacing(3);
                col.Item().Text(row.Name ?? "").Bold().FontSize(10);
                col.Item().Text($"Ref. {row.Model.ExternalReference}").FontColor(Muted).FontSize(8);
                var sizes = SizeRange(row);
                if (sizes.Length > 0) col.Item().Text(sizes).FontColor(Muted).FontSize(8);
                if ((row.Pvd ?? row.Pvp) is { } price)
                    col.Item().PaddingTop(2).Text($"{price:#,##0.00} {Currency(row)}").FontColor(Green).Bold().FontSize(12);
            });
        });

        private void Footer(IContainer c) => c.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(clientName.Length > 0 ? $"Precios para: {clientName}" : "Precios de tarifa")
                .FontSize(8).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(txt =>
            {
                txt.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                txt.Span($"lejan · {DateTime.Now:dd/MM/yyyy} · ");
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
