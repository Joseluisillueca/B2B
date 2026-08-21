using B2B.Api.Data;
using B2B.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Activación de cuenta y recuperación de contraseña. Todo PÚBLICO (sin token JWT):
// el usuario aún no puede entrar, precisamente por eso activa. La seguridad la da el
// token de un solo uso del enlace, no la sesión.
public static class ActivationEndpoints
{
    private const int MinPasswordLength = 8;

    public static void MapActivationEndpoints(this IEndpointRouteBuilder app)
    {
        // ¿Es válido este enlace? La página de activación lo consulta al abrirse para
        // enseñar el formulario o un aviso de "enlace caducado" sin exponer nada más.
        app.MapGet("/api/activation/{token}", async (string token, ActivationService activation) =>
        {
            var found = await activation.ValidateAsync(token);
            return found is { } pair
                ? Results.Ok(new
                {
                    valid = true,
                    email = pair.User.Email,
                    name = pair.User.Name,
                    purpose = pair.Token.Purpose,
                    expiresAt = pair.Token.ExpiresAt
                })
                : Results.Ok(new { valid = false });
        }).AllowAnonymous();

        // Canje: fija la contraseña. El token se sella (un solo uso). Tras esto el
        // usuario ya puede iniciar sesión con su nueva contraseña.
        app.MapPost("/api/activation/{token}", async (
            string token, SetPasswordRequest body, ActivationService activation) =>
        {
            var next = body.Password ?? "";
            if (next.Length < MinPasswordLength)
                return Results.BadRequest(new { error = $"La contraseña necesita al menos {MinPasswordLength} caracteres." });
            if (next != (body.Repeat ?? ""))
                return Results.BadRequest(new { error = "La repetición no coincide con la contraseña." });

            var ok = await activation.RedeemAsync(token, next);
            return ok
                ? Results.NoContent()
                : Results.BadRequest(new { error = "El enlace no es válido o ha caducado. Pide uno nuevo." });
        }).AllowAnonymous();

        // Reenvío: sirve tanto para "no me llegó la activación" como para el enlace
        // "¿Olvidaste tu contraseña?" del login. SIEMPRE responde 200 aunque el email
        // no exista: no se filtra qué correos están dados de alta.
        app.MapPost("/api/activation/resend", async (
            ResendRequest body, AppDbContext db, ActivationService activation) =>
        {
            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length > 0)
            {
                var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
                if (user is not null)
                {
                    // Sin contraseña → es una activación pendiente; con contraseña → es
                    // un restablecimiento (olvido). El correo se adapta al caso.
                    var purpose = string.IsNullOrEmpty(user.PasswordHash)
                        ? ActivationPurpose.Activation
                        : ActivationPurpose.Reset;
                    await activation.SendAsync(user, purpose);
                }
            }
            return Results.Ok(new { sent = true });
        }).AllowAnonymous();
    }

    public sealed record SetPasswordRequest(string? Password, string? Repeat);
    public sealed record ResendRequest(string? Email);
}
