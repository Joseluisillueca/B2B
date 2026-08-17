namespace B2B.Api.Data;

// Preferencias de la tarjeta "Preferencias" de /profile (09-profile.png): cómo
// quiere el usuario ver los precios y los listados, y con qué dirección de envío
// entra al checkout. Plan §4, Fase 4: tabla portal_user_prefs.
//
// Son del USUARIO, no del cliente: dos personas de la misma tienda pueden querer
// ver PVD o PVP sin pisarse.
public class PortalUserPrefs
{
    public Guid UserId { get; set; }

    /// "pvd" (precio de distribución) | "pvp" (precio recomendado al público)
    public string ShowPrices { get; set; } = PortalPrefValues.Pvd;

    /// "list" | "grid" — el modo del catálogo en escritorio y en móvil
    public string ListDesktop { get; set; } = PortalPrefValues.List;
    public string ListMobile { get; set; } = PortalPrefValues.List;

    /// Id de la dirección de envío (shipping-address del sync) elegida por defecto
    public string? ShippingAddressId { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public static class PortalPrefValues
{
    public const string Pvd = "pvd";
    public const string Pvp = "pvp";
    public const string List = "list";
    public const string Grid = "grid";

    public static readonly string[] Prices = [Pvd, Pvp];
    public static readonly string[] Modes = [List, Grid];

    /// Devuelve el valor si es uno de los admitidos; si no, el que ya había
    public static string Pick(string? candidate, string[] allowed, string fallback) =>
        candidate is not null && allowed.Contains(candidate.Trim().ToLowerInvariant())
            ? candidate.Trim().ToLowerInvariant()
            : fallback;
}
