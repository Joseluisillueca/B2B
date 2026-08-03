namespace B2B.Api.Data;

// Oferta/tarifa normalizada (contrato 03 §4): una fila por id determinista de BC.
public class Offer
{
    public required string ExternalId { get; set; }
    public string ModelId { get; set; } = "";
    public string? ProductId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientGroupId { get; set; }
    public string PriceType { get; set; } = "PVD";
    public string PriceCode { get; set; } = "EUR";
    public decimal PriceValue { get; set; }
    public decimal MinQuantity { get; set; }
    public decimal? DiscountPercent { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? OrderType { get; set; }
    public int Priority { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
