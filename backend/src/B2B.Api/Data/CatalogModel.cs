namespace B2B.Api.Data;

// Proyección consultable del payload de modelo del conector (contrato 02 §2)
public class CatalogModel
{
    public required string ExternalId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Active { get; set; }
    public string ExternalReference { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public string NameTranslationsJson { get; set; } = "{}";
    public string AttributesJson { get; set; } = "{}";
    public string ProductSegmentsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}
