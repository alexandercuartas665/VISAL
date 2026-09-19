using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Agendas;

/// <summary>Una novedad de un profesional.</summary>
public sealed record NovedadProfesionalDto(
    Guid Id,
    Guid ProfesionalId,
    TipoNovedadProfesional Tipo,
    DateOnly FechaDesde,
    DateOnly FechaHasta,
    TimeOnly? HoraDesde,
    TimeOnly? HoraHasta,
    string? Nota)
{
    /// <summary>La novedad cubre el/los dia(s) completo(s) (sin franja horaria).</summary>
    public bool DiaCompleto => HoraDesde is null && HoraHasta is null;
}

/// <summary>Comando para crear (Id null) o editar (Id != null) una novedad.</summary>
public sealed record GuardarNovedadProfesionalCmd(
    Guid? Id,
    TipoNovedadProfesional Tipo,
    DateOnly FechaDesde,
    DateOnly FechaHasta,
    TimeOnly? HoraDesde,
    TimeOnly? HoraHasta,
    string? Nota);

/// <summary>
/// Novedades por profesional (Ola 4): vacaciones, incapacidad, permiso, otro.
/// Cierran su agenda en un rango de fechas (opcionalmente en una franja horaria).
/// La futura asignacion por agendas las restara de la disponibilidad.
/// </summary>
public interface INovedadProfesionalService
{
    /// <summary>Novedades de un profesional, mas recientes primero.</summary>
    Task<IReadOnlyList<NovedadProfesionalDto>> ListarPorProfesionalAsync(Guid profesionalId, CancellationToken ct = default);

    /// <summary>Crea o edita una novedad. Devuelve el DTO persistido.</summary>
    Task<NovedadProfesionalDto> GuardarAsync(Guid profesionalId, GuardarNovedadProfesionalCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Elimina una novedad. true si existia.</summary>
    Task<bool> EliminarAsync(Guid novedadId, Guid actor, CancellationToken ct = default);
}
