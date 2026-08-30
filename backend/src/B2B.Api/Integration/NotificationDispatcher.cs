using System.Text.Json.Nodes;
using B2B.Api.Data;
using B2B.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Integration;

// Motor de canales: para un evento, aplica cada canal configurado (Business Central con
// transformer JUST.net, o Email con destinatarios) y registra el resultado en
// "Notificaciones realizadas". Fire-and-forget: nunca lanza al llamante.
public static class NotificationDispatcher
{
    public static async Task DispatchAsync(
        AppDbContext db, BcClient bc, IEmailSender email, IntegrationSettings settings,
        string eventKey, string entityType, string entityId, JsonObject source,
        IReadOnlyDictionary<string, string?>? emailVars = null)
    {
        // Fire-and-forget total: NADA aquí debe romper el flujo de negocio (el pedido/
        // cliente ya está guardado y confirmado antes de llegar aquí).
        try
        {
            var channels = await db.NotificationChannels
                .Where(c => c.EventKey == eventKey && c.Active)
                .OrderBy(c => c.Order).ToListAsync();
            if (channels.Count == 0) return;

            var inputJson = source.ToJsonString();
            foreach (var ch in channels)
            {
                try
                {
                    if (ch.ChannelType == "business-central")
                        await DispatchBcAsync(db, bc, settings, ch, eventKey, entityType, entityId, inputJson);
                    else if (ch.ChannelType == "email")
                        await DispatchEmailAsync(db, email, settings, ch, eventKey, entityType, entityId, emailVars);
                }
                catch (Exception ex)
                {
                    Log(db, eventKey, entityType, entityId, ch.ChannelType, "errors", ex.Message, null);
                }
            }
            await db.SaveChangesAsync();
        }
        catch { /* el despacho nunca debe propagar un fallo al checkout/alta */ }
    }

    private static async Task DispatchBcAsync(
        AppDbContext db, BcClient bc, IntegrationSettings settings, NotificationChannel ch,
        string eventKey, string entityType, string entityId, string inputJson)
    {
        var payload = JsonTransformService.Transform(ch.Transformer ?? "{}", inputJson);
        if (!settings.BcConfigured)
        {
            // Pipeline inerte: se registra el JSON que SE ENVIARÍA, sin llamar a BC.
            Log(db, eventKey, entityType, entityId, "business-central", "simulated",
                $"Conexión BC no configurada · endpoint {ch.Endpoint}", payload);
            return;
        }
        var res = await bc.PostAsync(settings, ch.Endpoint ?? "", payload);
        Log(db, eventKey, entityType, entityId, "business-central",
            res.Ok ? "completed" : "errors", $"{ch.Endpoint} → HTTP {res.Status}" + (res.Ok ? "" : $": {Trim(res.Body)}"), payload);
    }

    private static async Task DispatchEmailAsync(
        AppDbContext db, IEmailSender email, IntegrationSettings settings, NotificationChannel ch,
        string eventKey, string entityType, string entityId, IReadOnlyDictionary<string, string?>? vars)
    {
        var to = ResolveRecipients(ch.ToVars, vars);
        if (to.Count == 0)
        {
            Log(db, eventKey, entityType, entityId, "email", "skipped", "Sin destinatario resuelto", null);
            return;
        }
        var name = IntegrationDefaults.Event(eventKey)?.Name ?? eventKey;

        // Variables de contenido: las del despacho (escapadas, son datos) + utilitarias del evento.
        var content = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (vars is not null)
            foreach (var kv in vars) content[kv.Key] = System.Net.WebUtility.HtmlEncode(kv.Value ?? "");
        content["eventName"] = System.Net.WebUtility.HtmlEncode(name);
        content["ref"] = System.Net.WebUtility.HtmlEncode(entityId);
        content["year"] = DateTime.UtcNow.Year.ToString();

        // Asunto y cuerpo editables por canal (o los por defecto), dentro del layout de marca.
        var subjTpl = string.IsNullOrWhiteSpace(ch.Subject) ? EmailTemplate.DefaultSubjectFor(eventKey) : ch.Subject;
        var bodyTpl = string.IsNullOrWhiteSpace(ch.BodyHtml) ? EmailTemplate.DefaultBodyFor(eventKey) : ch.BodyHtml;
        var subject = System.Net.WebUtility.HtmlDecode(EmailTemplate.Fill(subjTpl, content));
        var html = EmailTemplate.RenderHtml(settings.EmailLayoutHtml, bodyTpl, content);
        var text = EmailTemplate.ToText(html);

        var cc = ResolveRecipients(ch.CcVars, vars);
        var bcc = ResolveRecipients(ch.BccVars, vars);
        // Envío por el transporte configurado (en modo "log" solo se registra, no sale correo).
        var res = await email.SendAsync(new EmailMessage(string.Join(",", to), subject, html, text));
        var extra = (cc.Count > 0 ? " · CC: " + string.Join(", ", cc) : "") + (bcc.Count > 0 ? " · BCC: " + string.Join(", ", bcc) : "");
        Log(db, eventKey, entityType, entityId, "email", res.Ok ? "completed" : "errors", "To: " + string.Join(", ", to) + extra, null);
    }

    private static List<string> ResolveRecipients(string? spec, IReadOnlyDictionary<string, string?>? vars)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(spec)) return result;
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('{') && raw.EndsWith('}'))
            {
                var key = raw[1..^1];
                if (vars is not null && vars.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                    result.Add(v!);
            }
            else if (raw.Contains('@')) result.Add(raw);
        }
        return result;
    }

    private static void Log(AppDbContext db, string ev, string type, string id, string channel, string status, string? detail, string? payload) =>
        db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(), EventKey = ev, EntityType = type, EntityId = id,
            ChannelType = channel, Status = status, Detail = detail, PayloadJson = payload, CreatedAt = DateTime.UtcNow,
        });

    private static string Trim(string s) => s.Length > 400 ? s[..400] : s;
}
