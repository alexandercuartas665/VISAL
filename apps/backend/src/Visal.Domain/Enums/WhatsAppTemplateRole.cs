namespace Visal.Domain.Enums;

/// <summary>Cada rol identifica un proceso del sistema que necesita iniciar
/// conversacion fuera de la ventana 24h de WhatsApp. La agencia asigna una
/// plantilla HSM (aprobada por Meta) a cada rol; el servicio de envio la
/// resuelve por rol y no por eleccion del usuario en tiempo de envio.</summary>
public enum WhatsAppTemplateRole
{
    /// <summary>Enviar link de solicitud de firma a paciente o pariente
    /// desde HC, Notas Medicas o el modulo Atencion.</summary>
    SolicitudFirma = 1,
}
