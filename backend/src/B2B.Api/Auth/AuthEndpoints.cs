using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace B2B.Api.Auth;

public record LoginRequest(string? Email, string? Password, string? Type, bool LongDuration);

public static class AuthEndpoints
{
    // BC parsea tokenExpiresIn con Evaluate sobre DateTime en sesión es-ES:
    // debe ser fecha absoluta local en este formato, no segundos de duración.
    private const string BcDateTimeFormat = "dd/MM/yyyy HH:mm:ss";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, AppDbContext db, IConfiguration config) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(request.Password))
                return Results.Json(new { error = "Invalid credentials" }, statusCode: StatusCodes.Status401Unauthorized);

            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email && u.IsActive);
            if (user is null)
                return Results.Json(new { error = "Invalid credentials" }, statusCode: StatusCodes.Status401Unauthorized);

            var verdict = new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verdict == PasswordVerificationResult.Failed)
                return Results.Json(new { error = "Invalid credentials" }, statusCode: StatusCodes.Status401Unauthorized);

            var hours = request.LongDuration
                ? config.GetValue("Jwt:LongDurationHours", 24)
                : config.GetValue("Jwt:ShortDurationHours", 2);
            var expiresAt = DateTime.Now.AddHours(hours);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]
                ?? throw new InvalidOperationException("Jwt:SigningKey is not configured")));
            var jwt = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, user.Email)
                ],
                expires: expiresAt.ToUniversalTime(),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return Results.Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(jwt),
                tokenExpiresIn = expiresAt.ToString(BcDateTimeFormat)
            });
        });
    }
}
