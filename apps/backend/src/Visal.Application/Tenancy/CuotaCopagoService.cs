using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class CuotaCopagoService(IApplicationDbContext db, ITenantContext tenant) : ICuotaCopagoService
{
    public async Task<IReadOnlyList<CuotaCopagoDto>> ListarAsync(string? tipo = null, bool soloActivos = false, CancellationToken ct = default)
    {
        var q = db.CuotasCopagos.AsNoTracking();
        if (soloActivos) { q = q.Where(x => x.Activo); }
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var t = tipo.Trim().ToUpperInvariant();
            q = q.Where(x => x.Tipo == t);
        }
        return await q.OrderBy(x => x.Tipo).ThenBy(x => x.Categoria)
            .Select(x => new CuotaCopagoDto(x.Id, x.Tipo, x.Categoria, x.ValorSugerido, x.Descripcion, x.Activo))
            .ToListAsync(ct);
    }

    public async Task<CuotaCopagoDto?> SaveAsync(SaveCuotaCopagoRequest req, Guid actor, CancellationToken ct = default)
    {
        var tipo = (req.Tipo ?? "").Trim().ToUpperInvariant();
        var cat = (req.Categoria ?? "").Trim();
        if (tipo is not ("CUOTA" or "COPAGO")) { throw new InvalidOperationException("Tipo debe ser 'CUOTA' o 'COPAGO'."); }
        if (cat.Length == 0) { throw new InvalidOperationException("La categoria es obligatoria."); }
        if (req.ValorSugerido < 0) { throw new InvalidOperationException("El valor sugerido no puede ser negativo."); }

        CuotaCopago entity;
        if (req.Id is Guid id)
        {
            entity = await db.CuotasCopagos.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Registro no encontrado.");
            if (await db.CuotasCopagos.AnyAsync(x => x.Id != id && x.Tipo == tipo && x.Categoria == cat, ct))
            {
                throw new InvalidOperationException($"Ya existe otro registro para {tipo} / {cat}.");
            }
        }
        else
        {
            if (tenant.TenantId is not Guid tid) { return null; }
            if (await db.CuotasCopagos.AnyAsync(x => x.Tipo == tipo && x.Categoria == cat, ct))
            {
                throw new InvalidOperationException($"Ya existe un registro para {tipo} / {cat}.");
            }
            entity = new CuotaCopago { TenantId = tid };
            db.CuotasCopagos.Add(entity);
        }
        entity.Tipo = tipo;
        entity.Categoria = cat;
        entity.ValorSugerido = req.ValorSugerido;
        entity.Descripcion = req.Descripcion?.Trim();
        entity.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return new CuotaCopagoDto(entity.Id, entity.Tipo, entity.Categoria, entity.ValorSugerido, entity.Descripcion, entity.Activo);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.CuotasCopagos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.CuotasCopagos.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SeedDefaultsAsync(Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return 0; }
        if (await db.CuotasCopagos.AnyAsync(ct)) { return 0; }
        // Rangos estandar (aproximados) para 2026. El usuario los ajusta luego.
        var defaults = new (string Tipo, string Categoria, decimal Valor, string Desc)[]
        {
            ("CUOTA", "SMLDV < 2",             5300m,  "Salario menor a 2 SMMLV"),
            ("CUOTA", "SMLDV 2 A 5",          20900m, "Salario entre 2 y 5 SMMLV"),
            ("CUOTA", "SMLDV > 5",            55200m, "Salario mayor a 5 SMMLV"),
            ("COPAGO", "SMLDV < 2",           15000m, "Copago paciente subsidiado - nivel 1"),
            ("COPAGO", "SMLDV 2 A 5",         50000m, "Copago paciente subsidiado - nivel 2"),
            ("COPAGO", "SMLDV > 5",          150000m, "Copago paciente subsidiado - nivel 3"),
        };
        foreach (var d in defaults)
        {
            db.CuotasCopagos.Add(new CuotaCopago
            {
                TenantId = tid, Tipo = d.Tipo, Categoria = d.Categoria,
                ValorSugerido = d.Valor, Descripcion = d.Desc, Activo = true
            });
        }
        await db.SaveChangesAsync(ct);
        return defaults.Length;
    }
}
