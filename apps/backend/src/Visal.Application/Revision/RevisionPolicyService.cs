using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>
/// Servicio del singleton <see cref="RevisionPolicy"/>. Fetch idempotente por
/// tenant activo — si no hay fila, se devuelven los defaults del enum sin
/// crearla. El upsert la crea la primera vez que un Owner guarda.
/// </summary>
public sealed class RevisionPolicyService : IRevisionPolicyService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public RevisionPolicyService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<RevisionPolicyDto> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.RevisionPolicies.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null)
        {
            // Defaults del enum — ver [[1. Vision, arquitectura y decisiones]] §4 (D5).
            return new RevisionPolicyDto(
                AutoTriggerCierre: false,
                PreRevisionIAAutoTrigger: false,
                AdopcionAutomaticaAgente: false,
                UmbralConfianza: 0.95m,
                VentanaAsignacionesRelacionadasDias: 30,
                ConfirmarAprobado: false,
                MotivoInactivacionMinChars: 10);
        }
        return ToDto(row);
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
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);
    }

    private static RevisionPolicyDto ToDto(RevisionPolicy r) => new(
        r.AutoTriggerCierre,
        r.PreRevisionIAAutoTrigger,
        r.AdopcionAutomaticaAgente,
        r.UmbralConfianza,
        r.VentanaAsignacionesRelacionadasDias,
        r.ConfirmarAprobado,
        r.MotivoInactivacionMinChars);
}
