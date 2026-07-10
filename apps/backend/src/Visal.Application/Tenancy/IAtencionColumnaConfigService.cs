namespace Visal.Application.Tenancy;

public sealed record AtencionColumnaConfigDto(
    string ColumnaKey,
    bool Visible,
    string? Alias,
    int? Orden);

public sealed record GuardarAtencionColumnaRequest(
    string ColumnaKey,
    bool Visible,
    string? Alias,
    int? Orden);

/// <summary>
/// Config administrativa (nivel tenant) de las columnas de la tabla MIS SERVICIOS
/// ASIGNADOS en /atencion. El admin decide que columnas se ven, con que nombre y en
/// que orden — la misma vista para todos los profesionales del tenant.
/// </summary>
public interface IAtencionColumnaConfigService
{
    /// <summary>Lista los overrides configurados. Las columnas sin fila usan sus defaults.</summary>
    Task<IReadOnlyList<AtencionColumnaConfigDto>> ListarAsync(CancellationToken ct = default);

    /// <summary>Guarda multiples columnas en un solo commit. Reemplaza (upsert) los overrides.</summary>
    Task GuardarLoteAsync(IReadOnlyList<GuardarAtencionColumnaRequest> items, CancellationToken ct = default);
}
