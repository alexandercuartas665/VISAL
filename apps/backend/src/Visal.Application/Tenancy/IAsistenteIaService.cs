namespace Visal.Application.Tenancy;

/// <summary>Mensaje en la conversacion con el asistente IA.</summary>
public sealed record AsistenteMensajeDto(string Rol, string Texto, DateTimeOffset Cuando);

public sealed record AsistenteRespuestaDto(
    string Texto,
    string AgenteNombre,
    bool ProveedorReal,
    string? Aviso);

public sealed record AsistenteContextoDto(
    Guid? AgenteId,
    string? AgenteNombre,
    string? AgenteRole,
    bool TieneAgente,
    string? RazonSinAgente);

public interface IAsistenteIaService
{
    /// <summary>
    /// Resuelve cual es el agente IA asignado a la accion "Revisar notas medicas con IA"
    /// en las automatizaciones activas del tenant. Devuelve null cuando no hay regla
    /// activa o cuando la regla activa no tiene agente asignado.
    /// </summary>
    Task<AsistenteContextoDto> ResolverContextoAsync(CancellationToken ct = default);

    /// <summary>
    /// Envia un mensaje al asistente. El agente decide si lo responde basandose en
    /// su system prompt — el cual debe limitar al asistente a validacion documental
    /// (no diagnostico, no tratamiento). El servicio incluye como contexto la HC
    /// resumida del paciente + la nota actual.
    /// </summary>
    Task<AsistenteRespuestaDto> EnviarMensajeAsync(
        Guid historiaClinicaId,
        string contenidoNotaActual,
        string mensajeUsuario,
        IReadOnlyList<AsistenteMensajeDto> historial,
        CancellationToken ct = default);
}
