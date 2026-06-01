namespace Visal.Application.Tenancy;

/// <summary>Resumen ligero para la grilla del modulo Medicamentos.</summary>
public sealed record MedicamentoDto(
    Guid Id,
    string? Producto,
    string? RegistroSanitario,
    string? PrincipioActivo,
    string? Concentracion,
    string? FormaFarmaceutica,
    string? DescripcionComercial,
    string? EstadoRegistro,
    string? EstadoCum,
    string? Ium);

/// <summary>Detalle completo (todas las 29 columnas del CUM).</summary>
public sealed record MedicamentoDetailDto(
    Guid Id,
    string? Expediente, string? Producto, string? Titular, string? RegistroSanitario,
    DateOnly? FechaExpedicion, DateOnly? FechaVencimiento, string? EstadoRegistro,
    string? ExpedienteCum, string? ConsecutivoCum, string? CantidadCum, string? DescripcionComercial,
    string? EstadoCum, DateOnly? FechaActivo, DateOnly? FechaInactivo, string? MuestraMedica, string? Unidad,
    string? Atc, string? DescripcionAtc, string? ViaAdministracion, string? Concentracion,
    string? PrincipioActivo, string? UnidadMedida, string? Cantidad, string? UnidadReferencia,
    string? FormaFarmaceutica, string? NombreRol, string? TipoRol, string? Modalidad, string? Ium);

/// <summary>Payload de alta/edicion. Si Id es null se crea; si no, se actualiza.</summary>
public sealed record SaveMedicamentoRequest(
    Guid? Id,
    string? Expediente, string? Producto, string? Titular, string? RegistroSanitario,
    DateOnly? FechaExpedicion, DateOnly? FechaVencimiento, string? EstadoRegistro,
    string? ExpedienteCum, string? ConsecutivoCum, string? CantidadCum, string? DescripcionComercial,
    string? EstadoCum, DateOnly? FechaActivo, DateOnly? FechaInactivo, string? MuestraMedica, string? Unidad,
    string? Atc, string? DescripcionAtc, string? ViaAdministracion, string? Concentracion,
    string? PrincipioActivo, string? UnidadMedida, string? Cantidad, string? UnidadReferencia,
    string? FormaFarmaceutica, string? NombreRol, string? TipoRol, string? Modalidad, string? Ium);

/// <summary>
/// Notificacion de avance durante la importacion. La UI la usa para pintar
/// la barra de progreso. Fases: "Validando", "Insertando", "Listo".
/// </summary>
public sealed record MedicamentoImportProgress(string Fase, int Procesados, int Total);

/// <summary>Fila tal como llega del Excel del INVIMA - todas string para tolerar el formato real.</summary>
public sealed record MedicamentoImportRow(
    string? Expediente, string? Producto, string? Titular, string? RegistroSanitario,
    string? FechaExpedicion, string? FechaVencimiento, string? EstadoRegistro,
    string? ExpedienteCum, string? ConsecutivoCum, string? CantidadCum, string? DescripcionComercial,
    string? EstadoCum, string? FechaActivo, string? FechaInactivo, string? MuestraMedica, string? Unidad,
    string? Atc, string? DescripcionAtc, string? ViaAdministracion, string? Concentracion,
    string? PrincipioActivo, string? UnidadMedida, string? Cantidad, string? UnidadReferencia,
    string? FormaFarmaceutica, string? NombreRol, string? TipoRol, string? Modalidad, string? Ium);

public interface IMedicamentoService
{
    /// <summary>Lista con paginacion + busqueda libre sobre producto/principio/ATC/IUM/descripcion.</summary>
    Task<(IReadOnlyList<MedicamentoDto> rows, int total)> SearchAsync(
        string? termino, int skip, int take, CancellationToken ct = default);

    Task<MedicamentoDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<MedicamentoDetailDto?> SaveAsync(SaveMedicamentoRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Importa filas del Excel del CUM en lotes, reportando avance via
    /// <paramref name="progress"/>. Devuelve cuantas se insertaron en total.
    /// </summary>
    Task<int> ImportAsync(
        IReadOnlyList<MedicamentoImportRow> rows,
        Guid actorUserId,
        IProgress<MedicamentoImportProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Borra TODA la BD de medicamentos del tenant (para recargar limpio). Devuelve cuantas se borraron.</summary>
    Task<int> ClearAllAsync(Guid actorUserId, CancellationToken ct = default);
}
