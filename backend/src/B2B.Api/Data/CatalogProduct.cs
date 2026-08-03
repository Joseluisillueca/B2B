namespace B2B.Api.Data;

// Proyección de variantes y case packs (contrato 02 §4-5, mismo endpoint PUT)
public class CatalogProduct
{
    public required string ExternalId { get; set; }
    public string ModelExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
    public string Sku { get; set; } = "";
    public string Ean { get; set; } = "";
    public string? Size { get; set; }
    public string TaxId { get; set; } = "";
    public string AttributesJson { get; set; } = "{}";
    public bool IsCasePack { get; set; }
    public string? BundleJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}
