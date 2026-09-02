namespace Visal.Application.Voz;

/// <summary>Cliente HTTP de la API de Retell. La implementacion vive en
/// Infraestructura (auth Bearer, timeouts, retry solo en errores transitorios).</summary>
public interface IRetellClient
{
    /// <summary>Crea una llamada saliente. NUNCA reintenta a ciegas (podria llamar dos veces).</summary>
    Task<CrearLlamadaResult> CrearLlamadaAsync(CrearLlamadaRequest req, CancellationToken ct = default);

    /// <summary>Consulta el estado de una llamada por su call_id. Null si no existe.</summary>
    Task<LlamadaSnapshot?> ConsultarLlamadaAsync(string callId, CancellationToken ct = default);
}
