using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record CatalogoServicioDto(Guid Id, string Codigo, string Nombre, bool Activo);

public sealed record SaveCatalogoServicioRequest(
    Guid? Id, TipoCatalogoServicio Tipo, string Codigo, string Nombre, bool Activo);

public sealed record CatalogoServicioImportRow(string? Codigo, string? Nombre);

public sealed record CatalogoServicioImportProgress(string Fase, int Procesados, int Total);

/// <summary>
/// Catalogo de referencia de servicios de salud. Los 4 tipos (Rx imagenologia,
/// laboratorios, servicios generales, insumos) comparten esta interfaz — cada
/// llamada recibe el tipo como filtro. Independientes entre si: importar o
/// vaciar en uno no toca los otros.
/// </summary>
public interface ICatalogoServicioService
{
    Task<(IReadOnlyList<CatalogoServicioDto> rows, int total)> SearchAsync(
        TipoCatalogoServicio tipo, string? termino, int skip, int take, CancellationToken ct = default);

    Task<CatalogoServicioDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<CatalogoServicioDto?> SaveAsync(SaveCatalogoServicioRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Importa filas (upsert por codigo dentro del tipo). Devuelve cuantas se procesaron.</summary>
    Task<int> ImportAsync(TipoCatalogoServicio tipo,
        IReadOnlyList<CatalogoServicioImportRow> rows, Guid actorUserId,
        IProgress<CatalogoServicioImportProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Borra TODAS las entradas de ese tipo en el tenant. Devuelve cuantas se borraron.</summary>
    Task<int> ClearAllAsync(TipoCatalogoServicio tipo, Guid actorUserId, CancellationToken ct = default);
}
