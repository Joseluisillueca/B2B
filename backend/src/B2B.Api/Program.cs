using System.Text;
using B2B.Api.Admin;
using B2B.Api.Auth;
using B2B.Api.Data;
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
builder.Services.AddAuthorization();

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
    {
        var user = new AppUser { Id = Guid.NewGuid(), Email = seedEmail.ToLowerInvariant(), PasswordHash = "" };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, seedPassword);
        db.Users.Add(user);
        db.SaveChanges();
    }

    // Portada de demostración: solo mientras el CMS no haya publicado nada
    if (app.Configuration.GetValue("Seed:PortalContent", true))
        PortalContentSeed.EnsureDemoContent(db);
}

app.MapOpenApi();
app.MapScalarApiReference("/docs", options => options.WithTitle("B2B Platform API"));

app.UseDefaultFiles();
app.UseStaticFiles();

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
app.MapShopEndpoints();
app.MapPortalEndpoints();
app.MapCartEndpoints();

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

public partial class Program { }
