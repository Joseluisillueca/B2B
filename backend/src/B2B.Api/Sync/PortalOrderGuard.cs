using System.Text.Json.Nodes;

namespace B2B.Api.Sync;

// Un pedido hecho en el portal y el mismo pedido devuelto por Business Central son UN
// SOLO documento: el conector reenvía el pedido con el id del portal (campo "B2B Sync
// Id"), y de ahí que no se dupliquen. Pero el upsert reemplaza el payload ENTERO, así
// que la primera vuelta de BC borraría lo que el ERP no conoce: las notas que escribió
// el comprador, el comercial que hizo el pedido en su nombre y el cobro con tarjeta.
//
// Regla, deliberadamente estrecha:
//   · solo protege documentos "order" que NACIERON en el portal (source == "portal");
//   · solo cuando el valor que llega viene VACÍO (ausente, null, "", [] o paid:false).
// Si BC manda un valor de verdad, manda BC. Por eso esto convive sin conflicto con que
// el conector acabe devolviendo esos campos: en cuanto los devuelva, esto no hace nada.
public static class PortalOrderGuard
{
    // Datos que nacen en el portal y que el ERP no tiene por qué conocer.
    private static readonly string[] Protected =
    [
        "observations",     // notas del comprador al confirmar; se ven en pedido, albarán y factura
        "saleId",           // comercial que creó el pedido suplantando (el "gestionado por")
        "payments",         // cobros registrados en el portal (pasarela)
        "paid",             // ídem: un pedido cobrado con tarjeta no se "descobra"
        "payMethodId",      // forma de pago elegida en el checkout
        "reference",        // referencia del cliente…
        "purchaseOrderId",  // …y su eco, ambos tecleados por el comprador
    ];

    /// Devuelve el payload entrante con los campos del portal conservados cuando el ERP
    /// los manda vacíos. Ante cualquier duda (JSON ilegible, documento ajeno) devuelve el
    /// entrante tal cual: esto nunca puede bloquear una sincronización.
    public static string Merge(string? storedPayload, string incomingPayload)
    {
        if (string.IsNullOrWhiteSpace(storedPayload)) return incomingPayload;

        JsonObject stored, incoming;
        try
        {
            if (JsonNode.Parse(storedPayload) is not JsonObject s) return incomingPayload;
            if (JsonNode.Parse(incomingPayload) is not JsonObject i) return incomingPayload;
            stored = s;
            incoming = i;
        }
        catch { return incomingPayload; }

        if (!IsPortalBorn(stored)) return incomingPayload;

        var changed = false;
        foreach (var key in Protected)
        {
            if (!IsEmpty(incoming[key]) || IsEmpty(stored[key])) continue;
            incoming[key] = stored[key]!.DeepClone();
            changed = true;
        }

        // `source` es la marca de ORIGEN, no la del último que escribió: un pedido nacido
        // en el portal lo sigue siendo aunque BC lo devuelva marcado como "ERP". Y además
        // es lo que sostiene esta guarda: si dejáramos que BC lo cambiara, la segunda
        // sincronización ya no reconocería el pedido como propio y borraría todo lo de
        // arriba. Por eso este campo se conserva SIEMPRE, no solo cuando llega vacío.
        if ((string?)(incoming["source"] as JsonValue) != "portal")
        {
            incoming["source"] = "portal";
            changed = true;
        }

        return changed ? incoming.ToJsonString() : incomingPayload;
    }

    private static bool IsPortalBorn(JsonObject stored) =>
        stored["source"] is JsonValue value
        && value.TryGetValue<string>(out var source)
        && string.Equals(source, "portal", StringComparison.OrdinalIgnoreCase);

    // "Vacío" en el sentido de "el ERP no trae este dato": ausente, null, cadena en
    // blanco, lista sin elementos o el false de `paid`.
    private static bool IsEmpty(JsonNode? node)
    {
        switch (node)
        {
            case null: return true;
            case JsonArray array: return array.Count == 0;
            case JsonObject obj: return obj.Count == 0;
            case JsonValue value:
                if (value.TryGetValue<string>(out var text)) return string.IsNullOrWhiteSpace(text);
                if (value.TryGetValue<bool>(out var flag)) return !flag;
                return false;
            default: return false;
        }
    }
}
