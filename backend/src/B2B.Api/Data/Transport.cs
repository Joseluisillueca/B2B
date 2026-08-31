namespace B2B.Api.Data;

// Regla de transporte (portes) configurable. Calcula el coste de transporte de un pedido en
// función de condiciones combinables: cliente concreto, país de la dirección de envío, tipo de
// pedido (reposición/programación), mínimo de unidades y/o mínimo de importe. Editable desde
// /manage → Transporte. Las reglas se evalúan por `Priority` ascendente y gana la PRIMERA que
// casa (todas sus condiciones). El resultado (coste + incoterm) viaja en el JSON del pedido a
// Business Central (`totalTransport` e `incotermId`), sin tocar el conector.
public class TransportRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Priority { get; set; }               // menor = se evalúa antes; 1ª coincidencia gana

    // ── Condiciones (null/vacío = "cualquiera") ──
    public string? ClientExternalId { get; set; }   // aplica solo a este cliente (SystemId de BC)
    public string? CountryIsoId { get; set; }        // país de la dirección de envío: ES, FR, IT, PT…
    public string? OrderType { get; set; }           // "REPLENISHMENT" | "SCHEDULED" (null = cualquiera)
    public int? MinUnits { get; set; }               // se exige un mínimo de unidades en el pedido
    public decimal? MinAmount { get; set; }          // se exige un mínimo de importe (subtotal sin IVA)

    // ── Resultado ──
    public decimal Cost { get; set; }                // coste de transporte (0 = portes gratis)
    public bool PerUnit { get; set; }                // true → el coste es POR UNIDAD (Cost × unidades)
    public string? IncotermId { get; set; }          // código de método/incoterm que se envía a BC (opcional)

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
