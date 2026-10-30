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
/// Sobre portable de una plantilla para llevarla de un entorno a otro (dev -&gt; prod). Es la
/// MISMA definicion declarativa que persiste el constructor, pero SIN Id ni TenantId: el import
/// la materializa bajo el tenant destino. No incluye SQL; solo referencias al modelo semantico.
/// </summary>
public sealed record ReportePlantillaExportDto(
    string Formato,
    int FormatoVersion,
    DateTime ExportadoUtc,
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

/// <summary>Resultado de importar una plantilla: id destino y si reemplazo una existente (upsert por nombre).</summary>
public sealed record ImportarPlantillaResultado(Guid Id, string Nombre, bool Reemplazada);

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

    /// <summary>
    /// Exporta una plantilla guardada como JSON portable (sin Id/TenantId), listo para importar en
    /// otro entorno. Lanza si la plantilla no existe.
    /// </summary>
    Task<string> ExportarAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Importa una plantilla desde el JSON de <see cref="ExportarAsync"/>. Hace upsert por nombre:
    /// si el tenant ya tiene una plantilla con ese nombre, la reemplaza; si no, la crea. Antes de
    /// guardar valida que todos los campos dinamicos de paciente que referencian las hojas existan
    /// en el tenant destino (son datos por tenant, no viajan en el JSON); si falta alguno, no guarda
    /// y lanza con la lista de los que faltan.
    /// </summary>
    Task<ImportarPlantillaResultado> ImportarAsync(string json, CancellationToken ct = default);
}
