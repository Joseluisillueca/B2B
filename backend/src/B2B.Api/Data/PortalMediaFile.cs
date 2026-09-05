using System.ComponentModel.DataAnnotations;

namespace B2B.Api.Data;

// Medios que sube el CMS (portada, tarjetas, logos, tipografía, favicon). Viven en la base
// de datos y no en el disco del contenedor, que en cualquier PaaS es EFÍMERO: cada
// despliegue lo estrena vacío. Con la primera instancia perdimos toda la portada por eso, y
// la alternativa (un volumen persistente por instancia) topa con el límite de volúmenes
// del proyecto en cuanto hay tres clientes. Aquí sobreviven a todo, viajan con la copia de
// seguridad de la base y no hay nada que montar al crear una instancia nueva.
public class PortalMediaFile
{
    // Nombre único de fichero (con su sufijo aleatorio), que es también su URL:
    // /media/portal/{Name}. Por eso puede cachearse a largo plazo.
    [Key]
    public string Name { get; set; } = default!;

    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Bytes { get; set; } = [];
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
}
