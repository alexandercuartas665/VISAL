using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Asignacion "rol de negocio → plantilla HSM Gupshup" para un tenant. Solo
/// existe una binding activa por (TenantId, Role). Al asignar se toma
/// snapshot de TemplateId+TemplateName+ParameterCount para que el servicio
/// de envio pueda validar cantidad de parametros sin volver a llamar Gupshup.
/// La snapshot puede quedar desactualizada si el operador borra la plantilla
/// en el dashboard Gupshup — en ese caso el envio fallara con un mensaje
/// claro y el operador reasigna.
/// </summary>
public class TenantWhatsAppTemplateBinding : TenantEntity
{
    public WhatsAppTemplateRole Role { get; set; }

    /// <summary>Linea WhatsApp a traves de la cual se enviara. La UI solo
    /// deja elegir plantillas de la App Gupshup a la que esta linea apunta,
    /// por eso guardamos ambas juntas.</summary>
    public Guid LineId { get; set; }
    public WhatsAppLine? Line { get; set; }

    /// <summary>UUID de la plantilla en Gupshup — lo que /wa/api/v1/template/msg
    /// espera como template.id.</summary>
    public string TemplateId { get; set; } = null!;

    /// <summary>element_name (snake_case) — se muestra en la UI para que el
    /// operador vea que plantilla esta activa sin tener que recargar la lista.</summary>
    public string TemplateName { get; set; } = null!;

    /// <summary>Cantidad de placeholders {{1}}, {{2}}, ... que la plantilla
    /// exige. El sender valida que le pasen tantos parametros.</summary>
    public int ParameterCount { get; set; }

    /// <summary>es, es_CO, en, etc. Solo informativo — Gupshup lo trae de la
    /// plantilla al enviarla.</summary>
    public string LanguageCode { get; set; } = "es";
}
