using B2B.Api.Auth;
using System.Text;

namespace B2B.Api.Admin;

// Medios del portal (plan §3): las imágenes que el CMS pone en la portada. Se
// guardan en wwwroot/media/portal y las sirve el propio portal como estáticos —
// sin CDN y sin hotlinks a terceros.
public static class MediaEndpoints
{
    public const string UrlPrefix = "/media/portal/";
    private const long MaxBytes = 5 * 1024 * 1024;

    // Extensión y content-type tienen que cuadrar: ni .php declarado como imagen ni
    // .png con html dentro. El SVG entra (F-07: el wordmark y los iconos de la portada
    // son vectoriales) con dos cautelas: la subida es solo-admin desde B-1 y el dibujo
    // se revisa antes de escribirlo, porque se sirve desde el mismo origen.
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

    // El listado sí enseña los SVG que van con el producto (la portada de demo):
    // están en la carpeta, y el CMS tiene que poder verlos y borrarlos.
    private static readonly string[] Listable =
        [".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif", ".svg", ".mp4", ".webm", ".woff2", ".ico"];

    public static string MediaRoot(IConfiguration config, IWebHostEnvironment env) =>
        config["Media:Root"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "media", "portal");

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/media", async (HttpRequest request, IConfiguration config, IWebHostEnvironment env) =>
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

            // Dos casos se leen ENTEROS antes de escribirlos: el SVG, único formato que el
            // navegador ejecuta (se rechaza si trae script en vez de dibujo), y los binarios
            // que se admiten con MIME genérico (.woff2/.ico), de los que se comprueba la
            // cabecera para que "application/octet-stream" no sea una rendija.
            var isSvg = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
            var checksSignature = Signatures.ContainsKey(extension);
            byte[]? content = null;
            if (isSvg || checksSignature)
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                content = buffer.ToArray();
                if (isSvg && !IsSafeSvg(content))
                    return Results.BadRequest(new
                    {
                        error = "El SVG lleva script o enlaces ejecutables: súbelo sin <script>, "
                                + "sin atributos on… y sin javascript:."
                    });
                if (checksSignature && !HasExpectedSignature(extension, content))
                    return Results.BadRequest(new
                    {
                        error = $"El contenido no es un {extension.TrimStart('.')} de verdad: "
                                + "la cabecera del fichero no cuadra con la extensión."
                    });
            }

            var root = MediaRoot(config, env);
            Directory.CreateDirectory(root);

            var name = UniqueName(file.FileName ?? "imagen", extension);
            await using (var stream = File.Create(Path.Combine(root, name)))
            {
                if (content is not null)
                    await stream.WriteAsync(content);
                else
                    await file.CopyToAsync(stream);
            }

            var url = UrlPrefix + name;
            return Results.Created(url, new { url, name, size = file.Length, contentType });
        }).RequireAdmin().DisableAntiforgery();

        app.MapGet("/api/admin/media", (IConfiguration config, IWebHostEnvironment env) =>
        {
            var root = MediaRoot(config, env);
            if (!Directory.Exists(root))
                return Results.Ok(new { items = Array.Empty<object>() });

            var items = new DirectoryInfo(root).GetFiles()
                .Where(f => Listable.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new { name = f.Name, url = UrlPrefix + f.Name, size = f.Length, modifiedAt = f.LastWriteTimeUtc })
                .ToList();

            return Results.Ok(new { items });
        }).RequireAdmin();

        app.MapDelete("/api/admin/media/{name}", (string name, IConfiguration config, IWebHostEnvironment env) =>
        {
            // Nada de salir de la carpeta de medios: solo nombres de fichero pelados
            if (name != Path.GetFileName(name) || name is "." or ".." || name.Contains("..", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Nombre de fichero no válido." });

            var path = Path.Combine(MediaRoot(config, env), name);
            if (!File.Exists(path))
                return Results.NotFound(new { error = "El fichero no existe." });

            File.Delete(path);
            return Results.NoContent();
        }).RequireAdmin();
    }

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
