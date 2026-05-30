using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class ConfiguracionClinicaService(IApplicationDbContext db, ITenantContext tenant) : IConfiguracionClinicaService
{
    private const string KeyMesesHC = "clinica.meses_validez_historia";
    private const int DefaultMeses = 3;

    public async Task<int> GetMesesValidezHistoriaClinicaAsync(CancellationToken ct = default)
    {
        var cfg = await db.TenantConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == KeyMesesHC, ct);
        if (cfg is null || !int.TryParse(cfg.ConfigValue, out var meses) || meses < 1) { return DefaultMeses; }
        return meses;
    }

    public async Task SetMesesValidezHistoriaClinicaAsync(int meses, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (meses < 1) { throw new InvalidOperationException("Los meses de validez deben ser >= 1."); }
        if (meses > 120) { throw new InvalidOperationException("Los meses de validez no pueden superar 120."); }

        var cfg = await db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == KeyMesesHC, ct);
        if (cfg is null)
        {
            db.TenantConfigurations.Add(new TenantConfiguration
            {
                TenantId = tid,
                ConfigKey = KeyMesesHC,
                ConfigValue = meses.ToString()
            });
        }
        else
        {
            cfg.ConfigValue = meses.ToString();
        }
        await db.SaveChangesAsync(ct);
    }
}
