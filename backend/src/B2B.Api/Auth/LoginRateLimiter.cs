using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace B2B.Api.Auth;

// Auditoría m-5: /api/auth/login no tenía freno y los correos de los usuarios los
// publica el propio sync de BC, así que la fuerza bruta salía gratis. Ventana fija
// por IP: los intentos de una oficina no bloquean a otro cliente.
public static class LoginRateLimiter
{
    public const string PolicyName = "login";

    private const int DefaultPermitLimit = 10;
    private const int DefaultWindowSeconds = 60;

    public static IServiceCollection AddLoginRateLimiter(this IServiceCollection services, IConfiguration config)
    {
        var permitLimit = config.GetValue("Auth:Login:PermitLimit", DefaultPermitLimit);
        var windowSeconds = config.GetValue("Auth:Login:WindowSeconds", DefaultWindowSeconds);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                // Detrás de un proxy la IP real llega en X-Forwarded-For; sin
                // ForwardedHeaders configurado, todos comparten cubo (falla cerrado).
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
        });
    }
}

public static class SigningKeys
{
    /// La clave versionada en appsettings.json: fuera de desarrollo el arranque la rechaza
    public const string DevelopmentDefault = "dev-only-signing-key-change-in-production-0123456789";
}
