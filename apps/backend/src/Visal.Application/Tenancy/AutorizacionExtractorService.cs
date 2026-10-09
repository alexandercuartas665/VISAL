using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Datos extraidos por IA del PDF de una autorizacion.</summary>
public sealed record AutorizacionExtraccionResult(
    bool Ok,
    string? Error,
    string? Nombre,
    string? TipoDocumento,
    string? Documento,
    string? NumeroAutorizacion,
    IReadOnlyList<string> Telefonos,
    string? Correo,
    string? Direccion,
    string? RawJson)
{
    public static AutorizacionExtraccionResult Falla(string error) =>
        new(false, error, null, null, null, null, Array.Empty<string>(), null, null, null);
}

/// <summary>Metadatos de autorizacion guardados (persistidos) de una asignacion.</summary>
public sealed record AutorizacionMetadatoDto(
    string? Nombre, string? TipoDocumento, string? Documento,
    string? NumeroAutorizacion, IReadOnlyList<string> Telefonos,
    string? Correo, string? Direccion, DateTimeOffset? ExtraidoEn);

/// <summary>Payload para guardar/actualizar los metadatos de autorizacion (tras revision).</summary>
public sealed record GuardarAutorizacionMetadatoInput(
    string? Nombre, string? TipoDocumento, string? Documento,
    string? NumeroAutorizacion, IReadOnlyList<string> Telefonos,
    string? Correo, string? Direccion, string? RawJson);

/// <summary>Extrae datos estructurados del PDF de una autorizacion con el agente de IA
/// "EXTRACTOR AUTORIZACION" (DeepSeek, texto). El texto del PDF se extrae con PdfPig; el
/// prompt (editable) vive en el agente. Persiste los datos revisados por asignacion.</summary>
public interface IAutorizacionExtractorService
{
    /// <summary>Nombre canonico del agente de IA que hace la extraccion (editable en Agentes).</summary>
    const string AgenteNombre = "EXTRACTOR AUTORIZACION";

    Task<AutorizacionExtraccionResult> ExtraerDesdePdfAsync(byte[] pdfBytes, CancellationToken ct = default);

    /// <summary>Metadatos ya guardados de la asignacion (null si no hay).</summary>
    Task<AutorizacionMetadatoDto?> GetMetadatoAsync(Guid asignacionId, CancellationToken ct = default);

    /// <summary>Guarda/actualiza (upsert) los metadatos revisados de la asignacion.</summary>
    Task GuardarMetadatoAsync(Guid asignacionId, GuardarAutorizacionMetadatoInput input, CancellationToken ct = default);
}

public sealed class AutorizacionExtractorService : IAutorizacionExtractorService
{
    private readonly IApplicationDbContext _db;
    private readonly IAiInferenceService _inference;
    private readonly IPdfTextExtractor _pdfText;

    // Minimo de caracteres de texto para considerar que el PDF tiene capa de texto
    // (por debajo de esto asumimos escaneado y avisamos).
    private const int MinTextoUtil = 40;

    public AutorizacionExtractorService(IApplicationDbContext db, IAiInferenceService inference, IPdfTextExtractor pdfText)
    {
        _db = db;
        _inference = inference;
        _pdfText = pdfText;
    }

    public async Task<AutorizacionExtraccionResult> ExtraerDesdePdfAsync(byte[] pdfBytes, CancellationToken ct = default)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            return AutorizacionExtraccionResult.Falla("El PDF de la autorizacion esta vacio o no se pudo leer.");
        }

        // DeepSeek es solo-texto (igual que la ingesta PQRSD): extraemos la capa de texto
        // del PDF y se la mandamos. Si el PDF viene escaneado (sin texto), avisamos.
        var texto = _pdfText.ExtractText(pdfBytes);
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < MinTextoUtil)
        {
            return AutorizacionExtraccionResult.Falla(
                "No se pudo leer texto del PDF (parece escaneado o solo imagen). La extraccion con DeepSeek necesita un PDF con capa de texto.");
        }

        var agentId = await ResolverOCrearAgenteAsync(ct);

        var turns = new List<AiChatTurn>
        {
            new("user",
                "A continuacion esta el TEXTO de una autorizacion. Devuelve SOLO el JSON pedido en el system prompt.\n\n" +
                "--- TEXTO DE LA AUTORIZACION ---\n" + texto)
        };

        var res = await _inference.RunAgentAsync(agentId, turns, null, "autorizacion-extract", ct);
        if (!res.Ok || string.IsNullOrWhiteSpace(res.Text))
        {
            return AutorizacionExtraccionResult.Falla(res.Error ?? "La IA no devolvio datos.");
        }

        return ParsearJson(res.Text!);
    }

    public async Task<AutorizacionMetadatoDto?> GetMetadatoAsync(Guid asignacionId, CancellationToken ct = default)
    {
        var m = await _db.AsignacionAutorizacionDatos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AsignacionId == asignacionId, ct);
        if (m is null) { return null; }
        return new AutorizacionMetadatoDto(
            m.Nombre, m.TipoDocumento, m.Documento, m.NumeroAutorizacion,
            SplitTelefonos(m.TelefonosCsv), m.Correo, m.Direccion, m.ExtraidoEn);
    }

    public async Task GuardarMetadatoAsync(Guid asignacionId, GuardarAutorizacionMetadatoInput input, CancellationToken ct = default)
    {
        var m = await _db.AsignacionAutorizacionDatos.FirstOrDefaultAsync(x => x.AsignacionId == asignacionId, ct);
        var nuevo = m is null;
        m ??= new AsignacionAutorizacionDato { AsignacionId = asignacionId };

        m.Nombre = Limpio(input.Nombre);
        m.TipoDocumento = Limpio(input.TipoDocumento);
        m.Documento = Limpio(input.Documento);
        m.NumeroAutorizacion = Limpio(input.NumeroAutorizacion);
        m.TelefonosCsv = input.Telefonos is { Count: > 0 }
            ? string.Join(", ", input.Telefonos.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()))
            : null;
        m.Correo = Limpio(input.Correo);
        m.Direccion = Limpio(input.Direccion);
        m.RawJson = input.RawJson;
        m.ExtraidoEn = DateTimeOffset.UtcNow;

        if (nuevo) { _db.AsignacionAutorizacionDatos.Add(m); }
        await _db.SaveChangesAsync(ct);
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static IReadOnlyList<string> SplitTelefonos(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // Busca el agente por nombre en el tenant; si no existe, lo crea con el prompt por defecto
    // (Provider=Gemini) para que el usuario pueda editarlo en el modulo Agentes.
    private async Task<Guid> ResolverOCrearAgenteAsync(CancellationToken ct)
    {
        var existente = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Name == IAutorizacionExtractorService.AgenteNombre)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync(ct);
        if (existente is not null) { return existente.Id; }

        var agente = new AiAgent
        {
            Name = IAutorizacionExtractorService.AgenteNombre,
            Role = "extractor de documentos",
            Provider = AiProvider.DeepSeek, // texto (igual que la ingesta PQRSD)
            Model = null,                   // usa el modelo del proveedor configurado
            SystemPrompt = PromptPorDefecto,
            IsActive = true,
            SortOrder = 100
        };
        _db.AiAgents.Add(agente);               // el interceptor estampa TenantId
        await _db.SaveChangesAsync(ct);
        return agente.Id;
    }

    // Toma el primer bloque JSON balanceado {...} (por si el modelo envuelve en ```json o texto)
    // y mapea los campos. Tolerante: cualquier faltante queda null.
    private static AutorizacionExtraccionResult ParsearJson(string texto)
    {
        var json = ExtraerBloqueJson(texto);
        if (json is null) { return AutorizacionExtraccionResult.Falla("La IA no devolvio un JSON valido."); }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new AutorizacionExtraccionResult(
                true, null,
                Str(r, "nombre"),
                Str(r, "tipoDocumento"),
                Str(r, "documento"),
                Str(r, "numeroAutorizacion"),
                Telefonos(r),
                Str(r, "correo"),
                Str(r, "direccion"),
                json);
        }
        catch (JsonException ex)
        {
            return AutorizacionExtraccionResult.Falla("No se pudo interpretar la respuesta de la IA: " + ex.Message);
        }
    }

    private static string? ExtraerBloqueJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return null; }
        var ini = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (ini < 0 || fin <= ini) { return null; }
        return texto.Substring(ini, fin - ini + 1);
    }

    private static string? Str(JsonElement r, string prop)
    {
        if (!r.TryGetProperty(prop, out var e) || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        var s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
    }

    private static IReadOnlyList<string> Telefonos(JsonElement r)
    {
        var list = new List<string>();
        if (r.TryGetProperty("telefonos", out var t))
        {
            if (t.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in t.EnumerateArray())
                {
                    var s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) { list.Add(s!.Trim()); }
                }
            }
            else if (t.ValueKind == JsonValueKind.String)
            {
                var s = t.GetString();
                if (!string.IsNullOrWhiteSpace(s)) { list.Add(s!.Trim()); }
            }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Prompt por defecto del agente (editable luego en Agentes). Pide JSON puro.
    private const string PromptPorDefecto = """
Eres un extractor de datos de AUTORIZACIONES medicas de EPS en Colombia. Recibes el TEXTO
de una autorizacion (extraido de un PDF). Tu tarea es extraer los datos del PACIENTE y de la
autorizacion.

Devuelve EXCLUSIVAMENTE un JSON valido, sin markdown, sin ```json y sin ningun texto adicional,
con EXACTAMENTE este schema:
{
  "nombre": string|null,
  "tipoDocumento": string|null,
  "documento": string|null,
  "numeroAutorizacion": string|null,
  "telefonos": string[],
  "correo": string|null,
  "direccion": string|null
}

Reglas:
- "nombre": nombre completo del paciente tal como aparece.
- "tipoDocumento": tipo de identificacion si aparece (CC, TI, CE, RC, PA, etc.).
- "documento": numero de identificacion del paciente (solo digitos, sin puntos).
- "numeroAutorizacion": numero de la autorizacion.
- "telefonos": todos los telefonos del paciente (celular/fijo), sin duplicar; array vacio si no hay.
- "correo": correo electronico del paciente.
- "direccion": direccion de residencia del paciente.
- Usa null cuando el dato NO este en el documento. NUNCA inventes datos.
- No incluyas datos del profesional, de la IPS ni de la EPS: solo del paciente/autorizacion.
""";
}
