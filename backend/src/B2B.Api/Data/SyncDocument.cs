namespace B2B.Api.Data;

// Payload crudo recibido del conector BC vía PUT-upsert. La normalización a
// tablas de dominio se hace después; aquí nunca se pierde lo que BC envió.
public class SyncDocument
{
    public Guid Id { get; set; }
    public required string EntityType { get; set; }
    public required string ExternalId { get; set; }
    public string? ParentId { get; set; }
    public required string Payload { get; set; }
    public DateTime FirstReceivedAt { get; set; }
    public DateTime LastReceivedAt { get; set; }
}
