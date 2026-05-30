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
}
