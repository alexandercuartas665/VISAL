namespace Visal.Application.Voz;

/// <summary>Peticion para crear una llamada saliente (mapea a POST /v2/create-phone-call).</summary>
public sealed record CrearLlamadaRequest(
    string FromNumber,
    string ToNumber,
    string? OverrideAgentId = null,
    IReadOnlyDictionary<string, string>? VariablesDinamicas = null,
    IReadOnlyDictionary<string, object>? Metadata = null,
    IReadOnlyDictionary<string, string>? CustomSipHeaders = null);

/// <summary>Resultado de crear una llamada. Ok=false trae Error legible.</summary>
public sealed record CrearLlamadaResult(bool Ok, string? Error, string? CallId, string? CallStatus, bool Transitorio = false);

/// <summary>Snapshot de una llamada consultada (get-call).</summary>
public sealed record LlamadaSnapshot(
    string CallId, string? CallStatus, string? Transcript,
    int? DuracionSegundos, decimal? CostoUsd,
    long? StartTimestamp, long? EndTimestamp);

/// <summary>Evento de webhook ya parseado (event + campos utiles de call).</summary>
public sealed record RetellWebhookEvento(
    string Evento, string? CallId, string? CallStatus,
    string? Transcript, int? DuracionSegundos, decimal? CostoUsd,
    long? StartTimestamp, long? EndTimestamp,
    string? DisconnectionReason, string? AnalisisJson,
    string? RecordingUrl = null);
