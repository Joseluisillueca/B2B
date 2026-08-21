namespace B2B.Api.Data;

// Enlace de activación / recuperación de contraseña. Los usuarios del portal se
// provisionan SIN contraseña (los crea el sync de BC o un agente al dar de alta un
// cliente); este token es el único camino para que fijen la suya la primera vez.
//
// Nunca se guarda el token en claro: se almacena el SHA-256 en hex. El enlace que
// viaja por correo lleva el token crudo; al canjearlo se vuelve a hashear y se busca.
// Así, aunque alguien lea la base de datos, no puede reconstruir enlaces válidos.
public class ActivationToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// SHA-256 en hexadecimal del token crudo (nunca el token en claro)
    public string TokenHash { get; set; } = "";

    /// "activation" (fijar contraseña por primera vez) | "reset" (recuperarla)
    public string Purpose { get; set; } = ActivationPurpose.Activation;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// Se sella al canjearlo: un token es de un solo uso
    public DateTime? UsedAt { get; set; }
}

public static class ActivationPurpose
{
    public const string Activation = "activation";
    public const string Reset = "reset";

    public static readonly string[] All = [Activation, Reset];
}

// Copia de cada correo que el portal manda. En modo "log" (dev/pruebas) es la ÚNICA
// forma de leer el enlace de activación sin un buzón real; en modo SMTP queda como
// registro de auditoría de qué se envió y a quién.
public class SentEmail
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }

    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";

    /// "log" | "smtp" — cómo se despachó (o se intentó)
    public string Transport { get; set; } = "log";

    /// null si se envió bien; el mensaje de error si el SMTP falló
    public string? Error { get; set; }
}
