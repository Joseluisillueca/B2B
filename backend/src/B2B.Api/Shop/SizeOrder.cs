namespace B2B.Api.Shop;

/// Orden de las tallas ALFABÉTICAS en la matriz y en la ficha (POLO CLUB: S/M/L/XL/XXL/3XL).
/// Hasta ahora el catálogo ordenaba las numéricas por su valor y dejaba las demás por texto,
/// así que «L, M, S, XL, XXL» salían en ese orden. Este rango va POR DELANTE del numérico en la
/// cadena de ordenación; las tallas numéricas (34–48, 21–35…) conservan su orden de siempre y
/// lo que no sea ni rango ni número (U, TU, «Única») sigue detrás, por texto.
public static class SizeOrder
{
    // Alias en la misma posición: 2XL = XXL, XXXL = 3XL, XXXXL = 4XL, XXXXXL = 5XL.
    private static readonly Dictionary<string, int> Ranks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XXS"] = 0, ["2XS"] = 0,
        ["XS"] = 1,
        ["S"] = 2,
        ["M"] = 3,
        ["L"] = 4,
        ["XL"] = 5,
        ["XXL"] = 6, ["2XL"] = 6,
        ["3XL"] = 7, ["XXXL"] = 7,
        ["4XL"] = 8, ["XXXXL"] = 8,
        ["5XL"] = 9, ["XXXXXL"] = 9,
    };

    /// Posición en el rango alfabético (0 = XXS … 9 = 5XL); int.MaxValue si la talla no es del
    /// rango (numéricas, única, vacía). Insensible a mayúsculas y a blancos alrededor.
    public static int Rank(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return int.MaxValue;
        return Ranks.TryGetValue(size.Trim(), out var rank) ? rank : int.MaxValue;
    }
}
