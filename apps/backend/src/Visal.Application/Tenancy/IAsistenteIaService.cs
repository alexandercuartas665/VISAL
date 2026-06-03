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
    string? RazonSinAgente,
    bool RevisarAlGuardarParcial = false,
    bool RevisarAlGuardarDefinitivo = false);

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
    ///
    /// IMPORTANTE: este metodo PERSISTE tanto el mensaje del usuario como la
    /// respuesta del asistente en el historial del paciente, atando ambos
    /// mensajes al PacienteId.
    /// </summary>
    Task<AsistenteRespuestaDto> EnviarMensajeAsync(
        Guid pacienteId,
        Guid historiaClinicaId,
        string contenidoNotaActual,
        string mensajeUsuario,
        IReadOnlyList<AsistenteMensajeDto> historial,
        bool persistirMensajeUsuario = true,
        CancellationToken ct = default);

    /// <summary>
    /// Lista el historial COMPLETO de mensajes del paciente con cualquier agente
    /// (el historial es propio del paciente, no de la nota ni del agente activo).
    /// Ordenado cronologicamente.
    /// </summary>
    Task<IReadOnlyList<AsistenteMensajeDto>> ListarHistorialPorPacienteAsync(
        Guid pacienteId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Agrega un mensaje al historial del paciente sin pasar por el LLM. Se usa
    /// para dividers "system" tipo "Auto-revision tras Guardado Parcial" que el
    /// frontend inyecta en la conversacion.
    /// </summary>
    Task<AsistenteMensajeDto> AgregarMensajeAsync(
        Guid pacienteId, string rol, string texto,
        Guid? historiaClinicaId = null,
        Guid? notaMedicaId = null,
        Guid? agenteId = null,
        string? agenteNombre = null,
        CancellationToken ct = default);

    /// <summary>Borra TODO el historial del paciente con el asistente. Devuelve cuantos mensajes borro.</summary>
    Task<int> LimpiarHistorialPorPacienteAsync(Guid pacienteId, CancellationToken ct = default);
}
