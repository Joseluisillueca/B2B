namespace B2B.Api.Data;

// Cita / parte de visita del agente (Fase 3, calendario). NO viene de BC: es una
// agenda propia del portal para que el comercial planifique visitas a sus clientes.
// Vive en local; el maestro de clientes sigue en BC (aquí solo se referencia).
public class AgentAppointment
{
    public Guid Id { get; set; }

    /// Comercial dueño de la cita (AgentExternalId). La agenda es suya y de nadie más
    public string AgentExternalId { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    /// Cliente al que se visita (opcional): SystemId + nombre desnormalizado
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }

    public string Title { get; set; } = "";
    public string? Notes { get; set; }

    /// "cita" (visita programada) | "parte" (parte de visita ya realizada)
    public string Kind { get; set; } = AppointmentKind.Cita;

    /// Momento de inicio y duración en minutos (una cita de agenda, no un rango libre)
    public DateTime Start { get; set; }
    public int DurationMinutes { get; set; } = 60;

    /// "pending" | "done" | "canceled"
    public string Status { get; set; } = "pending";
}

public static class AppointmentKind
{
    public const string Cita = "cita";
    public const string Parte = "parte";
    public static readonly string[] All = [Cita, Parte];
}
