using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Plantilla de agenda REUTILIZABLE del tenant (Ola 1 del modulo de Agendas).
/// A diferencia del proyecto hermano (donde el turno se define dentro del
/// recurso), aqui la agenda se define PRIMERO como plantilla independiente y
/// luego se IMPORTA (se copia) a cada profesional. No esta ligada a servicios.
///
/// Ej: "Agenda manana", "Agenda jornada completa", "Agenda fin de semana".
/// Cada plantilla agrupa sus turnos por dia de la semana en
/// <see cref="PlantillaAgendaTurno"/>.
///
/// OJO: es DISTINTA del concepto "turnos" ya existente en VISAL (rota de
/// enfermeria: TipoTurno / programacion de turnos). Por eso el prefijo "Agenda".
///
/// Tenant-scoped. Unicidad: (TenantId, Nombre).
/// </summary>
public class PlantillaAgenda : TenantEntity
{
    /// <summary>Nombre visible de la plantilla. Ej. "Agenda manana". varchar(120).</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Descripcion opcional para el operador. varchar(300).</summary>
    public string? Descripcion { get; set; }

    /// <summary>Soft-disable: una plantilla inactiva no aparece para importar
    /// pero se conserva para referencia historica.</summary>
    public bool Activa { get; set; } = true;

    /// <summary>Turnos de la plantilla (uno o varios por dia de la semana).</summary>
    public ICollection<PlantillaAgendaTurno> Turnos { get; set; } = new List<PlantillaAgendaTurno>();
}

/// <summary>
/// Turno recurrente de una <see cref="PlantillaAgenda"/> para un dia de la
/// semana. Una plantilla puede tener varios turnos por dia (manana + tarde).
/// El intervalo define el tamano del bloque/cupo (minutos).
///
/// Tenant-scoped (hereda TenantId para el filtro global; el vinculo real es
/// PlantillaAgendaId).
/// </summary>
public class PlantillaAgendaTurno : TenantEntity
{
    /// <summary>Plantilla a la que pertenece este turno.</summary>
    public Guid PlantillaAgendaId { get; set; }
    public PlantillaAgenda? PlantillaAgenda { get; set; }

    /// <summary>Dia de la semana (se persiste como texto: "Monday"...).</summary>
    public DayOfWeek DiaSemana { get; set; }

    /// <summary>Hora de inicio del turno.</summary>
    public TimeOnly HoraInicio { get; set; }

    /// <summary>Hora de fin del turno (debe ser mayor que HoraInicio).</summary>
    public TimeOnly HoraFin { get; set; }

    /// <summary>Tamano del bloque/cupo en minutos. Cupos = (fin - inicio) / intervalo.</summary>
    public int IntervaloMinutos { get; set; } = 30;
}
