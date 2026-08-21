using System.Net;
using System.Net.Mail;

namespace B2B.Api.Notifications;

// Un correo listo para enviar. El cuerpo va en dos versiones: HTML (lo que ve el
// usuario) y texto plano (respaldo para clientes que no pintan HTML).
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

// Resultado del transporte. `Ok=false` no debe romper el flujo de negocio: el
// correo se registra igualmente (SentEmail) con el error, y se puede reintentar.
public sealed record EmailResult(bool Ok, string Transport, string? Error);

public interface IEmailSender
{
    /// "log" | "smtp": cómo despacha este emisor (para registrarlo en SentEmail)
    string Transport { get; }
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

// Configuración del correo (sección "Email" de appsettings). Por defecto modo "log":
// no se envía nada real, el correo se guarda y su enlace se puede leer en dev/pruebas.
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// "log" (por defecto) | "smtp"
    public string Mode { get; set; } = "log";
    public string From { get; set; } = "no-reply@lejanbrand.com";
    public string FromName { get; set; } = "lejan B2B";

    public SmtpOptions Smtp { get; set; } = new();

    public sealed class SmtpOptions
    {
        // Office 365: Host=smtp.office365.com, Port=587, UseStartTls=true, y User/Password
        // del buzón (con SMTP AUTH habilitado en el tenant o contraseña de aplicación).
        public string Host { get; set; } = "smtp.office365.com";
        public int Port { get; set; } = 587;
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public bool UseStartTls { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 20;
    }
}

// Modo por defecto: no envía, solo deja traza en el log. Quien lo llama guarda además
// el correo completo en SentEmail, así que el enlace de activación se puede leer sin
// buzón real. Es lo que usan las pruebas y el arranque en desarrollo.
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public string Transport => "log";

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[correo:log] Para={To} Asunto=\"{Subject}\"\n{Body}",
            message.To, message.Subject, message.TextBody);
        return Task.FromResult(new EmailResult(true, Transport, null));
    }
}

// Envío real por SMTP (Office 365 y compatibles). Un fallo NO lanza: devuelve
// Ok=false con el mensaje, para que el alta de cliente o el sync no se caigan porque
// el correo no salió; queda registrado y se puede reintentar.
public sealed class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public string Transport => "smtp";

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = options.Smtp;
        try
        {
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.UseStartTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(smtp.User, smtp.Password),
                Timeout = smtp.TimeoutSeconds * 1000
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(options.From, options.FromName),
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(message.To);
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody, null, "text/plain"));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody, null, "text/html"));

            await client.SendMailAsync(mail, ct);
            return new EmailResult(true, Transport, null);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException or FormatException)
        {
            logger.LogError(ex, "Fallo al enviar correo SMTP a {To}", message.To);
            return new EmailResult(false, Transport, ex.Message);
        }
    }
}
