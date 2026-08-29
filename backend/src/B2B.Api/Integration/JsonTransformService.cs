using JUST;

namespace B2B.Api.Integration;

// Transformación de JSON con JUST.net — el MISMO motor y sintaxis que el portal de
// referencia (#valueof, #loop, #currentvalueatpath, #ifcondition, #existsandnotempty,
// #xconcat…). Convierte el JSON interno del portal (pedido/cliente/dirección) en el JSON
// que espera cada endpoint de Business Central. Las expresiones son editables desde el CMS.
public static class JsonTransformService
{
    // Aplica `transformer` (expresión JUST.net) sobre `inputJson` y devuelve el JSON resultante.
    public static string Transform(string transformer, string inputJson)
    {
        var jt = new JsonTransformer();
        return jt.Transform(transformer, inputJson);
    }
}
