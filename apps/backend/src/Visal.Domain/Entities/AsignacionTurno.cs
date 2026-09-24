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

    /// <summary>Hora de inicio del slot cuando el turno se agenda desde el modulo de
    /// asignacion por agendas (cita en un dia/hora concreta). Null en la coordinacion
    /// clasica, que trabaja a granularidad de dia. Se persiste como time sin zona.</summary>
    public TimeOnly? HoraInicio { get; set; }

    /// <summary>Momento en que Recepcion marco la llegada del paciente para esta cita.
    /// Null = no ha llegado. Cuando esta seteado, Atencion resalta la fila en verde
    /// para indicar que el paciente puede ser atendido.</summary>
    public DateTimeOffset? LlegoEn { get; set; }

    /// <summary>True cuando la llegada se marco como TARDE (fuera de la hora de la cita).
    /// Solo aplica cuando <see cref="LlegoEn"/> esta seteado; se limpia al quitar la
    /// llegada o reprogramar. Permite a Recepcion distinguir llegadas a tiempo de las
    /// tardias (que suelen implicar mover la cita a otra hora).</summary>
    public bool LlegadaTarde { get; set; }

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

    // ---------------- Trazabilidad de paquete (denormalizado desde Asignacion) ----------------
    // Copiados en <c>AsignacionService.AsignarServicioAsync</c> desde la Asignacion padre
    // para permitir reportes GROUP BY paquete_instancia_id sin JOIN.

    /// <summary>Guid heredado de <see cref="Asignacion.PaqueteInstanciaId"/>. Todas las
    /// filas de asignacion_turnos que provienen del mismo lote de paquete comparten este id.</summary>
    public Guid? PaqueteInstanciaId { get; set; }

    /// <summary>Codigo del paquete (snapshot) heredado de la Asignacion padre.</summary>
    public string? PaqueteCodigo { get; set; }

    /// <summary>Valor pactado del paquete. SOLO una fila del mismo PaqueteInstanciaId
    /// lo lleva (la que corresponde a la Asignacion que fue "primera con Cantidad>0"
    /// al aplicar el paquete). El resto queda null.</summary>
    public decimal? PaqueteValorPactado { get; set; }
}
