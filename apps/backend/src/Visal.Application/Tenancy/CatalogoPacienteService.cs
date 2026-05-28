using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class CatalogoPacienteService(IApplicationDbContext db, ITenantContext tenant) : ICatalogoPacienteService
{
    public async Task<IReadOnlyList<CatalogoPacienteDto>> ListAsync(CatalogoPacienteTipo? tipo, CancellationToken ct = default)
    {
        var q = db.CatalogosPaciente.AsNoTracking();
        if (tipo is CatalogoPacienteTipo t) { q = q.Where(c => c.Tipo == t); }
        return await q.OrderBy(c => c.Tipo).ThenBy(c => c.Nombre)
            .Select(c => new CatalogoPacienteDto(c.Id, c.Tipo, c.Codigo, c.Nombre, c.Descripcion, c.Activo))
            .ToListAsync(ct);
    }

    public async Task<CatalogoPacienteDto?> SaveAsync(SaveCatalogoPacienteRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0) { throw new InvalidOperationException("El codigo es obligatorio."); }
        if (nombre.Length == 0) { throw new InvalidOperationException("El nombre es obligatorio."); }

        CatalogoPaciente e;
        if (req.Id is Guid id)
        {
            e = await db.CatalogosPaciente.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Item no encontrado.");
            if (await db.CatalogosPaciente.AnyAsync(x => x.Tipo == req.Tipo && x.Codigo == codigo && x.Id != id, ct))
            {
                throw new InvalidOperationException($"Ya existe un item con codigo '{codigo}' en este catalogo.");
            }
        }
        else
        {
            if (tenant.TenantId is not Guid tid) { return null; }
            if (await db.CatalogosPaciente.AnyAsync(x => x.Tipo == req.Tipo && x.Codigo == codigo, ct))
            {
                throw new InvalidOperationException($"Ya existe un item con codigo '{codigo}' en este catalogo.");
            }
            e = new CatalogoPaciente { TenantId = tid, Tipo = req.Tipo };
            db.CatalogosPaciente.Add(e);
        }

        e.Tipo = req.Tipo;
        e.Codigo = codigo;
        e.Nombre = nombre;
        e.Descripcion = req.Descripcion?.Trim();
        e.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return new CatalogoPacienteDto(e.Id, e.Tipo, e.Codigo, e.Nombre, e.Descripcion, e.Activo);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.CatalogosPaciente.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.CatalogosPaciente.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
