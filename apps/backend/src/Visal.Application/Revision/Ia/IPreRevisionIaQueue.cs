namespace Visal.Application.Revision.Ia;

/// <summary>
/// Ola 8 RC8e — cola in-process del orquestador REVISOR CLINICO IA. La
/// implementacion vive en la capa de infraestructura (worker) para no atar
/// Application a <c>System.Threading.Channels</c>; Application solo publica.
///
/// Item mandado: (TenantId, RevisionClinicaId, ActorUserId). El worker recrea
/// el scope tenant via <c>TenantAmbient.Scope</c> antes de invocar el
/// orquestador para que las queries respeten el filtro tenant global.
/// </summary>
public interface IPreRevisionIaQueue
{
    /// <summary>Encola una revision para pre-revision IA. No espera al procesamiento.</summary>
    ValueTask EnqueueAsync(PreRevisionIaJob job, CancellationToken ct = default);
}

/// <summary>Trabajo mandado al worker. Se serializa como record inmutable.</summary>
/// <param name="TenantId">Tenant activo cuando se disparo el cierre.</param>
/// <param name="RevisionClinicaId">Revision a pre-revisar.</param>
/// <param name="ActorUserId">Usuario que cerro la HC (para trazas).</param>
public sealed record PreRevisionIaJob(Guid TenantId, Guid RevisionClinicaId, Guid ActorUserId);
