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

    /// <summary>
    /// Denormalizado. true cuando existe al menos una HC vinculada a esta
    /// sesion (via <see cref="AsignacionTurnoSesionHc"/>) en estado Cerrada
    /// (Guardado Definitivo). Se recalcula desde HistoriaClinicaService en
    /// Crear/Cerrar/Reabrir/Descartar. HC inactivas o solamente abiertas NO
    /// completan la sesion (una sesion Abierta si bloquea nuevas por otro
    /// camino, pero para efectos de "esta atendida" cuenta solo la Cerrada).
    ///
    /// Rendimiento: se denormaliza para que el filtro Pendiente/Completado
    /// de la parrilla /atencion (~5k filas por tenant) sea un WHERE indexado
    /// y no un JOIN + agregado por cada fila renderizada.
    /// </summary>
    public bool Completado { get; set; }
}
