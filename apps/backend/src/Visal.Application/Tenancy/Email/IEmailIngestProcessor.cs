namespace Visal.Application.Tenancy.Email;

/// <summary>
/// Procesa un buzon: lee correos nuevos, los clasifica con el agente y crea tarjetas PQR en el
/// tablero destino. Idempotente por Message-ID (dedup en EmailIngestLog). Se invoca desde el poller
/// automatico (por tenant) y desde el boton "Procesar ahora" de la UI. Asume que el contexto tenant
/// ya esta establecido (ambient scope o request Blazor).
/// </summary>
public interface IEmailIngestProcessor
{
    /// <param name="progress">Opcional: recibe el avance en vivo (correo N de M + asunto actual) para la
    /// UI de "Procesar ahora". El poller automatico lo deja en null.</param>
    Task<EmailIngestRunResult> ProcessConfigAsync(Guid configId, IProgress<EmailIngestProgress>? progress = null, CancellationToken ct = default);
}
