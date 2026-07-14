using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Plantilla mensual de rotacion (RRHH) para el personal clinico. Cada
/// TurnoProgramacion define una grilla de N turnos del dia x dias del mes
/// donde cada celda tiene un tipo (M, T, N, D, DN, L) y horas. La grilla
/// se persiste como JSON en <see cref="GridDataJson"/> — mismo formato
/// que el legacy vis_admturnos para poder importar si aparece BD real.
///
/// Tenant-scoped (regla del proyecto). Puede aplicar a una sede concreta
/// o a todo el tenant (SucursalId = null). Puede opcionalmente filtrarse
/// por TipoServicio (ej. Rotacion Enfermeria de Enero) — null = todos.
///
/// Unicidad: no puede haber dos programaciones con el mismo Nombre para
/// el mismo (Tenant, Sucursal, Anio, Mes).
/// </summary>
public class TurnoProgramacion : TenantEntity
{
    /// <summary>Sede donde aplica. null = global del tenant.</summary>
    public Guid? SucursalId { get; set; }

    /// <summary>Tipo de servicio al que aplica (CONSULTA/TERAPIA/EQUIPOS/ENFERMERIA).
    /// null = aplica a todos los tipos. Filtra a que profesionales se puede asignar
    /// cuando se implemente el modulo Asignacion (fase posterior).</summary>
    public Guid? TipoServicioId { get; set; }

    /// <summary>Nombre corto de la rotacion. Ej. "Rotacion A", "Turno Nocturno Cali".
    /// El mes/anio NO va en el nombre — se lee de los campos <see cref="Mes"/> y
    /// <see cref="Anio"/>. varchar(120).</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Mes al que aplica (1..12).</summary>
    public int Mes { get; set; }

    /// <summary>Anio al que aplica. Se agrego respecto al legacy porque los fines
    /// de semana caen distinto cada anio y los usuarios quieren "Rotacion A - Enero 2026"
    /// separada de "Rotacion A - Enero 2027".</summary>
    public int Anio { get; set; }

    /// <summary>Descripcion libre opcional. varchar(500).</summary>
    public string? Descripcion { get; set; }

    /// <summary>Grilla serializada como JSON. Formato:
    /// {"turnos":["Turno 1","Turno 2"],"dias":{"Turno 1":{"1":{"tipo":"M","horas":8}}}}
    /// Persistido como jsonb.</summary>
    public string GridDataJson { get; set; } = "{\"turnos\":[\"Turno 1\"],\"dias\":{\"Turno 1\":{}}}";

    /// <summary>Soft-disable. Si tiene profesionales asignados no se puede
    /// borrar fisicamente — se desactiva.</summary>
    public bool Activa { get; set; } = true;
}
