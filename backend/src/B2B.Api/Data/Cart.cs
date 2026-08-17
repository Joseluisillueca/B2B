namespace B2B.Api.Data;

// Carrito guardado del portal (plan §4, Fase 2). La misma tabla sirve para los
// "Carritos favoritos" de /shopping-carts (IsFavorite) y para el pedido que deja
// TERMINAR PEDIDO a la espera de su envío a Business Central (Status).
public class Cart
{
    public Guid Id { get; set; }

    // Ámbito: el cliente del token manda; UserId identifica a quién lo guardó
    // (columna PROPIETARIO/A del listado). Un usuario sin cliente vinculado
    // —el de integración— solo ve los suyos.
    public string? ClientId { get; set; }
    public Guid UserId { get; set; }

    public string Name { get; set; } = "";
    public string? ServiceWindowId { get; set; }
    public string LinesJson { get; set; } = "[]";

    /// Aparece en /shopping-carts
    public bool IsFavorite { get; set; } = true;

    /// draft (carrito guardado) | pending-bc (pedido terminado, aún sin enviar a BC)
    public string Status { get; set; } = CartStatus.Draft;

    /// Referencia del cliente para el pedido (REF. PEDIDO CLIENTE del checkout)
    public string? Reference { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class CartStatus
{
    public const string Draft = "draft";
    public const string PendingBc = "pending-bc";
}
