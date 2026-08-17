using System.Text.Json.Nodes;
using B2B.Api.Data;

namespace B2B.Api.Portal;

// Portada de demostración. Solo se siembra si portal_content está vacía: en cuanto
// el CMS publica algo, manda el CMS. Las imágenes son ficheros del propio portal
// (wwwroot/media/portal), nunca enlaces a terceros.
public static class PortalContentSeed
{
    // Sin texto sobre el carrusel, como en el portal actual: la imagen manda.
    private const string Hero = """
    { "items": [
      { "id": "demo-hero-1", "order": 0, "imageUrl": "/media/portal/demo-hero-carretera.svg",
        "alt": "Campaña lejan™ — carretera al amanecer" },
      { "id": "demo-hero-2", "order": 1, "imageUrl": "/media/portal/demo-hero-taller.svg",
        "alt": "Nueva colección lejan™ sobre fondo de estudio" }
    ] }
    """;

    // Las dos tarjetas de acceso: fijan la ventana de servicio y llevan al catálogo.
    // Sin título, para que cada idioma use su literal (Reposición / Programación).
    private const string Tiles = """
    { "items": [
      { "id": "demo-tile-replenishment", "order": 0, "window": "replenishment",
        "imageUrl": "/media/portal/demo-tile-reposicion.svg",
        "alt": "Reposición — stock disponible para servir" },
      { "id": "demo-tile-scheduled", "order": 1, "window": "scheduled",
        "imageUrl": "/media/portal/demo-tile-programacion.svg",
        "alt": "Programación — campaña de la próxima temporada" }
    ] }
    """;

    public static void EnsureDemoContent(AppDbContext db)
    {
        if (db.PortalContents.Any())
            return;

        Add(db, "dashboard.hero", Hero);
        Add(db, "dashboard.tiles", Tiles);
        db.SaveChanges();
    }

    private static void Add(AppDbContext db, string key, string json)
    {
        // Pasa por el mismo normalizador que el CMS: una sola forma del contenido
        if (!PortalContentModel.TryNormalize(key, JsonNode.Parse(json), out var items, out _))
            return;

        db.PortalContents.Add(new PortalContent
        {
            Key = key,
            Locale = PortalContentModel.CommonLocale,
            Json = items.ToJsonString(),
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "demo"
        });
    }
}
