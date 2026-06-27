using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Llama https://api.openai.com/v1/audio/transcriptions con multipart/form-data.
/// La API key proviene del registro AiProviderConfigs.Provider == ChatGpt (el
/// mismo que usa el chat). Para tarifa Whisper actual: $0.006/min audio. El
/// caller decide si registrar consumo via IAiUsageService.
/// </summary>
public sealed class WhisperTranscriptionService : ITranscriptionService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WhisperTranscriptionService> _log;

    public WhisperTranscriptionService(IApplicationDbContext db, ISecretProtector secretProtector,
        IHttpClientFactory httpFactory, ILogger<WhisperTranscriptionService> log)
    {
        _db = db;
        _secretProtector = secretProtector;
        _httpFactory = httpFactory;
        _log = log;
    }

    public async Task<TranscriptionResult> TranscribirAsync(Stream audio, string fileName, string? languageHint, CancellationToken ct = default)
    {
        var cfg = await _db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == AiProvider.ChatGpt, ct);
        if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            return new TranscriptionResult(false, null, "OpenAI no esta habilitado en la plataforma. Configuralo en /admin/ai-servers.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(cfg.ApiKeyEncrypted); }
        catch { return new TranscriptionResult(false, null, "La API key de OpenAI esta cifrada con una version anterior. Vuelve a guardarla."); }

        // Base url por defecto: la oficial de OpenAI. Si el SuperAdmin la
        // sobreescribio (ej. proxy Azure), Whisper igual vive bajo /v1/audio/...
        var baseUrl = (string.IsNullOrWhiteSpace(cfg.BaseUrl) ? "https://api.openai.com/v1" : cfg.BaseUrl).TrimEnd('/');
        var url = $"{baseUrl}/audio/transcriptions";

        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(audio);
            // Whisper detecta el codec desde el header del file part; webm/opus
            // y wav son los tipicos que mandara MediaRecorder del browser.
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMime(fileName));
            content.Add(streamContent, "file", fileName);
            content.Add(new StringContent("whisper-1"), "model");
            if (!string.IsNullOrWhiteSpace(languageHint))
            {
                content.Add(new StringContent(languageHint), "language");
            }
            content.Add(new StringContent("json"), "response_format");

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var resp = await client.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Whisper transcribe fallo {Code}: {Body}", resp.StatusCode, Truncate(raw, 400));
                return new TranscriptionResult(false, null, $"OpenAI rechazo el audio ({(int)resp.StatusCode}).");
            }

            using var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
            // verbose_json devuelve "duration"; con response_format=json no, asi
            // que el caller hace su propia estimacion para el contador de uso.
            return new TranscriptionResult(true, text ?? string.Empty, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Whisper transcribe excepcion");
            return new TranscriptionResult(false, null, $"No se pudo contactar a OpenAI: {ex.Message}");
        }
    }

    private static string GuessMime(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".webm" => "audio/webm",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/m4a",
            ".mp4" => "audio/mp4",
            _ => "application/octet-stream"
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
