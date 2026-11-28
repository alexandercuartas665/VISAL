using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;
using Visal.Domain.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Revision.Bitacora;

/// <summary>IMP-72. Ver <see cref="IBitacoraIaService"/>.</summary>
public sealed class BitacoraIaService : IBitacoraIaService
{
    private static readonly TimeSpan Bogota = TimeSpan.FromHours(-5);
    /// <summary>Tope de historias que se analizan para el filtro "solo con alertas" y el KPI.</summary>
    private const int TopeAnalisis = 1000;
    private const int Lote = 100;

    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public BitacoraIaService(IApplicationDbContextFactory dbFactory, ISecretProtector secretProtector, IAuditWriter audit, TimeProvider time)
    {
        _dbFactory = dbFactory;
        _secretProtector = secretProtector;
        _audit = audit;
        _time = time;
    }

    // ===================================================================================
    //  Consultas
    // ===================================================================================

    public async Task<BitacoraResumenDto> ResumenAsync(Guid tenantId, BitacoraFiltro filtro, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var q = Base(db, tenantId, filtro);

        var porResultado = await q.GroupBy(l => new { l.Ok, l.Resultado })
            .Select(g => new { g.Key.Ok, g.Key.Resultado, N = g.Count() })
            .ToListAsync(ct);
        int Cuenta(string r) => porResultado.Where(x => x.Ok && x.Resultado == r).Sum(x => x.N);

        var porHc = await q.GroupBy(l => l.HistoriaClinicaId)
            .Select(g => new { HcId = g.Key, N = g.Count(), Ultima = g.Max(x => x.CreatedAt) })
            .ToListAsync(ct);
        var multi = porHc.Where(x => x.N >= 2).OrderByDescending(x => x.Ultima).Select(x => x.HcId).ToList();

        var conAlertas = 0;
        foreach (var lote in multi.Take(TopeAnalisis).Chunk(Lote))
        {
            var datos = await CargarHistoriasAsync(db, tenantId, lote, ct);
            conAlertas += datos.Values.Count(d => d.Fila.Senales.Total > 0);
        }

        var tokensIn = await q.SumAsync(l => (long)l.TokensIn, ct);
        var tokensOut = await q.SumAsync(l => (long)l.TokensOut, ct);
        var arriba = await q.CountAsync(l => l.FeedbackUtil == true, ct);
        var abajo = await q.CountAsync(l => l.FeedbackUtil == false, ct);

        // Por profesional (de la HC): una fila liviana por corrida, se agrupa en memoria.
        var filasProf = await (
            from l in q
            join h in db.HistoriasClinicas.IgnoreQueryFilters() on l.HistoriaClinicaId equals h.Id
            select new { h.ProfesionalId, l.HistoriaClinicaId, Rech = l.Ok && l.Resultado == "Rechazado" })
            .ToListAsync(ct);
        var profIds = filasProf.Where(x => x.ProfesionalId != null).Select(x => x.ProfesionalId!.Value).Distinct().ToList();
        var nombresProf = await db.Profesionales.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenantId && profIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto })
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);
        var profesionales = filasProf
            .GroupBy(x => x.ProfesionalId)
            .Select(g => new BitacoraProfesionalDto(
                g.Key is Guid pid && nombresProf.TryGetValue(pid, out var n) ? n : "(sin profesional)",
                g.Count(), g.Count(x => x.Rech), g.Select(x => x.HistoriaClinicaId).Distinct().Count()))
            .OrderByDescending(x => x.Corridas)
            .ToList();

        return new BitacoraResumenDto(
            porResultado.Sum(x => x.N), porHc.Count,
            Cuenta("Aprobado"), Cuenta("Rechazado"), Cuenta("Neutral"),
            porResultado.Where(x => !x.Ok).Sum(x => x.N),
            multi.Count, conAlertas, multi.Count > TopeAnalisis,
            arriba, abajo, tokensIn, tokensOut, profesionales);
    }

    public async Task<BitacoraPagina<BitacoraHcFilaDto>> ListarHistoriasAsync(Guid tenantId, BitacoraFiltro filtro, int page, int size, CancellationToken ct = default)
    {
        (page, size) = Normalizar(page, size);
        using var db = _dbFactory.Create();
        var grupos = Base(db, tenantId, filtro)
            .GroupBy(l => l.HistoriaClinicaId)
            .Select(g => new { HcId = g.Key, N = g.Count(), Ultima = g.Max(x => x.CreatedAt) });

        if (!filtro.SoloAlertas)
        {
            var total = await grupos.CountAsync(ct);
            var ids = await grupos.OrderByDescending(x => x.Ultima).ThenBy(x => x.HcId)
                .Skip((page - 1) * size).Take(size).Select(x => x.HcId).ToListAsync(ct);
            var datos = await CargarHistoriasAsync(db, tenantId, ids, ct);
            return new BitacoraPagina<BitacoraHcFilaDto>(
                ids.Where(datos.ContainsKey).Select(i => datos[i].Fila).ToList(), total, page, size);
        }

        // Solo con alertas: necesita al menos 2 corridas; se analiza por lotes (con tope).
        var candidatos = await grupos.Where(x => x.N >= 2).OrderByDescending(x => x.Ultima).ThenBy(x => x.HcId)
            .Select(x => x.HcId).Take(TopeAnalisis).ToListAsync(ct);
        var conAlerta = new List<BitacoraHcFilaDto>();
        foreach (var lote in candidatos.Chunk(Lote))
        {
            var datos = await CargarHistoriasAsync(db, tenantId, lote, ct);
            conAlerta.AddRange(lote.Where(datos.ContainsKey).Select(i => datos[i].Fila).Where(f => f.Senales.Total > 0));
        }
        return new BitacoraPagina<BitacoraHcFilaDto>(
            conAlerta.Skip((page - 1) * size).Take(size).ToList(), conAlerta.Count, page, size);
    }

    public async Task<BitacoraPagina<BitacoraCorridaDto>> ListarCorridasAsync(Guid tenantId, BitacoraFiltro filtro, int page, int size, CancellationToken ct = default)
    {
        (page, size) = Normalizar(page, size);
        using var db = _dbFactory.Create();
        var q = Base(db, tenantId, filtro);
        var total = await q.CountAsync(ct);
        var logs = await Proyectar(q.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * size).Take(size)).ToListAsync(ct);
        var usuarios = await NombresUsuariosAsync(db, logs, ct);
        return new BitacoraPagina<BitacoraCorridaDto>(logs.Select(l => ToDto(l, usuarios)).ToList(), total, page, size);
    }

    public async Task<BitacoraHcDetalleDto?> DetalleHistoriaAsync(Guid tenantId, string hcRef, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var hcId = await ResolverHcAsync(db, tenantId, hcRef, ct);
        if (hcId is null) { return null; }
        var datos = await CargarHistoriasAsync(db, tenantId, [hcId.Value], ct);
        if (!datos.TryGetValue(hcId.Value, out var d)) { return null; }
        return new BitacoraHcDetalleDto(d.Fila, d.Corridas, d.Analisis);
    }

    public async Task<BitacoraCorridaCompletaDto?> CorridaAsync(Guid tenantId, Guid logId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var l = await db.RevisionAgenteLogs.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == logId && x.TenantId == tenantId, ct);
        if (l is null) { return null; }
        var lite = new LogLite(l.Id, l.HistoriaClinicaId, l.CreatedAt, l.CreatedBy, l.Origen, l.RevisionClinicaId != null,
            l.AgenteCodigo, l.Modelo, l.Ok, l.Error, l.Resultado, l.Confianza, l.TokensIn, l.TokensOut, l.Nota,
            l.HallazgosJson, l.SugerenciasJson, l.FeedbackUtil, l.FeedbackComentario, l.FeedbackUsuarioId, l.FeedbackEn);
        var usuarios = await NombresUsuariosAsync(db, [lite], ct);
        var consecutivo = await db.HistoriasClinicas.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.Id == l.HistoriaClinicaId).Select(h => (long?)h.Consecutivo).FirstOrDefaultAsync(ct);
        return new BitacoraCorridaCompletaDto(ToDto(lite, usuarios),
            consecutivo is long c ? CodigoPublico.Hc(c) : "",
            l.Proveedor, l.SystemPrompt, l.ContextoEnviado, l.CamposLegiblesJson, l.ToolsJson, l.RespuestaCruda);
    }

    // ===================================================================================
    //  API de solo lectura (llave)
    // ===================================================================================

    public async Task<BitacoraApiConfigDto> GetApiAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await db.BitacoraIaApiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        return cfg is null
            ? new BitacoraApiConfigDto(false, null, false, null)
            : new BitacoraApiConfigDto(!string.IsNullOrEmpty(cfg.ApiKeyHash), Decrypt(cfg.ApiKeyEncrypted), cfg.IsEnabled, cfg.LastUsedAt);
    }

    public async Task<BitacoraApiConfigDto> RegenerarApiAsync(Guid tenantId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await db.BitacoraIaApiConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        var nuevo = cfg is null;
        if (cfg is null) { cfg = new BitacoraIaApiConfig { TenantId = tenantId, IsEnabled = true }; db.BitacoraIaApiConfigs.Add(cfg); }
        var key = "vbi_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        cfg.ApiKeyHash = Hash(key);
        cfg.ApiKeyEncrypted = _secretProtector.Protect(key);
        _audit.Write(db, actor, nuevo ? "bitacora-ia-api.crear" : "bitacora-ia-api.regenerar",
            nameof(BitacoraIaApiConfig), cfg.Id, previousValue: null, newValue: new { cfg.IsEnabled }, tenantId: tenantId);
        await db.SaveChangesAsync(ct);
        return new BitacoraApiConfigDto(true, key, cfg.IsEnabled, cfg.LastUsedAt);
    }

    public async Task<BitacoraApiConfigDto> SetApiHabilitadaAsync(Guid tenantId, bool enabled, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await db.BitacoraIaApiConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (cfg is null) { return new BitacoraApiConfigDto(false, null, false, null); }
        cfg.IsEnabled = enabled;
        _audit.Write(db, actor, "bitacora-ia-api.habilitar", nameof(BitacoraIaApiConfig), cfg.Id,
            previousValue: null, newValue: new { enabled }, tenantId: tenantId);
        await db.SaveChangesAsync(ct);
        return new BitacoraApiConfigDto(true, Decrypt(cfg.ApiKeyEncrypted), cfg.IsEnabled, cfg.LastUsedAt);
    }

    public async Task<Guid?> ResolverLlaveAsync(string apiKey, string endpoint, string? query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { return null; }
        using var db = _dbFactory.Create();
        var hash = Hash(apiKey.Trim());
        var cfg = await db.BitacoraIaApiConfigs.FirstOrDefaultAsync(c => c.ApiKeyHash == hash, ct);
        if (cfg is null || !cfg.IsEnabled) { return null; }
        cfg.LastUsedAt = _time.GetUtcNow();
        var q = query is { Length: > 500 } ? query[..500] : query;
        _audit.Write(db, Guid.Empty, "bitacora-ia-api.consulta", nameof(BitacoraIaApiConfig), cfg.Id,
            previousValue: null, newValue: new { endpoint, query = q }, tenantId: cfg.TenantId, actorType: AuditActorType.System);
        await db.SaveChangesAsync(ct);
        return cfg.TenantId;
    }

    // ===================================================================================
    //  Internos
    // ===================================================================================

    private sealed record LogLite(
        Guid Id, Guid HistoriaClinicaId, DateTimeOffset CreatedAt, Guid? CreatedBy, string? Origen, bool EnCiclo,
        string AgenteCodigo, string? Modelo, bool Ok, string? Error, string? Resultado, decimal? Confianza,
        int TokensIn, int TokensOut, string? Nota, string? HallazgosJson, string? SugerenciasJson,
        bool? FeedbackUtil, string? FeedbackComentario, Guid? FeedbackUsuarioId, DateTimeOffset? FeedbackEn);

    private sealed record HcDatos(BitacoraHcFilaDto Fila, IReadOnlyList<BitacoraCorridaDto> Corridas, IReadOnlyList<BitacoraIntentoAnalisis> Analisis);

    private static IQueryable<LogLite> Proyectar(IQueryable<RevisionAgenteLog> q)
        => q.Select(l => new LogLite(l.Id, l.HistoriaClinicaId, l.CreatedAt, l.CreatedBy, l.Origen, l.RevisionClinicaId != null,
            l.AgenteCodigo, l.Modelo, l.Ok, l.Error, l.Resultado, l.Confianza, l.TokensIn, l.TokensOut, l.Nota,
            l.HallazgosJson, l.SugerenciasJson, l.FeedbackUtil, l.FeedbackComentario, l.FeedbackUsuarioId, l.FeedbackEn));

    private static (int Page, int Size) Normalizar(int page, int size)
        => (Math.Max(1, page), Math.Clamp(size <= 0 ? 50 : size, 1, IBitacoraIaService.MaxSize));

    /// <summary>Corridas del tenant con los filtros aplicados. Fechas en dia local de Colombia.</summary>
    private static IQueryable<RevisionAgenteLog> Base(IApplicationDbContext db, Guid tenantId, BitacoraFiltro f)
    {
        var q = db.RevisionAgenteLogs.IgnoreQueryFilters().AsNoTracking().Where(l => l.TenantId == tenantId);
        if (f.Desde is DateOnly d)
        {
            var ini = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), Bogota).ToUniversalTime();
            q = q.Where(l => l.CreatedAt >= ini);
        }
        if (f.Hasta is DateOnly h)
        {
            var fin = new DateTimeOffset(h.AddDays(1).ToDateTime(TimeOnly.MinValue), Bogota).ToUniversalTime();
            q = q.Where(l => l.CreatedAt < fin);
        }
        if (!string.IsNullOrWhiteSpace(f.Resultado))
        {
            var r = f.Resultado.Trim();
            if (r.Equals("Fallida", StringComparison.OrdinalIgnoreCase)) { q = q.Where(l => !l.Ok); }
            else
            {
                var rr = char.ToUpperInvariant(r[0]) + r[1..].ToLowerInvariant();
                q = q.Where(l => l.Ok && l.Resultado == rr);
            }
        }
        if (!string.IsNullOrWhiteSpace(f.Origen))
        {
            q = f.Origen.Trim().Equals(PreRevisionIaService.OrigenCierre, StringComparison.OrdinalIgnoreCase)
                ? q.Where(l => l.Origen == PreRevisionIaService.OrigenCierre)
                : q.Where(l => l.Origen == null || l.Origen != PreRevisionIaService.OrigenCierre);
        }
        if (f.SoloPulgarAbajo) { q = q.Where(l => l.FeedbackUtil == false); }

        var hcs = db.HistoriasClinicas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId);
        var filtraHc = false;
        if (!string.IsNullOrWhiteSpace(f.Hc))
        {
            var txt = f.Hc.Trim();
            if (Guid.TryParse(txt, out var gid)) { hcs = hcs.Where(x => x.Id == gid); }
            else
            {
                var n = CodigoPublico.ParseNumero(txt);
                hcs = n is long num
                    ? hcs.Where(x => x.Consecutivo == num || x.Paciente!.NumeroDocumento == txt)
                    : hcs.Where(x => x.Paciente!.NumeroDocumento == txt);
            }
            filtraHc = true;
        }
        if (!string.IsNullOrWhiteSpace(f.Profesional))
        {
            var p = f.Profesional.Trim().ToLower();
            hcs = hcs.Where(x => x.Profesional != null && x.Profesional.NombreCompleto.ToLower().Contains(p));
            filtraHc = true;
        }
        if (!string.IsNullOrWhiteSpace(f.Formato))
        {
            var fo = f.Formato.Trim().ToLower();
            hcs = hcs.Where(x => x.FormDefinition!.Codigo.ToLower().Contains(fo) || x.FormDefinition.Nombre.ToLower().Contains(fo));
            filtraHc = true;
        }
        if (filtraHc)
        {
            var ids = hcs.Select(x => x.Id);
            q = q.Where(l => ids.Contains(l.HistoriaClinicaId));
        }
        return q;
    }

    /// <summary>Carga TODAS las corridas de cada HC (no solo las del rango) y las analiza.</summary>
    private async Task<Dictionary<Guid, HcDatos>> CargarHistoriasAsync(IApplicationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var res = new Dictionary<Guid, HcDatos>();
        if (ids.Count == 0) { return res; }
        var idList = ids.ToList();

        var info = await db.HistoriasClinicas.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.TenantId == tenantId && idList.Contains(h.Id))
            .Select(h => new
            {
                h.Id, h.Consecutivo, h.Estado,
                Paciente = h.Paciente!.NombreCompleto,
                Doc = h.Paciente.TipoDocumento + " " + h.Paciente.NumeroDocumento,
                FCod = h.FormDefinition!.Codigo,
                FNom = h.FormDefinition.Nombre,
                Prof = h.Profesional != null ? h.Profesional.NombreCompleto : null,
            })
            .ToDictionaryAsync(h => h.Id, ct);

        var logs = await Proyectar(db.RevisionAgenteLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.TenantId == tenantId && idList.Contains(l.HistoriaClinicaId)))
            .ToListAsync(ct);
        var campos = await db.RevisionAgenteLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tenantId && idList.Contains(l.HistoriaClinicaId) && l.Ok)
            .Select(l => new { l.Id, l.CamposLegiblesJson })
            .ToDictionaryAsync(x => x.Id, x => x.CamposLegiblesJson, ct);
        var usuarios = await NombresUsuariosAsync(db, logs, ct);

        foreach (var g in logs.GroupBy(l => l.HistoriaClinicaId))
        {
            if (!info.TryGetValue(g.Key, out var hc)) { continue; }
            var orden = g.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToList();
            var corridas = orden.Select(l => ToDto(l, usuarios)).ToList();
            var analisis = BitacoraIaAnalisis.Analizar(orden.Select((l, i) => new BitacoraIntentoInput(
                l.Id, l.CreatedAt, l.Ok, l.Resultado,
                corridas[i].Hallazgos, corridas[i].Sugerencias,
                BitacoraIaAnalisis.ParseCampos(campos.TryGetValue(l.Id, out var cj) ? cj : null))).ToList());
            var ultima = orden[^1];
            var ultimaOk = orden.LastOrDefault(l => l.Ok);
            var fila = new BitacoraHcFilaDto(
                g.Key, CodigoPublico.Hc(hc.Consecutivo), hc.Paciente, hc.Doc, hc.FCod, hc.FNom, hc.Prof,
                hc.Estado.ToString(), orden.Count, orden.Count(l => !l.Ok),
                ultimaOk?.Resultado, ultimaOk?.Confianza, orden[0].CreatedAt, ultima.CreatedAt,
                orden.Count(l => l.FeedbackUtil == true), orden.Count(l => l.FeedbackUtil == false),
                BitacoraIaAnalisis.Senales(analisis));
            res[g.Key] = new HcDatos(fila, corridas, analisis);
        }
        return res;
    }

    private static BitacoraCorridaDto ToDto(LogLite l, IReadOnlyDictionary<Guid, string> usuarios) => new(
        l.Id, l.HistoriaClinicaId, l.CreatedAt,
        l.Origen ?? (l.EnCiclo ? PreRevisionIaService.OrigenRevision : PreRevisionIaService.OrigenCierre),
        l.CreatedBy is Guid u && usuarios.TryGetValue(u, out var un) ? un : null,
        l.EnCiclo, l.AgenteCodigo, l.Modelo, l.Ok, l.Error, l.Resultado, l.Confianza, l.TokensIn, l.TokensOut, l.Nota,
        BitacoraIaAnalisis.ParseHallazgos(l.HallazgosJson),
        BitacoraIaAnalisis.ParseSugerencias(l.SugerenciasJson),
        l.FeedbackUtil, l.FeedbackComentario,
        l.FeedbackUsuarioId is Guid fu && usuarios.TryGetValue(fu, out var fn) ? fn : null,
        l.FeedbackEn)
    {
        Observaciones = BitacoraIaAnalisis.ParseObservaciones(l.HallazgosJson),
    };

    private static async Task<IReadOnlyDictionary<Guid, string>> NombresUsuariosAsync(IApplicationDbContext db, IEnumerable<LogLite> logs, CancellationToken ct)
    {
        var ids = logs.SelectMany(l => new[] { l.CreatedBy, l.FeedbackUsuarioId })
            .Where(x => x is Guid g && g != Guid.Empty).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) { return new Dictionary<Guid, string>(); }
        var us = await db.PlatformUsers.IgnoreQueryFilters().AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.PrimerNombre, u.PrimerApellido, u.Email })
            .ToListAsync(ct);
        return us.ToDictionary(u => u.Id, u =>
            !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName!
            : !string.IsNullOrWhiteSpace(u.PrimerNombre) ? (u.PrimerNombre + " " + u.PrimerApellido).Trim()
            : u.Email);
    }

    private static async Task<Guid?> ResolverHcAsync(IApplicationDbContext db, Guid tenantId, string hcRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hcRef)) { return null; }
        var hcs = db.HistoriasClinicas.IgnoreQueryFilters().AsNoTracking().Where(h => h.TenantId == tenantId);
        if (Guid.TryParse(hcRef.Trim(), out var g))
        {
            return await hcs.Where(h => h.Id == g).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(ct);
        }
        return CodigoPublico.ParseNumero(hcRef) is long n
            ? await hcs.Where(h => h.Consecutivo == n).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(ct)
            : null;
    }

    private string? Decrypt(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) { return null; }
        try { return _secretProtector.Unprotect(enc); } catch { return null; }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
