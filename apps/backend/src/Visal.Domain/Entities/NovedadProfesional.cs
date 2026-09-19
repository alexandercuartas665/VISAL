using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Novedad de un profesional que cierra (total o parcialmente) su agenda en un
/// rango de fechas (Ola 4 del modulo de Agendas): vacaciones, incapacidad,
/// permiso, otro. Equivalente por-profesional del dia inactivo de sede; la
/// futura asignacion por agendas la restara de la disponibilidad del profesional.
///
/// Si <see cref="HoraDesde"/>/<see cref="HoraHasta"/> son null, cubre el/los
/// dia(s) completo(s). El rango de fechas es inclusivo [FechaDesde, FechaHasta].
///
/// Tenant-scoped. El vinculo real es ProfesionalId.
/// </summary>
public class NovedadProfesional : TenantEntity
{
    /// <summary>Profesional al que aplica la novedad.</summary>
    public Guid ProfesionalId { get; set; }

    /// <summary>Tipo de novedad (se persiste como texto).</summary>
    public TipoNovedadProfesional Tipo { get; set; } = TipoNovedadProfesional.Vacaciones;

    /// <summary>Primer dia de la novedad (inclusive).</summary>
    public DateOnly FechaDesde { get; set; }

    /// <summary>Ultimo dia de la novedad (inclusive).</summary>
    public DateOnly FechaHasta { get; set; }

    /// <summary>Hora de inicio (null = dia completo).</summary>
    public TimeOnly? HoraDesde { get; set; }

    /// <summary>Hora de fin (null = dia completo).</summary>
    public TimeOnly? HoraHasta { get; set; }

    /// <summary>Nota/motivo opcional. varchar(300).</summary>
    public string? Nota { get; set; }
}
