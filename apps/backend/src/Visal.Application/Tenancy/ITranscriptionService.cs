namespace Visal.Application.Tenancy;

/// <summary>
/// Resultado de una transcripcion de audio: texto + indicador de exito y mensaje
/// de error opcional. Los segundos de audio enviados se devuelven para que el
/// caller los pase a IAiUsageService (Whisper se cobra por minuto).
/// </summary>
public sealed record TranscriptionResult(bool Ok, string? Text, string? Error, double SecondsBilled = 0);

/// <summary>
/// Servicio que envia audio a Whisper (OpenAI) y devuelve la transcripcion.
/// Reutiliza la API key de AiProviderConfigs[ChatGpt] (config global del Super
/// Admin) — Whisper y ChatGPT comparten cuenta. No persiste el audio en ningun
/// lado: lo manda directo, recibe el texto y descarta el stream.
/// </summary>
public interface ITranscriptionService
{
    /// <param name="audio">Stream del audio (idealmente audio/webm con opus).</param>
    /// <param name="fileName">Nombre con extension (audio.webm / audio.wav). Whisper lo usa para detectar el codec.</param>
    /// <param name="languageHint">"es" para español. Acepta cualquier ISO-639-1; null = auto-detectar.</param>
    Task<TranscriptionResult> TranscribirAsync(Stream audio, string fileName, string? languageHint, CancellationToken cancellationToken = default);
}
