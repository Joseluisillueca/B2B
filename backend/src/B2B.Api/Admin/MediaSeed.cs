namespace B2B.Api.Admin;

// Los medios de demostración (portada, tarjetas, fotos de los modelos de ejemplo) viajan
// DENTRO de la imagen, en MediaSeed/, fuera de wwwroot. Antes vivían directamente en
// wwwroot/media/portal y eso tenía una trampa: en cuanto se montaba un disco persistente
// en esa ruta, el volumen vacío TAPABA lo que traía la imagen y la portada de demostración
// se quedaba sin una sola imagen. Se sirven como un proveedor de estáticos más, detrás de
// las subidas del CMS, así que un fichero subido con el mismo nombre siempre gana.
public static class MediaSeed
{
    // En producción la carpeta está junto al binario (ContentRoot = /app). En pruebas el
    // ContentRoot es el proyecto fuente, y la copia que hace MSBuild al compilar vive junto
    // al ensamblado: se mira en los dos sitios.
    public static string Root(IWebHostEnvironment env)
    {
        var candidatos = new[]
        {
            Path.Combine(env.ContentRootPath, "MediaSeed"),
            Path.Combine(AppContext.BaseDirectory, "MediaSeed"),
        };
        return candidatos.FirstOrDefault(Directory.Exists) ?? candidatos[0];
    }
}
