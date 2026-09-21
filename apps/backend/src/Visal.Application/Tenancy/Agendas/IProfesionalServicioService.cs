namespace Visal.Application.Tenancy.Agendas;

/// <summary>Un servicio que presta un profesional.</summary>
public sealed record ProfesionalServicioDto(Guid Id, string Codigo, string Nombre);

/// <summary>Resultado de busqueda en el catalogo maestro (para agregar).</summary>
public sealed record CatalogoServicioBusquedaDto(Guid Id, string Codigo, string Nombre, string Tipo);

/// <summary>
/// Servicios que presta cada profesional (vinculo explicito contra el catalogo
/// maestro). Alimenta el tab "Servicios" de Agendas de profesionales y el filtro
/// del modulo de asignacion por agendas.
/// </summary>
public interface IProfesionalServicioService
{
    /// <summary>Servicios del profesional, por nombre.</summary>
    Task<IReadOnlyList<ProfesionalServicioDto>> ListarPorProfesionalAsync(Guid profesionalId, CancellationToken ct = default);

    /// <summary>Busca en el catalogo maestro (los 4 tipos) por codigo o nombre, para el
    /// buscador del tab Servicios. Excluye los que el profesional ya tiene.</summary>
    Task<IReadOnlyList<CatalogoServicioBusquedaDto>> BuscarCatalogoAsync(Guid profesionalId, string? termino, int take = 20, CancellationToken ct = default);

    /// <summary>Agrega un servicio del catalogo al profesional (por Id del catalogo).
    /// Idempotente por (profesional, codigo). Devuelve el servicio agregado.</summary>
    Task<ProfesionalServicioDto> AgregarAsync(Guid profesionalId, Guid catalogoServicioReferenciaId, Guid actor, CancellationToken ct = default);

    /// <summary>Quita un servicio del profesional. true si existia.</summary>
    Task<bool> QuitarAsync(Guid profesionalServicioId, Guid actor, CancellationToken ct = default);
}
