namespace B2B.Api.Data;

// Corazón de la fila del catálogo (17-catalog-catalog.png). Es del usuario, no del
// cliente: dos personas de la misma tienda pueden marcar modelos distintos.
public class PortalFavorite
{
    public Guid UserId { get; set; }
    public required string ModelId { get; set; }
    public DateTime CreatedAt { get; set; }
}
