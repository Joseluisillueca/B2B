using System.Text.Json.Nodes;
using JUST;

namespace B2B.Api.Integration;

// Transformación de JSON con JUST.net — el MISMO motor y sintaxis que el portal de
// referencia (#valueof, #loop, #currentvalueatpath, #ifcondition, #existsandnotempty,
// #xconcat…). Convierte el JSON interno del portal (pedido/cliente/dirección) en el JSON
// que espera cada endpoint de Business Central. Las expresiones son editables desde el CMS.
public static class JsonTransformService
{
    // Aplica `transformer` (expresión JUST.net) sobre `inputJson` y devuelve el JSON resultante,
    // ya saneado de valores nulos (ver StripNulls).
    public static string Transform(string transformer, string inputJson)
    {
        var jt = new JsonTransformer();
        return StripNulls(jt.Transform(transformer, inputJson));
    }

    // BC rechaza propiedades a null. Un caso típico: si el cliente no trae direcciones de envío
    // (p.ej. "la dirección de envío es la misma que la fiscal"), el #loop deja
    // "shipToAddresss": null y BC devuelve 400 ("a 'StartArray' node was expected"). Lo correcto
    // es OMITIR la propiedad, no mandarla nula (BC usa entonces la dirección principal). Se
    // eliminan recursivamente las claves con valor null del JSON transformado.
    private static string StripNulls(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { return json; }                    // si no es JSON válido, se deja tal cual
        if (node is not JsonObject o) return json;
        StripObject(o);
        return o.ToJsonString();
    }

    private static void StripObject(JsonObject obj)
    {
        foreach (var key in obj.Select(kv => kv.Key).ToList())
        {
            var v = obj[key];
            if (v is null) { obj.Remove(key); continue; }   // propiedad con valor JSON null
            if (v is JsonObject child) StripObject(child);
            else if (v is JsonArray arr)
                foreach (var e in arr) if (e is JsonObject eo) StripObject(eo);
        }
    }
}
