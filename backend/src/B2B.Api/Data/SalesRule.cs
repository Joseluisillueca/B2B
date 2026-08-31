namespace B2B.Api.Data;

// "Condiciones de venta / promos": regla configurable con una lista de CONDICIONES (se cumplen
// TODAS = AND) y una lista de ACCIONES (qué pasa cuando casan). Replica el modelo del portal de
// referencia. Editable desde /manage → Ventas › Condiciones de venta. Las condiciones y acciones
// son heterogéneas (cada una con su `type` + parámetros), por eso se guardan como JSON.
//
// Las reglas de TRANSPORTE simples (portes) son un subconjunto de esto (acciones "free_shipping"
// e "fixed_transport" + condiciones de país/tipo/unidades/importe/cliente).
public class SalesRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Priority { get; set; }                // "Orden de aplicación": menor = antes

    // Lista de condiciones (AND) y de acciones, como JSON. Cada elemento: { "type": "...", ... }.
    // Se validan/interpretan por tipo en SalesRules (motor). Ver los tipos soportados allí.
    public string ConditionsJson { get; set; } = "[]";
    public string ActionsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
