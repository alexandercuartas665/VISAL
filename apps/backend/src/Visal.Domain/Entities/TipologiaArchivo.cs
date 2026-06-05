using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Tipologia de archivo: concepto que el tenant le da a los documentos que se
/// adjuntan a notas medicas (ej. "Firma del paciente", "Escala", "Examen"...).
/// Configurable desde "Configuracion del Sistema" -> "Tipologia Archivos".
/// Reemplaza al dropdown "Categoria" hardcoded del tab Documentos Externos.
/// </summary>
public class TipologiaArchivo : TenantEntity
{
    /// <summary>Nombre visible (ej. "Firma del paciente").</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Color hexadecimal para identificar visualmente el tipo (ej. "#10B981").</summary>
    public string Color { get; set; } = "#64748b";

    /// <summary>Si esta inactiva no aparece en el dropdown pero se mantiene la
    /// historia de documentos previos que la usaron.</summary>
    public bool Activo { get; set; } = true;
}
