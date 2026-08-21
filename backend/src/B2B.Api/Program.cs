using System.Text;
using B2B.Api.Admin;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Notifications;
using B2B.Api.Portal;
using B2B.Api.Shop;
using B2B.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            builder.Configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured")))
    });
builder.Services.AddAuthorization(options =>
{
    options.AddAdminPolicy();
    options.AddConnectorPolicy();
    options.AddAgentPolicy();
});
builder.Services.AddLoginRateLimiter(builder.Configuration);
builder.Services.AddHttpClient();   // asistente del portal → API de Anthropic (opcional)

// Correo transaccional (activación de cuenta, recuperación de contraseña). Por
// defecto modo "log": no envía nada real, el correo queda en sent_emails y su enlace
// se puede leer en dev. Con Email:Mode=smtp usa un servidor real (p.ej. Office 365).
var emailOptions = builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton(emailOptions);
if (string.Equals(emailOptions.Mode, "smtp", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
builder.Services.AddScoped<ActivationService>();

// Auditoría m-6: la clave de firma de appsettings.json se auto-documenta como de
// desarrollo, pero nada impedía arrancar producción con ella. Ahora sí.
if (!builder.Environment.IsDevelopment()
    && builder.Configuration["Jwt:SigningKey"] == SigningKeys.DevelopmentDefault)
    throw new InvalidOperationException(
        $"Jwt:SigningKey sigue siendo la clave de desarrollo y el entorno es " +
        $"\"{builder.Environment.EnvironmentName}\". Configura una clave propia " +
        "(variable de entorno Jwt__SigningKey o secreto del despliegue) antes de arrancar.");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();

    // Usuario de integración para el conector BC (Setup: Integration User/Password)
    var seedEmail = app.Configuration["Seed:UserEmail"];
    var seedPassword = app.Configuration["Seed:UserPassword"];
    if (!string.IsNullOrEmpty(seedEmail) && !string.IsNullOrEmpty(seedPassword) && !db.Users.Any())
        SeedUser(db, seedEmail, seedPassword, ClientIdentity.IntegrationRole);

    // Administrador del CMS (auditoría B-1). El sync solo provisiona usuarios de cliente
    // (client-admin), así que el rol "admin" tiene que nacer de la configuración. En
    // desarrollo viene de appsettings.Development.json; en producción, de secretos.
    var adminEmail = app.Configuration["Seed:AdminEmail"];
    var adminPassword = app.Configuration["Seed:AdminPassword"];
    if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPassword)
        && !db.Users.Any(u => u.Email == adminEmail.ToLowerInvariant()))
        SeedUser(db, adminEmail, adminPassword, AdminPolicy.Role);

    // Portada de demostración: solo mientras el CMS no haya publicado nada
    if (app.Configuration.GetValue("Seed:PortalContent", true))
        PortalContentSeed.EnsureDemoContent(db);
}

// Auditoría m-4: la superficie completa de la API sin autenticar no se publica
// fuera de desarrollo.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/docs", options => options.WithTitle("B2B Platform API"));
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Content(
    """
    <!doctype html><html lang="es"><head><meta charset="utf-8"><title>B2B Platform</title></head>
    <body style="font-family:system-ui;max-width:40rem;margin:4rem auto;padding:0 1rem">
    <h1>B2B Platform — API</h1>
    <p>El backend está en marcha. Esta es una API para el conector de Business Central
    y el portal B2B; no tiene interfaz web en esta dirección.</p>
    <ul>
    <li><a href="/health">/health</a> — estado</li>
    <li><a href="/docs">/docs</a> — documentación de la API</li>
    <li><a href="/admin">/admin</a> — panel de administración</li>
    </ul>
    </body></html>
    """, "text/html"));

app.MapGet("/admin", () => Results.Redirect("/admin.html"));
app.MapGet("/shop", () => Results.Redirect("/shop.html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSyncEndpoints();
app.MapQueryEndpoints();
app.MapAdminEndpoints();
app.MapContentEndpoints();
app.MapMediaEndpoints();
app.MapModelImageEndpoints();
app.MapShopEndpoints();
app.MapPortalEndpoints();
app.MapAgentEndpoints();
app.MapCartEndpoints();
app.MapDocumentEndpoints();
app.MapAccountEndpoints();
app.MapSatEndpoints();
app.MapAssistantEndpoints();
app.MapActivationEndpoints();

// Portal del cliente: enrutado por History API sobre las rutas reales del portal
// actual, /{market}/{lang}/{vista} (p.ej. /es/es/orders). Recargar cualquiera de
// ellas debe servir el cascarón, así que van por MapFallbackToFile con los
// segmentos de mercado e idioma acotados a dos letras para no tragarse /api ni /docs.
const string PortalShell = "portal/index.html";
// Sin cuantificador {2}: las llaves colisionan con la sintaxis de plantilla de ruta
const string Locale = "^[a-z][a-z]$";

app.MapGet("/portal", () => Results.Redirect("/es/es/dashboard"));
app.MapFallbackToFile($"/{{market:regex({Locale})}}/{{lang:regex({Locale})}}", PortalShell);
app.MapFallbackToFile($"/{{market:regex({Locale})}}/{{lang:regex({Locale})}}/{{view}}", PortalShell);
app.MapFallbackToFile($"/{{market:regex({Locale})}}/{{lang:regex({Locale})}}/{{view}}/{{subview}}", PortalShell);
app.MapFallbackToFile("/login", PortalShell);

app.Run();

static void SeedUser(AppDbContext db, string email, string password, string role)
{
    var user = new AppUser
    {
        Id = Guid.NewGuid(),
        Email = email.ToLowerInvariant(),
        PasswordHash = "",
        Role = role
    };
    user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
    db.Users.Add(user);
    db.SaveChanges();
}

public partial class Program { }
