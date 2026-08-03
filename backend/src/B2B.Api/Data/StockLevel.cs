namespace B2B.Api.Data;

// Stock por (producto, ventana de servicio) — contrato 03 §2: la URL identifica
// el producto y la ventana viaja en el body con mayúsculas inconsistentes.
public class StockLevel
{
    public Guid Id { get; set; }
    public required string ProductExternalId { get; set; }
    public required string ServiceWindowId { get; set; }
    public required string ServiceWindowKey { get; set; }
    public decimal Stock { get; set; }
    public string OrderType { get; set; } = "";
    public string EntryDate { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
