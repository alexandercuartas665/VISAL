namespace Visal.Application.Tenancy.Email;

/// <summary>
/// Procesa un buzon: lee correos nuevos, los clasifica con el agente y crea tarjetas PQR en el
/// tablero destino. Idempotente por Message-ID (dedup en EmailIngestLog). Se invoca desde el poller
/// automatico (por tenant) y desde el boton "Procesar ahora" de la UI. Asume que el contexto tenant
/// ya esta establecido (ambient scope o request Blazor).
/// </summary>
public interface IEmailIngestProcessor
{
    Task<EmailIngestRunResult> ProcessConfigAsync(Guid configId, CancellationToken ct = default);
}
