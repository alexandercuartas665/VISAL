using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Documento externo adjuntado a una nota medica (imagen, PDF, etc.).
/// El binario vive en wwwroot/uploads/notas/{TenantId}/{guid}{ext}.
/// La base solo guarda metadata + ruta relativa servible.
/// </summary>
public class NotaMedicaDocumento : TenantEntity
{
    /// <summary>Nota a la que pertenece el documento. Nullable para permitir
    /// documentos "libres" del paciente que no vienen de una nota especifica
    /// (ej. firma remota capturada desde el WhatsApp panel del paciente sin
    /// haber creado una nota). En esos casos, el documento se lista igual bajo
    /// "Documentos adjuntos" del paciente en /admision usando PacienteId.</summary>
    public Guid? NotaMedicaId { get; set; }
    public NotaMedica? NotaMedica { get; set; }

    /// <summary>Paciente al que pertenece este documento. Se copia desde la nota al adjuntar
    /// para que el modulo de Admision pueda listar todos los documentos del paciente sin
    /// tener que ir nota por nota.</summary>
    public Guid PacienteId { get; set; }

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
