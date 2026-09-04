using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Voz;

public sealed class VozLlamadaService : IVozLlamadaService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRetellClient _retell;
    private readonly IRetellConfig _config;
    private readonly ILogger<VozLlamadaService> _log;

    public VozLlamadaService(
        IApplicationDbContext db, ITenantContext tenant,
        IRetellClient retell, IRetellConfig config, ILogger<VozLlamadaService> log)
    {
        _db = db;
        _tenant = tenant;
        _retell = retell;
        _config = config;
        _log = log;
    }

    public async Task<VozLoteResult> LlamarPendientesAsync(int desdeMes, int hastaMes, bool dryRun, Guid actor, CancellationToken ct = default)
    {
        var msgs = new List<string>();
        await _config.EnsureLoadedAsync(ct);
        // La simulacion (dryRun) NO necesita config: solo valida telefonos y cuenta.
        // El envio real si exige Retell configurado.
        if (!dryRun && !_config.EstaConfigurado)
        {
            throw new InvalidOperationException("Retell no esta configurado (faltan RETELL_API_KEY / RETELL_AGENT_ID / RETELL_FROM_NUMBER).");
        }
        if (_tenant.TenantId is not Guid tid) { return new(0, 0, 0, dryRun, msgs); }
        if (hastaMes < desdeMes) { (desdeMes, hastaMes) = (hastaMes, desdeMes); }

        var pendientes = await _db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => x.Mes >= desdeMes && x.Mes <= hastaMes && x.Estado == "Pendiente")
            .ToListAsync(ct);
        if (pendientes.Count == 0) { return new(0, 0, 0, dryRun, msgs); }

        var pacIds = pendientes.Select(p => p.PacienteId).Distinct().ToList();
        var pacientes = (await _db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.PrimerNombre, p.Telefono })
            .ToListAsync(ct))
            .ToDictionary(p => p.Id);

        // Dedup: encuestas que ya tienen una llamada activa (no relanzar).
        var encIds = pendientes.Select(p => p.Id).ToList();
        var activas = await _db.LlamadasVoz.AsNoTracking()
            .Where(l => l.SeguimientoEncuestaId != null && encIds.Contains(l.SeguimientoEncuestaId.Value)
                && (l.Estado == LlamadaVozEstado.Registrada || l.Estado == LlamadaVozEstado.EnCurso))
            .Select(l => l.SeguimientoEncuestaId!.Value)
            .ToListAsync(ct);
        var activasSet = new HashSet<Guid>(activas);

        int lanzadas = 0, omitidas = 0, errores = 0;
        var sip = string.IsNullOrWhiteSpace(_config.TelnyxSipUsername)
            ? null
            : new Dictionary<string, string> { ["X-Telnyx-Username"] = _config.TelnyxSipUsername!.Trim() };

        foreach (var enc in pendientes)
        {
            if (activasSet.Contains(enc.Id)) { omitidas++; continue; }
            if (!pacientes.TryGetValue(enc.PacienteId, out var pac)) { omitidas++; continue; }
            var to = TelefonoE164.Normalizar(pac.Telefono);
            if (to is null)
            {
                omitidas++;
                msgs.Add($"{pac.NombreCompleto}: sin telefono valido.");
                continue;
            }

            if (dryRun) { lanzadas++; continue; }

            var vars = new Dictionary<string, string>
            {
                ["nombre"] = string.IsNullOrWhiteSpace(pac.PrimerNombre) ? PrimerPalabra(pac.NombreCompleto) : pac.PrimerNombre!,
                ["paciente"] = pac.NombreCompleto,
            };
            var meta = new Dictionary<string, object>
            {
                ["tenant_id"] = tid.ToString(),
                ["encuesta_id"] = enc.Id.ToString(),
                ["paciente_id"] = pac.Id.ToString(),
            };

            var req = new CrearLlamadaRequest(_config.FromNumber!, to, _config.AgentId, vars, meta, sip);
            var r = await _retell.CrearLlamadaAsync(req, ct);

            _db.LlamadasVoz.Add(new LlamadaVoz
            {
                TenantId = tid,
                SeguimientoEncuestaId = enc.Id,
                PacienteId = pac.Id,
                FromNumber = _config.FromNumber!,
                ToNumber = to,
                AgentId = _config.AgentId,
                CallId = r.CallId,
                Estado = r.Ok ? LlamadaVozEstado.Registrada : LlamadaVozEstado.Error,
                Error = r.Ok ? null : r.Error,
            });

            if (r.Ok) { lanzadas++; }
            else { errores++; msgs.Add($"{pac.NombreCompleto}: {r.Error}"); }
        }

        if (!dryRun) { await _db.SaveChangesAsync(ct); }
        _log.LogInformation("Voz lote {Desde}-{Hasta} tenant {Tenant}: {L} lanzadas, {O} omitidas, {E} errores (dryRun={Dry}).",
            desdeMes, hastaMes, tid, lanzadas, omitidas, errores, dryRun);
        return new(lanzadas, omitidas, errores, dryRun, msgs);
    }

    public async Task ProcesarWebhookEventoAsync(RetellWebhookEvento ev, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ev.CallId)) { return; }
        var llamada = await _db.LlamadasVoz.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.CallId == ev.CallId, ct);
        if (llamada is null)
        {
            _log.LogWarning("Webhook Retell para call_id desconocido: {CallId}", ev.CallId);
            return;
        }

        var noContactado = EsNoContactado(ev.DisconnectionReason);
        switch (ev.Evento)
        {
            case "call_started":
                if (llamada.Estado == LlamadaVozEstado.Registrada) { llamada.Estado = LlamadaVozEstado.EnCurso; }
                llamada.InicioLlamada ??= FromUnix(ev.StartTimestamp);
                break;
            case "call_ended":
                llamada.FinLlamada ??= FromUnix(ev.EndTimestamp);
                llamada.DuracionSegundos ??= ev.DuracionSegundos;
                llamada.CostoUsd ??= ev.CostoUsd;
                if (!string.IsNullOrWhiteSpace(ev.Transcript)) { llamada.Transcripcion = ev.Transcript; }
                if (!string.IsNullOrWhiteSpace(ev.RecordingUrl)) { llamada.RecordingUrl = ev.RecordingUrl; }
                llamada.Estado = noContactado ? LlamadaVozEstado.NoContactado : LlamadaVozEstado.Finalizada;
                break;
            case "call_analyzed":
                if (!string.IsNullOrWhiteSpace(ev.Transcript)) { llamada.Transcripcion = ev.Transcript; }
                if (!string.IsNullOrWhiteSpace(ev.RecordingUrl)) { llamada.RecordingUrl = ev.RecordingUrl; }
                llamada.AnalisisJson = ev.AnalisisJson ?? llamada.AnalisisJson;
                llamada.DuracionSegundos ??= ev.DuracionSegundos;
                llamada.CostoUsd ??= ev.CostoUsd;
                if (llamada.Estado != LlamadaVozEstado.NoContactado) { llamada.Estado = LlamadaVozEstado.Analizada; }
                break;
        }

        // Reflejar en la tarjeta de Seguimiento (solo si sigue Pendiente: no pisar edicion manual).
        if (llamada.SeguimientoEncuestaId is Guid encId
            && (ev.Evento == "call_ended" || ev.Evento == "call_analyzed"))
        {
            var enc = await _db.SeguimientoEncuestas.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == encId, ct);
            if (enc is not null && enc.Estado == "Pendiente")
            {
                enc.FechaLlamada = DateTime.UtcNow;
                if (llamada.Estado == LlamadaVozEstado.NoContactado)
                {
                    enc.Estado = "NoContactado";
                }
                else
                {
                    enc.Estado = "Realizada";
                    enc.PersonaAtiende ??= "Agente IA";
                    // Recoge los datos estructurados del analisis post-llamada (custom_analysis_data)
                    // en el formulario de encuesta: preguntas 1-5, resumen y persona que atiende.
                    MapearAnalisisAEncuesta(llamada.AnalisisJson, enc);
                    // Si no vino resumen estructurado, deja la transcripcion como observacion.
                    if (string.IsNullOrWhiteSpace(enc.Observaciones) && !string.IsNullOrWhiteSpace(llamada.Transcripcion))
                    {
                        enc.Observaciones = Truncar(llamada.Transcripcion!, 3500);
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LlamadaVozDto>> ListarPorMesAsync(int mes, CancellationToken ct = default)
    {
        var encIds = await _db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => x.Mes == mes)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (encIds.Count == 0) { return Array.Empty<LlamadaVozDto>(); }

        var llam = await _db.LlamadasVoz.AsNoTracking()
            .Where(l => l.SeguimientoEncuestaId != null && encIds.Contains(l.SeguimientoEncuestaId.Value))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        // La mas reciente por encuesta.
        return llam
            .GroupBy(l => l.SeguimientoEncuestaId!.Value)
            .Select(g => g.First())
            .Select(l => new LlamadaVozDto(l.Id, l.SeguimientoEncuestaId, l.PacienteId, l.CallId, l.Estado.ToString(), l.Error, l.CreatedAt))
            .ToList();
    }

    public async Task<VozLlamadaAccionResult> LlamarUnaAsync(Guid encuestaId, string? telefonoOverride, Guid actor, CancellationToken ct = default)
    {
        await _config.EnsureLoadedAsync(ct);
        if (!_config.EstaConfigurado) { throw new InvalidOperationException("Retell no esta configurado."); }
        if (_tenant.TenantId is not Guid tid) { return new(false, "Sin tenant activo.", null); }

        var enc = await _db.SeguimientoEncuestas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == encuestaId, ct);
        if (enc is null) { return new(false, "Encuesta no encontrada.", null); }
        var pac = await _db.Pacientes.AsNoTracking().Where(p => p.Id == enc.PacienteId)
            .Select(p => new { p.Id, p.NombreCompleto, p.PrimerNombre, p.Telefono })
            .FirstOrDefaultAsync(ct);
        if (pac is null) { return new(false, "Paciente no encontrado.", null); }

        var esPrueba = !string.IsNullOrWhiteSpace(telefonoOverride);
        var to = TelefonoE164.Normalizar(esPrueba ? telefonoOverride : pac.Telefono);
        if (to is null) { return new(false, "Telefono invalido.", null); }

        return await CrearYRegistrarAsync(tid, enc.Id, pac.Id, pac.NombreCompleto, pac.PrimerNombre, to, esPrueba, ct);
    }

    public async Task<VozLlamadaAccionResult> LlamarPruebaAsync(string telefono, string? nombre, Guid actor, CancellationToken ct = default)
    {
        await _config.EnsureLoadedAsync(ct);
        if (!_config.EstaConfigurado) { throw new InvalidOperationException("Retell no esta configurado."); }
        if (_tenant.TenantId is not Guid tid) { return new(false, "Sin tenant activo.", null); }
        var to = TelefonoE164.Normalizar(telefono);
        if (to is null) { return new(false, "Telefono invalido (usa formato con indicativo, ej. 3001234567).", null); }

        var nom = string.IsNullOrWhiteSpace(nombre) ? "Prueba" : nombre.Trim();
        return await CrearYRegistrarAsync(tid, null, Guid.Empty, nom, nom, to, esPrueba: true, ct);
    }

    public async Task<LlamadaDetalleDto?> ObtenerUltimaLlamadaAsync(Guid encuestaId, CancellationToken ct = default)
    {
        var l = await _db.LlamadasVoz.AsNoTracking()
            .Where(x => x.SeguimientoEncuestaId == encuestaId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (l is null) { return null; }
        return new LlamadaDetalleDto(l.Id, l.CallId, l.Estado.ToString(), l.Error, l.ToNumber,
            l.DuracionSegundos, l.CostoUsd, l.RecordingUrl, l.Transcripcion, l.CreatedAt, l.EsPrueba);
    }

    /// <summary>Crea la llamada en Retell y registra la LlamadaVoz. Guarda de una vez.</summary>
    private async Task<VozLlamadaAccionResult> CrearYRegistrarAsync(
        Guid tid, Guid? encuestaId, Guid pacienteId, string nombreCompleto, string? primerNombre,
        string to, bool esPrueba, CancellationToken ct)
    {
        var sip = string.IsNullOrWhiteSpace(_config.TelnyxSipUsername)
            ? null
            : new Dictionary<string, string> { ["X-Telnyx-Username"] = _config.TelnyxSipUsername!.Trim() };
        var vars = new Dictionary<string, string>
        {
            ["nombre"] = string.IsNullOrWhiteSpace(primerNombre) ? PrimerPalabra(nombreCompleto) : primerNombre!,
            ["paciente"] = nombreCompleto,
        };
        var meta = new Dictionary<string, object>
        {
            ["tenant_id"] = tid.ToString(),
            ["es_prueba"] = esPrueba,
        };
        if (encuestaId is Guid eid) { meta["encuesta_id"] = eid.ToString(); }
        if (pacienteId != Guid.Empty) { meta["paciente_id"] = pacienteId.ToString(); }

        var req = new CrearLlamadaRequest(_config.FromNumber!, to, _config.AgentId, vars, meta, sip);
        var r = await _retell.CrearLlamadaAsync(req, ct);

        _db.LlamadasVoz.Add(new LlamadaVoz
        {
            TenantId = tid,
            SeguimientoEncuestaId = encuestaId,
            PacienteId = pacienteId,
            FromNumber = _config.FromNumber!,
            ToNumber = to,
            AgentId = _config.AgentId,
            CallId = r.CallId,
            Estado = r.Ok ? LlamadaVozEstado.Registrada : LlamadaVozEstado.Error,
            Error = r.Ok ? null : r.Error,
            EsPrueba = esPrueba,
        });
        await _db.SaveChangesAsync(ct);
        return new(r.Ok, r.Error, r.CallId);
    }

    /// <summary>Vuelca los datos estructurados del analisis post-llamada (call_analysis.
    /// custom_analysis_data + call_summary) al formulario de encuesta. Best-effort y
    /// no pisa valores ya capturados manualmente.</summary>
    private static void MapearAnalisisAEncuesta(string? analisisJson, SeguimientoEncuesta enc)
    {
        if (string.IsNullOrWhiteSpace(analisisJson)) { return; }
        try
        {
            using var doc = JsonDocument.Parse(analisisJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return; }

            // Resumen -> Observaciones.
            if (root.TryGetProperty("call_summary", out var cs) && cs.ValueKind == JsonValueKind.String)
            {
                var resumen = cs.GetString();
                if (!string.IsNullOrWhiteSpace(resumen)) { enc.Observaciones = Truncar(resumen!, 3500); }
            }

            if (!root.TryGetProperty("custom_analysis_data", out var cad) || cad.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // Preguntas 1-5: acepta claves pregunta1/p1/pregunta_1 (case-insensitive).
            for (int i = 1; i <= 5; i++)
            {
                if (GetPreguntaActual(enc, i).HasValue) { continue; }
                var val = BuscarNumero(cad, $"pregunta{i}", $"pregunta_{i}", $"p{i}");
                if (val is int n && n is >= 1 and <= 5) { SetPreguntaEnc(enc, i, n); }
            }

            // Persona que atiende.
            if (string.IsNullOrWhiteSpace(enc.PersonaAtiende))
            {
                var persona = BuscarTexto(cad, "persona_atiende", "persona_que_atiende", "quien_contesta", "quien_atiende");
                if (!string.IsNullOrWhiteSpace(persona)) { enc.PersonaAtiende = persona!.Trim(); }
            }
        }
        catch { /* best-effort: analisis con forma inesperada se ignora */ }
    }

    private static int? GetPreguntaActual(SeguimientoEncuesta e, int i) => i switch
    {
        1 => e.Pregunta1, 2 => e.Pregunta2, 3 => e.Pregunta3, 4 => e.Pregunta4, 5 => e.Pregunta5, _ => null
    };

    private static void SetPreguntaEnc(SeguimientoEncuesta e, int i, int v)
    {
        switch (i)
        {
            case 1: e.Pregunta1 = v; break;
            case 2: e.Pregunta2 = v; break;
            case 3: e.Pregunta3 = v; break;
            case 4: e.Pregunta4 = v; break;
            case 5: e.Pregunta5 = v; break;
        }
    }

    private static int? BuscarNumero(JsonElement obj, params string[] claves)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!claves.Any(k => string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase))) { continue; }
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n)) { return n; }
            if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var s)) { return s; }
        }
        return null;
    }

    private static string? BuscarTexto(JsonElement obj, params string[] claves)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (claves.Any(k => string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase))
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }
        return null;
    }

    // -------------------- helpers --------------------

    private static string PrimerPalabra(string? s)
        => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private static DateTimeOffset? FromUnix(long? ms)
        => ms is long v ? DateTimeOffset.FromUnixTimeMilliseconds(v) : null;

    private static string Truncar(string s, int max)
        => s.Length <= max ? s : s[..max];

    private static bool EsNoContactado(string? disconnectionReason)
    {
        if (string.IsNullOrWhiteSpace(disconnectionReason)) { return false; }
        var r = disconnectionReason.ToLowerInvariant();
        return r.Contains("no_answer") || r.Contains("busy") || r.Contains("voicemail")
            || r.Contains("failed") || r.Contains("not_reachable") || r.Contains("no_pickup")
            || r.Contains("dial_failed") || r.Contains("unreachable");
    }
}
