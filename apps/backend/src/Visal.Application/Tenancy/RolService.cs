using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class RolService : IRolService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public RolService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<RolDto>> ListAsync(CancellationToken ct = default)
        => await _db.Roles.AsNoTracking().OrderBy(r => r.Nombre)
            .Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.Activo)).ToListAsync(ct);

    public async Task<RolDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return null; }
        var permisos = await _db.RolPermisos.AsNoTracking().Where(p => p.RolId == id)
            .Select(p => new PermisoDto(p.Modulo, p.Ver, p.Crear, p.Editar, p.Eliminar)).ToListAsync(ct);
        return new RolDetailDto(r.Id, r.Nombre, r.Descripcion, r.Activo, permisos);
    }

    public async Task<RolDto?> SaveAsync(Guid? id, string nombre, string? descripcion, bool activo, Guid actor, CancellationToken ct = default)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) { throw new InvalidOperationException("El nombre del rol es obligatorio."); }
        Rol e;
        if (id is Guid gid)
        {
            e = await _db.Roles.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new InvalidOperationException("Rol no encontrado.");
            if (await _db.Roles.AnyAsync(x => x.Nombre == nombre && x.Id != gid, ct)) { throw new InvalidOperationException($"Ya existe el rol '{nombre}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.Roles.AnyAsync(x => x.Nombre == nombre, ct)) { throw new InvalidOperationException($"Ya existe el rol '{nombre}'."); }
            e = new Rol { TenantId = tid };
            _db.Roles.Add(e);
        }
        e.Nombre = nombre; e.Descripcion = descripcion?.Trim(); e.Activo = activo;
        await _db.SaveChangesAsync(ct);
        return new RolDto(e.Id, e.Nombre, e.Descripcion, e.Activo);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.Roles.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SavePermisosAsync(Guid rolId, IReadOnlyList<PermisoDto> permisos, Guid actor, CancellationToken ct = default)
    {
        var rol = await _db.Roles.FirstOrDefaultAsync(x => x.Id == rolId, ct);
        if (rol is null) { return; }
        var tenant = rol.TenantId;
        var existing = await _db.RolPermisos.Where(p => p.RolId == rolId).ToListAsync(ct);
        _db.RolPermisos.RemoveRange(existing);
        foreach (var p in permisos.Where(p => p.Ver || p.Crear || p.Editar || p.Eliminar))
        {
            _db.RolPermisos.Add(new RolPermiso
            {
                TenantId = tenant, RolId = rolId, Modulo = p.Modulo,
                Ver = p.Ver, Crear = p.Crear, Editar = p.Editar, Eliminar = p.Eliminar
            });
        }
        await _db.SaveChangesAsync(ct);
    }
}
