namespace Visal.Application.Facturacion.Rips;

/// <summary>
/// Constructor del payload JSON RIPS conforme a la Resolucion 2275 de 2023 (MinSalud).
/// Recibe un snapshot ya generado y produce un DTO serializable. La logica de mapeo
/// entre columnas del snapshot y llaves RIPS crece por olas: R1 solo emite el
/// esqueleto (transaccion + usuarios + servicios con arrays vacios); olas siguientes
/// completaran consultas/procedimientos/medicamentos/otros.
/// </summary>
public interface IRipsJsonBuilder
{
    /// <summary>
    /// Construye el payload a partir de un snapshot ya cargado + sus filas. Sincronica y pura
    /// para evitar dependencia circular con <see cref="IFacturacionSnapshotService"/>; el
    /// servicio es el que carga snapshot + filas y llama al builder.
    /// </summary>
    RipsPayload Build(
        FacturacionSnapshotDetalleDto detalle,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> filas);
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

// Ola R1: placeholders vacios para los sub-arrays. Olas R2-R6 hidrataran las llaves reales.
public sealed record RipsConsulta();
public sealed record RipsProcedimiento();
public sealed record RipsUrgencia();
public sealed record RipsHospitalizacion();
public sealed record RipsRecienNacido();
public sealed record RipsMedicamento();
public sealed record RipsOtroServicio();
