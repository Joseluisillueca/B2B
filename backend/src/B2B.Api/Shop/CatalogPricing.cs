using System.Globalization;
using B2B.Api.Data;

namespace B2B.Api.Shop;

/// A quién se le pone precio y contra qué ventana de servicio.
public sealed record PriceContext(string? ClientId, IReadOnlyCollection<string> GroupIds, string? OrderType)
{
    public static readonly PriceContext Anonymous = new(null, [], null);
}

// Resolución de tarifa del contrato 03 §4: de todas las ofertas publicadas para un
// modelo, cuál es la que ve ESTE cliente en ESTA ventana, y por qué talla.
public static class CatalogPricing
{
    /// Precio de escaparate (1 unidad) del producto, o null si no hay tarifa aplicable.
    public static decimal? Resolve(
        IEnumerable<Offer> offers, string priceType, string? productId, PriceContext context, DateTimeOffset now)
    {
        Offer? best = null;
        foreach (var offer in offers)
        {
            if (!string.Equals(offer.PriceType, priceType, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Applies(offer, productId, context, now)) continue;
            if (best is null || Beats(offer, best)) best = offer;
        }

        return best is null ? null : Effective(best);
    }

    /// Precio con el descuento de la oferta ya aplicado
    public static decimal Effective(Offer offer) =>
        Math.Round(offer.PriceValue * (1 - (offer.DiscountPercent ?? 0m) / 100m), 2, MidpointRounding.AwayFromZero);

    // Empieza mandando el alcance: la oferta por productId gana a la de modelId
    // (hallazgo del plan §5). Luego la especificidad de cliente, luego la prioridad
    // que el propio conector numeró, y a igualdad el precio más bajo.
    private static bool Beats(Offer candidate, Offer champion)
    {
        if (Scope(candidate) != Scope(champion)) return Scope(candidate) > Scope(champion);
        if (Specificity(candidate) != Specificity(champion)) return Specificity(candidate) > Specificity(champion);
        if (candidate.Priority != champion.Priority) return candidate.Priority < champion.Priority;
        return Effective(candidate) < Effective(champion);
    }

    private static int Scope(Offer offer) => string.IsNullOrEmpty(offer.ProductId) ? 0 : 1;

    private static int Specificity(Offer offer) =>
        !string.IsNullOrEmpty(offer.ClientId) ? 2 : !string.IsNullOrEmpty(offer.ClientGroupId) ? 1 : 0;

    private static bool Applies(Offer offer, string? productId, PriceContext context, DateTimeOffset now)
    {
        // Oferta de otra talla del mismo modelo
        if (!string.IsNullOrEmpty(offer.ProductId)
            && !string.Equals(offer.ProductId, productId, StringComparison.OrdinalIgnoreCase))
            return false;

        // Tarifa de otro cliente: nunca debe asomar en la respuesta
        if (!string.IsNullOrEmpty(offer.ClientId)
            && !string.Equals(offer.ClientId, context.ClientId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(offer.ClientGroupId)
            && !context.GroupIds.Contains(offer.ClientGroupId, StringComparer.OrdinalIgnoreCase))
            return false;

        // Contrato 03 §1.2: una oferta con orderType solo aplica a esa clase de pedido
        if (!string.IsNullOrEmpty(offer.OrderType) && !string.IsNullOrEmpty(context.OrderType)
            && !string.Equals(offer.OrderType, context.OrderType, StringComparison.OrdinalIgnoreCase))
            return false;

        // Tramos de cantidad: el catálogo enseña el precio de la primera unidad
        if (offer.MinQuantity > 1) return false;

        return InWindow(offer.FromDate, now) is not false && InWindow(offer.ToDate, now, upper: true) is not false;
    }

    private static bool? InWindow(string? bound, DateTimeOffset now, bool upper = false)
    {
        if (string.IsNullOrWhiteSpace(bound)) return null;
        if (!DateTimeOffset.TryParse(bound, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment))
            return null;
        return upper ? now.Date <= moment.Date : moment.Date <= now.Date;
    }
}
