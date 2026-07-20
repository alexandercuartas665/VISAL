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
public sealed class RevisionPolicyService : IRevisionPolicyService
{
    private readonly IApplicationDbContext _db;
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

    public RevisionPolicyService(IApplicationDbContext db, ITenantContext tenant, IMemoryCache cache)
    {
        _db = db;
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

        var row = await _db.RevisionPolicies.AsNoTracking().FirstOrDefaultAsync(ct);
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

        var row = await _db.RevisionPolicies.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new RevisionPolicy { TenantId = tid };
            _db.RevisionPolicies.Add(row);
        }
        row.AutoTriggerCierre = policy.AutoTriggerCierre;
        row.PreRevisionIAAutoTrigger = policy.PreRevisionIAAutoTrigger;
        row.AdopcionAutomaticaAgente = policy.AdopcionAutomaticaAgente;
        row.UmbralConfianza = policy.UmbralConfianza;
        row.VentanaAsignacionesRelacionadasDias = policy.VentanaAsignacionesRelacionadasDias;
        row.ConfirmarAprobado = policy.ConfirmarAprobado;
        row.MotivoInactivacionMinChars = policy.MotivoInactivacionMinChars;
        row.NotificarRechazoWhatsApp = policy.NotificarRechazoWhatsApp;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);

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
        r.NotificarRechazoWhatsApp);
}
