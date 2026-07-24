namespace Visal.Application.Facturacion.Rips;

/// <summary>
/// Constructor del payload JSON RIPS conforme a la Resolucion 2275 de 2023 (MinSalud).
/// Recibe un snapshot ya generado y produce un DTO serializable. La logica de mapeo
/// entre columnas del snapshot y llaves RIPS crece por olas.
/// </summary>
public interface IRipsJsonBuilder
{
    /// <summary>
    /// Construye el payload a partir de un snapshot ya cargado + sus filas. Sincronica y pura
    /// para evitar dependencia circular con <see cref="IFacturacionSnapshotService"/>; el
    /// servicio es el que carga snapshot + filas y llama al builder.
    /// </summary>
    /// <param name="numDocumentoIdObligado">NIT del tenant emisor (sin DV ni guiones). Va al nodo transaccion.</param>
    RipsPayload Build(
        FacturacionSnapshotDetalleDto detalle,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> filas,
        string numDocumentoIdObligado);

    /// <summary>
    /// Valida que el payload cumpla las reglas duras del manual antes de serializar. Retorna
    /// la lista de errores (vacia = OK). R2 solo valida numFactura no vacio; R5 anadira
    /// cuadre financiero y regla ciclica de copago.
    /// </summary>
    IReadOnlyList<string> Validate(RipsPayload payload);
}

/// <summary>Raiz del documento RIPS. Los arrays vacios deben serializarse como [] (no omitirse).</summary>
public sealed record RipsPayload(
    RipsTransaccion Transaccion,
    IReadOnlyList<RipsUsuario> Usuarios,
    RipsServicios Servicios);

/// <summary>Cabecera de facturacion. Vincula el JSON con la FEV enviada a la DIAN.</summary>
public sealed record RipsTransaccion(
    string NumDocumentoIdObligado,
    string NumFactura,
    string? NumNota,
    string? TipoNota);

/// <summary>Demografico del paciente. Lista unica en el JSON (sin duplicados por documento).</summary>
public sealed record RipsUsuario(
    string TipoDocumentoIdentificacion,
    string NumDocumentoIdentificacion,
    string TipoUsuario,
    string FechaNacimiento,
    string CodSexo,
    string CodPaisResidencia,
    string? CodMunicipioResidencia,
    string CodZonaTerritorialResidencia,
    string Incapacidad,
    int Consecutivo);

/// <summary>Contenedor de sub-arrays clinicos. Los arrays vacios se serializan como [].</summary>
public sealed record RipsServicios(
    IReadOnlyList<RipsConsulta> Consultas,
    IReadOnlyList<RipsProcedimiento> Procedimientos,
    IReadOnlyList<RipsUrgencia> Urgencias,
    IReadOnlyList<RipsHospitalizacion> Hospitalizacion,
    IReadOnlyList<RipsRecienNacido> RecienNacidos,
    IReadOnlyList<RipsMedicamento> Medicamentos,
    IReadOnlyList<RipsOtroServicio> OtrosServicios);

/// <summary>Consulta externa (Archivo json = AC en el snapshot). Manual seccion 3.3.1.</summary>
public sealed record RipsConsulta(
    string CodPrestador,
    string FechaInicioAtencion,
    string? NumAutorizacion,
    string CodConsulta,
    string ModalidadGrupoServicioTecSal,
    string GrupoServicios,
    string CodServicio,
    string? FinalidadTecnologiaSalud,
    string? CausaMotivoAtencion,
    string CodDiagnosticoPrincipal,
    string TipoDiagnosticoPrincipal,
    string TipoDocumentoIdentificacion,
    string NumDocumentoIdentificacion,
    decimal VrServicio,
    string ConceptoRecaudo,
    decimal VrPagoModerador,
    int Consecutivo);

/// <summary>Procedimiento (Archivo json = AP). Manual seccion 3.3.2.</summary>
public sealed record RipsProcedimiento(
    string CodPrestador,
    string FechaInicioAtencion,
    string? NumAutorizacion,
    string CodProcedimiento,
    string ViaIngresoServicioSalud,
    string ModalidadGrupoServicioTecSal,
    string GrupoServicios,
    string CodServicio,
    string? FinalidadTecnologiaSalud,
    string CodDiagnosticoPrincipal,
    string TipoDocumentoIdentificacion,
    string NumDocumentoIdentificacion,
    decimal VrServicio,
    string ConceptoRecaudo,
    decimal VrPagoModerador,
    int Consecutivo,
    // R6: opcionales del §3.3.2. Se emiten si el snapshot los alimenta; por defecto null (se omiten del JSON).
    string? CodDiagnosticoRelacionado = null,
    string? CodComplicacion = null);

/// <summary>Urgencia (Archivo json = AU). Manual seccion 3.3.3. R3 placeholder.</summary>
public sealed record RipsUrgencia();

/// <summary>Hospitalizacion (Archivo json = AH). Manual seccion 3.3.4. R3 placeholder.</summary>
public sealed record RipsHospitalizacion();

/// <summary>Recien nacido (Archivo json = AN). Manual nota final seccion 3.3. R3 placeholder.</summary>
public sealed record RipsRecienNacido();

/// <summary>Medicamento (Archivo json = AM). Manual seccion 3.3.5.</summary>
public sealed record RipsMedicamento(
    string CodPrestador,
    string? NumAutorizacion,
    string FechaDispensacionAdmon,
    string CodDiagnosticoPrincipal,
    string TipoMedicamento,
    string CodTecnologiaSalud,
    string NomTecnologiaSalud,
    int CantidadMedicamento,
    string TipoDocumentoIdentificacion,
    string NumDocumentoIdentificacion,
    decimal VrServicio,
    string ConceptoRecaudo,
    decimal VrPagoModerador,
    int Consecutivo,
    // R6: campos ricos del §3.3.5. Se emiten si se pueden derivar del nomTecnologiaSalud
    // (concentracion via regex) o vienen en columnas futuras. Null -> se omiten del JSON.
    string? ConcentracionMedicamento = null,
    string? UnidadMedida = null,
    string? FormaFarmaceutica = null,
    string? UnidadMinDispensa = null,
    int? DiasTratamiento = null);

/// <summary>Otro servicio (Archivo json = AT). Manual seccion 3.3.6.</summary>
public sealed record RipsOtroServicio(
    string CodPrestador,
    string? NumAutorizacion,
    string FechaSuministroTecnologia,
    string TipoOS,
    string CodTecnologiaSalud,
    string NomTecnologiaSalud,
    int CantidadOS,
    string TipoDocumentoIdentificacion,
    string NumDocumentoIdentificacion,
    decimal VrServicio,
    string ConceptoRecaudo,
    decimal VrPagoModerador,
    int Consecutivo);
