using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Revision.Bitacora;
using Visal.Application.Tenancy;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Revision.Ia;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class PreRevisionIaService : IPreRevisionIaService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;
    private readonly ISecretProtector _secretProtector;
    private readonly IRevisionMcpToolsService _tools;
    private readonly IRevisionClinicaService _revision;
    private readonly IRevisionPolicyService _policy;
    private readonly IAuditWriter? _audit;

    // Nombre de un AiAgentPrompt opcional que, si existe en el agente REVISOR CLINICO IA,
    // define el allow-list de tools como CSV en el Body. Si no existe, se usan las 9 tools.
    // Solucion pragmatica para RC5b sin migracion; RC5c evalua promover a columna dedicada.
    private const string AllowedToolsPromptName = "revision.allowed_tools";

    public PreRevisionIaService(
        IApplicationDbContextFactory dbFactory,
        IAiProviderClient client,
        IAiUsageService usage,
        ISecretProtector secretProtector,
        IRevisionMcpToolsService tools,
        IRevisionClinicaService revision,
        IRevisionPolicyService policy,
        IAuditWriter? audit = null)
    {
        _dbFactory = dbFactory;
        _client = client;
        _usage = usage;
        _secretProtector = secretProtector;
        _tools = tools;
        _revision = revision;
        _policy = policy;
        _audit = audit;
    }

    /// <summary>IMP-72: valores de RevisionAgenteLog.Origen.</summary>
    public const string OrigenCierre = "Cierre";
    public const string OrigenRevision = "Revision";

    public async Task<PreRevisionIaResult> EjecutarAsync(Guid revisionClinicaId, Guid? agenteId = null, CancellationToken ct = default)
    {
        Guid hcId;
        using (var db = _dbFactory.Create())
        {
            var revision = await db.RevisionesClinica.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == revisionClinicaId, ct);
            if (revision is null) { return Err("El ciclo de revision no existe."); }
            hcId = revision.HistoriaClinicaId;
        }

        // Auditar (LLM) y registrar (veredicto + log + adopcion) son dos pasos separados para
        // que la auditoria AL CERRAR (Atencion) pueda auditar con la HC aun abierta y registrar
        // el MISMO resultado despues del cierre, sin una segunda llamada al proveedor.
        var auditoria = await AuditarHcAsync(hcId, agenteId, ct);
        return await RegistrarAuditoriaAsync(revisionClinicaId, auditoria, ct);
    }

    public async Task<AuditoriaIaResultado> AuditarHcAsync(Guid historiaClinicaId, Guid? agenteId = null, CancellationToken ct = default,
        OpcionesAuditoriaIa? opciones = null)
    {
        var opc = opciones ?? new OpcionesAuditoriaIa();
        using var db = _dbFactory.Create();
        var hcExiste = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == historiaClinicaId)
            .Select(h => new { h.ValoresJson })
            .FirstOrDefaultAsync(ct);
        if (hcExiste is null) { return AuditoriaIaResultado.Fallo(historiaClinicaId, "La historia clinica no existe."); }

        // Resolucion del agente:
        //   - Si viene agenteId (usuario eligio en el selector) buscamos por Id.
        //     El global query filter por tenant garantiza que no se pueda usar
        //     un agente de otro tenant aunque el Id se conozca.
        //   - Sin agenteId (triggers automaticos, cierre, botones legacy) caemos al
        //     canonico REVISOR CLINICO IA por nombre.
        AiAgent? agent;
        if (agenteId is Guid aid)
        {
            agent = await db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid, ct);
            if (agent is null) { return AuditoriaIaResultado.Fallo(historiaClinicaId, "El agente seleccionado no existe o no pertenece a este tenant."); }
            if (!agent.IsActive) { return AuditoriaIaResultado.Fallo(historiaClinicaId, $"El agente '{agent.Name}' esta apagado."); }
        }
        else
        {
            agent = await db.AiAgents.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Name == IPreRevisionIaService.AgenteNombre, ct);
            if (agent is null) { return AuditoriaIaResultado.Fallo(historiaClinicaId, $"No hay agente '{IPreRevisionIaService.AgenteNombre}' configurado para este tenant."); }
            if (!agent.IsActive) { return AuditoriaIaResultado.Fallo(historiaClinicaId, $"El agente '{IPreRevisionIaService.AgenteNombre}' esta apagado."); }
        }

        var providerCfg = await db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == agent.Provider, ct);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return AuditoriaIaResultado.Fallo(historiaClinicaId, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return AuditoriaIaResultado.Fallo(historiaClinicaId, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        var quota = await _usage.GetQuotaAsync(ct);
        if (quota.Exceeded && quota.Hard)
        {
            return AuditoriaIaResultado.Fallo(historiaClinicaId, $"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}).");
        }

        var allowed = await ResolveAllowedToolsAsync(db, agent.Id, ct);
        if (allowed.Count == 0) { return AuditoriaIaResultado.Fallo(historiaClinicaId, "El agente no tiene tools permitidas configuradas."); }

        var policy = await _policy.GetAsync(ct);

        var contexto = await _tools.ArmarContextoAsync(
            historiaClinicaId,
            RevisionMcpToolNames.Todas,
            allowed,
            policy.VentanaAsignacionesRelacionadasDias,
            ct,
            new OpcionesHistorial(policy.EnviarHistorialPaciente, policy.HistorialMeses,
                policy.HistorialMaxCompletas, policy.HistorialMaxTokens));

        var systemPrompt = ArmarSystemPrompt(agent.SystemPrompt);

        // IMP-73: ultima corrida valida de ESTA HC (memoria entre intentos + reuso si no cambio nada).
        var previo = await db.RevisionAgenteLogs.AsNoTracking()
            .Where(l => l.HistoriaClinicaId == historiaClinicaId && l.Ok && l.Resultado != null)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new CorridaPrevia(l.Id, l.CreatedAt, l.Resultado!, l.Confianza, l.Nota, l.HallazgosJson,
                l.SugerenciasJson, l.SystemPrompt, l.ContextoEnviado, l.CamposLegiblesJson))
            .FirstOrDefaultAsync(ct);
        var camposActuales = previo is null ? null : await BuildCamposLegiblesAsync(db, historiaClinicaId, ct);

        var memoria = previo is null ? null : ArmarRevisionAnterior(previo, camposActuales);
        var contextoJson = SerializarContexto(contexto, memoria, opc.AlCerrar);

        if (opc.ReutilizarSiSinCambios && previo is not null
            && EsReutilizable(previo, systemPrompt, contextoJson, DateTimeOffset.UtcNow))
        {
            return Reutilizar(historiaClinicaId, previo);
        }

        var instruccion = "Revisa el 'documento_a_revisar' del siguiente JSON y emite tu veredicto SOLO sobre ese documento. "
                        + "El 'historial_del_paciente' son historias ANTERIORES (solo contexto, no se califican). ";
        if (memoria is not null)
        {
            instruccion += "'revision_anterior' es TU concepto previo sobre este mismo documento y los campos que el profesional "
                         + "cambio desde entonces: aplica las reglas de REVISION ANTERIOR. ";
        }
        if (opc.AlCerrar)
        {
            instruccion += "El profesional esta CERRANDO esta historia en este momento (ver 'momento'). ";
        }
        var turns = new List<AiChatTurn>
        {
            new("user", instruccion + "Responde SOLO con el JSON pedido en el system prompt.\n\n" + contextoJson),
        };

        var result = await CompleteWithRetryAsync(agent, apiKey, providerCfg.BaseUrl, model, systemPrompt, turns, ct);

        await _usage.RecordAsync(agent.Id, agent.Provider, model, result.InputTokens, result.OutputTokens, "revision", result.Ok, ct);

        var traza = new AuditoriaIaTraza(
            string.IsNullOrWhiteSpace(agent.Name) ? IPreRevisionIaService.AgenteNombre : agent.Name,
            agent.Provider.ToString(), model, systemPrompt, contextoJson, contexto, result.Text,
            HashValores(hcExiste.ValoresJson));

        if (!result.Ok || string.IsNullOrWhiteSpace(result.Text))
        {
            return AuditoriaIaResultado.Fallo(historiaClinicaId, result.Error ?? "El proveedor no devolvio texto.",
                result.InputTokens, result.OutputTokens, traza);
        }

        VeredictoParsed parsed;
        try { parsed = ParseVeredicto(result.Text!); }
        catch (Exception ex)
        {
            return AuditoriaIaResultado.Fallo(historiaClinicaId, "No se pudo interpretar la respuesta del agente: " + ex.Message,
                result.InputTokens, result.OutputTokens, traza);
        }

        return new AuditoriaIaResultado(true, null, parsed.Resultado, parsed.Confianza, parsed.Nota,
            parsed.Hallazgos, parsed.Sugerencias, result.InputTokens, result.OutputTokens)
        {
            HistoriaClinicaId = historiaClinicaId,
            Traza = traza,
            Observaciones = parsed.Observaciones,
        };
    }

    // ------------- IMP-73: memoria entre intentos y reuso del resultado -------------

    /// <summary>Ventana maxima para reutilizar una corrida si el documento no cambio.</summary>
    public static readonly TimeSpan VentanaReuso = TimeSpan.FromHours(24);

    public sealed record CorridaPrevia(Guid Id, DateTimeOffset CreatedAt, string Resultado, decimal? Confianza,
        string? Nota, string? HallazgosJson, string? SugerenciasJson, string? SystemPrompt,
        string? ContextoEnviado, string? CamposLegiblesJson);

    /// <summary>Bloque 'revision_anterior' que recibe el agente: su concepto previo y los campos que el
    /// profesional cambio desde entonces (comparando la HC legible guardada en aquel log con la actual).</summary>
    internal static JsonObject ArmarRevisionAnterior(CorridaPrevia previo, string? camposActuales)
    {
        var (hallazgos, observaciones) = HallazgosIa.Separar(previo.HallazgosJson);
        var cambios = BitacoraIaAnalisis.DiffCampos(
            BitacoraIaAnalisis.ParseCampos(previo.CamposLegiblesJson),
            BitacoraIaAnalisis.ParseCampos(camposActuales));
        var arr = new JsonArray();
        foreach (var c in cambios.Take(40)) { arr.Add(c.Campo); }
        var sug = new JsonArray();
        foreach (var x in BitacoraIaAnalisis.ParseSugerencias(previo.SugerenciasJson)) { sug.Add(x.Campo); }
        var h = new JsonArray();
        foreach (var x in hallazgos) { h.Add(x); }
        var o = new JsonArray();
        foreach (var x in observaciones) { o.Add(x); }
        return new JsonObject
        {
            ["rol"] = "TU concepto anterior sobre ESTE MISMO documento. No es otra historia: es tu revision previa.",
            ["fecha_utc"] = previo.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm"),
            ["resultado"] = previo.Resultado,
            ["nota"] = previo.Nota,
            ["hallazgos"] = h,
            ["observaciones"] = o,
            ["campos_con_texto_sugerido"] = sug,
            ["campos_cambiados_desde_entonces"] = arr,
            ["documento_sin_cambios"] = cambios.Count == 0,
        };
    }

    /// <summary>El 'documento_a_revisar' del contexto (JSON), o null si no se puede leer.</summary>
    internal static string? BloqueDocumento(string? contextoJson)
    {
        if (string.IsNullOrWhiteSpace(contextoJson)) { return null; }
        try
        {
            return JsonNode.Parse(contextoJson) is JsonObject o && o["documento_a_revisar"] is JsonNode d
                ? d.ToJsonString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Se reutiliza la corrida previa solo si: es de menos de 24 h, uso las MISMAS
    /// instrucciones (system prompt) y el documento enviado (campos, ordenes, escalas, evoluciones,
    /// documentos) es identico al de ahora.</summary>
    public static bool EsReutilizable(CorridaPrevia previo, string systemPrompt, string contextoJson, DateTimeOffset ahora)
    {
        if (ahora - previo.CreatedAt > VentanaReuso) { return false; }
        if (!string.Equals(previo.SystemPrompt, systemPrompt, StringComparison.Ordinal)) { return false; }
        if (!Enum.TryParse<RevisionResultado>(previo.Resultado, out _)) { return false; }
        var antes = BloqueDocumento(previo.ContextoEnviado);
        return antes is not null && string.Equals(antes, BloqueDocumento(contextoJson), StringComparison.Ordinal);
    }

    private static AuditoriaIaResultado Reutilizar(Guid hcId, CorridaPrevia previo)
    {
        var (hallazgos, observaciones) = HallazgosIa.Separar(previo.HallazgosJson);
        return new AuditoriaIaResultado(true, null, Enum.Parse<RevisionResultado>(previo.Resultado), previo.Confianza ?? 0m,
            previo.Nota, hallazgos, BitacoraIaAnalisis.ParseSugerencias(previo.SugerenciasJson), 0, 0)
        {
            HistoriaClinicaId = hcId,
            LogId = previo.Id,
            Observaciones = observaciones,
            ReutilizadaDe = previo.CreatedAt,
        };
    }

    /// <summary>Registra en el ciclo de revision el resultado de una auditoria YA hecha: evento
    /// PreRevisionAgente, log completo del agente y adopcion automatica (si la politica lo permite).
    /// Si la auditoria fallo, deja el log del fallo (cuando alcanzo a llamar al proveedor).</summary>
    private async Task<PreRevisionIaResult> RegistrarAuditoriaAsync(Guid revisionClinicaId, AuditoriaIaResultado a, CancellationToken ct,
        string origen = OrigenRevision)
    {
        using var db = _dbFactory.Create();
        var revision = await db.RevisionesClinica.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == revisionClinicaId, ct);
        if (revision is null) { return Err("El ciclo de revision no existe."); }

        if (!a.Ok || a.Resultado is not RevisionResultado resultado)
        {
            if (a.Traza is not null)
            {
                await GuardarAgenteLogAsync(db, revision.TenantId, revision.Id, revision.HistoriaClinicaId, a.Traza,
                    ok: false, error: a.Error, resultado: null, confianza: null,
                    a.InputTokens, a.OutputTokens, ct, origen: origen);
            }
            return Err(a.Error ?? "La auditoria no produjo veredicto.") with { InputTokens = a.InputTokens, OutputTokens = a.OutputTokens };
        }

        var policy = await _policy.GetAsync(ct);
        var payload = new
        {
            resultado = resultado.ToString(),
            confianza = a.Confianza,
            hallazgos = a.Hallazgos,
            umbral = policy.UmbralConfianza,
            tools_ok = a.Traza?.Contexto?.InvocacionesExitosas.Count ?? 0,
            tools_fail = a.Traza?.Contexto?.InvocacionesFallidas.Count ?? 0,
            tokens_in = a.InputTokens,
            tokens_out = a.OutputTokens,
        };
        var payloadJson = JsonSerializer.Serialize(payload);

        await _revision.RegistrarVeredictoAgenteAsync(
            new VeredictoAgenteCmd(revisionClinicaId, IPreRevisionIaService.AgenteNombre, resultado, a.Nota, payloadJson),
            ct);

        // Log completo del agente (que recibio, que proceso hizo, que respondio) para el
        // panel "Log del agente". Best-effort: nunca rompe la revision ya registrada.
        // Si la corrida ya quedo guardada como intento de cierre (Atencion), se VINCULA ese mismo
        // log al ciclo (conserva la calificacion del usuario) en vez de duplicarlo.
        var vinculado = a.LogId is Guid logId && await VincularLogAsync(db, logId, revision.Id, ct);
        if (!vinculado && a.Traza is not null)
        {
            await GuardarAgenteLogAsync(db, revision.TenantId, revision.Id, revision.HistoriaClinicaId, a.Traza,
                ok: true, error: null, resultado: resultado.ToString(), confianza: a.Confianza,
                a.InputTokens, a.OutputTokens, ct,
                nota: a.Nota, hallazgos: a.Hallazgos, sugerencias: a.Sugerencias, origen: origen,
                observaciones: a.Observaciones);
        }

        // Ola 6 RC6c — Adopcion automatica del veredicto agente.
        // Reglas: solo aplica si (a) la policy del tenant lo permite explicitamente,
        // (b) el veredicto es Aprobado, (c) la confianza es >= umbral configurado.
        // Cualquier fallo aqui NO invalida el veredicto ya registrado — la HC queda
        // con el evento PreRevisionAgente y el revisor humano puede decidir.
        if (policy.AdopcionAutomaticaAgente
            && resultado == RevisionResultado.Aprobado
            && a.Confianza >= policy.UmbralConfianza)
        {
            try
            {
                await _revision.AprobarPorSistemaAsync(new AprobarPorSistemaCmd(
                    revisionClinicaId,
                    IPreRevisionIaService.AgenteNombre,
                    a.Confianza,
                    policy.UmbralConfianza,
                    "Adopcion automatica por confianza >= umbral."), ct);
            }
            catch { /* Silencio: el veredicto quedo grabado, la adopcion es best-effort. */ }
        }

        return new PreRevisionIaResult(true, resultado, a.Confianza, a.Nota, a.Hallazgos, null, a.InputTokens, a.OutputTokens, a.Sugerencias)
        {
            Observaciones = a.Observaciones,
        };
    }

    // ───────────── Auditoria IA AL CERRAR (Atencion -> "Guardar definitivamente") ─────────────

    public async Task<AuditoriaCierreConfig?> ConfigAuditoriaAlCerrarAsync(Guid historiaClinicaId, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(ct);
        if (!policy.AuditoriaIaAlCerrar) { return null; }
        if (policy.AuditoriaIaFormatos is { Count: > 0 } formatos)
        {
            using var db = _dbFactory.Create();
            var codigo = await db.HistoriasClinicas.AsNoTracking()
                .Where(h => h.Id == historiaClinicaId)
                .Join(db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id, (h, f) => f.Codigo)
                .FirstOrDefaultAsync(ct);
            if (codigo is null || !formatos.Any(f => string.Equals(f, codigo, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
        }
        if (policy.AuditoriaIaTiposServicio is { Count: > 0 } tipos)
        {
            // Tipo de servicio de la asignacion que origino la HC. HC sin asignacion (creada por
            // fuera de Coordinacion) no se audita.
            using var db = _dbFactory.Create();
            var tiposHc = await TiposServicioDeHcAsync(db, historiaClinicaId, ct);
            if (!tiposHc.Any(th => tipos.Any(tp => AsignacionService.TiposCoinciden(th, tp))))
            {
                return null;
            }
        }
        return new AuditoriaCierreConfig(Math.Clamp(policy.AuditoriaIaTimeoutSeg, 30, 300));
    }

    /// <summary>Tipos de servicio de la(s) asignacion(es) que originaron la HC (cadena pivote ->
    /// sesion -> turno -> asignacion), sin vacios. Plural/singular se resuelve al comparar
    /// (asignaciones legacy "TERAPIAS"/"CONSULTAS").</summary>
    private static async Task<List<string>> TiposServicioDeHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var tipos = await (
            from pv in db.AsignacionTurnoSesionHcs.AsNoTracking()
            where pv.HistoriaClinicaId == hcId
            join se in db.AsignacionTurnoSesiones.AsNoTracking() on pv.SesionId equals se.Id
            join t in db.AsignacionTurnos.AsNoTracking() on se.AsignacionTurnoId equals t.Id
            join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
            select a.TipoServicio)
            .Distinct()
            .ToListAsync(ct);
        return tipos.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
    }

    public async Task<BloqueoIaEstado?> EstadoBloqueoIaAsync(Guid historiaClinicaId, Guid actor, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(ct);
        if (!policy.AuditoriaIaAlCerrar || policy.AuditoriaIaBloqueo is not { Count: > 0 } bloqueo) { return null; }
        if (await ConfigAuditoriaAlCerrarAsync(historiaClinicaId, ct) is null) { return null; }

        using var db = _dbFactory.Create();
        var tiposHc = await TiposServicioDeHcAsync(db, historiaClinicaId, ct);
        var maximos = bloqueo
            .Where(b => tiposHc.Any(th => AsignacionService.TiposCoinciden(th, b.Key)))
            .Select(b => b.Value)
            .ToList();
        if (maximos.Count == 0) { return null; }
        // Coordinadores (y Owner/Admin) no responden al bloqueo: les sale informativo.
        if (await PermisosUsuario.TieneAsync(db, actor, PermisosUsuario.SinControlCierre, ct)) { return null; }

        // Inicio de la ronda: la ultima reapertura de la HC y, despues, la ultima solicitud que el
        // coordinador rechazo (el profesional corrige con intentos nuevos).
        var reabiertaEn = await db.SuperAdminAuditLogs.AsNoTracking()
            .Where(l => l.EntityId == historiaClinicaId && l.ActionName == "historia-clinica.reabrir")
            .MaxAsync(l => (DateTimeOffset?)l.CreatedAt, ct);
        var sols = await db.AutorizacionSolicitudes.AsNoTracking()
            .Where(s => s.HistoriaClinicaId == historiaClinicaId
                        && s.Tipo == AutorizacionSolicitudTipo.CierreAuditoriaIa
                        && (reabiertaEn == null || s.CreatedAt > reabiertaEn))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Estado, s.Respuesta, s.RespuestaHtml, s.CreatedAt, s.ResueltoEn })
            .ToListAsync(ct);
        var desde = reabiertaEn;
        var ultRechazo = sols.Where(s => s.Estado == AutorizacionSolicitudEstado.Rechazada)
            .Select(s => (DateTimeOffset?)(s.ResueltoEn ?? s.CreatedAt)).Max();
        if (ultRechazo is not null && (desde is null || ultRechazo > desde)) { desde = ultRechazo; }

        var rechazos = await db.RevisionAgenteLogs.AsNoTracking()
            .CountAsync(l => l.HistoriaClinicaId == historiaClinicaId && l.Ok
                             && l.Resultado == nameof(RevisionResultado.Rechazado)
                             && (desde == null || l.CreatedAt > desde), ct);
        var ult = sols.FirstOrDefault();
        string? estado = ult?.Estado switch
        {
            AutorizacionSolicitudEstado.Pendiente => "Pendiente",
            AutorizacionSolicitudEstado.Aprobada => "Aprobada",
            AutorizacionSolicitudEstado.Rechazada => "Rechazada",
            _ => null,
        };
        return new BloqueoIaEstado(maximos.Min(), rechazos, estado,
            estado == "Rechazada" ? ult!.Respuesta : null, ult?.CreatedAt,
            estado == "Rechazada" ? ult!.RespuestaHtml : null);
    }

    public async Task<(bool Ok, string? Mensaje)> SolicitarAutorizacionCierreIaAsync(
        Guid historiaClinicaId, string motivo, string? conceptoNombre, string? actorNombre, Guid actor, CancellationToken ct = default)
    {
        var mot = (motivo ?? "").Trim();
        if (mot.Length == 0) { return (false, "Escribe el motivo de la solicitud."); }
        var est = await EstadoBloqueoIaAsync(historiaClinicaId, actor, ct);
        if (est is null) { return (true, "La historia ya no requiere autorizacion para cerrarse."); }
        if (est.Autorizada) { return (true, "La solicitud ya fue aprobada: puedes cerrar la historia."); }
        if (est.Pendiente) { return (true, "Ya hay una solicitud pendiente para esta historia."); }
        if (!est.Agotado)
        {
            return (false, $"Aun tienes {est.Restantes} intento(s) con el auditor IA antes de pedir autorizacion.");
        }

        using var db = _dbFactory.Create();
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == historiaClinicaId)
            .Select(h => new { h.TenantId, h.FechaAtencion })
            .FirstOrDefaultAsync(ct);
        if (hc is null) { return (false, "Historia clinica no encontrada."); }
        var asignacionId = await (
            from pv in db.AsignacionTurnoSesionHcs.AsNoTracking()
            where pv.HistoriaClinicaId == historiaClinicaId
            join se in db.AsignacionTurnoSesiones.AsNoTracking() on pv.SesionId equals se.Id
            join t in db.AsignacionTurnos.AsNoTracking() on se.AsignacionTurnoId equals t.Id
            select (Guid?)t.AsignacionId)
            .FirstOrDefaultAsync(ct);
        var ultNota = await db.RevisionAgenteLogs.AsNoTracking()
            .Where(l => l.HistoriaClinicaId == historiaClinicaId && l.Ok && l.Resultado == nameof(RevisionResultado.Rechazado))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.Nota)
            .FirstOrDefaultAsync(ct);
        var detalle = $"El auditor IA rechazo la historia {est.Rechazos} vez/veces (maximo {est.MaxIntentos} intentos).";
        if (!string.IsNullOrWhiteSpace(ultNota))
        {
            var n = ultNota.Trim();
            detalle += " Ultimo concepto: " + (n.Length > 600 ? n[..600] + "..." : n);
        }
        var sol = new AutorizacionSolicitud
        {
            TenantId = hc.TenantId,
            HistoriaClinicaId = historiaClinicaId,
            AsignacionId = asignacionId,
            Tipo = AutorizacionSolicitudTipo.CierreAuditoriaIa,
            CreatedAt = DateTimeOffset.UtcNow,
            FechaAtencion = hc.FechaAtencion ?? DateTimeOffset.UtcNow,
            Detalle = detalle,
            ConceptoNombre = string.IsNullOrWhiteSpace(conceptoNombre) ? null : conceptoNombre.Trim(),
            Motivo = mot,
            SolicitadoPorUserId = actor,
            SolicitadoPorNombre = string.IsNullOrWhiteSpace(actorNombre) ? null : actorNombre.Trim(),
            Estado = AutorizacionSolicitudEstado.Pendiente,
        };
        db.AutorizacionSolicitudes.Add(sol);
        _audit?.Write(db, actor, "autorizacion-solicitud.crear", nameof(AutorizacionSolicitud), sol.Id,
            previousValue: null,
            newValue: new { hcId = historiaClinicaId, tipo = "CierreAuditoriaIa", est.Rechazos, est.MaxIntentos, sol.ConceptoNombre, motivo = mot },
            tenantId: hc.TenantId);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<PreRevisionIaResult> RegistrarAuditoriaCierreAsync(
        Guid historiaClinicaId, AuditoriaIaResultado auditoria, bool cerradaPeseARechazo, Guid actor, CancellationToken ct = default)
    {
        if (auditoria.HistoriaClinicaId != historiaClinicaId)
        {
            return Err("La auditoria no corresponde a esta historia clinica.");
        }
        // Abre (o reusa) el ciclo de revision de la HC ya cerrada y registra el MISMO resultado
        // que vio el profesional: Ordenes Clinicas llega con el veredicto cargado, sin reprocesar.
        var rev = await _revision.SolicitarAsync(new SolicitarRevisionCmd(historiaClinicaId, actor, null), ct);
        var res = await RegistrarAuditoriaAsync(rev.Id, auditoria, ct, OrigenCierre);

        await AuditarEventoAsync(actor, historiaClinicaId,
            cerradaPeseARechazo ? "historia-clinica.cerrar-pese-rechazo-ia" : "historia-clinica.auditoria-ia-cierre",
            new
            {
                momento = "cierre",
                resultado = auditoria.Resultado?.ToString(),
                confianza = auditoria.Confianza,
                hallazgos = auditoria.Hallazgos.Count,
                observaciones = auditoria.Observaciones.Count,
                reutilizada = auditoria.ReutilizadaDe is not null,
                cerradaPeseARechazo,
                revisionId = rev.Id,
            }, ct);
        return res;
    }

    public async Task<Guid?> GuardarLogIntentoCierreAsync(AuditoriaIaResultado auditoria, CancellationToken ct = default)
    {
        if (!auditoria.Ok || auditoria.Traza is null || auditoria.Resultado is null) { return null; }
        if (auditoria.LogId is Guid ya) { return ya; }
        using var db = _dbFactory.Create();
        var tenantId = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == auditoria.HistoriaClinicaId).Select(h => (Guid?)h.TenantId).FirstOrDefaultAsync(ct);
        if (tenantId is null) { return null; }
        return await GuardarAgenteLogAsync(db, tenantId.Value, null, auditoria.HistoriaClinicaId, auditoria.Traza,
            ok: true, error: null, resultado: auditoria.Resultado.ToString(), confianza: auditoria.Confianza,
            auditoria.InputTokens, auditoria.OutputTokens, ct,
            nota: auditoria.Nota, hallazgos: auditoria.Hallazgos, sugerencias: auditoria.Sugerencias, origen: OrigenCierre,
            observaciones: auditoria.Observaciones);
    }

    public async Task<bool> RegistrarFeedbackAsync(Guid logId, bool util, string? comentario, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var log = await db.RevisionAgenteLogs.FirstOrDefaultAsync(x => x.Id == logId, ct);
        if (log is null) { return false; }
        var txt = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        if (txt is { Length: > MaxFeedbackChars }) { txt = txt[..MaxFeedbackChars]; }
        log.FeedbackUtil = util;
        log.FeedbackComentario = txt;
        log.FeedbackUsuarioId = actor == Guid.Empty ? null : actor;
        log.FeedbackEn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuditarEventoAsync(actor, log.HistoriaClinicaId, "revision-ia.feedback",
            new { logId, util, conComentario = txt is not null }, ct);
        return true;
    }

    /// <summary>Tope del comentario del usuario al calificar al agente.</summary>
    public const int MaxFeedbackChars = 2000;

    private static async Task<bool> VincularLogAsync(IApplicationDbContext db, Guid logId, Guid revisionId, CancellationToken ct)
    {
        try
        {
            var log = await db.RevisionAgenteLogs.FirstOrDefaultAsync(x => x.Id == logId, ct);
            if (log is null) { return false; }
            log.RevisionClinicaId = revisionId;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch { return false; }
    }

    public Task RegistrarIntentoCierreAsync(Guid historiaClinicaId, AuditoriaIaResultado auditoria, Guid actor, CancellationToken ct = default)
        => AuditarEventoAsync(actor, historiaClinicaId, "historia-clinica.auditoria-ia-cierre", new
        {
            momento = "intento-corregir",
            resultado = auditoria.Resultado?.ToString(),
            confianza = auditoria.Confianza,
            hallazgos = auditoria.Hallazgos.Count,
            observaciones = auditoria.Observaciones.Count,
            reutilizada = auditoria.ReutilizadaDe is not null,
            error = auditoria.Error,
        }, ct);

    private async Task AuditarEventoAsync(Guid actor, Guid hcId, string accion, object valor, CancellationToken ct)
    {
        if (_audit is null) { return; }
        try
        {
            using var db = _dbFactory.Create();
            var tenantId = await db.HistoriasClinicas.AsNoTracking()
                .Where(h => h.Id == hcId).Select(h => (Guid?)h.TenantId).FirstOrDefaultAsync(ct);
            _audit.Write(db, actor, accion, nameof(HistoriaClinica), hcId,
                previousValue: null, newValue: valor, tenantId: tenantId);
            await db.SaveChangesAsync(ct);
        }
        catch { /* la auditoria de metricas es best-effort */ }
    }

    /// <summary>Huella de los valores auditados: permite saber si el profesional cambio el texto
    /// despues de la auditoria (en cuyo caso hay que volver a auditar).</summary>
    public static string HashValores(string? valoresJson)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(valoresJson ?? ""));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Envuelve <see cref="IAiProviderClient.CompleteAsync"/> con retry+backoff
    /// exponencial (Ola 6 RC6b). Reintenta hasta 3 veces con delays 400ms/1200ms
    /// ante fallos transitorios: exception del cliente HTTP, timeouts, y errores
    /// del provider con mensaje que sugiere transitorio (429/5xx). Un
    /// <see cref="AiChatResult"/> con <c>Ok=false</c> pero motivo permanente
    /// (401/403, "invalid api key", "unauthorized") NO se reintenta.
    /// </summary>
    private async Task<AiChatResult> CompleteWithRetryAsync(
        AiAgent agent, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken ct)
    {
        var delays = new[] { TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200) };
        AiChatResult? last = null;
        Exception? lastEx = null;

        for (var intento = 0; intento <= delays.Length; intento++)
        {
            try
            {
                var r = await _client.CompleteAsync(agent.Provider, apiKey, baseUrl, model, systemPrompt, turns, ct);
                if (r.Ok) { return r; }
                last = r;
                if (!EsErrorTransitorio(r.Error)) { return r; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastEx = ex;
            }

            if (intento < delays.Length)
            {
                try { await Task.Delay(delays[intento], ct); }
                catch (OperationCanceledException) { throw; }
            }
        }

        // Se agotaron los intentos: devolvemos el ultimo estado conocido.
        if (last is not null) { return last; }
        return new AiChatResult(false, null, lastEx?.Message ?? "El proveedor no respondio tras varios intentos.");
    }

    private static bool EsErrorTransitorio(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) { return true; }
        var low = error.ToLowerInvariant();
        // No reintentamos si la config del provider es la que esta mal — reintentarlo
        // solo quema tokens y hace ruido en el log.
        if (low.Contains("unauthorized") || low.Contains("401")
            || low.Contains("forbidden") || low.Contains("403")
            || low.Contains("invalid api key") || low.Contains("api key")) { return false; }
        return low.Contains("timeout") || low.Contains("timed out")
            || low.Contains("429") || low.Contains("rate limit")
            || low.Contains("500") || low.Contains("502") || low.Contains("503") || low.Contains("504")
            || low.Contains("transient") || low.Contains("temporarily");
    }

    private static async Task<IReadOnlyList<string>> ResolveAllowedToolsAsync(IApplicationDbContext db, Guid agentId, CancellationToken ct)
    {
        // Ola 8 RC8a — solo columna dedicada `AiAgent.AllowedToolsCsv`.
        // El fallback legacy a `AiAgentPrompt` name=revision.allowed_tools fue eliminado.
        // La migracion `MigrateAllowedToolsSeedsToColumn` (Ola 8) trasladó los datos
        // seedeados por Ola 5c a la columna dedicada. Si `AllowedToolsCsv` esta null
        // o vacio, se usan las 9 tools por defecto.
        var csv = await db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.AllowedToolsCsv)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(csv)) { return RevisionMcpToolNames.Todas; }

        var raw = csv!.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = raw.Where(x => RevisionMcpToolNames.Todas.Contains(x)).Distinct().ToList();
        return valid.Count == 0 ? Array.Empty<string>() : valid;
    }

    private static string ArmarSystemPrompt(string agentSystemPrompt)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentSystemPrompt))
        {
            sb.AppendLine(agentSystemPrompt);
            sb.AppendLine();
        }
        sb.AppendLine("Eres el agente REVISOR CLINICO IA de una IPS colombiana. Tu unica salida es un JSON valido con este schema exacto:");
        sb.AppendLine("{");
        sb.AppendLine("  \"resultado\": \"Aprobado\" | \"Rechazado\" | \"Neutral\",");
        sb.AppendLine("  \"confianza\": number entre 0 y 1,");
        sb.AppendLine("  \"nota\": \"justificacion clara y especifica de tu decision, en 2 a 4 frases\",");
        sb.AppendLine("  \"hallazgos\": [\"lo que el profesional DEBE corregir 1\", \"... 2\"],");
        sb.AppendLine("  \"observaciones\": [\"detalle menor o administrativo que NO impide aprobar\"],");
        sb.AppendLine("  \"sugerencias\": [{ \"campo\": \"etiqueta del campo\", \"texto_sugerido\": \"redaccion mejorada\" }]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Reglas:");
        sb.AppendLine("- Basate SOLO en el contexto que te llega. No inventes datos ni condiciones.");
        sb.AppendLine("- 'Aprobado' = HC completa, congruente, sin faltantes clinicos evidentes.");
        sb.AppendLine("- 'Rechazado' = falta un dato clinico critico, incongruencia grave o riesgo clinico.");
        sb.AppendLine("- 'Neutral' = evidencia insuficiente para decidir.");
        sb.AppendLine("- La 'nota' es tu JUSTIFICACION, no una etiqueta generica: en 2 a 4 frases explica POR QUE");
        sb.AppendLine("  llegaste a ese resultado, CITANDO los datos concretos de la HC que lo sustentan (nombres de");
        sb.AppendLine("  campos, fechas, valores, ordenes, incongruencias). Si RECHAZAS, di EXACTAMENTE que falta o");
        sb.AppendLine("  que esta mal, en que parte de la HC, y POR QUE impide aprobar o facturar.");
        sb.AppendLine("- HALLAZGOS vs OBSERVACIONES (regla dura): 'hallazgos' son SOLO los problemas que el profesional");
        sb.AppendLine("  DEBE corregir en el documento: cada uno CONCRETO y verificable (nombra el campo y el problema");
        sb.AppendLine("  puntual), ordenados del MAS GRAVE al menos grave (primero lo que pone en riesgo al paciente o");
        sb.AppendLine("  hace inverosimil el registro, p.ej. un signo vital imposible). Maximo 6. 'observaciones' son los");
        sb.AppendLine("  detalles menores, administrativos o de estilo que NO impiden aprobar (maximo 4). NUNCA pongas en");
        sb.AppendLine("  ninguna de las dos listas lo que validaste o lo que esta correcto ('Verificado sin hallazgo',");
        sb.AppendLine("  'Completitud: los N campos estan llenos', 'la fecha coincide'), ni comentarios sobre lo que NO");
        sb.AppendLine("  vas a reportar ('el formato no define X, por lo que no se reporta'): eso, si aporta, va en la nota.");
        sb.AppendLine("  Si el resultado es 'Rechazado' debe haber al menos un hallazgo; si solo quedan observaciones, el");
        sb.AppendLine("  resultado es 'Aprobado'.");
        sb.AppendLine("- NO son hallazgos (a lo sumo una observacion, y solo si es relevante): los dias entre la atencion y");
        sb.AppendLine("  la apertura o el cierre del registro, el estado 'Abierta' de la historia, el numero del campo");
        sb.AppendLine("  'Registro' del profesional, la sigla del tipo de documento, o el RIPS.");
        sb.AppendLine("- REVISION ANTERIOR (si llega 'revision_anterior'): es TU concepto previo sobre ESTE MISMO documento y");
        sb.AppendLine("  la lista 'campos_cambiados_desde_entonces'. Se CONSISTENTE con lo que ya dijiste:");
        sb.AppendLine("  1) Revisa cada hallazgo anterior: si el profesional lo corrigio (el campo cambio y ya no tiene el");
        sb.AppendLine("     problema), NO lo repitas. Si sigue sin resolver, repitelo con el MISMO fondo (no lo reformules");
        sb.AppendLine("     como un problema distinto).");
        sb.AppendLine("  2) NO agregues hallazgos NUEVOS sobre campos que NO cambiaron y que no marcaste antes, salvo que sean");
        sb.AppendLine("     CRITICOS: valor imposible o riesgo para el paciente, copia/pega evidente de un campo clinico,");
        sb.AppendLine("     campo requerido vacio, o contradiccion clinica grave. Lo demas va a 'observaciones'.");
        sb.AppendLine("  3) Si el profesional copio tu texto sugerido en un campo ('campos_con_texto_sugerido'), no vuelvas a");
        sb.AppendLine("     marcar ese campo salvo que haya quedado un [COMPLETAR: ...] sin llenar.");
        sb.AppendLine("  4) Si corrigio todo lo que senalaste y no hay criticos nuevos, el resultado es 'Aprobado'.");
        sb.AppendLine("  5) Si 'documento_sin_cambios' es true, el veredicto debe ser el MISMO de la revision anterior.");
        sb.AppendLine("- MOMENTO DE CIERRE (si llega 'momento'): el profesional esta cerrando la historia ahora; que figure");
        sb.AppendLine("  'Abierta', sin fecha de cierre o con apertura posterior a la atencion NO es hallazgo ni observacion.");
        sb.AppendLine("- Se especifico y evita frases de relleno. La nota debe permitirle a un auditor humano entender");
        sb.AppendLine("  tu decision sin volver a leer toda la HC.");
        sb.AppendLine("- ALCANCE SEGUN EL FORMATO (PRIORIDAD MAXIMA): la tool get_form_definition te entrega el");
        sb.AppendLine("  CATALOGO DE CAMPOS que ESTE formato captura (con 'requerido' y 'estado' lleno/vacio). Evalua la");
        sb.AppendLine("  COMPLETITUD unica y exclusivamente contra esos campos. NO exijas, NO reportes como faltante ni");
        sb.AppendLine("  glosa ningun dato que el formato NO define como campo (por ejemplo, NO reclames signos vitales,");
        sb.AppendLine("  antecedentes u otros datos si el formato no tiene ese campo). Cada formato define su propio alcance.");
        sb.AppendLine("- Un campo ausente es hallazgo SOLO si el formato lo define y su 'estado' es 'vacio'; prioriza los");
        sb.AppendLine("  campos marcados como 'requerido' que esten vacios. Un campo opcional vacio es, a lo sumo, una");
        sb.AppendLine("  observacion menor, nunca un Rechazo por si solo.");
        sb.AppendLine("- La coherencia/congruencia (entre diagnostico, ordenes, fechas y los datos que la HC SI tiene) se");
        sb.AppendLine("  evalua normalmente. El formato acota la auditoria de COMPLETITUD, no la de congruencia.");
        sb.AppendLine("- FECHA DEL SERVICIO (regla dura): la fecha en que se presto el servicio es SIEMPRE");
        sb.AppendLine("  'fechas.fecha_atencion_servicio' de get_historia_clinica (ya resuelta por el sistema). NUNCA uses");
        sb.AppendLine("  'apertura_registro_admin' ni 'cierre_registro_admin' como fecha del servicio/atencion: son marcas");
        sb.AppendLine("  administrativas del registro. Toda referencia a 'cuando se atendio / se presto el servicio' usa");
        sb.AppendLine("  fecha_atencion_servicio, AUNQUE el cierre sea posterior o haya dias de diferencia (eso NO cambia");
        sb.AppendLine("  la fecha del servicio, a lo sumo es una observacion administrativa).");
        sb.AppendLine("- FECHAS DE ATENCION (chequeo de congruencia): compara la fecha de atencion del HEADER ('Ciudad y");
        sb.AppendLine("  Fecha') contra la(s) reportada(s) como atencion en el CUERPO (campos marcados como fecha de");
        sb.AppendLine("  atencion). Las tienes YA SEPARADAS en get_form_definition -> 'fechas_atencion' ('header' vs");
        sb.AppendLine("  'cuerpo'). DEBEN COINCIDIR; si difieren es un HALLAZGO de congruencia temporal. Si coinciden (o");
        sb.AppendLine("  el cuerpo no tiene campo de fecha de atencion), NO es hallazgo.");
        sb.AppendLine("- ESTRUCTURA DEL CONTEXTO (regla dura): recibes UN JSON con tres bloques. 'documento_a_revisar' es");
        sb.AppendLine("  la UNICA historia que evaluas: el veredicto, la nota y TODOS los hallazgos son sobre ella.");
        sb.AppendLine("  'datos_del_paciente' es contexto administrativo. 'historial_del_paciente' contiene historias");
        sb.AppendLine("  ANTERIORES del paciente (orden cronologico por fecha de atencion, n=1 la mas antigua): son SOLO");
        sb.AppendLine("  CONTEXTO, NUNCA generes hallazgos sobre ellas ni las califiques. Cuando las uses como evidencia,");
        sb.AppendLine("  citalas por consecutivo y fecha de atencion (ej. 'segun HC-000123 del 2026-09-14').");
        sb.AppendLine("- USOS DEL HISTORIAL (contrasta el documento_a_revisar contra las historias anteriores):");
        sb.AppendLine("  1) CONTINUIDAD: diagnostico, plan de tratamiento y numero de sesion coherentes con lo previo.");
        sb.AppendLine("  2) EVOLUCION: que se evidencie progreso, cambio o respuesta al tratamiento frente a lo anterior.");
        sb.AppendLine("  3) TEXTO COPIADO: si un campo clinico es identico al de una historia anterior (apoyate en");
        sb.AppendLine("     'campos_identicos_a_la_revisada' de cada historia del historial), reportalo como copia/pega.");
        sb.AppendLine("     'campos_casi_identicos_a_la_revisada' marca campos con similitud >= 90% (con el porcentaje):");
        sb.AppendLine("     es copia/pega con minimos cambios SOLO si el texto repite hechos propios de OTRO turno o fecha");
        sb.AppendLine("     (horas, eventos, rotulos de fecha, valores); si el parecido es solo la plantilla o frases");
        sb.AppendLine("     estandar del servicio (p.ej. la entrega de turno de enfermeria), NO es motivo de rechazo.");
        sb.AppendLine("  4) CONTRADICCIONES: antecedentes, alergias, lateralidad, diagnostico u otros datos que contradigan");
        sb.AppendLine("     el historial sin justificacion en el documento_a_revisar.");
        sb.AppendLine("- PESO DEL HISTORIAL: copia/pega EVIDENTE de campos clinicos (evolucion, analisis, plan) o una");
        sb.AppendLine("  contradiccion clinica RELEVANTE pueden llevar a 'Rechazado'. Diferencias menores o una evolucion");
        sb.AppendLine("  poco detallada son hallazgos que NO cambian el veredicto por si solos. Que se repita un dato");
        sb.AppendLine("  administrativo o estable (nombre, documento, diagnostico de base) NO es copia/pega.");
        sb.AppendLine("- MEDICAMENTOS Y ORDENES: una nota de enfermeria o de terapia NO genera ordenes propias; administra");
        sb.AppendLine("  lo que un medico ordeno en OTRA historia. Por eso 'list_ordenes_hc' vacio en esas notas es lo");
        sb.AppendLine("  normal y NO es hallazgo. Valida lo administrado contra 'historial_del_paciente' ->");
        sb.AppendLine("  'medicamentos_insumos_ordenados_en_ventana' (todo lo ordenado al paciente en la ventana). Un");
        sb.AppendLine("  medicamento sin orden en la ventana es una OBSERVACION (puede venir de una orden externa o");
        sb.AppendLine("  anterior a la ventana), nunca motivo de rechazo por si solo.");
        sb.AppendLine("- NO GENERES RUIDO: no reportes como hallazgo que algo narrado en el texto (escalas, ordenes,");
        sb.AppendLine("  procedimientos) no este registrado ademas en OTRO modulo del sistema si el formato no lo exige.");
        sb.AppendLine("  Errores de digitacion solo son hallazgo si cambian el sentido clinico o hacen ilegible el dato.");
        sb.AppendLine("- TEXTO DE PLANTILLA (regla dura): en get_form_definition, los campos con 'origen' AUTOGENERADO los");
        sb.AppendLine("  arma el FORMATO a partir de otros campos (p.ej. el renglon 'TA: .. FC: .. SpO2: ..' armado desde");
        sb.AppendLine("  los signos vitales). Las claves '__tmpl:...' de ValoresJson son copias internas del sistema:");
        sb.AppendLine("  IGNORALAS. Que un texto AUTOGENERADO se repita igual en varios turnos o secciones NO es copia/pega");
        sb.AppendLine("  ni falta del profesional. Lo que SI evaluas es el CONTENIDO clinico de esos valores (p.ej. una");
        sb.AppendLine("  TA o SpO2 fuera de rango registrada como 'estable').");
        sb.AppendLine("- MEDICACION PREVIA vs NUEVA FORMULACION (regla dura): distingue la medicacion que el paciente");
        sb.AppendLine("  VENIA tomando (textos como 'en manejo con', 'medicamentos actuales', 'antecedentes farmacologicos',");
        sb.AppendLine("  conciliacion) de lo que se FORMULA en esta atencion (plan, formula, tabla de medicamentos).");
        sb.AppendLine("  Suspender, cambiar de molecula (p.ej. omeprazol -> esomeprazol) o ajustar dosis/esquema frente a");
        sb.AppendLine("  lo previo es una DECISION MEDICA, NO una contradiccion: a lo sumo una observacion menor sugiriendo");
        sb.AppendLine("  dejar escrito el motivo del cambio, nunca motivo de rechazo. SI es hallazgo cuando DENTRO de la");
        sb.AppendLine("  nueva formulacion el MISMO medicamento aparece con posologias distintas, o cuando el plan nombra");
        sb.AppendLine("  un medicamento/examen que no aparece en la formula u ordenes de esta misma historia.");
        sb.AppendLine("- CALIFICADORES DE TIEMPO Y CONDICION: lee completos los calificadores ('en el dia', 'nocturno',");
        sb.AppendLine("  'en reposo', 'durante la visita', 'al momento', 'previo', 'por ahora'). Antes de reportar una");
        sb.AppendLine("  contradiccion verifica que ambos textos hablen del MISMO momento, condicion y sujeto: 'sin oxigeno");
        sb.AppendLine("  en el dia, ni VMI' NO contradice el uso de VMI nocturna. Ante la duda, NO es contradiccion.");
        sb.AppendLine("- TABLAS DEL FORMATO: en get_form_definition cada tabla trae sus filas diligenciadas en 'Valor'");
        sb.AppendLine("  (tipo 'tabla (N filas diligenciadas)'). Una tabla solo esta vacia si su 'estado' es 'vacio'.");
        sb.AppendLine("- Las historias del historial con estado 'Abierta - en curso' son contexto PROVISIONAL: no bases un");
        sb.AppendLine("  rechazo solo en ellas. 'documentos' trae las historias anteriores COMPLETAS (las mas recientes);");
        sb.AppendLine("  'indice' lista el resto en una linea cada una: solo prueban que la atencion existio, no infieras");
        sb.AppendLine("  su contenido. Si el historial viene vacio o 'SIN HISTORIAL', evalua sin ese contraste y no lo");
        sb.AppendLine("  reportes como faltante.");
        sb.AppendLine("- TEXTO SUGERIDO ('sugerencias'): SOLO si el resultado NO es 'Aprobado', y SOLO para los campos de");
        sb.AppendLine("  texto del documento_a_revisar que motivaron un hallazgo (maximo 5). 'campo' = la etiqueta del campo");
        sb.AppendLine("  tal como aparece en el catalogo del formato. 'texto_sugerido' = como deberia haber quedado ese texto.");
        sb.AppendLine("  REGLAS DURAS: reescribe usando UNICAMENTE hechos que YA estan escritos en el documento_a_revisar;");
        sb.AppendLine("  NUNCA inventes ni completes signos vitales, dosis, horas, diagnosticos, actividades, respuestas del");
        sb.AppendLine("  paciente ni datos de otro turno o de otra historia. Si falta un dato que el profesional debe aportar,");
        sb.AppendLine("  deja el marcador [COMPLETAR: que dato falta]. Si el problema es texto copiado de otra atencion, quita");
        sb.AppendLine("  lo ajeno a esta atencion y marca con [COMPLETAR: ...] lo que debe describir de ESTE turno. Si el");
        sb.AppendLine("  resultado es 'Aprobado', devuelve \"sugerencias\": [].");
        sb.AppendLine("- Devuelve JSON puro. Nada de markdown, ni prefijos, ni sufijos, ni ```json```.");
        return sb.ToString();
    }

    // Agrupacion de las tools en los bloques del mensaje al modelo.
    private static readonly string[] ToolsDocumento =
    {
        RevisionMcpToolNames.GetHistoriaClinica, RevisionMcpToolNames.GetFormDefinition,
        RevisionMcpToolNames.ListOrdenesHc, RevisionMcpToolNames.ListEscalasHc,
        RevisionMcpToolNames.ListEvolucionesHc, RevisionMcpToolNames.ListConsentimientosHc,
        RevisionMcpToolNames.ListDocumentosHc,
    };
    private static readonly string[] ToolsPaciente =
    {
        RevisionMcpToolNames.GetPaciente, RevisionMcpToolNames.ListAsignacionesRelacionadas,
        RevisionMcpToolNames.ListNotasHc,
    };

    /// <summary>
    /// Arma el mensaje al modelo como UN JSON con bloques separados e inequivocos:
    /// <c>documento_a_revisar</c> (el objeto de la revision), <c>datos_del_paciente</c> (contexto
    /// administrativo) e <c>historial_del_paciente</c> (historias ANTERIORES, solo contexto).
    /// Cada payload de tool se inserta como JSON anidado (no como string escapado).
    /// </summary>
    public static string SerializarContexto(HistoriaClinicaContextoIa contexto, JsonObject? revisionAnterior = null,
        bool alCerrar = false)
    {
        var porTool = new Dictionary<string, ToolInvocationResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var inv in contexto.InvocacionesExitosas) { porTool.TryAdd(inv.ToolName, inv); }

        JsonNode? Payload(string tool)
        {
            if (!porTool.TryGetValue(tool, out var inv) || string.IsNullOrWhiteSpace(inv.JsonPayload)) { return null; }
            try { return JsonNode.Parse(inv.JsonPayload); }
            catch (JsonException) { return JsonValue.Create(inv.JsonPayload); }
        }

        var doc = new JsonObject
        {
            ["rol"] = "OBJETO DE LA REVISION. El veredicto, la nota y los hallazgos son SOLO sobre este documento.",
        };
        foreach (var t in ToolsDocumento) { if (porTool.ContainsKey(t)) { doc[t] = Payload(t); } }

        var pac = new JsonObject
        {
            ["rol"] = "Contexto administrativo del paciente (datos, asignaciones y notas). No se califica.",
        };
        foreach (var t in ToolsPaciente) { if (porTool.ContainsKey(t)) { pac[t] = Payload(t); } }

        // Tools exitosas no clasificadas (si se agregan nuevas) van con el documento para no perderlas.
        var conocidas = ToolsDocumento.Concat(ToolsPaciente).Append(RevisionMcpToolNames.HistorialPaciente)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var inv in contexto.InvocacionesExitosas.Where(i => !conocidas.Contains(i.ToolName)))
        {
            doc[inv.ToolName] = Payload(inv.ToolName);
        }

        var histObj = new JsonObject();
        if (Payload(RevisionMcpToolNames.HistorialPaciente) is JsonObject h)
        {
            histObj["rol"] = "SOLO CONTEXTO. Historias ANTERIORES del paciente (por fecha de atencion), en orden "
                           + "cronologico. NO se revisan ni se califican; sirven para contrastar el documento_a_revisar.";
            foreach (var kv in h.ToList())
            {
                h.Remove(kv.Key);
                histObj[kv.Key] = kv.Value;
            }
        }
        else
        {
            histObj["rol"] = "SIN HISTORIAL: no se envio historial del paciente para esta revision.";
        }

        var raiz = new JsonObject
        {
            ["documento_a_revisar"] = doc,
            ["datos_del_paciente"] = pac,
            ["historial_del_paciente"] = histObj,
        };
        if (alCerrar)
        {
            raiz["momento"] = "CIERRE: el profesional esta cerrando esta historia ahora mismo. Por eso su estado es "
                            + "'Abierta' y aun no tiene fecha de cierre; eso NO es hallazgo ni observacion.";
        }
        if (revisionAnterior is not null) { raiz["revision_anterior"] = revisionAnterior; }

        if (contexto.InvocacionesFallidas.Count > 0)
        {
            var errs = new JsonArray();
            foreach (var f in contexto.InvocacionesFallidas)
            {
                errs.Add(new JsonObject { ["tool"] = f.ToolName, ["error"] = f.Error ?? "(sin detalle)" });
            }
            raiz["tools_con_error"] = errs;
        }

        return raiz.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>Persiste el log completo de una corrida del agente (best-effort). Nunca lanza.
    /// <paramref name="revisionId"/> null = intento de cierre todavia sin ciclo de revision.
    /// Devuelve el Id del log (null si no se pudo guardar).</summary>
    private async Task<Guid?> GuardarAgenteLogAsync(
        IApplicationDbContext db,
        Guid tenantId, Guid? revisionId, Guid historiaClinicaId, AuditoriaIaTraza traza,
        bool ok, string? error, string? resultado, decimal? confianza,
        int tokensIn, int tokensOut, CancellationToken ct,
        string? nota = null, IReadOnlyList<string>? hallazgos = null,
        IReadOnlyList<SugerenciaTexto>? sugerencias = null, string origen = OrigenRevision,
        IReadOnlyList<string>? observaciones = null)
    {
        var contexto = traza.Contexto;
        try
        {
            var camposJson = await BuildCamposLegiblesAsync(db, historiaClinicaId, ct);
            string? toolsJson = null;
            if (contexto is not null)
            {
                var tools = contexto.InvocacionesExitosas
                    .Select(i => new { tool = i.ToolName, ok = true, tokens = i.EstimatedTokens, error = (string?)null, payload = i.JsonPayload })
                    .Concat(contexto.InvocacionesFallidas
                        .Select(i => new { tool = i.ToolName, ok = false, tokens = i.EstimatedTokens, error = i.Error, payload = i.JsonPayload }))
                    .ToList();
                toolsJson = JsonSerializer.Serialize(tools, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            var log = new RevisionAgenteLog
            {
                TenantId = tenantId,
                CreatedAt = DateTimeOffset.UtcNow, // el interceptor lo vuelve a sellar; sin el (tests) queda la hora real
                RevisionClinicaId = revisionId,
                HistoriaClinicaId = historiaClinicaId,
                AgenteCodigo = traza.AgenteNombre,
                Origen = origen,
                Proveedor = traza.Proveedor,
                Modelo = traza.Modelo,
                Ok = ok,
                Error = error,
                Resultado = resultado,
                Confianza = confianza,
                TokensIn = tokensIn,
                TokensOut = tokensOut,
                Nota = nota,
                HallazgosJson = HallazgosIa.Serializar(hallazgos, observaciones),
                SugerenciasJson = sugerencias is { Count: > 0 }
                    ? JsonSerializer.Serialize(sugerencias.Select(x => new { campo = x.Campo, texto_sugerido = x.TextoSugerido }),
                        new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                    : null,
                SystemPrompt = traza.SystemPrompt,
                ContextoEnviado = traza.ContextoJson,
                CamposLegiblesJson = camposJson,
                ToolsJson = toolsJson,
                RespuestaCruda = traza.RespuestaCruda,
            };
            db.RevisionAgenteLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return log.Id;
        }
        catch { return null; /* el log es best-effort; nunca romper la revision */ }
    }

    /// <summary>HC legible como JSON { "label del campo": "contenido" } cruzando ValoresJson con el
    /// esquema del formato. Solo para mostrar en el log (no es lo que se le envia al agente).</summary>
    private static async Task<string?> BuildCamposLegiblesAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new { h.ValoresJson, h.FormDefinitionId })
            .FirstOrDefaultAsync(ct);
        if (hc is null || string.IsNullOrWhiteSpace(hc.ValoresJson)) { return null; }
        var schemaJson = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == hc.FormDefinitionId)
            .Select(f => f.SchemaJson)
            .FirstOrDefaultAsync(ct);
        return CamposLegibles(hc.ValoresJson, schemaJson);
    }

    private static string? CamposLegibles(string valoresJson, string? schemaJson)
    {
        Dictionary<string, string?>? v;
        try { v = JsonSerializer.Deserialize<Dictionary<string, string?>>(valoresJson); }
        catch { return null; }
        if (v is null || v.Count == 0) { return null; }

        var keyToLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                var schema = FormSchema.FromJson(schemaJson);
                if (schema?.Children is not null)
                {
                    void Recurse(IEnumerable<FormNode> nodes)
                    {
                        foreach (var n in nodes)
                        {
                            if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                            if (!string.IsNullOrWhiteSpace(n.Name) && !string.IsNullOrWhiteSpace(n.Label))
                            {
                                keyToLabel[n.Name!] = n.Label!;
                            }
                        }
                    }
                    Recurse(schema.Children);
                }
                if (schema?.Header?.Campos is { Count: > 0 } campos)
                {
                    foreach (var c in campos)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Label)) { keyToLabel["hdr:" + c.Id] = c.Label!; }
                    }
                }
            }
            catch { /* sin labels, se usan las claves crudas */ }
        }

        var outMap = new Dictionary<string, object?>();
        foreach (var kv in v)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) { continue; }
            var label = keyToLabel.TryGetValue(kv.Key!, out var lbl) && !string.IsNullOrWhiteSpace(lbl) ? lbl : kv.Key!;
            var valor = kv.Value;
            if ((kv.Key ?? "").Contains("firma", StringComparison.OrdinalIgnoreCase)) { valor = "[firma registrada]"; } // firmas base64
            var finalLabel = label;
            var i = 2;
            while (outMap.ContainsKey(finalLabel)) { finalLabel = label + " (" + i++ + ")"; }
            outMap[finalLabel] = valor;
        }
        if (outMap.Count == 0) { return null; }
        return JsonSerializer.Serialize(outMap, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private sealed record VeredictoParsed(RevisionResultado Resultado, decimal Confianza, string? Nota,
        IReadOnlyList<string> Hallazgos, IReadOnlyList<SugerenciaTexto> Sugerencias, IReadOnlyList<string> Observaciones);

    /// <summary>Solo para tests: (hallazgos, observaciones) tal como se interpretan de la respuesta.</summary>
    public static (IReadOnlyList<string> Hallazgos, IReadOnlyList<string> Observaciones) ParsearHallazgos(string texto)
    {
        var p = ParseVeredicto(texto);
        return (p.Hallazgos, p.Observaciones);
    }

    /// <summary>Solo para tests: parsea la respuesta del modelo y devuelve las sugerencias.</summary>
    public static IReadOnlyList<SugerenciaTexto> ParsearSugerencias(string texto) => ParseVeredicto(texto).Sugerencias;

    private static VeredictoParsed ParseVeredicto(string texto)
    {
        var json = ExtraerJson(texto);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var resultadoStr = root.TryGetProperty("resultado", out var rEl) ? rEl.GetString() : "Neutral";
        var resultado = ParseResultado(resultadoStr);

        decimal confianza = 0m;
        if (root.TryGetProperty("confianza", out var cEl))
        {
            if (cEl.ValueKind == JsonValueKind.Number) { confianza = cEl.GetDecimal(); }
            else if (cEl.ValueKind == JsonValueKind.String && decimal.TryParse(cEl.GetString(), out var d)) { confianza = d; }
        }
        if (confianza < 0m) { confianza = 0m; }
        if (confianza > 1m) { confianza = 1m; }

        var nota = root.TryGetProperty("nota", out var nEl) ? nEl.GetString() : null;

        static List<string> Lista(JsonElement root, string nombre)
        {
            var res = new List<string>();
            if (root.TryGetProperty(nombre, out var el) && el.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in el.EnumerateArray())
                {
                    var s = it.ValueKind == JsonValueKind.String ? it.GetString() : it.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) { res.Add(s!.Trim()); }
                }
            }
            return res;
        }
        // IMP-73: 'hallazgos' = lo que hay que corregir; 'observaciones' = menor/administrativo. Si el
        // modelo igual mete en hallazgos una validacion positiva o una observacion declarada, se mueve.
        var hallazgos = new List<string>();
        var observaciones = Lista(root, "observaciones").Select(QuitarPrefijoObservacion).ToList();
        foreach (var h in Lista(root, "hallazgos"))
        {
            if (EsValidacionPositiva(h)) { continue; }
            if (EsObservacionDeclarada(h)) { observaciones.Add(QuitarPrefijoObservacion(h)); continue; }
            hallazgos.Add(h);
        }

        // Texto sugerido (opcional): [{ campo, texto_sugerido }]. Se toleran nombres alternos y
        // la ausencia del bloque. Solo lectura: nunca se aplica a la HC.
        var sugerencias = new List<SugerenciaTexto>();
        if (root.TryGetProperty("sugerencias", out var sEl) && sEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in sEl.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) { continue; }
                string? Prop(params string[] nombres)
                {
                    foreach (var n in nombres)
                    {
                        if (it.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String) { return v.GetString(); }
                    }
                    return null;
                }
                var campo = Prop("campo", "field");
                var sugerido = Prop("texto_sugerido", "textoSugerido", "sugerencia");
                if (!string.IsNullOrWhiteSpace(campo) && !string.IsNullOrWhiteSpace(sugerido))
                {
                    sugerencias.Add(new SugerenciaTexto(campo!.Trim(), sugerido!.Trim()));
                }
            }
        }

        return new VeredictoParsed(resultado, confianza, nota, hallazgos, sugerencias, observaciones);
    }

    private static readonly System.Text.RegularExpressions.Regex RxValidacion = new(
        @"^\W*(verificado|validado|validacion|se valida|se verifica|completitud( formal| conforme| del formato)?\s*:|aspectos validados|sin hallazgos?\b)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RxObsDeclarada = new(
        @"^\W*observaci[o\u00f3]n( menor| administrativa)?\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"Verificado sin hallazgo: ...", "Completitud: los 11 campos estan llenos..." no son hallazgos.</summary>
    internal static bool EsValidacionPositiva(string h)
    {
        var t = SinTildes(h);
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\bsin hallazgo")) { return true; }
        return RxValidacion.IsMatch(t) && !System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(pero|sin embargo|excepto|falta|faltan|vacio|vacia|no coincide|incongru|contradic)");
    }

    private static string SinTildes(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) { sb.Append(ch); }
        }
        return sb.ToString();
    }

    internal static bool EsObservacionDeclarada(string h) => RxObsDeclarada.IsMatch(h.TrimStart());

    private static string QuitarPrefijoObservacion(string h)
    {
        var t = HallazgosIa.EsObservacion(h) ? h[HallazgosIa.PrefijoObservacion.Length..] : h;
        var m = RxObsDeclarada.Match(t.TrimStart());
        if (m.Success)
        {
            t = t.TrimStart()[m.Length..].TrimStart(' ', ':', '-', '(', ')');
            if (t.StartsWith("riesgo bajo)", StringComparison.OrdinalIgnoreCase)) { t = t["riesgo bajo)".Length..].TrimStart(' ', ':'); }
        }
        return t.Trim();
    }

    private static RevisionResultado ParseResultado(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return RevisionResultado.Neutral; }
        var norm = s.Trim();
        if (Enum.TryParse<RevisionResultado>(norm, ignoreCase: true, out var val)) { return val; }
        return RevisionResultado.Neutral;
    }

    private static string ExtraerJson(string texto)
    {
        // Si el modelo devuelve el JSON dentro de una fence ```json ... ``` o texto adicional,
        // tomamos el primer bloque { ... } balanceado. Es defensivo pero simple.
        var start = texto.IndexOf('{');
        var end = texto.LastIndexOf('}');
        if (start < 0 || end <= start) { return texto; }
        return texto.Substring(start, end - start + 1);
    }

    private static PreRevisionIaResult Err(string msg)
        => new(false, null, 0m, null, null, msg, 0, 0);
}
