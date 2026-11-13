namespace Visal.Application.Tenancy;

public sealed record AseguradoraDto(Guid Id, string Codigo, string Tipo, string Nombre, string? Nit, string? Regimen, int Contratos);

public sealed record AseguradoraDetailDto(
    Guid Id, string Codigo, string Tipo, string Nombre, string? CodigoMovilidad,
    string? Nit, string? Regimen, string? CodInt, string? Descripcion,
    string? CorreoFacturacion);

public sealed record SaveAseguradoraRequest(
    Guid? Id, string Codigo, string Tipo, string Nombre, string? CodigoMovilidad,
    string? Nit, string? Regimen, string? CodInt, string? Descripcion,
    string? CorreoFacturacion);

public sealed record ContratoDto(
    Guid Id, Guid AseguradoraId, string CodigoContrato, DateOnly? FechaInicial,
    DateOnly? FechaFinal, string Estado, bool Prorroga, bool RequierePdfAutorizacion,
    string? Cucon, Visal.Domain.Entities.TipoContrato? TipoContrato);

public sealed record SaveContratoRequest(
    Guid? Id, Guid AseguradoraId, string CodigoContrato, DateOnly? FechaInicial,
    DateOnly? FechaFinal, string Estado, bool Prorroga, bool RequierePdfAutorizacion,
    string? Cucon, Visal.Domain.Entities.TipoContrato? TipoContrato);

public sealed record ServicioDto(
    Guid Id, Guid ContratoId, string? Sede, string? Historia,
    Guid? PaqueteId, string? PaqueteCodigo,
    string? CodigoServicio,
    string? CodigoInterno, string? Descripcion, decimal? Tarifa, string? Modulo,
    string? Especialidad, string? Modalidad, string? Clasificacion, string? Observaciones,
    // RIPS Res 2275 + ValorTotal (Fase 4 Facturacion).
    string? Finalidad, string? CausaExterna, string? ModalidadAtencion,
    string? ViaIngreso, string? GrupoServicios, string? Servicios, decimal? ValorTotal,
    // Campos comerciales bulk-editables (spec BulkUpdate 2026-07-23).
    string? ModalidadFacturacion = null, string? GrupoServicioFacturacion = null,
    string? ServicioFacturacion = null,
    // COD OTRO SERVICIO (Res 202/2021, alineacion Excel EPS ASMET 2026-08-01).
    string? CodOtroServicio = null,
    // TIPO ARCHIVO RIPS override por servicio (AC/AP/AT/AM). Si null, cae al catalogo.
    string? TipoArchivoRips = null);

public sealed record SaveServicioRequest(
    Guid? Id, Guid ContratoId, string? Sede, string? Historia,
    Guid? PaqueteId,
    string? CodigoServicio,
    string? CodigoInterno, string? Descripcion, decimal? Tarifa, string? Modulo,
    string? Especialidad, string? Modalidad, string? Clasificacion, string? Observaciones,
    string? Finalidad = null, string? CausaExterna = null, string? ModalidadAtencion = null,
    string? ViaIngreso = null, string? GrupoServicios = null, string? Servicios = null,
    decimal? ValorTotal = null,
    string? ModalidadFacturacion = null, string? GrupoServicioFacturacion = null,
    string? ServicioFacturacion = null,
    string? CodOtroServicio = null,
    string? TipoArchivoRips = null);

/// <summary>Fila de servicio leida del Excel de carga (Hoja1). El campo PaqueteCodigo
/// es opcional; si viene y matchea un Paquete existente por codigo, se enlaza.
/// Alineacion 2026-08-01 con Excel EPS ASMET: los 3 codigos de facturacion
/// (ModalidadFacturacion, GrupoServicioFacturacion, ServicioFacturacion) mas
/// Finalidad y CodOtroServicio se leen del Excel y se persisten al servicio.</summary>
public sealed record ServicioImportRow(
    string? Contrato, string? Sede, string? Historia,
    string? PaqueteCodigo,
    string? CodigoServicio,
    string? CodigoInterno, string? Descripcion, decimal? Tarifa, string? Modulo,
    string? Especialidad, string? Modalidad, string? Clasificacion, string? Observaciones,
    string? Finalidad = null, string? CodOtroServicio = null,
    string? ModalidadFacturacion = null, string? GrupoServicioFacturacion = null,
    string? ServicioFacturacion = null,
    string? TipoArchivoRips = null);

/// <summary>Resultado del import Excel de servicios (TS6).</summary>
/// <param name="Importados">Filas persistidas.</param>
/// <param name="ModulosDesconocidos">Valores unicos de MODULO que no matchean
/// (tras normalizar plural/singular) contra el catalogo tipos_servicio del
/// tenant. Vacio = todos los modulos son validos. La UI puede mostrar aviso.</param>
/// <param name="PaquetesInexistentes">Codigos de PAQUETE presentes en el Excel
/// que no existen en el catalogo de paquetes del tenant. Las filas con esos
/// codigos NO se importan (se saltan) y se reportan aca para que el usuario
/// cree los paquetes primero o corrija el codigo en el Excel.</param>
/// <param name="FilasSaltadasPaqueteInexistente">Cantidad de filas del Excel
/// que se saltaron por venir con un PAQUETE que no existe.</param>
public sealed record ServiciosImportResult(
    int Importados,
    IReadOnlyList<string> ModulosDesconocidos,
    IReadOnlyList<string>? PaquetesInexistentes = null,
    int FilasSaltadasPaqueteInexistente = 0);

/// <summary>Fila del Excel de aseguradoras. Todas las columnas salvo CODIGO / TIPO / NOMBRE
/// son opcionales. El import hace MERGE por CODIGO (case-insensitive): si existe una
/// aseguradora con ese codigo, se actualiza; si no, se crea.</summary>
public sealed record AseguradoraImportRow(
    string? Codigo, string? Tipo, string? Nombre,
    string? Nit = null, string? Regimen = null, string? CodigoMovilidad = null,
    string? CodInt = null, string? Descripcion = null, string? CorreoFacturacion = null);

/// <summary>Resultado del import Excel de aseguradoras.</summary>
/// <param name="Creadas">Filas nuevas insertadas.</param>
/// <param name="Actualizadas">Filas existentes cuyo CODIGO empato y se sobreescribieron.</param>
/// <param name="Omitidas">Filas descartadas por faltar CODIGO, TIPO o NOMBRE.</param>
public sealed record AseguradorasImportResult(int Creadas, int Actualizadas, int Omitidas);

/// <summary>Sedes en las que un contrato esta activo (N:M). Solo informativo por ahora,
/// no filtra operaciones. Se muestra en el modal "Sedes" del contrato.</summary>
public sealed record ContratoSucursalDto(Guid ContratoAseguradoraId, Guid SucursalId, string SucursalNombre);

/// <summary>Modulo Entidades Aseguradoras: aseguradoras, contratos y servicios. Tenant-scoped.</summary>
public interface IAseguradoraService
{
    /// <summary>Lista aseguradoras del tenant. Si <paramref name="soloConContratoVigente"/>
    /// es true, filtra a solo aquellas con al menos un contrato Activo cuya vigencia
    /// cubra la fecha actual (fecha_inicial &lt;= hoy AND (fecha_final IS NULL OR &gt;= hoy)).</summary>
    Task<IReadOnlyList<AseguradoraDto>> ListAseguradorasAsync(bool soloConContratoVigente = false, CancellationToken ct = default);
    Task<AseguradoraDetailDto?> GetAseguradoraAsync(Guid id, CancellationToken ct = default);
    Task<AseguradoraDetailDto?> SaveAseguradoraAsync(SaveAseguradoraRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAseguradoraAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Importa aseguradoras del tenant desde Excel. MERGE por CODIGO:
    /// si existe se actualiza, si no se crea. Solo omite filas sin CODIGO/TIPO/NOMBRE.</summary>
    Task<AseguradorasImportResult> ImportAseguradorasAsync(IReadOnlyList<AseguradoraImportRow> rows, Guid actor, CancellationToken ct = default);

    /// <summary>Lista contratos de la aseguradora. Si <paramref name="soloVigentes"/> es
    /// true, filtra a solo Activos + vigentes por fecha (mismo criterio que ListAseguradorasAsync).</summary>
    Task<IReadOnlyList<ContratoDto>> ListContratosAsync(Guid aseguradoraId, bool soloVigentes = false, CancellationToken ct = default);

    /// <summary>Sedes activas para el contrato. Vacio = no configurado (por ahora
    /// interpretado como "todas"). Ordenado por nombre de sede.</summary>
    Task<IReadOnlyList<ContratoSucursalDto>> ListContratoSucursalesAsync(Guid contratoId, CancellationToken ct = default);

    /// <summary>Reemplaza el set de sedes del contrato. Delete+Insert transaccional.
    /// <paramref name="sucursalIds"/> vacio = borra todas y deja "sin sedes configuradas".</summary>
    Task GuardarContratoSucursalesAsync(Guid contratoId, IReadOnlyList<Guid> sucursalIds, Guid actor, CancellationToken ct = default);
    Task<ContratoDto?> SaveContratoAsync(SaveContratoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteContratoAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<IReadOnlyList<ServicioDto>> ListServiciosAsync(Guid contratoId, string? filtro, CancellationToken ct = default);
    Task<ServicioDto?> SaveServicioAsync(SaveServicioRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteServicioAsync(Guid id, Guid actor, CancellationToken ct = default);
    /// <summary>
    /// Importa servicios al contrato desde filas de Excel. TS6: valida que la
    /// columna MODULO exista en el catalogo dinamico tipos_servicio del tenant.
    /// Filas con MODULO desconocido igualmente se importan (para no perder
    /// datos) pero el DTO devuelve la lista de valores no reconocidos para que
    /// la UI muestre un aviso al admin.
    /// </summary>
    Task<ServiciosImportResult> ImportServiciosAsync(Guid contratoId, IReadOnlyList<ServicioImportRow> rows, Guid actor, CancellationToken ct = default);

    /// <summary>Borra todos los servicios de un contrato. Devuelve cantidad borrada.
    /// Pensado para corregir un import erroneo (ej. cargar de nuevo sin codigo de
    /// historia). Las asignaciones existentes que referencien al servicio quedan
    /// con servicio_contrato_id en NULL (FK ON DELETE SET NULL).</summary>
    Task<int> EliminarServiciosDeContratoAsync(Guid contratoId, Guid actor, CancellationToken ct = default);

    // ---- Busqueda global de servicios (todas las aseguradoras y contratos del tenant) ----

    /// <summary>
    /// Busca servicios en TODOS los contratos de TODAS las aseguradoras del tenant por
    /// codigo (CUPS / CodigoServicio, Cod. interno) o por descripcion. La coincidencia es
    /// insensible a mayusculas y tildes ("atencion" encuentra "ATENCIÓN") y por palabras:
    /// cada palabra del texto debe aparecer (en cualquier orden). Devuelve como maximo
    /// <paramref name="max"/> filas; <see cref="ServicioBusquedaResultado.Truncado"/> avisa
    /// si hubo mas coincidencias.
    /// </summary>
    Task<ServicioBusquedaResultado> BuscarServiciosGlobalAsync(string texto, bool soloContratosActivos = false, int max = 500, CancellationToken ct = default);

    /// <summary>Formularios ACTIVOS del tenant que pueden asignarse como historia de un
    /// servicio (codigo + nombre). Mismo criterio que usa Atencion para resolver el formato.</summary>
    Task<IReadOnlyList<FormatoHistoriaOpcionDto>> ListFormatosHistoriaActivosAsync(CancellationToken ct = default);

    /// <summary>
    /// Cambia SOLO la historia (formato de HC) de un servicio de contrato, sin tocar ningun
    /// otro campo. El codigo debe corresponder a un formulario activo (por Codigo o
    /// CodigoSecundario); vacio = quitar la historia. Queda auditado (antes/despues).
    /// Afecta las asignaciones que se creen DESPUES: las existentes conservan su snapshot.
    /// </summary>
    Task<ActualizarHistoriaResultado> ActualizarHistoriaServicioAsync(Guid servicioId, string? historia, Guid actor, CancellationToken ct = default);
}

/// <summary>Fila de la busqueda global de servicios: aseguradora + contrato + detalle completo del servicio.</summary>
public sealed record ServicioBusquedaDto(
    Guid ServicioId,
    Guid AseguradoraId, string AseguradoraNombre, string AseguradoraCodigo, string AseguradoraTipo,
    Guid ContratoId, string CodigoContrato, string ContratoEstado, DateOnly? ContratoFechaInicial, DateOnly? ContratoFechaFinal,
    string? Historia,
    /// <summary>Nombre del formulario activo al que resuelve <see cref="Historia"/>; null si no resuelve o no hay historia.</summary>
    string? HistoriaNombre,
    string? CodigoServicio,
    /// <summary>CodigoServicio sin el sufijo de modalidad d/f (el codigo que viaja a RIPS).</summary>
    string? CodigoCups,
    string? CodigoInterno, string? Descripcion,
    decimal? Tarifa, decimal? ValorTotal,
    string? Modulo, string? Especialidad, string? Modalidad, string? Sede, string? PaqueteCodigo,
    string? Clasificacion, string? Observaciones,
    string? TipoArchivoRips, string? ModalidadAtencion, string? GrupoServicios, string? Servicios,
    string? Finalidad, string? CodOtroServicio, string? CausaExterna, string? ViaIngreso,
    string? ModalidadFacturacion, string? GrupoServicioFacturacion, string? ServicioFacturacion);

public sealed record ServicioBusquedaResultado(
    IReadOnlyList<ServicioBusquedaDto> Filas,
    int TotalCoincidencias,
    bool Truncado);

public sealed record FormatoHistoriaOpcionDto(string Codigo, string Nombre);

public sealed record ActualizarHistoriaResultado(bool Ok, string? Error, string? Historia, string? HistoriaNombre);
