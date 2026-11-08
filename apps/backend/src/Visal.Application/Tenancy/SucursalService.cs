using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class SucursalService : ISucursalService
{
    // Ola 4c estabilizacion (P0): contexto CORTO por operacion (using var db = _dbFactory.Create();)
    // en vez del IApplicationDbContext scoped. Las sedes se leen en el selector de sede de la barra
    // superior (en cada pagina autenticada), punto de alto solapamiento del circuito -> "second operation".
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;

    public SucursalService(IApplicationDbContextFactory dbFactory, ITenantContext tenant)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<SucursalDto>> ListAsync(bool soloActivas = false, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var q = db.Sucursales.AsNoTracking();
        if (soloActivas) { q = q.Where(s => s.Activo); }
        return await q.OrderBy(s => s.Nombre)
            .Select(s => new SucursalDto(s.Id, s.Codigo, s.Nombre, s.Direccion, s.Ciudad, s.Telefono, s.Activo, s.MipresObligatorio, s.CodigoHabilitacion, s.ExigirHcRevisadaParaFacturar, s.Email, s.CodigoAceptacion, s.Departamento, s.Nit)).ToListAsync(ct);
    }

    public async Task<SucursalDto?> SaveAsync(SaveSucursalRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0 || nombre.Length == 0) { throw new InvalidOperationException("Codigo y nombre son obligatorios."); }
        Sucursal e;
        if (req.Id is Guid gid)
        {
            e = await db.Sucursales.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new InvalidOperationException("Sucursal no encontrada.");
            if (await db.Sucursales.AnyAsync(x => x.Codigo == codigo && x.Id != gid, ct)) { throw new InvalidOperationException($"Ya existe el codigo '{codigo}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await db.Sucursales.AnyAsync(x => x.Codigo == codigo, ct)) { throw new InvalidOperationException($"Ya existe el codigo '{codigo}'."); }
            e = new Sucursal { TenantId = tid };
            db.Sucursales.Add(e);
        }
        e.Codigo = codigo; e.Nombre = nombre; e.Direccion = req.Direccion?.Trim();
        e.Ciudad = req.Ciudad?.Trim(); e.Telefono = req.Telefono?.Trim(); e.Activo = req.Activo;
        e.MipresObligatorio = req.MipresObligatorio;
        e.CodigoHabilitacion = req.CodigoHabilitacion?.Trim();
        e.ExigirHcRevisadaParaFacturar = req.ExigirHcRevisadaParaFacturar;
        e.CodigoAceptacion = req.CodigoAceptacion?.Trim();
        e.Departamento = req.Departamento?.Trim();
        e.Nit = req.Nit?.Trim();
        await db.SaveChangesAsync(ct);
        return new SucursalDto(e.Id, e.Codigo, e.Nombre, e.Direccion, e.Ciudad, e.Telefono, e.Activo, e.MipresObligatorio, e.CodigoHabilitacion, e.ExigirHcRevisadaParaFacturar, e.Email, e.CodigoAceptacion, e.Departamento, e.Nit);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var e = await db.Sucursales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.Sucursales.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> GetMipresObligatorioAsync(Guid sucursalId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await db.Sucursales.AsNoTracking()
            .Where(s => s.Id == sucursalId)
            .Select(s => s.MipresObligatorio)
            .FirstOrDefaultAsync(ct);
    }
}
