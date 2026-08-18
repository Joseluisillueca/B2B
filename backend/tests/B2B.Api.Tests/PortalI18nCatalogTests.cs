using System.Text.Json;

namespace B2B.Api.Tests;

// Auditoría m-1: i18n.js fusiona el diccionario español como base, así que toda clave
// que falte en en/fr/it se sirve en español sin ningún síntoma —ni error de consola ni
// clave cruda a la vista—. M-2 (la fila LANGUAGE del perfil diciendo "Español" en los
// cuatro idiomas) fue exactamente eso. Este test cierra la categoría entera: los cuatro
// ficheros tienen que declarar el mismo conjunto de claves.
public class PortalI18nCatalogTests
{
    private static readonly string[] Languages = ["es", "en", "fr", "it"];

    private static string I18nFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName,
                "src", "B2B.Api", "wwwroot", "portal", "js", "i18n");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encuentra wwwroot/portal/js/i18n desde " + AppContext.BaseDirectory);
    }

    /// Claves aplanadas: "a.b" si algún día el diccionario se anida
    private static SortedSet<string> Keys(string language)
    {
        var path = Path.Combine(I18nFolder(), $"{language}.json");
        Assert.True(File.Exists(path), $"Falta el diccionario {language}.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        Flatten(document.RootElement, prefix: "", keys);
        return keys;
    }

    private static void Flatten(JsonElement element, string prefix, SortedSet<string> keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            if (prefix.Length > 0) keys.Add(prefix);
            return;
        }

        foreach (var property in element.EnumerateObject())
            Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", keys);
    }

    [Fact]
    public void LosCuatroDiccionariosDeclaranLasMismasClaves()
    {
        var reference = Keys("es");
        Assert.NotEmpty(reference);

        foreach (var language in Languages.Skip(1))
        {
            var keys = Keys(language);
            var missing = reference.Except(keys).ToList();
            var extra = keys.Except(reference).ToList();

            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"{language}.json no cuadra con es.json — "
                + $"faltan {missing.Count} ({string.Join(", ", missing.Take(10))}); "
                + $"sobran {extra.Count} ({string.Join(", ", extra.Take(10))})");
        }
    }

    // Una clave presente pero vacía se pinta como un hueco en la vista
    [Fact]
    public void NingunaTraduccionEstaVacia()
    {
        foreach (var language in Languages)
        {
            var path = Path.Combine(I18nFolder(), $"{language}.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in document.RootElement.EnumerateObject())
                Assert.False(property.Value.ValueKind == JsonValueKind.String
                             && string.IsNullOrWhiteSpace(property.Value.GetString()),
                    $"{language}.json: la clave \"{property.Name}\" está vacía");
        }
    }
}
