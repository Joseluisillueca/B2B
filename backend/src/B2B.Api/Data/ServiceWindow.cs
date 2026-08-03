namespace B2B.Api.Data;

// Ventana de servicio (contrato 03 §1). Id normalizado a minúsculas: la URL
// llega sin lowercase y el body sí (hallazgo §7.2).
public class ServiceWindow
{
    public required string ExternalId { get; set; }
    public string Name { get; set; } = "";
    public string OrderType { get; set; } = "";
    public string FromDate { get; set; } = "";
    public string ToDate { get; set; } = "";
    public string LimitDate { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
