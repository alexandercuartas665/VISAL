using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Documento externo adjuntado a una nota medica (imagen, PDF, etc.).
/// El binario vive en wwwroot/uploads/notas/{TenantId}/{guid}{ext}.
/// La base solo guarda metadata + ruta relativa servible.
/// </summary>
public class NotaMedicaDocumento : TenantEntity
{
    public Guid NotaMedicaId { get; set; }
    public NotaMedica? NotaMedica { get; set; }

    public string NombreOriginal { get; set; } = "";

    /// <summary>Ruta relativa servida por wwwroot (ej. /uploads/notas/{tid}/abc.pdf).</summary>
    public string RutaArchivo { get; set; } = "";

    public string? TipoMime { get; set; }

    /// <summary>Tamano en bytes.</summary>
    public long Tamano { get; set; }

    /// <summary>Categoria libre: "Lista de firmas", "Escala", "Formato", etc.</summary>
    public string? Categoria { get; set; }

    /// <summary>Tipo de terapia (snapshot del modulo).</summary>
    public string? TipoTerapia { get; set; }

    public string? Mes { get; set; }

    public string? Anotaciones { get; set; }
}
