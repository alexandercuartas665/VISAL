namespace Visal.Application.Tenancy.Reportes;

// ---- DTOs del modelo semantico (para poblar el asistente) ----------------------

public sealed record CampoReportableDto(string Key, string Label, string Kind);

public sealed record RelacionReportableDto(
    string Key,
    string Label,
    string TargetKey,
    string TargetLabel,
    IReadOnlyList<CampoReportableDto> Campos);

public sealed record EntidadReportableDto(
    string Key,
    string Label,
    IReadOnlyList<CampoReportableDto> Campos,
    IReadOnlyList<RelacionReportableDto> Relaciones);

// ---- DTOs de plantillas ---------------------------------------------------------

public sealed record ReportePlantillaDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Categoria,
    int Orden,
    bool Activo,
    int Hojas);

public sealed record ReportePlantillaDetalleDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Categoria,
    int Orden,
    bool Activo,
    IReadOnlyList<ReporteHojaDef> Hojas,
    bool MostrarEncabezado = true,
    string? Codigo = null,
    string? Version = null,
    string? Subtitulo = null,
    IReadOnlyList<ReporteParametroDef>? Parametros = null);

public sealed record GuardarPlantillaRequest(
    string Nombre,
    string? Descripcion,
    string? Categoria,
    int Orden,
    bool Activo,
    IReadOnlyList<ReporteHojaDef> Hojas,
    bool MostrarEncabezado = true,
    string? Codigo = null,
    string? Version = null,
    string? Subtitulo = null,
    IReadOnlyList<ReporteParametroDef>? Parametros = null);

/// <summary>Opcion de sede para poblar un parametro de ejecucion tipo lista de sedes.</summary>
public sealed record SedeOpcionDto(Guid Id, string Nombre);

/// <summary>
/// Servicio del constructor de reportes multi-hoja (self-service del tenant). Expone el
/// modelo semantico al asistente, persiste plantillas/hojas (sin SQL), y ejecuta preview /
/// exportacion reusando el motor con aislamiento por tenant.
/// </summary>
public interface IReporteBuilderService
{
    /// <summary>
    /// Entidades reportables (con campos y cruces) que ofrece el asistente, incluidos los
    /// campos dinamicos del tenant fusionados en la entidad paciente.
    /// </summary>
    Task<IReadOnlyList<EntidadReportableDto>> ListarEntidadesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReportePlantillaDto>> ListarAsync(CancellationToken ct = default);
    Task<ReportePlantillaDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Materializa la plantilla CENSO 2026 (Ola 4) para el tenant actual. Idempotente por
    /// nombre: si ya existe una plantilla "Censo 2026" devuelve su id sin duplicar.
    /// </summary>
    Task<Guid> CrearCensoAsync(CancellationToken ct = default);
    Task<Guid> GuardarAsync(Guid? id, GuardarPlantillaRequest req, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ejecuta UNA hoja con un limite pequeno, para la vista previa del asistente.</summary>
    Task<ReporteHojaResultado> PreviewAsync(ReporteHojaDef hoja, int limite = 50, CancellationToken ct = default);

    /// <summary>
    /// Genera el Excel multi-hoja de una plantilla guardada. <paramref name="parametros"/> trae
    /// los valores de los parametros de ejecucion (clave -&gt; valores) elegidos al descargar;
    /// los filtros enlazados a un parametro sin valor se omiten en esa corrida.
    /// </summary>
    Task<byte[]> GenerarExcelAsync(
        Guid id,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parametros = null,
        CancellationToken ct = default);

    /// <summary>Sedes (sucursales) del tenant, para poblar parametros de ejecucion tipo lista de sedes.</summary>
    Task<IReadOnlyList<SedeOpcionDto>> ListarSedesAsync(CancellationToken ct = default);
}
