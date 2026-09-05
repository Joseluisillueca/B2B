using Microsoft.AspNetCore.Hosting;
using B2B.Api.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Los medios de demostración se siembran al arrancar en la carpeta de medios, que en
// producción es un disco persistente que arranca VACÍO y tapa lo que traía la imagen.
public class MediaSeedTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public MediaSeedTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void AlArrancar_LosMediosDeDemostracionEstanEnLaCarpetaDeMedios()
    {
        _factory.CreateClient(); // fuerza el arranque
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var root = MediaEndpoints.MediaRoot(config, env);

        Assert.True(File.Exists(Path.Combine(root, "demo-hero-carretera.svg")), $"falta la portada de demo en {root}");
        Assert.True(File.Exists(Path.Combine(root, "demo-tile-reposicion.svg")));
        Assert.True(Directory.Exists(Path.Combine(root, "products")), "faltan las fotos de los modelos de ejemplo");
    }

    [Fact]
    public void LaSiembra_NoPisaLoQueYaExiste()
    {
        _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var root = MediaEndpoints.MediaRoot(config, env);

        // El CMS sustituye una imagen de demo por la suya: una segunda siembra la respeta
        var propia = Path.Combine(root, "demo-hero-taller.svg");
        File.WriteAllText(propia, "<svg>propia</svg>");
        var copiados = MediaSeed.CopyMissing(config, env);
        Assert.Equal("<svg>propia</svg>", File.ReadAllText(propia));
        Assert.Equal(0, copiados);
    }
}
