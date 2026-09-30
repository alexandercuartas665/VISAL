using Visal.Application.Common;
using Visal.Domain.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Visal.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Sella campos de auditoria (CreatedAt/By, UpdatedAt/By) y asigna TenantId a las entidades
/// tenant-scoped recien agregadas que aun no lo tengan, usando el ITenantContext. Esto evita
/// inserciones cruzadas por olvido de setear el tenant.
/// </summary>
public sealed class AuditableTenantInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AuditableTenantInterceptor(ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        await AsignarNumeroSesionAsync(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Estampa <see cref="AsignacionTurno.NumeroSesion"/> (persistido) a los turnos recien
    /// agregados que aun no lo tengan: MAX(NumeroSesion de la asignacion) + 1, en orden de
    /// Id (mismo desempate historico cuando el CreatedAt del lote colisiona). Asi el numero
    /// de sesion deja de recomputarse en cada carga y no se cruza.
    /// </summary>
    private static async Task AsignarNumeroSesionAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null) { return; }

        var nuevos = context.ChangeTracker.Entries<AsignacionTurno>()
            .Where(e => e.State == EntityState.Added && e.Entity.NumeroSesion is null)
            .Select(e => e.Entity)
            .ToList();
        if (nuevos.Count == 0) { return; }

        foreach (var grupo in nuevos.GroupBy(t => t.AsignacionId))
        {
            var asigId = grupo.Key;
            var maxActual = await context.Set<AsignacionTurno>().AsNoTracking()
                .Where(t => t.AsignacionId == asigId && t.NumeroSesion != null)
                .Select(t => t.NumeroSesion!.Value)
                .OrderByDescending(v => v)
                .FirstOrDefaultAsync(ct);
            var siguiente = maxActual;
            foreach (var turno in grupo.OrderBy(t => t.Id))
            {
                turno.NumeroSesion = ++siguiente;
            }
        }
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var userId = _tenantContext.UserId;

        foreach (EntityEntry<BaseEntity> entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= userId;

                    if (entry.Entity is TenantEntity added
                        && added.TenantId == Guid.Empty
                        && _tenantContext.TenantId is Guid tenantId)
                    {
                        added.TenantId = tenantId;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
            }
        }
    }
}
