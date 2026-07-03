using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class PaqueteService(IApplicationDbContext db, ITenantContext tenant) : IPaqueteService
{
    public async Task<IReadOnlyList<PaqueteDto>> ListarAsync(string? filtro = null, bool soloActivos = false, CancellationToken ct = default)
    {
        var q = db.Paquetes.AsNoTracking();
        if (soloActivos) { q = q.Where(p => p.Activo); }
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim().ToLower();
            q = q.Where(p => p.Codigo.ToLower().Contains(f) || p.Nombre.ToLower().Contains(f));
        }
        return await q.OrderBy(p => p.Codigo)
            .Select(p => new PaqueteDto(p.Id, p.Codigo, p.Nombre, p.Activo))
            .ToListAsync(ct);
    }

    public async Task<PaqueteDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Paquetes.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PaqueteDto(p.Id, p.Codigo, p.Nombre, p.Activo))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PaqueteDto?> SaveAsync(SavePaqueteRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0 || nombre.Length == 0)
        {
            throw new InvalidOperationException("El codigo y el nombre del paquete son obligatorios.");
        }

        Paquete entity;
        if (req.Id is Guid id)
        {
            entity = await db.Paquetes.FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new InvalidOperationException("Paquete no encontrado.");
            if (await db.Paquetes.AnyAsync(p => p.Codigo == codigo && p.Id != id, ct))
            {
                throw new InvalidOperationException($"Ya existe otro paquete con el codigo '{codigo}'.");
            }
        }
        else
        {
            if (tenant.TenantId is not Guid tid) { return null; }
            if (await db.Paquetes.AnyAsync(p => p.Codigo == codigo, ct))
            {
                throw new InvalidOperationException($"Ya existe un paquete con el codigo '{codigo}'.");
            }
            entity = new Paquete { TenantId = tid };
            db.Paquetes.Add(entity);
        }

        entity.Codigo = codigo;
        entity.Nombre = nombre;
        entity.Activo = req.Activo;

        await db.SaveChangesAsync(ct);
        return new PaqueteDto(entity.Id, entity.Codigo, entity.Nombre, entity.Activo);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.Paquetes.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (e is null) { return false; }
        db.Paquetes.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SembrarAsync(IReadOnlyList<SavePaqueteRequest> items, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return 0; }
        if (items.Count == 0) { return 0; }

        // Codigos ya presentes para saltarlos (idempotencia).
        var codigos = items.Select(i => (i.Codigo ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existentes = await db.Paquetes
            .Where(p => codigos.Contains(p.Codigo))
            .Select(p => p.Codigo)
            .ToListAsync(ct);
        var existentesSet = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);

        var n = 0;
        foreach (var it in items)
        {
            var codigo = (it.Codigo ?? "").Trim();
            var nombre = (it.Nombre ?? "").Trim();
            if (codigo.Length == 0 || nombre.Length == 0) { continue; }
            if (existentesSet.Contains(codigo)) { continue; }
            db.Paquetes.Add(new Paquete
            {
                TenantId = tid,
                Codigo = codigo,
                Nombre = nombre,
                Activo = it.Activo
            });
            existentesSet.Add(codigo);
            n++;
        }
        if (n > 0) { await db.SaveChangesAsync(ct); }
        return n;
    }
}
