using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Resultado de un POST al API IHCE de MinSalud. Tiene exito si HTTP 2xx.
/// </summary>
public sealed record IhceCallResult(
    bool Exito,
    int HttpStatus,
    string? ResponseBody,
    string? ResponseContentType,
    string? Mensaje,
    int ElapsedMs);

/// <summary>
/// Resultado del envio de un RdaEvento: incluye el call + el nuevo estado del evento.
/// </summary>
public sealed record EnvioRdaResultado(
    IhceCallResult Call,
    Guid RdaEventoId,
    EstadoRdaEvento NuevoEstado,
    string? ReferenciaMinsalud);

/// <summary>
/// Payload de consulta de paciente exacto al IHCE (operacion FHIR $consultar-paciente-exacto).
/// </summary>
public sealed record ConsultaPacienteRequest(
    string TipoDocumento,
    string NumeroDocumento,
    string? HumanUserCcCedula = null);

/// <summary>
/// Payload de consulta de organizacion (IPS) al IHCE (operacion FHIR $consultar-organizacion).
/// Al menos uno de los tres identificadores debe ir con valor. NIT (TaxIdentifier) o
/// CodigoHabilitacion (HealthcareProviderIdentifier / REPS) son los mas confiables.
/// </summary>
public sealed record ConsultaOrganizacionRequest(
    string? TaxIdentifier,
    string? HealthcareProviderIdentifier,
    string? Name);

/// <summary>
/// Payload de consulta de EAPB (aseguradora/pagador) al IHCE (operacion FHIR $consultar-eapb).
/// Segun el Postman oficial, se consulta por nombre.
/// </summary>
public sealed record ConsultaEapbRequest(string Name);

/// <summary>
/// Cliente de los servicios FHIR del API IHCE de MinSalud (Resolucion 1888/2025).
/// Lee la config del tenant (endpoint base + APIM key + paths) y dispara los POSTs
/// con los headers requeridos. La autenticacion en sandbox solo necesita la APIM key;
/// el token Azure AD se reserva para produccion.
/// </summary>
public interface IIhceSenderService
{
    /// <summary>
    /// Envia el Bundle del RdaEvento al endpoint configurado y actualiza el estado
    /// del evento segun la respuesta de MinSalud. Si HTTP 2xx -> Aceptado;
    /// 4xx -> Rechazado; 5xx o timeout -> Error. Guarda la respuesta cruda en
    /// ErroresJson para auditoria.
    /// </summary>
    Task<EnvioRdaResultado> EnviarRdaAsync(Guid rdaEventoId, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Consulta paciente exacto en el IHCE (resumen consolidado de antecedentes).
    /// </summary>
    Task<IhceCallResult> ConsultarPacienteAsync(ConsultaPacienteRequest req, CancellationToken ct = default);

    /// <summary>
    /// Consulta profesional de salud en el directorio FHIR del IHCE (cruzado contra ReTHUS).
    /// Devuelve 200 si el profesional esta registrado y puede firmar RDAs. Se usa como
    /// pre-flight check antes de enviar un RDA: si el profesional no esta, MinSalud
    /// rechazaria el envio con BUNDLE-005.
    /// </summary>
    Task<IhceCallResult> ConsultarProfesionalAsync(ConsultaPacienteRequest req, CancellationToken ct = default);

    /// <summary>
    /// Consulta una organizacion (IPS) en el directorio FHIR del IHCE. Sirve para
    /// verificar si un REPS o NIT esta pre-registrado antes de emitir RDAs — si
    /// devuelve 404/vacio, el envio del RDA fallara con BUNDLE-005.
    /// </summary>
    Task<IhceCallResult> ConsultarOrganizacionAsync(ConsultaOrganizacionRequest req, CancellationToken ct = default);

    /// <summary>
    /// Consulta una EAPB (pagador/EPS) en el directorio FHIR del IHCE. Sirve para
    /// verificar si una aseguradora esta pre-registrada antes de emitir RDAs. El
    /// endpoint del Postman oficial acepta busqueda por nombre.
    /// </summary>
    Task<IhceCallResult> ConsultarEapbAsync(ConsultaEapbRequest req, CancellationToken ct = default);
}
