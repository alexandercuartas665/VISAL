using Visal.Application.Common;
using Visal.Application.Common.Auth;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class UsuarioAdminService : IUsuarioAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;

    public UsuarioAdminService(IApplicationDbContext db, ITenantContext tenant, IPasswordHasher hasher)
    {
        _db = db;
        _tenant = tenant;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<UsuarioDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await _db.TenantUsers.AsNoTracking().ToListAsync(ct);
        var roles = await _db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);
        var sucs = await _db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nombre, ct);
        var assigns = await _db.TenantUserSucursales.AsNoTracking().ToListAsync(ct);
        var byUser = assigns.GroupBy(a => a.TenantUserId).ToDictionary(g => g.Key, g => g.Select(x => x.SucursalId).ToList());
        var puIds = users.Select(u => u.PlatformUserId).ToList();
        var pus = await _db.PlatformUsers.AsNoTracking().IgnoreQueryFilters().Where(p => puIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        return users
            .OrderBy(u => u.Email)
            .Select(u =>
            {
                pus.TryGetValue(u.PlatformUserId, out var pu);
                var sids = byUser.TryGetValue(u.Id, out var lst) ? lst : new List<Guid>();
                var snames = sids.Where(id => sucs.ContainsKey(id)).Select(id => sucs[id]).OrderBy(n => n).ToList();
                return new UsuarioDto(u.Id, u.PlatformUserId, u.Email, pu?.DisplayName,
                    u.RolId, u.RolId is Guid rid && roles.TryGetValue(rid, out var rn) ? rn : null,
                    sids, snames,
                    u.Status.ToString(), pu?.EsGlobal ?? false);
            })
            .ToList();
    }

    public async Task<UsuarioDto?> CrearAsync(CrearUsuarioRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0) { throw new InvalidOperationException("El correo es obligatorio."); }
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6) { throw new InvalidOperationException("La clave debe tener al menos 6 caracteres."); }

        var pu = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Email == email, ct);
        if (pu is null)
        {
            pu = new PlatformUser
            {
                Email = email,
                DisplayName = req.DisplayName?.Trim(),
                EmailVerified = true,
                AuthProvider = "local",
                PasswordHash = _hasher.Hash(req.Password),
                Status = PlatformUserStatus.Active,
                EsGlobal = req.EsGlobal
            };
            _db.PlatformUsers.Add(pu);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (req.EsGlobal) { pu.EsGlobal = true; }
            if (string.IsNullOrWhiteSpace(pu.PasswordHash)) { pu.PasswordHash = _hasher.Hash(req.Password); }
        }

        if (await _db.TenantUsers.AnyAsync(u => u.PlatformUserId == pu.Id, ct))
        {
            throw new InvalidOperationException("El usuario ya pertenece a esta entidad.");
        }

        var tu = new TenantUser
        {
            TenantId = tid,
            PlatformUserId = pu.Id,
            Email = email,
            TenantRole = TenantRole.Advisor,
            Status = PlatformUserStatus.Active,
            RolId = req.RolId,
            SucursalId = req.SucursalIds?.Count > 0 ? req.SucursalIds[0] : null
        };
        _db.TenantUsers.Add(tu);
        await _db.SaveChangesAsync(ct);

        foreach (var sid in (req.SucursalIds ?? Array.Empty<Guid>()).Distinct())
        {
            _db.TenantUserSucursales.Add(new TenantUserSucursal
            {
                TenantId = tid, TenantUserId = tu.Id, SucursalId = sid
            });
        }
        await _db.SaveChangesAsync(ct);

        return (await ListAsync(ct)).FirstOrDefault(u => u.Id == tu.Id);
    }

    public async Task<UsuarioDto?> AsignarAsync(Guid tenantUserId, Guid? rolId, IReadOnlyList<Guid> sucursalIds, bool esGlobal, Guid actor, CancellationToken ct = default)
    {
        var tu = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, ct);
        if (tu is null) { return null; }
        tu.RolId = rolId;
        var distinct = (sucursalIds ?? Array.Empty<Guid>()).Distinct().ToList();
        tu.SucursalId = distinct.Count > 0 ? distinct[0] : null;
        var pu = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == tu.PlatformUserId, ct);
        if (pu is not null) { pu.EsGlobal = esGlobal; }

        // Sincronizar asignaciones de sedes (borrar todas y reinsertar las elegidas).
        var existentes = await _db.TenantUserSucursales.Where(s => s.TenantUserId == tenantUserId).ToListAsync(ct);
        _db.TenantUserSucursales.RemoveRange(existentes);
        foreach (var sid in distinct)
        {
            _db.TenantUserSucursales.Add(new TenantUserSucursal
            {
                TenantId = tu.TenantId, TenantUserId = tenantUserId, SucursalId = sid
            });
        }
        await _db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).FirstOrDefault(u => u.Id == tenantUserId);
    }

    public async Task<bool> EliminarAsync(Guid tenantUserId, Guid actor, CancellationToken ct = default)
    {
        var tu = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, ct);
        if (tu is null) { return false; }
        _db.TenantUsers.Remove(tu);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(Guid tenantUserId, string nuevaClave, Guid actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nuevaClave) || nuevaClave.Length < 6)
        {
            throw new InvalidOperationException("La nueva clave debe tener al menos 6 caracteres.");
        }
        var tu = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, ct);
        if (tu is null) { return false; }
        var pu = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == tu.PlatformUserId, ct);
        if (pu is null) { return false; }
        pu.PasswordHash = _hasher.Hash(nuevaClave);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
