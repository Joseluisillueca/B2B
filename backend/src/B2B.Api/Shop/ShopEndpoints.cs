using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Shop;

// API del portal de clientes: el catálogo comprable en una llamada —
// modelos con variantes (tallas), PVD/PVP y stock por ventana de servicio.
public static class ShopEndpoints
{
    public static void MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shop/catalog", async (AppDbContext db) =>
        {
            var models = await db.CatalogModels.Where(m => m.Active).OrderBy(m => m.Name).ToListAsync();
            var products = await db.CatalogProducts.Where(p => p.Active).ToListAsync();
            var stocks = await db.StockLevels.ToListAsync();
            var offers = await db.Offers.ToListAsync();
            var windows = await db.ServiceWindows.ToListAsync();
            var imageDocs = await db.SyncDocuments.Where(d => d.EntityType == "model-image").ToListAsync();

            var stockByProduct = stocks
                .GroupBy(s => s.ProductExternalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                    g => g.GroupBy(s => s.ServiceWindowKey).ToDictionary(w => w.Key, w => w.Max(s => s.Stock)),
                    StringComparer.OrdinalIgnoreCase);
            var productsByModel = products
                .GroupBy(p => p.ModelExternalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var offersByModel = offers
                .GroupBy(o => o.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var imageByModel = imageDocs.ToDictionary(d => d.ExternalId, d =>
            {
                try
                {
                    return (JsonNode.Parse(d.Payload) as JsonObject)?["images"]?[0]?["image"]?["uri"]?.GetValue<string>();
                }
                catch (JsonException) { return null; }
            }, StringComparer.OrdinalIgnoreCase);

            var items = models.Select(m =>
            {
                var modelOffers = offersByModel.GetValueOrDefault(m.ExternalId) ?? [];
                var pvdOffers = modelOffers.Where(o => o.PriceType == "PVD").ToList();
                var pvpOffers = modelOffers.Where(o => o.PriceType == "PVP").ToList();
                List<string> segments = [];
                try { segments = JsonSerializer.Deserialize<List<string>>(m.ProductSegmentsJson) ?? []; }
                catch (JsonException) { }

                return new
                {
                    modelId = m.ExternalId,
                    name = m.Name,
                    reference = m.ExternalReference,
                    familyId = m.FamilyId,
                    segments,
                    imageUri = imageByModel.GetValueOrDefault(m.ExternalId),
                    pvd = pvdOffers.Count > 0 ? pvdOffers.Min(o => o.PriceValue) : (decimal?)null,
                    pvp = pvpOffers.Count > 0 ? pvpOffers.Max(o => o.PriceValue) : (decimal?)null,
                    currency = modelOffers.FirstOrDefault()?.PriceCode is { Length: > 0 } c ? c : "EUR",
                    products = (productsByModel.GetValueOrDefault(m.ExternalId) ?? [])
                        .Where(p => !p.IsCasePack)
                        .OrderBy(p => decimal.TryParse(p.Size, out var n) ? n : decimal.MaxValue)
                        .ThenBy(p => p.Size)
                        .Select(p => new
                        {
                            productId = p.ExternalId,
                            size = p.Size,
                            sku = p.Sku,
                            stock = stockByProduct.GetValueOrDefault(p.ExternalId) ?? []
                        })
                        .ToList()
                };
            }).ToList();

            return Results.Ok(new
            {
                windows = windows.Select(w => new { id = w.ExternalId, name = w.Name, orderType = w.OrderType }),
                items
            });
        }).RequireAuthorization();
    }
}
