using System.Security.Cryptography;
using B2B.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Notifications;

// Orquesta el ciclo de vida del enlace de activación: lo emite, compone el correo,
// lo envía (por el emisor configurado) y lo canjea para fijar la contraseña.
//
// Regla de un solo token vivo por usuario+propósito: emitir uno nuevo invalida los
// anteriores, así un reenvío no deja varios enlaces válidos a la vez.
public sealed class ActivationService(
    AppDbContext db,
    IEmailSender email,
    IConfiguration config,
    ILogger<ActivationService> logger)
{
    /// El enlace de activación caduca a las 72 horas (requisito del alta de cliente)
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(72);

    // ── Emisión ──────────────────────────────────────────────────────────────

    /// Crea un token nuevo (invalidando los previos del mismo propósito) y devuelve
    /// el token EN CLARO para construir el enlace. En la BD solo queda su hash.
    public async Task<string> IssueAsync(AppUser user, string purpose, TimeSpan? lifetime = null)
    {
        var previous = await db.ActivationTokens
            .Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var token in previous)
            token.UsedAt = now;   // invalidados: nunca dos enlaces vivos a la vez

        var raw = NewRawToken();
        db.ActivationTokens.Add(new ActivationToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(raw),
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime ?? DefaultLifetime)
        });
        await db.SaveChangesAsync();
        return raw;
    }

    /// Emite el token, compone el correo en el idioma del usuario y lo envía. El envío
    /// nunca lanza: si el SMTP falla, queda registrado en SentEmail y se puede reenviar.
    public async Task<string> SendAsync(AppUser user, string purpose, TimeSpan? lifetime = null)
    {
        var raw = await IssueAsync(user, purpose, lifetime);
        var lang = Lang(user.Culture);
        var link = BuildLink(raw, lang);
        var message = Compose(user, purpose, link, lang);

        EmailResult result;
        try
        {
            result = await email.SendAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado enviando activación a {Email}", user.Email);
            result = new EmailResult(false, email.Transport, ex.Message);
        }

        db.SentEmails.Add(new SentEmail
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            To = user.Email,
            Subject = message.Subject,
            Body = message.TextBody,
            Transport = result.Transport,
            Error = result.Ok ? null : result.Error
        });
        await db.SaveChangesAsync();
        return raw;
    }

    // ── Validación y canje ─────────────────────────────────────────────────────

    /// Busca un token vivo (no usado, no caducado) por su valor en claro. Devuelve el
    /// token y su usuario, o null. No distingue "no existe" de "caducado": el que
    /// canjea recibe siempre el mismo mensaje, sin pistas.
    public async Task<(ActivationToken Token, AppUser User)?> ValidateAsync(string? rawToken, string? purpose = null)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = Hash(rawToken.Trim());
        var token = await db.ActivationTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null || token.UsedAt != null || token.ExpiresAt <= DateTime.UtcNow)
            return null;
        if (purpose is not null && !string.Equals(token.Purpose, purpose, StringComparison.Ordinal))
            return null;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == token.UserId);
        return user is null ? null : (token, user);
    }

    /// Canjea el token: fija la contraseña del usuario y sella el token como usado.
    /// Devuelve false si el token no es válido.
    public async Task<bool> RedeemAsync(string? rawToken, string newPassword)
    {
        var found = await ValidateAsync(rawToken);
        if (found is not { } pair)
            return false;

        pair.User.PasswordHash = new PasswordHasher<AppUser>().HashPassword(pair.User, newPassword);
        pair.Token.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    // ── Construcción del enlace y del correo ───────────────────────────────────

    public string BuildLink(string rawToken, string lang)
    {
        var baseUrl = (config["Portal:BaseUrl"] ?? "http://localhost:5199").TrimEnd('/');
        var market = config["Portal:Market"] ?? "es";
        return $"{baseUrl}/{market}/{lang}/activate?token={Uri.EscapeDataString(rawToken)}";
    }

    private EmailMessage Compose(AppUser user, string purpose, string link, string lang)
    {
        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name!;
        var t = Templates.TryGetValue(lang, out var found) ? found : Templates["es"];
        var reset = purpose == ActivationPurpose.Reset;

        var subject = reset ? t.ResetSubject : t.ActivationSubject;
        var intro = reset ? t.ResetIntro : t.ActivationIntro;

        var text = $"{t.Hello} {name},\n\n{intro}\n\n{link}\n\n{t.Expiry}\n\n{t.Signature}";
        var html = $"""
            <div style="font-family:Inter,Arial,sans-serif;max-width:520px;margin:0 auto;color:#1a1a1a">
              <p style="font-size:1.4rem;font-weight:700;color:#1f5c46">lejan<sup>™</sup></p>
              <p>{Esc(t.Hello)} <b>{Esc(name)}</b>,</p>
              <p>{Esc(intro)}</p>
              <p style="margin:1.6em 0">
                <a href="{Esc(link)}" style="background:#1f5c46;color:#fff;text-decoration:none;
                   padding:.8em 1.4em;border-radius:8px;display:inline-block">{Esc(t.Button)}</a>
              </p>
              <p style="font-size:.85rem;color:#666">{Esc(t.Expiry)}</p>
              <p style="font-size:.8rem;color:#999;word-break:break-all">{Esc(link)}</p>
              <p style="font-size:.85rem;color:#666">{Esc(t.Signature)}</p>
            </div>
            """;
        return new EmailMessage(user.Email, subject, html, text);
    }

    // ── Utilidades ─────────────────────────────────────────────────────────────

    private static string NewRawToken()
    {
        // 32 bytes de aleatoriedad criptográfica en base64url (sin +/= para que viaje
        // limpio en la URL). Suficiente entropía para que no se pueda adivinar.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string raw)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// "es_ES" → "es"; solo idiomas del portal, resto cae a "es"
    private static string Lang(string? culture)
    {
        var two = (culture ?? "").Trim().ToLowerInvariant();
        two = two.Length >= 2 ? two[..2] : "es";
        return two is "es" or "en" or "fr" or "it" ? two : "es";
    }

    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

    private sealed record Template(
        string Hello, string ActivationSubject, string ActivationIntro,
        string ResetSubject, string ResetIntro, string Button, string Expiry, string Signature);

    // Plantillas en los cuatro idiomas del portal. Texto sobrio, sin promesas de
    // marketing: es un correo transaccional.
    private static readonly Dictionary<string, Template> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = new(
            "Hola",
            "Activa tu cuenta del portal B2B de lejan",
            "Se ha creado tu acceso al portal B2B de lejan. Para empezar, define tu contraseña pulsando el botón:",
            "Restablece tu contraseña del portal B2B de lejan",
            "Has pedido restablecer tu contraseña. Define una nueva pulsando el botón:",
            "Definir mi contraseña",
            "El enlace caduca en 72 horas. Si no esperabas este correo, puedes ignorarlo.",
            "Equipo lejan B2B"),
        ["en"] = new(
            "Hello",
            "Activate your lejan B2B portal account",
            "Your access to the lejan B2B portal has been created. To get started, set your password:",
            "Reset your lejan B2B portal password",
            "You requested a password reset. Set a new one below:",
            "Set my password",
            "The link expires in 72 hours. If you didn't expect this email, you can ignore it.",
            "The lejan B2B team"),
        ["fr"] = new(
            "Bonjour",
            "Activez votre compte du portail B2B lejan",
            "Votre accès au portail B2B de lejan a été créé. Pour commencer, définissez votre mot de passe :",
            "Réinitialisez le mot de passe de votre portail B2B lejan",
            "Vous avez demandé la réinitialisation de votre mot de passe. Définissez-en un nouveau :",
            "Définir mon mot de passe",
            "Le lien expire dans 72 heures. Si vous n'attendiez pas cet e-mail, ignorez-le.",
            "L'équipe lejan B2B"),
        ["it"] = new(
            "Ciao",
            "Attiva il tuo account del portale B2B lejan",
            "Il tuo accesso al portale B2B di lejan è stato creato. Per iniziare, imposta la password:",
            "Reimposta la password del portale B2B lejan",
            "Hai richiesto di reimpostare la password. Impostane una nuova:",
            "Imposta la password",
            "Il link scade tra 72 ore. Se non ti aspettavi questa email, puoi ignorarla.",
            "Il team lejan B2B")
    };
}
