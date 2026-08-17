using System.Security.Claims;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Shop;

// API del portal de clientes: el catálogo comprable en una llamada — modelos con
// variantes (tallas), PVD/PVP del cliente que pregunta, stock por ventana de
// servicio y las facetas del rail lateral (17-catalog-catalog.png).
public static class ShopEndpoints
{
    public static void MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shop/catalog", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var query = CatalogQuery.From(request.Query);
            var page = await CatalogService.QueryAsync(db, Prices(actor), query, DateTimeOffset.UtcNow);
            var favorites = await FavoritesAsync(db, actor);

            return Results.Ok(new
            {
                page.Windows,
                window = page.Window,
                page.Total,
                query.Skip,
                query.Take,
                sort = query.Sort,
                facets = new
                {
                    families = page.Families,
                    availability = page.AvailabilityFacet.Select(f => new { id = f.Value, count = f.Count }),
                    attributes = page.AttributeFacets
                },
                items = page.Rows.Select(row => new
                {
                    modelId = row.Model.ExternalId,
                    name = row.Model.Name,
                    reference = row.Model.ExternalReference,
                    familyId = row.Model.FamilyId,
                    familyLabel = Label(row.Model.FamilyId),
                    segments = row.Segments,
                    attributes = row.Attributes,
                    imageUri = row.ImageUri,
                    pvd = row.Pvd,
                    pvp = row.Pvp,
                    currency = row.Currency,
                    availability = row.Availability,
                    pricePerSize = row.PricePerSize,
                    favorite = favorites.Contains(row.Model.ExternalId),
                    products = row.Variants.Select(variant => new
                    {
                        productId = variant.Product.ExternalId,
                        size = variant.Product.Size,
                        sku = variant.Product.Sku,
                        ean = variant.Product.Ean,
                        stock = variant.Stock,
                        pvd = variant.Pvd,
                        pvp = variant.Pvp
                    })
                })
            });
        }).RequireAuthorization();

        // Botón "Desc. Stock" de la toolbar: el listado que se está viendo, con los
        // mismos filtros, en un CSV que Excel abre sin preguntar nada.
        app.MapGet("/api/shop/stock-export.csv", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var actor = await PortalScope.ActorAsync(principal, db);
            var query = CatalogQuery.From(request.Query);
            var page = await CatalogService.QueryAsync(db, Prices(actor), query with { Skip = 0, Take = CatalogQuery.MaxTake },
                DateTimeOffset.UtcNow);

            var rows = page.Rows.SelectMany(row => row.Variants.Select(variant => new object?[]
            {
                row.Model.ExternalReference,
                row.Model.Name,
                Label(row.Model.FamilyId),
                variant.Product.Size,
                variant.Product.Sku,
                variant.Product.Ean,
                page.Window,
                StockText(CatalogService.StockOf(variant, page.Window)),
                variant.Pvd ?? row.Pvd,
                variant.Pvp ?? row.Pvp
            }));

            var csv = Csv.Build(
                ["Referencia", "Modelo", "Línea", "Talla", "SKU", "EAN", "Ventana", "Stock", "PVD", "PVP"],
                rows);

            return Results.File(csv, "text/csv; charset=utf-8", $"stock-{DateTime.Now:yyyyMMdd}.csv");
        }).RequireAuthorization();
    }

    private static PortalActorPrices Prices(PortalActor? actor) =>
        actor is null ? PortalActorPrices.Anonymous : new PortalActorPrices(actor.ClientId, actor.GroupIds);

    private static async Task<HashSet<string>> FavoritesAsync(AppDbContext db, PortalActor? actor)
    {
        if (actor is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = await db.PortalFavorites
            .Where(f => f.UserId == actor.UserId)
            .Select(f => f.ModelId)
            .ToListAsync();
        return new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);
    }

    // El sentinela de infinito no es una cifra que se pueda llevar a una hoja de cálculo
    private static string StockText(decimal stock) =>
        stock >= Availability.Infinite ? "∞" : stock.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));

    private static string Label(string familyId) =>
        familyId.Length == 0 ? familyId : char.ToUpperInvariant(familyId[0]) + familyId[1..];
}
