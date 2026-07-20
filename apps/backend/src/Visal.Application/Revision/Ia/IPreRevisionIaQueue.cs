namespace Visal.Application.Revision.Ia;

/// <summary>
/// Ola 8 RC8e — cola in-process del orquestador REVISOR CLINICO IA. La
/// implementacion vive en la capa de infraestructura (worker) para no atar
/// Application a <c>System.Threading.Channels</c>; Application solo publica.
///
/// Item mandado: (TenantId, RevisionClinicaId, ActorUserId). El worker recrea
/// el scope tenant via <c>TenantAmbient.Scope</c> antes de invocar el
/// orquestador para que las queries respeten el filtro tenant global.
///
/// Ola 9 RC9c: cada enqueue tambien se persiste en <c>pre_revision_ia_pendings</c>
/// via <see cref="IPreRevisionIaPendingStore"/>. El worker borra la fila al
/// terminar cada item; si el proceso muere entre encolar y procesar, el startup
/// del worker relee la tabla y reencola.
/// </summary>
public interface IPreRevisionIaQueue
{
    /// <summary>Encola una revision para pre-revision IA. No espera al procesamiento.</summary>
    ValueTask EnqueueAsync(PreRevisionIaJob job, CancellationToken ct = default);
}

/// <summary>
/// Ola 9 RC9c — persistencia del channel al restart. El caller que encola
/// hace INSERT antes de <see cref="IPreRevisionIaQueue.EnqueueAsync"/>; el
/// worker hace DELETE cuando el item terminan (exito o fallo).
/// </summary>
public interface IPreRevisionIaPendingStore
{
    /// <summary>Guarda un job pending y devuelve su Id (usar como <see cref="PreRevisionIaJob.PendingId"/>).</summary>
    Task<Guid> InsertAsync(PreRevisionIaJob job, CancellationToken ct = default);

    /// <summary>Borra la fila pending por Id. No lanza si ya no existe.</summary>
    Task DeleteAsync(Guid pendingId, CancellationToken ct = default);

    /// <summary>Lista todos los jobs pending para reencolar al startup del worker.</summary>
    Task<IReadOnlyList<PreRevisionIaJob>> LoadAllAsync(CancellationToken ct = default);
}

/// <summary>Trabajo mandado al worker. Se serializa como record inmutable.</summary>
/// <param name="TenantId">Tenant activo cuando se disparo el cierre.</param>
/// <param name="RevisionClinicaId">Revision a pre-revisar.</param>
/// <param name="ActorUserId">Usuario que cerro la HC (para trazas).</param>
/// <param name="PendingId">Id de la fila en <c>pre_revision_ia_pendings</c>. Guid.Empty si no fue persistido (edge, best-effort).</param>
public sealed record PreRevisionIaJob(Guid TenantId, Guid RevisionClinicaId, Guid ActorUserId, Guid PendingId = default);
