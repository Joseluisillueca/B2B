using B2B.Api.Auth;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace B2B.Api.Admin;

// Medios del portal: las imágenes, el vídeo, los logos y la tipografía que el CMS pone en
// la portada y en la marca. Se guardan en la BASE DE DATOS (PortalMediaFile) y se sirven en
// /media/portal/{name}: el disco del contenedor es efímero y un volumen por instancia no
// escala (límite de volúmenes por proyecto). Siguen leyéndose, por compatibilidad, los
// ficheros que quedaran en la carpeta de medios de instancias anteriores y los medios de
// demostración que viajan en la imagen (MediaSeed/), en ese orden de prioridad.
public static class MediaEndpoints
{
    public const string UrlPrefix = "/media/portal/";
    private const long MaxBytes = 5 * 1024 * 1024;

    // Extensión y content-type tienen que cuadrar: ni .php declarado como imagen ni
    // .png con html dentro. El SVG entra (F-07: el wordmark y los iconos de la portada
    // son vectoriales) con dos cautelas: la subida es solo-admin desde B-1 y el dibujo
    // se revisa antes de escribirlo, porque se sirve desde el mismo origen.
    // El primer tipo de cada lista es el CANÓNICO con el que se sirve.
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".webp"] = ["image/webp"],
        [".avif"] = ["image/avif"],
        [".gif"] = ["image/gif"],
        [".svg"] = ["image/svg+xml"],
        // Vídeo para el hero de la portada (autoplay/muted/loop en el portal)
        [".mp4"] = ["video/mp4"],
        [".webm"] = ["video/webm"],
        // Marca de la instancia (theming): la tipografía .woff2 y el favicon .ico que el
        // back-office ya ofrece subir. Ni el navegador ni Windows saben siempre su tipo
        // (llegan a menudo como application/octet-stream), así que se admite el genérico
        // y, a cambio, se comprueba la CABECERA del fichero antes de escribirlo: ninguno
        // de los dos se sirve como algo ejecutable (font/woff2 e image/x-icon).
        [".woff2"] = ["font/woff2", "application/font-woff2", "application/octet-stream"],
        [".ico"] = ["image/x-icon", "image/vnd.microsoft.icon", "image/ico", "application/octet-stream"]
    };

    // El listado enseña también los medios de demostración que van con el producto: el
    // CMS tiene que poder verlos y elegirlos.
    private static readonly string[] Listable =
        [".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif", ".svg", ".mp4", ".webm", ".woff2", ".ico"];

    public static string MediaRoot(IConfiguration config, IWebHostEnvironment env) =>
        config["Media:Root"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "media", "portal");

    public static string ContentTypeFor(string extension) =>
        Allowed.TryGetValue(extension, out var types) ? types[0] : "application/octet-stream";

    // Un nombre relativo seguro: sin raíz, sin "..", solo separadores hacia dentro. Los
    // medios de demostración viven en subcarpetas (products/…), las subidas no.
    private static bool IsSafeName(string name) =>
        name.Length > 0 && name.Length <= 200
        && !Path.IsPathRooted(name)
        && !name.Contains("..", StringComparison.Ordinal)
        && !name.Contains('\\')
        && name.Split('/').All(segment => segment.Length > 0 && segment == Path.GetFileName(segment));

    /// Bytes y tipo de un medio por su nombre, en orden de prioridad: base de datos
    /// (subidas), carpeta de medios (subidas antiguas en disco) y medios de demostración
    /// de la imagen. Lo usa el endpoint que los sirve y el generador de PDF.
    public static async Task<(byte[] Bytes, string ContentType, string Origin)?> ReadAsync(
        string name, AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        if (!IsSafeName(name)) return null;

        var row = await db.PortalMediaFiles.FindAsync(name);
        if (row is not null && row.Bytes.Length > 0) return (row.Bytes, row.ContentType, "subida");

        foreach (var (root, origin) in new[] { (MediaRoot(config, env), "disco"), (MediaSeed.Root(env), "demo") })
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                return (await File.ReadAllBytesAsync(path), ContentTypeFor(Path.GetExtension(path)), origin);
        }
        return null;
    }

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        // Servir los medios. Un solo endpoint para los tres orígenes (base de datos, disco
        // de instancias anteriores, demostración de la imagen): el enrutado elige este
        // comodín ANTES que cualquier proveedor de estáticos, así que estos nunca llegarían
        // a responder por /media/portal/…. Público (las imágenes de la portada no llevan
        // token). Las subidas llevan sufijo único en el nombre y se cachean a largo plazo;
        // la demostración cambia con el producto y se revalida cada hora.
        app.MapGet(UrlPrefix + "{*name}", async (string name, HttpResponse response,
            AppDbContext db, IConfiguration config, IWebHostEnvironment env) =>
        {
            if (await ReadAsync(name, db, config, env) is not { } medio) return Results.NotFound();
            response.Headers.CacheControl = medio.Origin == "subida"
                ? "public, max-age=31536000, immutable"
                : "public, max-age=3600";
            return Results.File(medio.Bytes, medio.ContentType, enableRangeProcessing: true);
        });

        app.MapPost("/api/admin/media", async (HttpRequest request, AppDbContext db) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Envía el fichero como multipart/form-data." });

            var form = await request.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No has adjuntado ningún fichero." });
            if (file.Length > MaxBytes)
                return Results.BadRequest(new { error = $"La imagen supera el máximo de {MaxBytes / (1024 * 1024)} MB." });

            var extension = Path.GetExtension(file.FileName ?? "");
            if (extension.Length == 0 || !Allowed.TryGetValue(extension, out var types))
                return Results.BadRequest(new
                {
                    error = $"Formato no permitido. Admitidos: {string.Join(", ", Allowed.Keys)}."
                });

            var contentType = (file.ContentType ?? "").Split(';')[0].Trim();
            if (!types.Contains(contentType, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new
                {
                    error = $"El contenido dice ser \"{contentType}\" y la extensión {extension}: no cuadran."
                });

            // Se lee ENTERO antes de guardarlo (va a la base de datos de todos modos) y se
            // revisa lo que lo necesita: el SVG, único formato que el navegador ejecuta (se
            // rechaza si trae script en vez de dibujo), y los binarios que se admiten con
            // MIME genérico (.woff2/.ico), de los que se comprueba la cabecera para que
            // "application/octet-stream" no sea una rendija.
            byte[] content;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer);
                content = buffer.ToArray();
            }
            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) && !IsSafeSvg(content))
                return Results.BadRequest(new
                {
                    error = "El SVG lleva script o enlaces ejecutables: súbelo sin <script>, "
                            + "sin atributos on… y sin javascript:."
                });
            if (!HasExpectedSignature(extension, content))
                return Results.BadRequest(new
                {
                    error = $"El contenido no es un {extension.TrimStart('.')} de verdad: "
                            + "la cabecera del fichero no cuadra con la extensión."
                });

            var name = UniqueName(file.FileName ?? "imagen", extension);
            db.PortalMediaFiles.Add(new PortalMediaFile
            {
                Name = name,
                // Se sirve siempre con el tipo canónico: un woff2 subido como octet-stream
                // tiene que llegar al navegador como font/woff2.
                ContentType = types[0],
                Bytes = content,
                Size = content.Length,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var url = UrlPrefix + name;
            return Results.Created(url, new { url, name, size = content.Length, contentType = types[0] });
        }).RequireAdmin().DisableAntiforgery();

        app.MapGet("/api/admin/media", async (AppDbContext db, IConfiguration config, IWebHostEnvironment env) =>
        {
            // Subidas (base de datos) primero, luego lo que quede en disco de instancias
            // anteriores y por último la demostración. Sin duplicar nombres.
            var items = new List<MediaItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in await db.PortalMediaFiles
                         .Select(f => new { f.Name, f.Size, f.CreatedAt })
                         .OrderByDescending(f => f.CreatedAt).ToListAsync())
                if (seen.Add(row.Name))
                    items.Add(new MediaItem(row.Name, UrlPrefix + row.Name, row.Size, row.CreatedAt, "subida"));

            foreach (var (root, origin) in new[] { (MediaRoot(config, env), "disco"), (MediaSeed.Root(env), "demo") })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in new DirectoryInfo(root).GetFiles()
                             .Where(f => Listable.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                             .OrderByDescending(f => f.LastWriteTimeUtc))
                    if (seen.Add(f.Name))
                        items.Add(new MediaItem(f.Name, UrlPrefix + f.Name, f.Length, f.LastWriteTimeUtc, origin));
            }

            return Results.Ok(new { items });
        }).RequireAdmin();

        app.MapDelete("/api/admin/media/{name}", async (string name, AppDbContext db, IConfiguration config, IWebHostEnvironment env) =>
        {
            // Nada de salir de la carpeta de medios: solo nombres de fichero pelados
            if (name != Path.GetFileName(name) || name is "." or ".." || name.Contains("..", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Nombre de fichero no válido." });

            var row = await db.PortalMediaFiles.FindAsync(name);
            if (row is not null)
            {
                db.PortalMediaFiles.Remove(row);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }

            var path = Path.Combine(MediaRoot(config, env), name);
            if (File.Exists(path))
            {
                File.Delete(path);
                return Results.NoContent();
            }

            if (File.Exists(Path.Combine(MediaSeed.Root(env), name)))
                return Results.BadRequest(new { error = "Los medios de demostración van con el producto y no se pueden borrar; sube los tuyos y elígelos en su lugar." });

            return Results.NotFound(new { error = "El fichero no existe." });
        }).RequireAdmin();
    }

    private sealed record MediaItem(string Name, string Url, long Size, DateTime ModifiedAt, string Origin);

    // Cabeceras de los binarios que se aceptan con MIME genérico. Se comparan tal cual:
    //   .woff2 → "wOF2" (firma del WOFF 2.0)
    //   .ico   → ICONDIR 00 00 01 00, o un PNG (los favicon "PNG con nombre .ico" corren
    //            por ahí y los navegadores los aceptan)
    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".woff2"] = [[0x77, 0x4F, 0x46, 0x32]],
        [".ico"] = [[0x00, 0x00, 0x01, 0x00], [0x89, 0x50, 0x4E, 0x47]]
    };

    private static bool HasExpectedSignature(string extension, byte[] bytes) =>
        !Signatures.TryGetValue(extension, out var signatures)
        || signatures.Any(signature => bytes.Length >= signature.Length
            && bytes.Take(signature.Length).SequenceEqual(signature));

    // Lista negra corta y explícita: lo que convierte un SVG en una página ejecutable
    // (script, manejadores on…, javascript: y documentos embebidos).
    private static readonly string[] SvgForbidden =
        ["<script", "javascript:", "<foreignobject", "<iframe", "<embed", "<object", "<use xlink:href=\"http"];

    private static bool IsSafeSvg(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        if (SvgForbidden.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Cualquier atributo de evento: on… = "…"
        return !System.Text.RegularExpressions.Regex.IsMatch(
            text, @"\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // banner portada SS26.png → banner-portada-ss26-4f2a91.png (URL limpia y sin pisar)
    private static string UniqueName(string original, string extension)
    {
        var stem = Path.GetFileNameWithoutExtension(original).ToLowerInvariant();
        var slug = new StringBuilder();
        foreach (var character in stem)
        {
            if (char.IsAsciiLetterOrDigit(character)) slug.Append(character);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        var clean = slug.ToString().Trim('-');
        if (clean.Length == 0) clean = "imagen";
        if (clean.Length > 60) clean = clean[..60].Trim('-');

        return $"{clean}-{Guid.NewGuid():N}"[..(clean.Length + 7)] + extension.ToLowerInvariant();
    }
}
