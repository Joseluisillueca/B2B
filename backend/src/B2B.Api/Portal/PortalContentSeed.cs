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

    // Lookbook "Colecciones lejan" — barefoot editorial. Portada (carrusel) con la voz
    // real de la marca; las historias comprables referencian modelos reales del catálogo.
    private const string LookbookHero = """
    { "items": [
      { "id": "lb-hero-1", "order": 0, "imageUrl": "/media/portal/demo-hero-carretera.svg",
        "alt": "Colección barefoot de lejan — Otoño/Invierno 26",
        "title": "Barefoot Bonito", "subtitle": "Salud y estilo en un mismo paso",
        "ctaText": "Explorar la colección", "ctaHref": "/es/es/catalog/catalog" },
      { "id": "lb-hero-2", "order": 1, "imageUrl": "/media/portal/demo-hero-taller.svg",
        "alt": "Lejan One® FW26",
        "title": "Lejan One® · FW26", "subtitle": "Tu pie, tal y como es",
        "ctaText": "Ver novedades", "ctaHref": "/es/es/catalog/catalog" }
    ] }
    """;

    private const string LookbookStories = """
    { "items": [
      { "id": "lb-pilares", "order": 0, "kicker": "Por qué barefoot",
        "title": "Horma ancha · Suela fina · Drop cero",
        "body": "Tu pie, tal y como es. Espacio para los dedos, máxima flexibilidad y cero elevación para moverte libre cada día. Barefoot que respeta la forma natural del pie, sin renunciar al estilo.",
        "imageUrl": "/media/portal/demo-hero-taller.svg", "accent": "#c4633a", "layout": "right", "refs": [] },
      { "id": "lb-one", "order": 1, "kicker": "Lejan One® · FW26",
        "title": "La zapatilla de todos los días",
        "body": "Mesh transpirable y horma que respeta tu paso. De la ciudad al finde, sin renuncias. Tallas 21–46, horma Standard y Wide.",
        "imageUrl": "/media/portal/demo-hero-carretera.svg", "accent": "#1f5c46", "layout": "left",
        "refs": ["DEMO0004-0000-4000-9000-000000000004", "DEMO0005-0000-4000-9000-000000000005", "DEMO0007-0000-4000-9000-000000000007"] },
      { "id": "lb-melrose", "order": 2, "kicker": "Lejan Melrose®",
        "title": "Barefoot que viste",
        "body": "El barefoot que combina con todo: napa suave, suela fina y cero drop. Del día a día a la ocasión, con la libertad del pie descalzo.",
        "imageUrl": "/media/portal/demo-hero-taller.svg", "accent": "#221d17", "layout": "right",
        "refs": ["DEMO0001-0000-4000-9000-000000000001", "DEMO0002-0000-4000-9000-000000000002", "DEMO0003-0000-4000-9000-000000000003"] },
      { "id": "lb-cuidado", "order": 3, "kicker": "Cuídalas",
        "title": "Que aguanten tu ritmo",
        "body": "Un gesto y siguen como el primer día. Kit de cuidado premium para que tu barefoot dure paso tras paso.",
        "imageUrl": "/media/portal/demo-hero-carretera.svg", "accent": "#1f5c46", "layout": "left",
        "refs": ["DEMO0006-0000-4000-9000-000000000006"] }
    ] }
    """;

    public static void EnsureDemoContent(AppDbContext db)
    {
        // Idempotente por clave: siembra lo que falte aunque ya haya contenido (así el
        // Lookbook entra en una BD que ya tenía la portada).
        AddIfMissing(db, "dashboard.hero", Hero);
        AddIfMissing(db, "dashboard.tiles", Tiles);
        AddIfMissing(db, "lookbook.hero", LookbookHero);
        AddIfMissing(db, "lookbook.stories", LookbookStories);
        db.SaveChanges();
    }

    private static void AddIfMissing(AppDbContext db, string key, string json)
    {
        if (db.PortalContents.Any(c => c.Key == key)) return;
        Add(db, key, json);
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
