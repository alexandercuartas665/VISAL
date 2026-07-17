using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Turno coordinado de una Asignacion: vincula una asignacion con un profesional
/// (especialista) y la cantidad de turnos / horas pactadas. Equivale a la tabla
/// legacy VISAL_ASIGNACIONES_R del modulo Visal.
///
/// Reglas:
/// - Una Asignacion puede tener N AsignacionTurno (varios profesionales / varios turnos).
/// - La suma de Cantidad de todos los turnos de una Asignacion debe ser &lt;= Asignacion.Cantidad.
/// - Cuando la suma = Asignacion.Cantidad, la Asignacion pasa de Pendiente a Asignado.
/// - Tenant-scoped.
/// </summary>
public class AsignacionTurno : TenantEntity
{
    public Guid AsignacionId { get; set; }
    public Asignacion? Asignacion { get; set; }

    public Guid ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }

    /// <summary>Cantidad de turnos asignados al especialista para esta asignacion.</summary>
    public int Cantidad { get; set; }

    /// <summary>Horas por cada turno (puede ser fraccionario: 1.5h, 2h, ...). Opcional.</summary>
    public decimal? HorasPorTurno { get; set; }

    /// <summary>Fecha de inicio de la atencion (cuando se agendan los turnos).</summary>
    public DateOnly? FechaInicio { get; set; }

    /// <summary>Mes en que se asigna (1..12). Equivale a la columna mes_asignar del legacy.</summary>
    public short? MesAsignar { get; set; }

    /// <summary>Tarifa pactada para este turno. Se pre-llena con la del ServicioContrato
    /// al momento de coordinarlo, pero el coordinador puede ajustarla manualmente
    /// (por descuento, tarifa especial, etc.). Persiste el valor final.</summary>
    public decimal? Tarifa { get; set; }

    /// <summary>Programacion de turnos (TurnoProgramacion) desde la que se genero
    /// este turno. Null cuando se creo manualmente en el modo clasico de Coordinacion.
    /// Se usa para rastrear que rotacion originaron las sesiones y poder mostrarlo
    /// en reportes / auditoria.</summary>
    public Guid? TurnoProgramacionId { get; set; }
    public TurnoProgramacion? TurnoProgramacion { get; set; }

    /// <summary>Nombre de la fila del grid de la programacion que cubre este profesional.
    /// Ej: "Turno 1", "Turno 2". Solo relevante cuando TurnoProgramacionId != null.</summary>
    public string? TurnoRowNombre { get; set; }
}
