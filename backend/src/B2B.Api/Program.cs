using System.Text;
using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSyncEndpoints();
app.MapQueryEndpoints();

app.Run();

public partial class Program { }
