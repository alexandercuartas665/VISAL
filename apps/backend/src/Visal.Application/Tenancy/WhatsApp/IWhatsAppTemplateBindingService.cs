using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>Representacion plana del binding, lista para servirse a la UI.</summary>
public sealed record WhatsAppTemplateBindingDto(
    Guid Id,
    WhatsAppTemplateRole Role,
    Guid LineId,
    string LineName,
    string TemplateId,
    string TemplateName,
    string LanguageCode,
    int ParameterCount);

/// <summary>Datos para asignar/reasignar una plantilla a un rol. TemplateId y
/// TemplateName son snapshots tomados del /wa/app/{name}/template al momento
/// de asignar.</summary>
public sealed record WhatsAppTemplateBindingSetRequest(
    WhatsAppTemplateRole Role,
    Guid LineId,
    string TemplateId,
    string TemplateName,
    string LanguageCode,
    int ParameterCount);

/// <summary>Registro de que plantilla HSM Gupshup usar para cada proceso del
/// sistema. Los "roles" son enum fija; la eleccion de plantilla la hace el
/// admin de la agencia desde /lineas/plantillas.</summary>
public interface IWhatsAppTemplateBindingService
{
    /// <summary>Devuelve el binding activo para el rol dado o null si no hay
    /// ninguno asignado todavia. El sender de firma lo consulta antes de
    /// decidir si envia HSM o texto de sesion.</summary>
    Task<WhatsAppTemplateBindingDto?> GetAsync(WhatsAppTemplateRole role, CancellationToken ct = default);

    /// <summary>Lista TODAS las bindings activas del tenant. La UI las muestra
    /// juntas en la seccion "Plantillas asignadas por proceso".</summary>
    Task<IReadOnlyList<WhatsAppTemplateBindingDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Crea o actualiza el binding para un rol. Idempotente por
    /// (tenant, rol): si ya existe, hace UPDATE.</summary>
    Task<WhatsAppTemplateBindingDto> UpsertAsync(WhatsAppTemplateBindingSetRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Elimina el binding de un rol (deja el proceso sin plantilla
    /// asignada). El envio caera al fallback de texto libre.</summary>
    Task<bool> DeleteAsync(WhatsAppTemplateRole role, Guid actorUserId, CancellationToken ct = default);
}
