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
    // .png con html dentro. El SVG queda fuera a propósito: se sirve desde el mismo
    // origen y puede ejecutar script.
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".webp"] = ["image/webp"],
        [".avif"] = ["image/avif"],
        [".gif"] = ["image/gif"]
    };

    // El listado sí enseña los SVG que van con el producto (la portada de demo):
    // están en la carpeta, y el CMS tiene que poder verlos y borrarlos.
    private static readonly string[] Listable =
        [".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif", ".svg"];

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

            var root = MediaRoot(config, env);
            Directory.CreateDirectory(root);

            var name = UniqueName(file.FileName ?? "imagen", extension);
            await using (var stream = File.Create(Path.Combine(root, name)))
                await file.CopyToAsync(stream);

            var url = UrlPrefix + name;
            return Results.Created(url, new { url, name, size = file.Length, contentType });
        }).RequireAuthorization().DisableAntiforgery();

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
        }).RequireAuthorization();

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
        }).RequireAuthorization();
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
