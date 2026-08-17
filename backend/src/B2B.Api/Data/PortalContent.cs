namespace B2B.Api.Data;

// Bloque de contenido editable del portal (plan §3): la portada y demás piezas que
// el CMS cambia sin desplegar. Key + Locale forman la clave; Json guarda la lista de
// elementos ya normalizada. Locale "*" es el contenido común a los cuatro idiomas.
public class PortalContent
{
    public required string Key { get; set; }      // dashboard.hero, dashboard.tiles…
    public required string Locale { get; set; }   // es | en | fr | it | *
    public string Json { get; set; } = "[]";      // jsonb: lista de elementos
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }        // email del administrador que publicó
}
