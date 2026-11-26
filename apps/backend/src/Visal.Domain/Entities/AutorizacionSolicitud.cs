using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Estado de una solicitud de autorizacion de cierre.</summary>
public enum AutorizacionSolicitudEstado
{
    Pendiente = 0,
    Aprobada = 1,
    Rechazada = 2,
}

/// <summary>Que control origino la solicitud.</summary>
public enum AutorizacionSolicitudTipo
{
    /// <summary>La fecha de atencion se sale del patron de cierre del tipo de servicio.</summary>
    FechaAtencion = 0,
    /// <summary>El auditor IA (modo bloqueante) rechazo la historia el maximo de intentos (IMP-68):
    /// al aprobarse, el profesional puede cerrar la HC pese al rechazo.</summary>
    CierreAuditoriaIa = 1,
}

/// <summary>
/// Solicitud que hace el PROFESIONAL cuando su historia clinica se sale del patron de
/// cierre del tipo de servicio: en vez de pedirle un token por telefono al coordinador,
/// la solicitud cae en el modulo /autorizaciones y un usuario autorizador la aprueba o la
/// rechaza con un clic. Al aprobarse, la HC queda autorizada para ESA fecha de atencion
/// (y se registra un token "usado" para que el historial de autorizaciones sea uno solo).
/// Tenant-scoped.
/// </summary>
public class AutorizacionSolicitud : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public Guid? AsignacionId { get; set; }

    /// <summary>Control que origino la solicitud (fecha de atencion / auditoria IA bloqueante).</summary>
    public AutorizacionSolicitudTipo Tipo { get; set; } = AutorizacionSolicitudTipo.FechaAtencion;

    /// <summary>Fecha de atencion de la HC al momento de solicitar (lo que se autoriza).</summary>
    public DateTimeOffset FechaAtencion { get; set; }

    /// <summary>Texto del control que se incumplio (para que el autorizador entienda el caso).</summary>
    public string? Detalle { get; set; }

    /// <summary>Concepto tipificado (snapshot del nombre) elegido por el profesional.</summary>
    public string? ConceptoNombre { get; set; }

    /// <summary>Motivo que escribe el profesional.</summary>
    public string Motivo { get; set; } = null!;

    public Guid SolicitadoPorUserId { get; set; }
    public string? SolicitadoPorNombre { get; set; }

    public AutorizacionSolicitudEstado Estado { get; set; } = AutorizacionSolicitudEstado.Pendiente;

    public DateTimeOffset? ResueltoEn { get; set; }
    public Guid? ResueltoPorUserId { get; set; }
    public string? ResueltoPorNombre { get; set; }

    /// <summary>Observacion del autorizador (obligatoria al rechazar). Texto plano; si la escribio
    /// con el editor enriquecido es el texto equivalente de <see cref="RespuestaHtml"/>.</summary>
    public string? Respuesta { get; set; }

    /// <summary>IMP-71: observacion enriquecida del autorizador (HTML saneado, imagenes embebidas).
    /// Null si no la escribio o si es texto plano. La ve el profesional en la historia clinica.</summary>
    public string? RespuestaHtml { get; set; }

    /// <summary>Token "usado" que se registro al aprobar (historial unico de autorizaciones).</summary>
    public Guid? TokenId { get; set; }
}
