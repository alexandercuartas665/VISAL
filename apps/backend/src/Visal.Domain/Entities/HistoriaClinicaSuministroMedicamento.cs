using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Registro de un medicamento SUMINISTRADO al paciente durante la atencion
/// (bitacora de administracion). Se diferencia del modulo "Ordenes de
/// medicamentos" que es la prescripcion: aqui cada fila documenta una toma
/// individual con su hora exacta, la via y el usuario que la administro.
/// El PDF de referencia es "REG MEDICAMENTOS" (una fila por dosis puesta).
/// </summary>
public class HistoriaClinicaSuministroMedicamento : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>Fecha y hora en la que se administro el medicamento.
    /// La captura el usuario (no es CreatedAt) porque puede diferir
    /// del momento del registro en el sistema.</summary>
    public DateTimeOffset FechaHora { get; set; }

    /// <summary>Presentacion / nombre comercial del medicamento (ej. "CLINDAMICINA 600MG").</summary>
    public string Presentacion { get; set; } = null!;

    /// <summary>Dosis administrada (ej. "600MG").</summary>
    public string? Dosis { get; set; }

    /// <summary>Cantidad de unidades administradas (ej. "1 amp").</summary>
    public string? Cantidad { get; set; }

    /// <summary>Via de administracion (ej. "INTRAVENOSA", "ORAL").</summary>
    public string? Via { get; set; }

    /// <summary>Snapshot del usuario que registro la fila. Se congela al crear
    /// para que la impresion sea reproducible aun si el usuario se renombra
    /// o se desactiva. UsuarioCreacionId apunta a PlatformUser vivo.</summary>
    public Guid? UsuarioCreacionId { get; set; }
    public string? UsuarioCreacionNombre { get; set; }

    public int Orden { get; set; }
}
