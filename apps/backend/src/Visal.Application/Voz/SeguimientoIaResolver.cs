using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Voz;

/// <summary>Resultado de resolver una encuesta SIAU con IA a partir de la transcripcion.</summary>
public sealed record SeguimientoIaResolverResult(bool Ok, string? Error, bool Cambio, string? Estado)
{
    public static SeguimientoIaResolverResult Falla(string error) => new(false, error, false, null);
}

/// <summary>
/// Resuelve una encuesta SIAU (preguntas 1-5 + observaciones + persona que atiende +
/// estado) a partir de la transcripcion de la llamada de voz, usando el agente de IA
/// "ENCUESTA SIAU" (DeepSeek, texto). Tenant-scoped: se dispara desde el modulo de
/// Seguimiento (que si tiene contexto de tenant), no desde el webhook anonimo.
/// No pisa valores ya capturados manualmente (completa solo lo vacio).
/// </summary>
public interface ISeguimientoIaResolver
{
    /// <summary>Nombre canonico del agente de IA (editable en Agentes).</summary>
    const string AgenteNombre = "ENCUESTA SIAU";

    /// <summary>Resuelve la encuesta con IA tomando la transcripcion de su ultima llamada.</summary>
    Task<SeguimientoIaResolverResult> ResolverDesdeUltimaLlamadaAsync(Guid encuestaId, CancellationToken ct = default);

    /// <summary>true si la encuesta tiene una llamada con transcripcion y aun esta sin resolver
    /// (preguntas vacias): sirve para el auto-resolver al abrir la tarjeta.</summary>
    Task<bool> TieneTranscripcionSinResolverAsync(Guid encuestaId, CancellationToken ct = default);
}

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();).
public sealed class SeguimientoIaResolver : ISeguimientoIaResolver
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly IAiInferenceService _inference;

    public SeguimientoIaResolver(IApplicationDbContextFactory dbFactory, IAiInferenceService inference)
    {
        _dbFactory = dbFactory;
        _inference = inference;
    }

    public async Task<bool> TieneTranscripcionSinResolverAsync(Guid encuestaId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var enc = await db.SeguimientoEncuestas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == encuestaId, ct);
        if (enc is null) { return false; }
        // Ya resuelta a mano si tiene alguna respuesta.
        if (enc.Pregunta1.HasValue || enc.Pregunta2.HasValue || enc.Pregunta3.HasValue
            || enc.Pregunta4.HasValue || enc.Pregunta5.HasValue) { return false; }

        var trans = await db.LlamadasVoz.AsNoTracking()
            .Where(x => x.SeguimientoEncuestaId == encuestaId && x.Transcripcion != null && x.Transcripcion != "")
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Transcripcion)
            .FirstOrDefaultAsync(ct);
        return !string.IsNullOrWhiteSpace(trans);
    }

    public async Task<SeguimientoIaResolverResult> ResolverDesdeUltimaLlamadaAsync(Guid encuestaId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var enc = await db.SeguimientoEncuestas.FirstOrDefaultAsync(x => x.Id == encuestaId, ct);
        if (enc is null) { return SeguimientoIaResolverResult.Falla("Encuesta no encontrada."); }

        var llamada = await db.LlamadasVoz.AsNoTracking()
            .Where(x => x.SeguimientoEncuestaId == encuestaId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (llamada is null) { return SeguimientoIaResolverResult.Falla("No hay una llamada registrada para esta encuesta."); }
        if (string.IsNullOrWhiteSpace(llamada.Transcripcion))
        {
            return SeguimientoIaResolverResult.Falla("La llamada aun no tiene transcripcion (espera a que termine y se analice).");
        }

        var agentId = await ResolverOCrearAgenteAsync(db, ct);

        var turns = new List<AiChatTurn>
        {
            new("user",
                "A continuacion esta la TRANSCRIPCION de una llamada de encuesta de satisfaccion SIAU. " +
                "Resuelve el cuestionario y devuelve SOLO el JSON pedido en el system prompt.\n\n" +
                "--- TRANSCRIPCION ---\n" + llamada.Transcripcion)
        };

        var res = await _inference.RunAgentAsync(agentId, turns, null, "encuesta-siau-resolver", ct);
        if (!res.Ok || string.IsNullOrWhiteSpace(res.Text))
        {
            return SeguimientoIaResolverResult.Falla(res.Error ?? "La IA no devolvio datos.");
        }

        var datos = ParsearJson(res.Text!);
        if (datos is null) { return SeguimientoIaResolverResult.Falla("La IA no devolvio un JSON valido."); }

        var cambio = AplicarAEncuesta(enc, datos, llamada);
        if (cambio) { await db.SaveChangesAsync(ct); }
        return new SeguimientoIaResolverResult(true, null, cambio, enc.Estado);
    }

    // Completa la encuesta SOLO en los campos vacios (modo sugerencia: no pisa lo manual).
    // Devuelve true si algo cambio. El estado solo se mueve si la tarjeta sigue Pendiente.
    private static bool AplicarAEncuesta(SeguimientoEncuesta enc, IaDatos d, LlamadaVoz llamada)
    {
        var cambio = false;

        if (!enc.Pregunta1.HasValue && d.Pregunta1 is int p1) { enc.Pregunta1 = p1; cambio = true; }
        if (!enc.Pregunta2.HasValue && d.Pregunta2 is int p2) { enc.Pregunta2 = p2; cambio = true; }
        if (!enc.Pregunta3.HasValue && d.Pregunta3 is int p3) { enc.Pregunta3 = p3; cambio = true; }
        if (!enc.Pregunta4.HasValue && d.Pregunta4 is int p4) { enc.Pregunta4 = p4; cambio = true; }
        if (!enc.Pregunta5.HasValue && d.Pregunta5 is int p5) { enc.Pregunta5 = p5; cambio = true; }

        // "Agente IA" es un placeholder que pone el webhook; la IA si sabe quien respondio,
        // asi que lo tratamos como vacio y lo reemplazamos por la persona real.
        var personaVacia = string.IsNullOrWhiteSpace(enc.PersonaAtiende)
            || string.Equals(enc.PersonaAtiende, "Agente IA", StringComparison.OrdinalIgnoreCase);
        if (personaVacia && !string.IsNullOrWhiteSpace(d.PersonaAtiende))
        {
            enc.PersonaAtiende = d.PersonaAtiende!.Trim();
            cambio = true;
        }
        if (string.IsNullOrWhiteSpace(enc.Observaciones) && !string.IsNullOrWhiteSpace(d.Observaciones))
        {
            enc.Observaciones = Truncar(d.Observaciones!.Trim(), 3500);
            cambio = true;
        }

        // Estado: solo se decide si la tarjeta sigue Pendiente (no revertir una clasificacion manual).
        if (enc.Estado == "Pendiente")
        {
            var nuevo = d.Contactado == false ? "NoContactado" : "Realizada";
            if (enc.Estado != nuevo)
            {
                enc.Estado = nuevo;
                enc.EstadoDesde = DateTimeOffset.UtcNow;
                cambio = true;
            }
            if (nuevo == "Realizada" && string.IsNullOrWhiteSpace(enc.PersonaAtiende))
            {
                enc.PersonaAtiende = "Agente IA";
                cambio = true;
            }
            enc.FechaLlamada ??= (llamada.FinLlamada ?? llamada.CreatedAt).UtcDateTime;
        }

        return cambio;
    }

    // Busca el agente por nombre en el tenant; si no existe lo crea con el prompt por defecto
    // (Provider=DeepSeek) para que el usuario pueda editarlo en el modulo Agentes.
    private static async Task<Guid> ResolverOCrearAgenteAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var existente = await db.AiAgents.FirstOrDefaultAsync(a => a.Name == ISeguimientoIaResolver.AgenteNombre, ct);
        if (existente is not null) { return existente.Id; }

        var agente = new AiAgent
        {
            Name = ISeguimientoIaResolver.AgenteNombre,
            Role = "resolutor de encuestas de satisfaccion",
            Provider = AiProvider.DeepSeek,
            Model = null,
            SystemPrompt = PromptPorDefecto,
            IsActive = true,
            SortOrder = 110
        };
        db.AiAgents.Add(agente);     // el interceptor estampa TenantId
        await db.SaveChangesAsync(ct);
        return agente.Id;
    }

    private sealed record IaDatos(
        bool? Contactado, int? Pregunta1, int? Pregunta2, int? Pregunta3, int? Pregunta4, int? Pregunta5,
        string? PersonaAtiende, string? Observaciones);

    private static IaDatos? ParsearJson(string texto)
    {
        var json = ExtraerBloqueJson(texto);
        if (json is null) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new IaDatos(
                Bool(r, "contactado"),
                Escala(r, "pregunta1"),
                Escala(r, "pregunta2"),
                Escala(r, "pregunta3"),
                Escala(r, "pregunta4"),
                NpsRecomendacion.TextoAOrdinal(Str(r, "recomendacion") ?? Str(r, "pregunta5")),
                Str(r, "persona_atiende"),
                Str(r, "observaciones"));
        }
        catch (JsonException) { return null; }
    }

    private static string? ExtraerBloqueJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return null; }
        var ini = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (ini < 0 || fin <= ini) { return null; }
        return texto.Substring(ini, fin - ini + 1);
    }

    private static int? Escala(JsonElement r, string prop)
    {
        if (!r.TryGetProperty(prop, out var e)) { return null; }
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && n is >= 1 and <= 5) { return n; }
        if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var s) && s is >= 1 and <= 5) { return s; }
        return null;
    }

    private static bool? Bool(JsonElement r, string prop)
    {
        if (!r.TryGetProperty(prop, out var e)) { return null; }
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(e.GetString(), out var b) ? b : null,
            _ => null
        };
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

    private static string Truncar(string s, int max) => s.Length <= max ? s : s[..max];

    // Prompt por defecto del agente (editable luego en Agentes). Pide JSON puro.
    private const string PromptPorDefecto = """
Eres un analista de ENCUESTAS DE SATISFACCION SIAU de VISAL RT S.A.S (empresa de salud en
Colombia). Recibes la TRANSCRIPCION de una llamada telefonica donde un agente encuesta al
paciente (o a su acompanante) sobre la atencion recibida. Tu tarea es resolver el cuestionario.

El cuestionario tiene 5 preguntas:
1. El profesional se presento oportunamente.
2. Buena presentacion personal del profesional.
3. Amable y respetuoso durante la atencion.
4. Como calificaria su experiencia global con VISAL RT S.A.S.
Las preguntas 1 a 4 se califican en escala 1 a 5 (1 = muy malo / muy en desacuerdo,
5 = excelente / muy de acuerdo).
5. Recomendaria a VISAL RT S.A.S a un familiar o amigo. Esta NO es escala numerica: es una
   opcion de esta lista EXACTA: "Definitivamente Si", "Probablemente Si",
   "Ni Probable Ni Improbable", "Probablemente No", "Definitivamente NO".

Devuelve EXCLUSIVAMENTE un JSON valido, sin markdown, sin ```json y sin texto adicional,
con EXACTAMENTE este schema:
{
  "contactado": true|false,
  "pregunta1": 1-5|null,
  "pregunta2": 1-5|null,
  "pregunta3": 1-5|null,
  "pregunta4": 1-5|null,
  "recomendacion": "Definitivamente Si"|"Probablemente Si"|"Ni Probable Ni Improbable"|"Probablemente No"|"Definitivamente NO"|null,
  "persona_atiende": string|null,
  "observaciones": string|null
}

Reglas:
- "contactado": true si se logro hablar con el paciente o su acompanante y responder la encuesta;
  false si no contesto, buzon, numero equivocado, colgo antes de responder, etc.
- "pregunta1".."pregunta4": el numero 1-5 que refleje la respuesta. Usa null si esa pregunta no
  se alcanzo a responder. NO inventes: si no hay evidencia en la transcripcion, usa null.
- "recomendacion": la opcion EXACTA de la lista que mejor refleje la respuesta del paciente; null
  si no se respondio.
- "persona_atiende": quien respondio la llamada (p.ej. "El paciente", "La hija", "La esposa").
- "observaciones": un resumen breve (1-3 frases) de comentarios, quejas o felicitaciones del
  paciente. null si no hay nada relevante.
- Responde en espanol. NUNCA inventes calificaciones que el paciente no dio.
""";
}
