using Visal.Application.Admin;

namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>
/// Servicio orientado a UI para trabajar con plantillas HSM Gupshup a
/// nivel de linea (resuelve credenciales + delega al cliente HTTP). La UI
/// nunca ve la apikey; se descifra al vuelo dentro del servicio.
/// </summary>
public interface IHsmTemplateService
{
    /// <summary>Trae las plantillas HSM del WABA de una linea Gupshup. Vacio
    /// si la linea no es Gupshup o no tiene App configurada.</summary>
    Task<HsmTemplateListResult> ListByLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>Crea una plantilla en Gupshup para la App de una linea. La
    /// plantilla entra PENDING y Meta la revisa en horas.</summary>
    Task<HsmCreateResult> CreateByLineAsync(Guid lineId, HsmCreateRequest req, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Envio de prueba de una plantilla ya aprobada al numero
    /// indicado. Reusa el connector de WhatsApp (no duplica auditoria).</summary>
    Task<HsmSendResult> SendTestAsync(Guid lineId, string templateId, string phone,
        IReadOnlyList<string> parameters, Guid actorUserId, CancellationToken ct = default);
}

public sealed record HsmTemplateListResult(bool Ok, string? Error, IReadOnlyList<GupshupTemplateInfo> Templates);
public sealed record HsmCreateRequest(
    string ElementName, string LanguageCode, string Category,
    string TemplateType, string Content, string Example,
    string? Header = null, string? ExampleHeader = null, string? Footer = null,
    IReadOnlyList<string>? Buttons = null);
public sealed record HsmCreateResult(bool Ok, string? Error, string? Id, string? Status);
public sealed record HsmSendResult(bool Ok, string? Error);
