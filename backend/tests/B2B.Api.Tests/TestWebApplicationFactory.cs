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

    /// Administrador del CMS: el único rol que abre /api/admin/* (B-1)
    public const string AdminEmail = "admin@test.com";
    public const string AdminPassword = "admin-secreto123";

    /// Cuenta de servicio del conector de BC (auditoría B-2): rol "integration" que
    /// abre /api/sync/* y /api/query/*. Es una cuenta APARTE de los administradores de
    /// cliente —a los que las pruebas provisionan sobre SeededEmail y acaban en
    /// "client-admin"—, así que la siembra vía sync la usa a ella y nunca se degrada.
    public const string ConnectorEmail = "conector@test.com";
    public const string ConnectorPassword = "conector-secreto123";

    private readonly string _dbName = $"b2b-tests-{Guid.NewGuid():N}";

    /// Carpeta de medios de esta fábrica: las subidas de prueba no tocan wwwroot
    public string MediaRoot { get; } =
        Path.Combine(Path.GetTempPath(), $"b2b-media-{Guid.NewGuid():N}");

    /// Adjuntos del formulario de contacto (fuera de wwwroot, como en producción)
    public string ContactRoot { get; } =
        Path.Combine(Path.GetTempPath(), $"b2b-contacto-{Guid.NewGuid():N}");

    /// La portada de demostración solo se siembra donde la prueba lo pide
    protected virtual bool SeedPortalContent => false;

    /// El límite de intentos del login (m-5) estorbaría a la batería entera: las
    /// pruebas se autentican en cada petición. Solo la prueba del límite lo baja.
    protected virtual int LoginPermitLimit => 100_000;

    public Task<string> GetTokenAsync(HttpClient client) =>
        LoginAsync(client, SeededEmail, SeededPassword);

    /// Token del administrador del CMS (rol "admin")
    public Task<string> GetAdminTokenAsync(HttpClient client) =>
        LoginAsync(client, AdminEmail, AdminPassword);

    /// Token de la cuenta de servicio del conector (rol "integration"): el que la
    /// siembra vía sync debe usar para no chocar con la política bc-connector (B-2).
    public Task<string> GetConnectorTokenAsync(HttpClient client) =>
        LoginAsync(client, ConnectorEmail, ConnectorPassword);

    public async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password,
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
        builder.UseSetting("Auth:Login:PermitLimit", LoginPermitLimit.ToString());
        // El límite de activación (m-P2-1) también estorbaría a la batería: las pruebas
        // del flujo de activación llaman al endpoint muchas veces desde la misma IP.
        builder.UseSetting("Auth:Activation:PermitLimit", LoginPermitLimit.ToString());

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_dbName));

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            Seed(db, SeededEmail, SeededPassword, role: "integration");
            Seed(db, AdminEmail, AdminPassword, role: "admin");
            Seed(db, ConnectorEmail, ConnectorPassword, role: "integration");
        });
    }

    private static void Seed(AppDbContext db, string email, string password, string role)
    {
        if (db.Users.Any(u => u.Email == email)) return;

        var user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = "", Role = role };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
        db.Users.Add(user);
        db.SaveChanges();
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
