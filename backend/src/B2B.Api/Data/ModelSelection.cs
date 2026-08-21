namespace B2B.Api.Data;

// Pedido de selección de modelos (Fase 3, portal real "Pedidos de selección"). El
// agente nombra una selección, elige modelos del catálogo y clientes de su cartera,
// y les manda por correo los modelos seleccionados para que hagan su pedido de
// temporada. No viene de BC: es una herramienta comercial que vive en local.
public class ModelSelection
{
    public Guid Id { get; set; }

    /// Comercial dueño de la selección (AgentExternalId)
    public string AgentExternalId { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public string Name { get; set; } = "";

    /// ExternalIds de los modelos del catálogo elegidos, y de los clientes destino.
    /// Se guardan como JSON para no crear tablas de unión de una herramienta local.
    public string ModelIdsJson { get; set; } = "[]";
    public string ClientIdsJson { get; set; } = "[]";

    /// "draft" (guardada sin enviar) | "sent" (correo enviado a los clientes)
    public string Status { get; set; } = "draft";
    public DateTime? SentAt { get; set; }
}
