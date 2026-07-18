using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class SucursalService : ISucursalService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public SucursalService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<SucursalDto>> ListAsync(bool soloActivas = false, CancellationToken ct = default)
    {
        var q = _db.Sucursales.AsNoTracking();
        if (soloActivas) { q = q.Where(s => s.Activo); }
        return await q.OrderBy(s => s.Nombre)
            .Select(s => new SucursalDto(s.Id, s.Codigo, s.Nombre, s.Direccion, s.Ciudad, s.Telefono, s.Activo, s.MipresObligatorio, s.CodigoHabilitacion)).ToListAsync(ct);
    }

    public async Task<SucursalDto?> SaveAsync(SaveSucursalRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0 || nombre.Length == 0) { throw new InvalidOperationException("Codigo y nombre son obligatorios."); }
        Sucursal e;
        if (req.Id is Guid gid)
        {
            e = await _db.Sucursales.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new InvalidOperationException("Sucursal no encontrada.");
            if (await _db.Sucursales.AnyAsync(x => x.Codigo == codigo && x.Id != gid, ct)) { throw new InvalidOperationException($"Ya existe el codigo '{codigo}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.Sucursales.AnyAsync(x => x.Codigo == codigo, ct)) { throw new InvalidOperationException($"Ya existe el codigo '{codigo}'."); }
            e = new Sucursal { TenantId = tid };
            _db.Sucursales.Add(e);
        }
        e.Codigo = codigo; e.Nombre = nombre; e.Direccion = req.Direccion?.Trim();
        e.Ciudad = req.Ciudad?.Trim(); e.Telefono = req.Telefono?.Trim(); e.Activo = req.Activo;
        e.MipresObligatorio = req.MipresObligatorio;
        e.CodigoHabilitacion = req.CodigoHabilitacion?.Trim();
        await _db.SaveChangesAsync(ct);
        return new SucursalDto(e.Id, e.Codigo, e.Nombre, e.Direccion, e.Ciudad, e.Telefono, e.Activo, e.MipresObligatorio, e.CodigoHabilitacion);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.Sucursales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.Sucursales.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> GetMipresObligatorioAsync(Guid sucursalId, CancellationToken ct = default)
    {
        return await _db.Sucursales.AsNoTracking()
            .Where(s => s.Id == sucursalId)
            .Select(s => s.MipresObligatorio)
            .FirstOrDefaultAsync(ct);
    }
}
