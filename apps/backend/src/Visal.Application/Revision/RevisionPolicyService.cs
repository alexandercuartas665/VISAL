using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>
/// Servicio del singleton <see cref="RevisionPolicy"/>. Fetch idempotente por
/// tenant activo — si no hay fila, se devuelven los defaults del enum sin
/// crearla. El upsert la crea la primera vez que un Owner guarda.
///
/// Ola 6 (RC6a): agregamos cache in-memory por tenant con TTL de 5 min. La
/// policy cambia raro (config manual del Owner), pero `GetAsync` se llama por
/// cada `HistoriaClinicaService.CerrarAsync`. `SaveAsync` invalida la entrada
/// del tenant en la misma request para no servir data stale.
/// </summary>
// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class RevisionPolicyService : IRevisionPolicyService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;

    private const string CacheKeyPrefix = "revision-policy:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Defaults del enum — ver [[1. Vision, arquitectura y decisiones]] §4 (D5).</summary>
    private static readonly RevisionPolicyDto Defaults = new(
        AutoTriggerCierre: false,
        PreRevisionIAAutoTrigger: false,
        AdopcionAutomaticaAgente: false,
        UmbralConfianza: 0.95m,
        VentanaAsignacionesRelacionadasDias: 30,
        ConfirmarAprobado: false,
        MotivoInactivacionMinChars: 10);

    public RevisionPolicyService(IApplicationDbContextFactory dbFactory, ITenantContext tenant, IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
        _cache = cache;
    }

    public async Task<RevisionPolicyDto> GetAsync(CancellationToken ct = default)
    {
        // Sin tenant activo devolvemos defaults sin cachear — es un edge case
        // (jobs de background sin scope resuelto). El resto del flujo tiene tenant.
        if (_tenant.TenantId is not Guid tid) { return Defaults; }

        var key = CacheKeyPrefix + tid;
        if (_cache.TryGetValue(key, out RevisionPolicyDto? cached) && cached is not null)
        {
            return cached;
        }

        using var db = _dbFactory.Create();
        var row = await db.RevisionPolicies.AsNoTracking().FirstOrDefaultAsync(ct);
        var dto = row is null ? Defaults : ToDto(row);
        _cache.Set(key, dto, CacheTtl);
        return dto;
    }

    public async Task SaveAsync(RevisionPolicyDto policy, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo para guardar RevisionPolicy.");
        }
        if (policy.UmbralConfianza < 0m || policy.UmbralConfianza > 1m)
        {
            throw new ArgumentException("UmbralConfianza debe estar entre 0 y 1.", nameof(policy));
        }
        if (policy.VentanaAsignacionesRelacionadasDias < 1)
        {
            throw new ArgumentException("VentanaAsignacionesRelacionadasDias debe ser >= 1.", nameof(policy));
        }
        if (policy.MotivoInactivacionMinChars < 0)
        {
            throw new ArgumentException("MotivoInactivacionMinChars no puede ser negativo.", nameof(policy));
        }
        if (policy.HistorialMeses < 1 || policy.HistorialMeses > 36)
        {
            throw new ArgumentException("Los meses de historial deben estar entre 1 y 36.", nameof(policy));
        }
        if (policy.HistorialMaxCompletas < 0 || policy.HistorialMaxCompletas > 100)
        {
            throw new ArgumentException("El maximo de historias completas debe estar entre 0 y 100.", nameof(policy));
        }
        if (policy.HistorialMaxTokens < 5000 || policy.HistorialMaxTokens > 120000)
        {
            throw new ArgumentException("El tope de tokens del historial debe estar entre 5.000 y 120.000.", nameof(policy));
        }
        if (policy.AuditoriaIaTimeoutSeg < 30 || policy.AuditoriaIaTimeoutSeg > 300)
        {
            throw new ArgumentException("El tiempo maximo de la auditoria al cerrar debe estar entre 30 y 300 segundos.", nameof(policy));
        }

        using var db = _dbFactory.Create();
        var row = await db.RevisionPolicies.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new RevisionPolicy { TenantId = tid };
            db.RevisionPolicies.Add(row);
        }
        row.AutoTriggerCierre = policy.AutoTriggerCierre;
        row.PreRevisionIAAutoTrigger = policy.PreRevisionIAAutoTrigger;
        row.AdopcionAutomaticaAgente = policy.AdopcionAutomaticaAgente;
        row.UmbralConfianza = policy.UmbralConfianza;
        row.VentanaAsignacionesRelacionadasDias = policy.VentanaAsignacionesRelacionadasDias;
        row.ConfirmarAprobado = policy.ConfirmarAprobado;
        row.MotivoInactivacionMinChars = policy.MotivoInactivacionMinChars;
        row.NotificarRechazoWhatsApp = policy.NotificarRechazoWhatsApp;
        row.EnviarHistorialPaciente = policy.EnviarHistorialPaciente;
        row.HistorialMeses = policy.HistorialMeses;
        row.HistorialMaxCompletas = policy.HistorialMaxCompletas;
        row.HistorialMaxTokens = policy.HistorialMaxTokens;
        row.AuditoriaIaAlCerrar = policy.AuditoriaIaAlCerrar;
        var formatos = (policy.AuditoriaIaFormatos ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        row.AuditoriaIaFormatosJson = formatos.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(formatos);
        row.AuditoriaIaTimeoutSeg = policy.AuditoriaIaTimeoutSeg;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);

        // Invalidamos el cache del tenant. El proximo `GetAsync` releera la
        // fila fresca y volvera a poblar la entrada.
        _cache.Remove(CacheKeyPrefix + tid);
    }

    private static RevisionPolicyDto ToDto(RevisionPolicy r) => new(
        r.AutoTriggerCierre,
        r.PreRevisionIAAutoTrigger,
        r.AdopcionAutomaticaAgente,
        r.UmbralConfianza,
        r.VentanaAsignacionesRelacionadasDias,
        r.ConfirmarAprobado,
        r.MotivoInactivacionMinChars,
        r.NotificarRechazoWhatsApp,
        r.EnviarHistorialPaciente,
        r.HistorialMeses,
        r.HistorialMaxCompletas,
        r.HistorialMaxTokens,
        r.AuditoriaIaAlCerrar,
        LeerFormatos(r.AuditoriaIaFormatosJson),
        r.AuditoriaIaTimeoutSeg);

    private static IReadOnlyList<string> LeerFormatos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (System.Text.Json.JsonException) { return Array.Empty<string>(); }
    }
}
