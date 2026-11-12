using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class ConfiguracionClinicaService(IApplicationDbContextFactory dbFactory, ITenantContext tenant) : IConfiguracionClinicaService
{
    private const string KeyMesesHC = "clinica.meses_validez_historia";
    private const int DefaultMeses = 3;
    private const string KeyBloquearOverloadTurnos = "turnos.bloquear_overload";
    private const string KeyPermitirOrdenesManuales = "clinica.permitir_ordenes_manuales";
    private const string KeyCorreoDefaultPaciente = "pacientes.correo_default";

    // Etapa destino del embudo por tipo de formulario web. Los lee FormWebhookService por tenant.
    public const string KeyEtapaFormPqrs = "formularios.etapa_pqrs";
    public const string KeyEtapaFormContacto = "formularios.etapa_contacto";

    public async Task<int> GetMesesValidezHistoriaClinicaAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
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

        using var db = dbFactory.Create();
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

    public async Task<bool> GetBloquearOverloadTurnosAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var cfg = await db.TenantConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == KeyBloquearOverloadTurnos, ct);
        return cfg is not null && bool.TryParse(cfg.ConfigValue, out var v) && v;
    }

    public async Task SetBloquearOverloadTurnosAsync(bool bloquear, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        using var db = dbFactory.Create();
        var cfg = await db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == KeyBloquearOverloadTurnos, ct);
        var valor = bloquear ? "true" : "false";
        if (cfg is null)
        {
            db.TenantConfigurations.Add(new TenantConfiguration
            {
                TenantId = tid,
                ConfigKey = KeyBloquearOverloadTurnos,
                ConfigValue = valor
            });
        }
        else
        {
            cfg.ConfigValue = valor;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> GetPermitirOrdenesManualesAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var cfg = await db.TenantConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == KeyPermitirOrdenesManuales, ct);
        return cfg is not null && bool.TryParse(cfg.ConfigValue, out var v) && v;
    }

    public async Task SetPermitirOrdenesManualesAsync(bool permitir, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        using var db = dbFactory.Create();
        var cfg = await db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == KeyPermitirOrdenesManuales, ct);
        var valor = permitir ? "true" : "false";
        if (cfg is null)
        {
            db.TenantConfigurations.Add(new TenantConfiguration
            {
                TenantId = tid,
                ConfigKey = KeyPermitirOrdenesManuales,
                ConfigValue = valor
            });
        }
        else
        {
            cfg.ConfigValue = valor;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetCorreoDefaultPacienteAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var cfg = await db.TenantConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == KeyCorreoDefaultPaciente, ct);
        return string.IsNullOrWhiteSpace(cfg?.ConfigValue) ? null : cfg!.ConfigValue!.Trim();
    }

    public async Task SetCorreoDefaultPacienteAsync(string? correo, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var valor = correo?.Trim() ?? string.Empty;
        // Validacion liviana: si viene algo, debe parecer un correo (tiene @ con texto a ambos lados).
        if (valor.Length > 0)
        {
            var at = valor.IndexOf('@');
            if (at <= 0 || at >= valor.Length - 1 || valor.Contains(' '))
            {
                throw new InvalidOperationException("El correo por defecto no es valido.");
            }
        }

        using var db = dbFactory.Create();
        var cfg = await db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == KeyCorreoDefaultPaciente, ct);
        if (cfg is null)
        {
            db.TenantConfigurations.Add(new TenantConfiguration
            {
                TenantId = tid,
                ConfigKey = KeyCorreoDefaultPaciente,
                ConfigValue = valor
            });
        }
        else
        {
            cfg.ConfigValue = valor;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string KeyEtapaPorTipo(string tipo)
        => string.Equals(tipo, "contacto", StringComparison.OrdinalIgnoreCase) ? KeyEtapaFormContacto : KeyEtapaFormPqrs;

    public async Task<string?> GetEtapaFormularioWebAsync(string tipo, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var key = KeyEtapaPorTipo(tipo);
        var cfg = await db.TenantConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.ConfigKey == key, ct);
        return string.IsNullOrWhiteSpace(cfg?.ConfigValue) ? null : cfg!.ConfigValue!.Trim();
    }

    public async Task SetEtapaFormularioWebAsync(string tipo, string? etapa, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        using var db = dbFactory.Create();
        var key = KeyEtapaPorTipo(tipo);
        var valor = etapa?.Trim() ?? string.Empty;

        var cfg = await db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == key, ct);
        if (cfg is null)
        {
            db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = key, ConfigValue = valor });
        }
        else
        {
            cfg.ConfigValue = valor;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListEtapasEmbudoAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        return await db.PipelineStages.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => s.Name).ToListAsync(ct);
    }
}
