namespace Visal.Domain.Enums;

/// <summary>Estado de una solicitud de firma remota del paciente.</summary>
public enum FirmaRequestStatus
{
    /// <summary>Enviada al WhatsApp del paciente y esperando que firme.</summary>
    Pendiente = 0,

    /// <summary>El paciente firmo desde su celular y la firma se persistio en la nota.</summary>
    Completada = 1,

    /// <summary>El link expiro sin firma. El profesional puede solicitar otra.</summary>
    Expirada = 2,

    /// <summary>El profesional cancelo la solicitud antes de que el paciente firmara.</summary>
    Cancelada = 3
}
