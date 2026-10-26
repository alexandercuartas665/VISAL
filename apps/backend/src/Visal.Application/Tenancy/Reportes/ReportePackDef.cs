namespace Visal.Application.Tenancy.Reportes;

/// <summary>Modo de una hoja: filas crudas (detalle) o agregacion (group by + funciones).</summary>
public enum ReporteHojaModo
{
    Detalle,
    Agregado
}

/// <summary>Operador de un filtro de hoja.</summary>
public enum ReporteFiltroOp
{
    Eq,
    Neq,
    In,
    Contains,
    Gte,
    Lte,
    Between,
    IsNull,
    NotNull
}

/// <summary>Funcion de agregacion para hojas en modo Agregado.</summary>
public enum ReporteAgregadoFunc
{
    Count,
    CountDistinct,
    Sum,
    Avg,
    Min,
    Max,

    /// <summary>
    /// Indice: porcentaje que representa cada grupo sobre el gran total de la hoja
    /// (participacion). Sin campo = share por conteo de filas; con campo numerico =
    /// share por suma. Se calcula con una funcion de ventana sobre el group-by.
    /// </summary>
    PctTotal
}

/// <summary>
/// Referencia a un campo dentro de una hoja. Es "campoKey" para un campo de la
/// entidad base, o "relacionKey.campoKey" para un campo traido por un cruce
/// declarado. El generador valida que exista en el modelo semantico.
/// </summary>
public sealed record ReporteColumnaDef(string CampoRef, string? Titulo = null);

/// <summary>Filtro de hoja. Los valores van SIEMPRE como parametros de comando.</summary>
public sealed record ReporteFiltroDef(
    string CampoRef,
    ReporteFiltroOp Op,
    IReadOnlyList<string>? Valores = null);

/// <summary>Orden de salida de una hoja.</summary>
public sealed record ReporteOrdenDef(string CampoRef, bool Desc = false);

/// <summary>Agregado (en modo Agregado): funcion sobre un campo, con titulo opcional.</summary>
public sealed record ReporteAgregadoDef(
    ReporteAgregadoFunc Func,
    string? CampoRef = null,
    string? Titulo = null);

/// <summary>
/// Pivote (Ola 3b) de una hoja Agregado. Es un reshape del resultado "largo" (group-by de
/// DOS campos + un agregado) a una matriz: <see cref="FilaCampoRef"/> en las filas,
/// <see cref="ColumnaCampoRef"/> abierto en columnas, y el PRIMER agregado en las celdas.
/// Caso canonico: Consolidado Encuesta (fila = pregunta, columna = mes, celda = conteo).
/// El group-by de la hoja debe ser exactamente esos dos campos.
/// </summary>
public sealed record ReportePivoteDef(string FilaCampoRef, string ColumnaCampoRef);

/// <summary>
/// Definicion de UNA hoja del reporte. En modo Detalle se usan <see cref="Columnas"/>;
/// en modo Agregado se usan <see cref="GroupBy"/> + <see cref="Agregados"/>.
/// </summary>
public sealed record ReporteHojaDef(
    string Nombre,
    string EntidadKey,
    ReporteHojaModo Modo,
    IReadOnlyList<ReporteColumnaDef> Columnas,
    IReadOnlyList<ReporteFiltroDef> Filtros,
    IReadOnlyList<ReporteColumnaDef> GroupBy,
    IReadOnlyList<ReporteAgregadoDef> Agregados,
    IReadOnlyList<ReporteOrdenDef> Orden,
    int? LimiteFilas = null,
    ReportePivoteDef? Pivote = null)
{
    public static ReporteHojaDef Detalle(
        string nombre,
        string entidadKey,
        IReadOnlyList<ReporteColumnaDef> columnas,
        IReadOnlyList<ReporteFiltroDef>? filtros = null,
        IReadOnlyList<ReporteOrdenDef>? orden = null,
        int? limite = null) =>
        new(nombre, entidadKey, ReporteHojaModo.Detalle, columnas, filtros ?? [], [], [], orden ?? [], limite);

    public static ReporteHojaDef Agregado(
        string nombre,
        string entidadKey,
        IReadOnlyList<ReporteColumnaDef> groupBy,
        IReadOnlyList<ReporteAgregadoDef> agregados,
        IReadOnlyList<ReporteFiltroDef>? filtros = null,
        IReadOnlyList<ReporteOrdenDef>? orden = null,
        int? limite = null,
        ReportePivoteDef? pivote = null) =>
        new(nombre, entidadKey, ReporteHojaModo.Agregado, [], filtros ?? [], groupBy, agregados, orden ?? [], limite, pivote);
}

/// <summary>Un reporte = N hojas.</summary>
public sealed record ReportePackDef(string Nombre, IReadOnlyList<ReporteHojaDef> Hojas);

// ---- Resultados ----------------------------------------------------------------

/// <summary>Resultado de ejecutar una hoja: columnas + filas.</summary>
public sealed record ReporteHojaResultado(
    string Nombre,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<object?>> Filas)
{
    public int TotalFilas => Filas.Count;
}

/// <summary>
/// Cabecera institucional (Ola 3) que el Excel pinta arriba de cada hoja. La agencia y la
/// fecha salen del tenant / reloj al generar; codigo, version y subtitulo salen de la plantilla.
/// </summary>
public sealed record ReporteEncabezado(
    string Agencia,
    string Titulo,
    string? Subtitulo,
    string? Codigo,
    string? Version,
    DateTime FechaGeneracion);

/// <summary>Resultado de ejecutar un pack completo (todas sus hojas).</summary>
public sealed record ReportePackResultado(
    string Nombre,
    IReadOnlyList<ReporteHojaResultado> Hojas,
    ReporteEncabezado? Encabezado = null);
