namespace Visal.Domain.Enums;

/// <summary>
/// Ciclo de vida de un RdaEvento desde que se genera hasta que MinSalud lo confirma.
/// </summary>
public enum EstadoRdaEvento
{
    /// <summary>Bundle FHIR construido pero aun no validado. Editable.</summary>
    Borrador = 0,

    /// <summary>Paso la validacion local contra los perfiles minsalud.fhir.co.rda.</summary>
    Validado = 1,

    /// <summary>Enviado al endpoint IHCE de MinSalud. Esperando confirmacion.</summary>
    Enviado = 2,

    /// <summary>MinSalud confirmo recepcion (200 OK o ID de recibo).</summary>
    Aceptado = 3,

    /// <summary>MinSalud rechazo el Bundle (4xx con detalle de error de validacion).</summary>
    Rechazado = 4,

    /// <summary>Error tecnico (5xx, timeout, red, descifrado fallido). Reintentar.</summary>
    Error = 5
}
