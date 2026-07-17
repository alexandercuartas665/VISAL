using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Sesion atendida de un AsignacionTurno. Cuando el profesional atiende una sesion
/// (presiona Notas en el modulo de Atencion), se crea un registro aqui. La
/// AsignacionTurno queda completada cuando NumSesionesCompletadas == Cantidad.
///
/// Reglas:
/// - SessionNo va 1..Cantidad. No puede saltarse: para registrar la session N
///   debe existir la session N-1.
/// - Tenant-scoped.
/// </summary>
public class AsignacionTurnoSesion : TenantEntity
{
    public Guid AsignacionTurnoId { get; set; }
    public AsignacionTurno? AsignacionTurno { get; set; }

    /// <summary>Numero correlativo dentro del turno (1, 2, 3...).</summary>
    public int SessionNo { get; set; }

    public DateOnly FechaAtencion { get; set; }

    public string? NotaTexto { get; set; }

    /// <summary>Codigo de tipo de turno (M/T/N/D/DN/L o el que el tenant agregue)
    /// que le correspondio a esta sesion segun la programacion aplicada. Null cuando
    /// el turno se creo manualmente sin usar programacion.</summary>
    public string? TipoTurnoCodigo { get; set; }

    /// <summary>Horas trabajadas en la sesion. Cuando viene de una programacion
    /// se toma de la celda del grid (una L=0h, un DN=24h, etc). Null cuando no aplica.</summary>
    public decimal? Horas { get; set; }
}
