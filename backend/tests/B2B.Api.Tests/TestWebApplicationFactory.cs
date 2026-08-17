using System.Net.Http.Json;
using B2B.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace B2B.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string SeededEmail = "integracion@test.com";
    public const string SeededPassword = "secreto123";

    private readonly string _dbName = $"b2b-tests-{Guid.NewGuid():N}";

    /// Carpeta de medios de esta fábrica: las subidas de prueba no tocan wwwroot
    public string MediaRoot { get; } =
        Path.Combine(Path.GetTempPath(), $"b2b-media-{Guid.NewGuid():N}");

    /// Adjuntos del formulario de contacto (fuera de wwwroot, como en producción)
    public string ContactRoot { get; } =
        Path.Combine(Path.GetTempPath(), $"b2b-contacto-{Guid.NewGuid():N}");

    /// La portada de demostración solo se siembra donde la prueba lo pide
    protected virtual bool SeedPortalContent => false;

    public async Task<string> GetTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededEmail,
            password = SeededPassword,
            type = "global",
            longDuration = true
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Media:Root", MediaRoot);
        builder.UseSetting("Contact:Root", ContactRoot);
        builder.UseSetting("Seed:PortalContent", SeedPortalContent ? "true" : "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_dbName));

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            if (!db.Users.Any(u => u.Email == SeededEmail))
            {
                var user = new AppUser { Id = Guid.NewGuid(), Email = SeededEmail, PasswordHash = "" };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, SeededPassword);
                db.Users.Add(user);
                db.SaveChanges();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var folder in new[] { MediaRoot, ContactRoot })
            if (Directory.Exists(folder))
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}
