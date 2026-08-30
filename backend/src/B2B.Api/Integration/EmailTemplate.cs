using System.Text.RegularExpressions;

namespace B2B.Api.Integration;

// Motor de plantillas de email. Un correo = LAYOUT de marca (compartido, editable) que
// envuelve un CUERPO (por evento, editable). Ambos admiten variables {{clave}}. El layout
// aporta la cabecera/pie de marca y expone {{content}} donde entra el cuerpo.
public static partial class EmailTemplate
{
    // Layout por defecto (marca MITO PROJECTS, rojo #ec3013). HTML "email-safe": estilos
    // en línea, sin dependencias externas. Editable desde Conexiones (IntegrationSettings).
    public const string DefaultLayout = """
        <div style="background:#f3f2f2;padding:24px 12px;font-family:Archivo,Arial,Helvetica,sans-serif;color:#201e1d">
          <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e2dede">
            <div style="background:#ec3013;padding:18px 28px">
              <span style="color:#ffffff;font-weight:800;font-size:20px;letter-spacing:-.5px">MITO PROJECTS&#8482;</span>
            </div>
            <div style="padding:28px 28px 24px;font-size:15px;line-height:1.55">{{content}}</div>
            <div style="padding:14px 28px;border-top:1px solid #eeeaea;color:#8a8785;font-size:12px">
              &#169; {{year}} MITO PROJECTS &#183; Portal B2B
            </div>
          </div>
        </div>
        """;

    // Cuerpo por defecto de un email de NOTIFICACIÓN (genérico; el usuario lo edita por evento).
    public const string DefaultNotificationBody = """
        <p style="margin:0 0 14px;font-size:17px;font-weight:800;letter-spacing:-.2px">{{eventName}}</p>
        <p style="margin:0 0 14px">Se ha registrado el evento <b>{{eventName}}</b> en el portal B2B.</p>
        <p style="margin:0;color:#8a8785;font-size:13px">Referencia: {{ref}}</p>
        """;

    // Cuerpo del email TRANSACCIONAL (activación / restablecer contraseña). La copia
    // (greeting/intro/button/expiry/signature) llega traducida desde ActivationService.
    public const string ActivationBody = """
        <p style="margin:0 0 14px">{{greeting}} <b>{{name}}</b>,</p>
        <p style="margin:0 0 14px">{{intro}}</p>
        <p style="margin:24px 0">
          <a href="{{link}}" style="background:#ec3013;color:#ffffff;text-decoration:none;padding:12px 22px;font-weight:700;display:inline-block">{{button}}</a>
        </p>
        <p style="margin:0 0 6px;font-size:13px;color:#8a8785">{{expiry}}</p>
        <p style="margin:0 0 14px;font-size:12px;color:#a8a4a2;word-break:break-all">{{link}}</p>
        <p style="margin:0;font-size:13px;color:#8a8785">{{signature}}</p>
        """;

    // Cuerpo por defecto según el evento (para "Restaurar por defecto" en la UI de email).
    public static string DefaultBodyFor(string? eventKey) => eventKey switch
    {
        "user.created" or "auth.validation-resent" or "auth.remind-password-requested" => ActivationBody,
        _ => DefaultNotificationBody,
    };

    public static string DefaultSubjectFor(string? eventKey) => eventKey switch
    {
        "order.selection-sent" => "Tu selección de pedido",
        "shoes.purchase_order.updated" => "Orden de compra {{ref}}",
        "agent.registration" => "Alta de cliente",
        _ => "{{eventName}}",
    };

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex VarRx();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"[ \t]*\r?\n[ \t]*")]
    private static partial Regex NlRx();

    // Sustituye {{clave}} por vars[clave] (vacío si no existe). Case-insensitive en la clave.
    public static string Fill(string? template, IReadOnlyDictionary<string, string?>? vars)
    {
        if (string.IsNullOrEmpty(template)) return "";
        var map = vars ?? EmptyVars;
        return VarRx().Replace(template, m =>
        {
            foreach (var kv in map)
                if (string.Equals(kv.Key, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    return kv.Value ?? "";
            return "";
        });
    }

    // Cuerpo (con variables) envuelto en el layout de marca (con variables). Devuelve el
    // HTML final listo para enviar.
    public static string RenderHtml(string? layout, string? bodyHtml, IReadOnlyDictionary<string, string?>? vars)
    {
        var body = Fill(bodyHtml, vars);
        var lay = string.IsNullOrWhiteSpace(layout) ? DefaultLayout : layout!;
        return Fill(lay.Replace("{{content}}", body), vars);
    }

    // Respaldo en texto plano a partir del HTML (para clientes que no pintan HTML).
    public static string ToText(string html)
    {
        var text = TagRx().Replace(html ?? "", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = NlRx().Replace(text, "\n");
        return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
    }

    private static readonly IReadOnlyDictionary<string, string?> EmptyVars =
        new Dictionary<string, string?>();
}
