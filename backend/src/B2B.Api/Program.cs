using System.Text;
using B2B.Api.Admin;
using B2B.Api.Auth;
using B2B.Api.Data;
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
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSyncEndpoints();
app.MapQueryEndpoints();
app.MapAdminEndpoints();

app.Run();

public partial class Program { }
