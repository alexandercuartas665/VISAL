namespace Visal.Application.Voz;

/// <summary>
/// Cola in-process para resolver con IA la encuesta SIAU de una llamada, SIN
/// bloquear el webhook de Retell (que es anonimo y debe responder 200 rapido).
/// La implementacion vive en Infraestructura (Channel + worker); Application solo
/// publica. El worker recrea el scope de tenant via <c>TenantAmbient.Scope</c>
/// antes de invocar <c>ISeguimientoIaResolver</c>, porque la inferencia de IA es
/// tenant-scoped y el webhook no trae contexto de tenant.
/// </summary>
public interface ISeguimientoIaQueue
{
    /// <summary>Encola la resolucion IA de una encuesta. No espera al procesamiento.</summary>
    ValueTask EnqueueAsync(SeguimientoIaJob job, CancellationToken ct = default);
}

/// <summary>Trabajo mandado al worker: resolver con IA la encuesta <paramref name="EncuestaId"/>
/// del tenant <paramref name="TenantId"/> (tomado del webhook por token).</summary>
public sealed record SeguimientoIaJob(Guid TenantId, Guid EncuestaId);
