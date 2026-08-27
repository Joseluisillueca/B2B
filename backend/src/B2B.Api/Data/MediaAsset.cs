using System.ComponentModel.DataAnnotations;

namespace B2B.Api.Data;

// Imagen de producto alojada por el propio portal (bytes en Postgres). El conector la
// manda en base64 junto a la sincronización de model-images cuando el modo imagen está
// activo (parámetro del Setup del conector); el portal la sirve en /media/models/{id}.jpg.
// Se guarda en la BD a propósito: en Railway el disco es efímero (se borra al
// redesplegar), la base de datos no. Así la foto persiste y viaja con el modelo.
public class MediaAsset
{
    // SystemId del modelo (el id de la URL del PUT de model-images).
    [Key]
    public string ExternalId { get; set; } = default!;

    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime UpdatedAt { get; set; }
}
