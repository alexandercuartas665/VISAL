using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Datos del paciente seleccionado para alimentar la columna izquierda del wizard.</summary>
public sealed record PacienteAsignacionDto(
    Guid Id, string NumeroDocumento, string TipoDocumento, string NombreCompleto,
    string? Sede, string? Ciudad,
    IReadOnlyList<ContratoMiniDto> Contratos,
    // ----- Campos clinicos del paciente para prefill de historias / notas -----
    string? PrimerNombre = null, string? SegundoNombre = null,
    string? PrimerApellido = null, string? SegundoApellido = null,
    DateOnly? FechaNacimiento = null,
    string? Sexo = null, string? EstadoCivil = null,
    string? Telefono = null, string? CodigoPaisTelefono = null, string? Email = null,
    string? Direccion = null, string? Zona = null,
    string? Ocupacion = null, string? Regimen = null,
    string? ContactoEmergencia = null, string? Parentesco = null, string? TelefonoEmergencia = null,
    // Nombre de la aseguradora/EPS principal del paciente. Disponible como ruta de
    // prefill "paciente.eps" en cualquier formulario.
    string? Eps = null,
    // Estado de admision: "Abierto" o "Cerrado". Solo pacientes Cerrados pueden asignarse.
    string EstadoAdmision = "Abierto",
    // ----- Ampliacion: campos administrativos, de clasificacion y diagnostico
    //       que la ficha de admision captura y que deben poder enrutarse como
    //       prefill a HC / Escalas / Consentimientos. Todos opcionales. -----
    string? Barrio = null,
    string? Incapacidad = null,
    string? GrupoRh = null,
    string? Estado = null,
    string? EstratoSocial = null,
    string? Tutela = null,
    string? CodigoAceptacion = null,
    string? Cie10Codigo = null,
    string? DiagnosticoPrincipal = null,
    // Fechas de la estancia PAD (formato ISO en el prefill).
    DateOnly? FechaComentan = null,
    DateOnly? FechaIngresoPad = null,
    DateOnly? FechaEgresoPad = null,
    // Nombre legible resuelto de los FK a catalogos_paciente. El servicio hace
    // el join una sola vez y devuelve el texto para que el prefill lo escriba
    // directo al input/dropdown del formulario destino.
    string? TipoUsuario = null,
    string? ClasificacionPaciente = null,
    string? ClasificacionGrupoPatologia = null,
    string? TipoTutela = null,
    string? MedContratado = null);

public sealed record ContratoMiniDto(Guid ContratoId, Guid AseguradoraId, string AseguradoraNombre, string CodigoContrato, string Estado, bool RequierePdfAutorizacion);

/// <summary>Filtro tipado para la busqueda avanzada de pacientes (modal BUSCAR PACIENTES).</summary>
public sealed record BusquedaPacienteFiltro(
    IReadOnlyList<Guid>? ContratoIds = null,
    string? Documento = null,
    string? Nombre = null,
    string? Telefono = null,
    string? Correo = null);

/// <summary>Fila del grid de resultados del modal (incluye contrato de la aseguradora del paciente).</summary>
public sealed record PacienteFiltroResultadoDto(
    Guid Id, string Documento, string NombreCompleto, string? Contrato,
    string? Telefono, string? Correo);

/// <summary>Item del catalogo de servicios filtrado por contrato + tipo de servicio.</summary>
public sealed record ServicioCatalogoDto(
    Guid Id, string? Codigo, string Descripcion, string? Modulo, string? Especialidad, decimal? Tarifa,
    string? CodigoInterno, string? Historia, string? Clasificacion, string? Modalidad,
    Guid? PaqueteId, string? PaqueteCodigo);

/// <summary>Fila del historico (ultimos N) del paciente. Incluye todos los datos
/// de la programacion (autorizacion, periodo y observaciones) para que el menu
/// "Copiar programacion" de la tarjeta pueda pre-llenar el formulario lateral
/// y pre-seleccionar el contrato + modulo + servicio en la grilla.</summary>
public sealed record AsignacionMiniDto(
    Guid Id, string NombreServicio, string TipoServicio, int Cantidad,
    DateOnly FechaInicio, DateOnly? FechaFinal, string Estado,
    string ContratoCodigo, DateTimeOffset CreadoEn,
    string? CodigoAutorizacion, short? AnioServicio,
    short? MesVigencia, short? MesFinal, string? Observaciones,
    string ServicioId, string? Modulo);

/// <summary>Fila del grid "Servicios No Asignados" en /coordinacion. Incluye paciente y contrato.
/// TurnosCoordinados es la suma de Cantidad de los AsignacionTurnos creados para esta
/// asignacion — sirve para distinguir "Parcial" (algun turno pero no todos) de
/// "Pendiente" (cero turnos) cuando el filtro es TODOS.</summary>
public sealed record AsignacionPendienteDto(
    Guid Id, int Orden, string PacienteNombre, string PacienteDocumento, string PacienteTipoDoc,
    string NombreServicio, int Cantidad, string? Observaciones,
    string TipoServicio, string ContratoCodigo, string CodigoServicio,
    DateOnly FechaInicio, DateOnly? FechaFinal,
    string? CodigoAutorizacion, DateTimeOffset CreadoEn, string EstadoTexto,
    int TurnosCoordinados,
    string? Especialidad);

/// <summary>Profesional disponible para asignar al servicio (alimenta "Seleccione Medico Especialista").</summary>
public sealed record EspecialistaDto(Guid Id, string NumeroDocumento, string NombreCompleto, string? TipoProfesional);

/// <summary>Turno coordinado: que profesional atendera cuantos turnos.</summary>
public sealed record TurnoCoordinadoRequest(
    Guid ProfesionalId, int Cantidad, decimal? HorasPorTurno,
    DateOnly? FechaInicio, short? MesAsignar,
    decimal? Tarifa = null);

/// <summary>Turno ya guardado para una asignacion.</summary>
public sealed record TurnoCoordinadoDto(
    Guid Id, Guid ProfesionalId, string ProfesionalNombre,
    int Cantidad, decimal? HorasPorTurno,
    DateOnly? FechaInicio, short? MesAsignar,
    decimal? Tarifa = null);

/// <summary>Payload del boton "Asignar el servicio": graba todos los turnos de un servicio en una transaccion.</summary>
public sealed record AsignarServicioRequest(
    Guid AsignacionId, IReadOnlyList<TurnoCoordinadoRequest> Turnos);

/// <summary>Filtro de estado para el grid de Coordinacion. Equivale al cmbEstado del legacy.</summary>
public enum AsignacionEstadoFiltro
{
    Pendientes = 0,
    Asignados = 1,
    Todos = 2
}

/// <summary>Tarifa del ServicioContrato consultada por (contratoCodigo, codigoServicio).</summary>
public sealed record TarifaServicioDto(decimal? Tarifa);

/// <summary>Item del carrito que se envia al guardar el lote.</summary>
public sealed record AsignacionItemRequest(
    string ServicioId, string NombreServicio, string TipoServicio, string? Modulo,
    int Cantidad, string? CodigoAutorizacion,
    short? AnioServicio, short MesVigencia, short? MesFinal,
    DateOnly FechaInicio, DateOnly? FechaFinal,
    string? Observaciones, string? FormatoHistoria);

public sealed record CrearLoteRequest(
    Guid PacienteId, string ContratoCodigo, string Sucursal,
    IReadOnlyList<AsignacionItemRequest> Items,
    string? PdfAutorizacionUrl = null,
    string? TipoPago = null,
    string? CategoriaCopago = null,
    decimal? ValorPagoSugerido = null,
    decimal? ValorPagoReal = null);

/// <summary>Filtros del tab "Listado" en /asignacion. Todos opcionales; los null/vacios
/// simplemente no aplican. Fecha_inicial y fecha_final aplican sobre FechaInicio de la
/// asignacion (rango inclusivo). El filtro NombreServicio es contains case-insensitive.</summary>
public sealed record AsignacionListadoFiltro(
    DateOnly? FechaInicial, DateOnly? FechaFinal,
    Guid? AseguradoraId, Guid? PacienteId,
    string? ContratoCodigo, string? Modulo, string? NombreServicio);

/// <summary>Fila del listado tabular de asignaciones. Incluye todos los datos
/// relacionados (paciente + aseguradora + contrato + programacion) para que el
/// tab "Listado" pueda mostrarlos sin joins extra en la UI.</summary>
public sealed record AsignacionListadoDto(
    Guid Id, DateTimeOffset CreadoEn,
    string PacienteDocumento, string PacienteNombre,
    string ContratoCodigo, string? AseguradoraNombre,
    string NombreServicio, string TipoServicio, string? Modulo,
    int Cantidad, string Estado,
    DateOnly FechaInicio, DateOnly? FechaFinal,
    short? AnioServicio, short? MesVigencia, short? MesFinal,
    string? CodigoAutorizacion, string? Observaciones,
    string? Sucursal);

/// <summary>Payload para actualizar una asignacion existente (solo si esta Pendiente).
/// Se persiste sobre el mismo registro sin tocar el lote. Los campos vienen de la
/// misma forma que un AsignacionItemRequest + el codigo del contrato.</summary>
public sealed record ActualizarAsignacionRequest(
    Guid AsignacionId, string ContratoCodigo,
    string ServicioId, string NombreServicio, string TipoServicio, string? Modulo,
    int Cantidad, string? CodigoAutorizacion,
    short? AnioServicio, short MesVigencia, short? MesFinal,
    DateOnly FechaInicio, DateOnly? FechaFinal,
    string? Observaciones, string? FormatoHistoria);

public sealed record LoteCreadoDto(Guid LoteId, int CantidadServicios);

public interface IAsignacionService
{
    /// <summary>Datos del paciente + sus contratos (de su aseguradora). Devuelve null si no existe.</summary>
    Task<PacienteAsignacionDto?> GetPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Busca pacientes por documento/nombre/telefono para el modal de busqueda avanzada (simple).</summary>
    Task<IReadOnlyList<PacienteAsignacionDto>> BuscarPacientesAsync(string? texto, Guid? contratoId, CancellationToken ct = default);

    /// <summary>Busqueda avanzada con filtro tipado (multi-contrato + 4 campos). Alimenta el grid del modal.</summary>
    Task<IReadOnlyList<PacienteFiltroResultadoDto>> BuscarPacientesAvanzadoAsync(BusquedaPacienteFiltro filtro, CancellationToken ct = default);

    /// <summary>Lista todos los contratos activos del tenant para el CheckBoxList del modal.</summary>
    Task<IReadOnlyList<ContratoMiniDto>> ListContratosDisponiblesAsync(CancellationToken ct = default);

    /// <summary>Tipos de servicio disponibles para un contrato: DISTINCT de servicios_contrato.Modulo.</summary>
    Task<IReadOnlyList<string>> TiposServicioPorContratoAsync(Guid contratoId, CancellationToken ct = default);

    /// <summary>Servicios del contrato filtrados por tipo (Modulo).</summary>
    Task<IReadOnlyList<ServicioCatalogoDto>> ServiciosPorContratoAsync(Guid contratoId, string? tipo, CancellationToken ct = default);

    /// <summary>Ultimas N asignaciones del paciente (para la columna del centro).</summary>
    Task<IReadOnlyList<AsignacionMiniDto>> UltimasAsignacionesAsync(Guid pacienteId, int n, CancellationToken ct = default);

    /// <summary>Crea un lote + N asignaciones en una sola transaccion. Estado = Pendiente.</summary>
    Task<LoteCreadoDto> CrearLoteAsync(CrearLoteRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Elimina una asignacion del lote (caso "eliminar item" de la grilla).</summary>
    Task<bool> EliminarAsignacionAsync(Guid asignacionId, Guid actor, CancellationToken ct = default);

    /// <summary>Actualiza una asignacion existente. Solo se permite si esta Pendiente y
    /// no tiene turnos coordinados. Lanza InvalidOperationException si no se cumple.</summary>
    Task<bool> ActualizarAsignacionAsync(ActualizarAsignacionRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Lista tabular de asignaciones para el tab "Listado" con filtros compuestos.
    /// Ordena por CreadoEn desc. Sin limite implicito; la UI puede paginar/scrollear.</summary>
    Task<IReadOnlyList<AsignacionListadoDto>> ListarAsignacionesAsync(AsignacionListadoFiltro filtro, CancellationToken ct = default);

    /// <summary>
    /// Lista las asignaciones cuyo modulo coincida con uno de los permitidos, filtradas
    /// por estado (Pendientes/Asignados/Todos), periodo (anio + mes vigencia), numero de
    /// orden, y documento del paciente. Es el feed del grid "SERVICIOS NO ASIGNADOS"
    /// del modulo Coordinacion.
    /// </summary>
    Task<IReadOnlyList<AsignacionPendienteDto>> ListarPendientesAsync(
        IReadOnlyList<string> modulosPermitidos,
        AsignacionEstadoFiltro estado = AsignacionEstadoFiltro.Pendientes,
        int? anio = null, int? mesVigencia = null,
        string? noOrden = null, string? documentoPaciente = null,
        string? sucursalNombre = null,
        CancellationToken ct = default);

    /// <summary>
    /// Profesionales habilitados para atender un modulo (TERAPIAS, ENFERMERIA, ...).
    /// El filtro es por TipoProfesional.Nombre comparado case-insensitive con el modulo.
    /// Si el catalogo de tipos esta vacio o sin matches, devuelve TODOS los profesionales.
    /// </summary>
    Task<IReadOnlyList<EspecialistaDto>> ListarEspecialistasPorModuloAsync(
        string modulo, CancellationToken ct = default);

    /// <summary>Lista los turnos ya coordinados para una asignacion (especialistas + cantidad).</summary>
    Task<IReadOnlyList<TurnoCoordinadoDto>> ListarTurnosAsync(Guid asignacionId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve la tarifa pactada en el ServicioContrato para un (contratoCodigo,
    /// codigoServicio) dado. Se usa para pre-llenar el campo TARIFA en el formulario
    /// de coordinacion. Devuelve null si no se encuentra el servicio o el contrato.
    /// </summary>
    Task<decimal?> ObtenerTarifaServicioAsync(string contratoCodigo, string codigoServicio, CancellationToken ct = default);

    /// <summary>
    /// Persiste los turnos de coordinacion del servicio. Valida que la suma de Cantidad
    /// no supere Asignacion.Cantidad. Si la suma total queda igual a la cantidad de la
    /// asignacion, marca la Asignacion como Asignada. Permite multiples turnos por
    /// profesional distinto.
    /// </summary>
    Task<int> AsignarServicioAsync(AsignarServicioRequest req, Guid actor, CancellationToken ct = default);
}
