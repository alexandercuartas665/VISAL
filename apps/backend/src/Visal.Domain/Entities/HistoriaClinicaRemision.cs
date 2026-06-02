using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la pestana "Remisiones" de una Historia Clinica. Cada remision
/// es a una especialidad (procedimiento del CUPS), seleccionada en cascada:
/// primero el Capitulo (descripcion del CUP), luego la especialidad/CUP por
/// codigo+nombre. El campo libre es el motivo.
/// </summary>
public class HistoriaClinicaRemision : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>Snapshot del capitulo CUPS (ej "CapItulo 03 SISTEMA VISUAL").</summary>
    public string Capitulo { get; set; } = null!;

    /// <summary>Snapshot del codigo CUPS (cup.Codigo) al momento de remitir.</summary>
    public string? EspecialidadCodigo { get; set; }

    /// <summary>Snapshot del nombre CUPS (cup.Nombre) al momento de remitir.</summary>
    public string EspecialidadNombre { get; set; } = null!;

    /// <summary>Motivo libre de la remision.</summary>
    public string? Motivo { get; set; }

    public int Orden { get; set; }
}
