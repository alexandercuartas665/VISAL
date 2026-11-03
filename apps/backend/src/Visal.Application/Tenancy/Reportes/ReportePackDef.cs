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
/// Formato de presentacion de una columna en el Excel. Se traduce a un formato de celda
/// de ClosedXML al exportar; <see cref="Auto"/> deja que Excel decida (sin formato explicito).
/// </summary>
public enum ReporteColumnaFormato
{
    Auto,
    Texto,
    Entero,
    Decimal,
    Moneda,
    Porcentaje,
    Fecha,
    FechaHora
}

/// <summary>
/// Referencia a un campo dentro de una hoja. Es "campoKey" para un campo de la
/// entidad base, o "relacionKey.campoKey" para un campo traido por un cruce
/// declarado. El generador valida que exista en el modelo semantico. El ORDEN en la
/// lista de columnas de la hoja es el orden de salida; <see cref="Formato"/> fija el
/// formato de la celda en el Excel.
/// </summary>
public sealed record ReporteColumnaDef(
    string CampoRef,
    string? Titulo = null,
    ReporteColumnaFormato Formato = ReporteColumnaFormato.Auto);

/// <summary>
/// Filtro de hoja. Los valores van SIEMPRE como parametros de comando. Si
/// <see cref="ParamClave"/> esta seteada, el VALOR del filtro no es fijo: se pide al
/// ejecutar (parametro global del reporte) y se resuelve antes de generar el SQL. Si al
/// ejecutar ese parametro viene vacio, el filtro se descarta (esa hoja no se filtra por el).
/// </summary>
public sealed record ReporteFiltroDef(
    string CampoRef,
    ReporteFiltroOp Op,
    IReadOnlyList<string>? Valores = null,
    string? ParamClave = null);

/// <summary>
/// Filtro de EXISTENCIA (semi-join / EXISTS) sobre una entidad relacionada: la fila base entra
/// solo si existe al menos un registro en la entidad destino que la referencia. NO multiplica
/// filas (a diferencia de un cruce con LEFT JOIN, que duplicaria la base por cada relacionado).
/// Lo usa la hoja "Base de Pacientes" del CENSO para listar solo pacientes ATENDIDOS (los que
/// tienen Historia Clinica), acotando opcionalmente por la fecha de atencion de la HC via el
/// rango de fechas del censo. El rango se resuelve igual que un filtro (ParamClave -&gt; Valores);
/// si llega vacio, la existencia se mantiene sin acotar por fecha (todo lo atendido).
/// </summary>
/// <param name="TargetEntityKey">Entidad reportable destino del EXISTS (p.ej. "historia").</param>
/// <param name="LocalProperty">Propiedad CLR de la base que empata (p.ej. "Id" del paciente).</param>
/// <param name="TargetProperty">Propiedad FK en el destino (p.ej. "PacienteId").</param>
/// <param name="FechaCampoRef">Campo fecha del destino para acotar por rango (p.ej. "fecha_atencion"); null = sin rango.</param>
/// <param name="ParamClave">Parametro de ejecucion que trae el rango (se resuelve antes de generar).</param>
/// <param name="Valores">Rango resuelto (2 valores) cuando el parametro trae fechas; null = sin acotar por fecha.</param>
public sealed record ReporteExisteDef(
    string TargetEntityKey,
    string LocalProperty,
    string TargetProperty,
    string? FechaCampoRef = null,
    string? ParamClave = null,
    IReadOnlyList<string>? Valores = null);

/// <summary>Tipo de un parametro de ejecucion (define como se pide el valor al descargar).</summary>
public enum ReporteParametroTipo
{
    Texto,
    Numero,
    Fecha,
    FechaHora,
    RangoFecha,
    Lista
}

/// <summary>Fuente de opciones para un parametro tipo lista (para ofrecer un desplegable).</summary>
public enum ReporteParametroFuente
{
    Ninguna,
    Sedes
}

/// <summary>
/// Parametro de ejecucion del reporte (global). Se declara una vez en el pack y se enlaza
/// desde los filtros de las hojas por <see cref="Clave"/>. Al descargar, la UI pide un valor
/// por parametro y lo aplica a TODAS las hojas cuyos filtros lo referencian.
/// </summary>
public sealed record ReporteParametroDef(
    string Clave,
    string Etiqueta,
    ReporteParametroTipo Tipo,
    ReporteParametroFuente Fuente = ReporteParametroFuente.Ninguna);

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
/// Un "cubo" de una matriz de encuesta: el valor crudo de la pregunta (1..5) y la etiqueta
/// con que se muestra en la columna. P.ej. P4 usa (5,"5"); P5 usa (5,"Definitivamente Si").
/// </summary>
public sealed record ReporteMatrizCuboDef(int Valor, string Etiqueta);

/// <summary>
/// Un bloque de preguntas de la matriz de encuesta: un titulo de grupo (cabecera superior,
/// fusionada) y la lista ordenada de cubos. Cada cubo abre dos columnas: conteo y porcentaje.
/// </summary>
public sealed record ReporteMatrizBloqueDef(string Titulo, IReadOnlyList<ReporteMatrizCuboDef> Cubos);

/// <summary>
/// Matriz fiel del "Consolidado Encuesta" (hoja 5 del Censo): una hoja computada que NO sale de
/// un pivote generico. Sobre las filas crudas (mes + pregunta4 + pregunta5, ya filtradas a
/// llamadas efectivas por el filtro de la hoja) arma, por mes, dos bloques de pregunta (conteo +
/// % por cada respuesta) y el total de llamadas efectivas del mes. El % es conteo/total del mes.
/// Los indices <see cref="ColMes"/>/<see cref="ColP4"/>/<see cref="ColP5"/> apuntan a las columnas
/// de la consulta base (en el orden en que se declararon en la hoja).
/// </summary>
public sealed record ReporteMatrizEncuestaDef(
    int ColMes,
    int ColP4,
    int ColP5,
    string TituloEsquina,
    string TituloMes,
    ReporteMatrizBloqueDef BloqueP4,
    ReporteMatrizBloqueDef BloqueP5,
    string TituloTotal);

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
    ReportePivoteDef? Pivote = null,
    ReporteMatrizEncuestaDef? Matriz = null,
    IReadOnlyList<ReporteExisteDef>? Existencias = null)
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

/// <summary>
/// Cabecera de GRUPO (fila superior fusionada) de una hoja: un titulo que abarca
/// <see cref="Span"/> columnas consecutivas. Solo lo usan las hojas con matriz (dos niveles de
/// cabecera); el export la pinta arriba de la fila de nombres de columna.
/// </summary>
public sealed record ReporteGrupoColumna(string Titulo, int Span);

// ---- Resultados ----------------------------------------------------------------

/// <summary>
/// Resultado de ejecutar una hoja: columnas + filas. <see cref="Formatos"/> (opcional) trae,
/// alineado 1:1 con <see cref="Columnas"/>, el formato de celda Excel por columna (null = sin
/// formato); el export lo aplica. Solo se llena en hojas de detalle.
/// </summary>
public sealed record ReporteHojaResultado(
    string Nombre,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<object?>> Filas,
    IReadOnlyList<string?>? Formatos = null,
    IReadOnlyList<ReporteGrupoColumna>? GruposColumna = null)
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
