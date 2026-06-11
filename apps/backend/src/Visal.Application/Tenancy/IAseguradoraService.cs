namespace Visal.Application.Tenancy;

public sealed record AseguradoraDto(Guid Id, string Codigo, string Tipo, string Nombre, string? Nit, string? Regimen, int Contratos);

public sealed record AseguradoraDetailDto(
    Guid Id, string Codigo, string Tipo, string Nombre, string? CodigoMovilidad,
    string? Nit, string? Regimen, string? CodInt, string? Descripcion);

public sealed record SaveAseguradoraRequest(
    Guid? Id, string Codigo, string Tipo, string Nombre, string? CodigoMovilidad,
    string? Nit, string? Regimen, string? CodInt, string? Descripcion);

public sealed record ContratoDto(
    Guid Id, Guid AseguradoraId, string CodigoContrato, DateOnly? FechaInicial,
    DateOnly? FechaFinal, string Estado, bool Prorroga);

public sealed record SaveContratoRequest(
    Guid? Id, Guid AseguradoraId, string CodigoContrato, DateOnly? FechaInicial,
    DateOnly? FechaFinal, string Estado, bool Prorroga);

public sealed record ServicioDto(
    Guid Id, Guid ContratoId, string? Sede, string? Historia, string? CodigoServicio,
    string? CodigoInterno, string? Descripcion, decimal? Tarifa, string? Modulo,
    string? Especialidad, string? Modalidad, string? Clasificacion, string? Observaciones);

public sealed record SaveServicioRequest(
    Guid? Id, Guid ContratoId, string? Sede, string? Historia, string? CodigoServicio,
    string? CodigoInterno, string? Descripcion, decimal? Tarifa, string? Modulo,
    string? Especialidad, string? Modalidad, string? Clasificacion, string? Observaciones);

/// <summary>Fila de servicio leida del Excel de carga (Hoja1).</summary>
public sealed record ServicioImportRow(
    string? Contrato, string? Sede, string? Historia, string? CodigoServicio,
    string? CodigoInterno, string? Descripcion, decimal? Tarifa, string? Modulo,
    string? Especialidad, string? Modalidad, string? Clasificacion, string? Observaciones);

/// <summary>Modulo Entidades Aseguradoras: aseguradoras, contratos y servicios. Tenant-scoped.</summary>
public interface IAseguradoraService
{
    Task<IReadOnlyList<AseguradoraDto>> ListAseguradorasAsync(CancellationToken ct = default);
    Task<AseguradoraDetailDto?> GetAseguradoraAsync(Guid id, CancellationToken ct = default);
    Task<AseguradoraDetailDto?> SaveAseguradoraAsync(SaveAseguradoraRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAseguradoraAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<IReadOnlyList<ContratoDto>> ListContratosAsync(Guid aseguradoraId, CancellationToken ct = default);
    Task<ContratoDto?> SaveContratoAsync(SaveContratoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteContratoAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<IReadOnlyList<ServicioDto>> ListServiciosAsync(Guid contratoId, string? filtro, CancellationToken ct = default);
    Task<ServicioDto?> SaveServicioAsync(SaveServicioRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteServicioAsync(Guid id, Guid actor, CancellationToken ct = default);
    Task<int> ImportServiciosAsync(Guid contratoId, IReadOnlyList<ServicioImportRow> rows, Guid actor, CancellationToken ct = default);

    /// <summary>Borra todos los servicios de un contrato. Devuelve cantidad borrada.
    /// Pensado para corregir un import erroneo (ej. cargar de nuevo sin codigo de
    /// historia). Las asignaciones existentes que referencien al servicio quedan
    /// con servicio_contrato_id en NULL (FK ON DELETE SET NULL).</summary>
    Task<int> EliminarServiciosDeContratoAsync(Guid contratoId, Guid actor, CancellationToken ct = default);
}
