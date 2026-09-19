using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Turno de la agenda PROPIA de un profesional (Ola 2 del modulo de Agendas).
/// Cada profesional tiene su propia agenda = el conjunto de sus turnos. Se crea
/// importando (copiando) una <see cref="PlantillaAgenda"/> y luego se puede
/// editar libremente sin afectar la plantilla origen.
///
/// <see cref="PlantillaOrigenId"/> es solo traza (de que plantilla se importo);
/// no es una dependencia viva: borrar la plantilla no toca la agenda del
/// profesional. No esta ligada a servicios.
///
/// Tenant-scoped. El vinculo real es ProfesionalId.
/// </summary>
public class AgendaProfesionalTurno : TenantEntity
{
    /// <summary>Profesional dueno de este turno.</summary>
    public Guid ProfesionalId { get; set; }

    /// <summary>Plantilla desde la que se importo (traza, opcional).</summary>
    public Guid? PlantillaOrigenId { get; set; }

    /// <summary>Dia de la semana (se persiste como texto: "Monday"...).</summary>
    public DayOfWeek DiaSemana { get; set; }

    /// <summary>Hora de inicio del turno.</summary>
    public TimeOnly HoraInicio { get; set; }

    /// <summary>Hora de fin del turno (debe ser mayor que HoraInicio).</summary>
    public TimeOnly HoraFin { get; set; }

    /// <summary>Tamano del bloque/cupo en minutos. Cupos = (fin - inicio) / intervalo.</summary>
    public int IntervaloMinutos { get; set; } = 30;
}
