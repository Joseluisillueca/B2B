using System.Text.Json;
using System.Text.Json.Nodes;

namespace B2B.Api.Portal;

// Esquema del contenido editable del portal (plan §3). Un bloque es una lista de
// elementos con la forma { id, order, active, imageUrl, imageUrlMobile, alt, title,
// subtitle, ctaText, ctaHref, publishFrom, publishTo } — más "window" en las
// tarjetas de la portada, que fijan la ventana de servicio activa.
//
// Todo lo que entra por el CMS pasa por Normalize: si el payload no cuadra, el
// bloque no se guarda (400) en lugar de dejar la portada a medio pintar.
public static class PortalContentModel
{
    /// Claves editables. Cualquier otra se rechaza: el CMS no inventa secciones.
    public static readonly string[] Keys =
        ["dashboard.hero", "dashboard.tiles", "login.background", "footer.social"];

    /// Idiomas del portal más "*" (contenido común a los cuatro)
    public static readonly string[] Locales = ["es", "en", "fr", "it", "*"];

    public const string CommonLocale = "*";
    public const string DefaultLocale = "es";

    private static readonly string[] ImageBlocks = ["dashboard.hero", "dashboard.tiles", "login.background"];
    private static readonly string[] ServiceWindows = ["replenishment", "scheduled"];
    private static readonly string[] TextFields = ["alt", "title", "subtitle", "ctaText"];
    private const int MaxItems = 24;

    public static bool IsKnownKey(string? key) => key is not null && Keys.Contains(key);

    /// locale de la query: vacío = es; devuelve null si no es un idioma del portal
    public static string? NormalizeLocale(string? locale)
    {
        var value = (locale ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) return DefaultLocale;
        return Locales.Contains(value) ? value : null;
    }

    /// Valida y normaliza el cuerpo del PUT. En error deja el motivo en `error`.
    public static bool TryNormalize(string key, JsonNode? body, out JsonArray items, out string error)
    {
        items = [];
        error = "";

        if (body is not JsonObject root || root["items"] is not JsonArray source)
        {
            error = "El bloque debe ser un objeto con una lista \"items\".";
            return false;
        }

        if (source.Count > MaxItems)
        {
            error = $"Un bloque admite como mucho {MaxItems} elementos.";
            return false;
        }

        var normalized = new List<(int Order, int Index, JsonObject Item)>();
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index] is not JsonObject item)
            {
                error = $"El elemento {index + 1} no es un objeto.";
                return false;
            }

            if (!TryItem(key, item, index, out var clean, out var order, out error))
            {
                error = $"Elemento {index + 1}: {error}";
                return false;
            }

            normalized.Add((order, index, clean));
        }

        // Orden explícito del CMS; a igualdad, el orden en el que llegaron
        foreach (var entry in normalized.OrderBy(e => e.Order).ThenBy(e => e.Index))
            items.Add(entry.Item);

        return true;
    }

    private static bool TryItem(string key, JsonObject source, int index,
        out JsonObject item, out int order, out string error)
    {
        item = [];
        order = index;
        error = "";

        if (!TryText(source["id"], out var id)) { error = "\"id\" debe ser texto."; return false; }
        if (id.Length == 0) id = Guid.NewGuid().ToString("N")[..12];

        if (source["order"] is not null)
        {
            if (source["order"] is not JsonValue value || !value.TryGetValue<double>(out var number))
            {
                error = "\"order\" debe ser un número.";
                return false;
            }
            order = (int)number;
        }

        var active = true;
        if (source["active"] is not null)
        {
            if (source["active"] is not JsonValue flag || !flag.TryGetValue<bool>(out active))
            {
                error = "\"active\" debe ser verdadero o falso.";
                return false;
            }
        }

        if (!TryText(source["imageUrl"], out var imageUrl)) { error = "\"imageUrl\" debe ser texto."; return false; }
        if (ImageBlocks.Contains(key) && imageUrl.Length == 0)
        {
            error = "hace falta una imagen (\"imageUrl\").";
            return false;
        }
        if (imageUrl.Length > 0 && !IsSafeUrl(imageUrl))
        {
            error = "\"imageUrl\" debe ser una ruta del portal (/media/…) o una URL http(s).";
            return false;
        }

        if (!TryText(source["imageUrlMobile"], out var mobile)) { error = "\"imageUrlMobile\" debe ser texto."; return false; }
        if (mobile.Length > 0 && !IsSafeUrl(mobile))
        {
            error = "\"imageUrlMobile\" debe ser una ruta del portal (/media/…) o una URL http(s).";
            return false;
        }

        if (!TryText(source["ctaHref"], out var ctaHref)) { error = "\"ctaHref\" debe ser texto."; return false; }
        if (ctaHref.Length > 0 && !IsSafeUrl(ctaHref))
        {
            error = "\"ctaHref\" debe ser una ruta del portal o una URL http(s).";
            return false;
        }

        foreach (var field in TextFields)
        {
            if (!TryText(source[field], out var text)) { error = $"\"{field}\" debe ser texto."; return false; }
            item[field] = text;
        }

        if (!TryDate(source["publishFrom"], out var from)) { error = "\"publishFrom\" no es una fecha válida."; return false; }
        if (!TryDate(source["publishTo"], out var to)) { error = "\"publishTo\" no es una fecha válida."; return false; }
        if (from is not null && to is not null && to < from)
        {
            error = "\"publishTo\" es anterior a \"publishFrom\".";
            return false;
        }

        // Las dos tarjetas de la portada fijan la ventana de servicio del carrito
        if (key == "dashboard.tiles")
        {
            if (!TryText(source["window"], out var window)) { error = "\"window\" debe ser texto."; return false; }
            if (window.Length == 0) window = ServiceWindows[0];
            if (!ServiceWindows.Contains(window))
            {
                error = $"\"window\" debe ser {string.Join(" o ", ServiceWindows)}.";
                return false;
            }
            item["window"] = window;
        }

        item["id"] = id;
        item["order"] = order;
        item["active"] = active;
        item["imageUrl"] = imageUrl;
        item["imageUrlMobile"] = mobile;
        item["ctaHref"] = ctaHref;
        item["publishFrom"] = from?.ToString("O");
        item["publishTo"] = to?.ToString("O");
        return true;
    }

    /// Elementos que el portal puede pintar ahora mismo: activos y en ventana.
    public static JsonArray Published(string json, DateTimeOffset now)
    {
        var visible = new JsonArray();
        if (Parse(json) is not JsonArray items) return visible;

        foreach (var node in items)
        {
            if (node is not JsonObject item) continue;
            if (item["active"] is JsonValue flag && flag.TryGetValue<bool>(out var active) && !active) continue;
            if (Date(item["publishFrom"]) is { } from && now < from) continue;
            if (Date(item["publishTo"]) is { } to && now > to) continue;
            visible.Add(item.DeepClone());
        }

        return visible;
    }

    public static JsonNode? Parse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    // Solo rutas del propio portal (/…) o http(s): fuera javascript:, data: y
    // protocolo relativo (//host), que se colarían en un href o en un <img>.
    private static bool IsSafeUrl(string url) =>
        (url.StartsWith('/') && !url.StartsWith("//")) ||
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool TryText(JsonNode? node, out string value)
    {
        value = "";
        if (node is null) return true;
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            value = text.Trim();
            return true;
        }
        return false;
    }

    private static bool TryDate(JsonNode? node, out DateTimeOffset? value)
    {
        value = null;
        if (!TryText(node, out var text)) return false;
        if (text.Length == 0) return true;

        if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static DateTimeOffset? Date(JsonNode? node) =>
        TryDate(node, out var value) ? value : null;
}
