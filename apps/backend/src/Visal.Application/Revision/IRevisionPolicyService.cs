using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>DTO plano de la politica singleton por tenant.</summary>
public sealed record RevisionPolicyDto(
    bool AutoTriggerCierre,
    bool PreRevisionIAAutoTrigger,
    bool AdopcionAutomaticaAgente,
    decimal UmbralConfianza,
    int VentanaAsignacionesRelacionadasDias,
    bool ConfirmarAprobado,
    int MotivoInactivacionMinChars);

/// <summary>
/// Acceso a la <see cref="RevisionPolicy"/> singleton por tenant. Cuando no hay
/// fila, <see cref="GetAsync"/> devuelve un DTO con los defaults del enum; asi
/// los callers nunca tratan con null.
/// </summary>
public interface IRevisionPolicyService
{
    /// <summary>Devuelve la politica del tenant activo. Nunca null: usa defaults cuando la fila no existe.</summary>
    Task<RevisionPolicyDto> GetAsync(CancellationToken ct = default);

    /// <summary>Persiste (upsert) la politica del tenant activo.</summary>
    Task SaveAsync(RevisionPolicyDto policy, Guid actor, CancellationToken ct = default);
}
