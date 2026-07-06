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
                    u.Status.ToString(), pu?.EsGlobal ?? false,
                    pu?.Documento, pu?.Username,
                    pu?.PrimerNombre, pu?.SegundoNombre, pu?.PrimerApellido, pu?.SegundoApellido,
                    pu?.Celular, pu?.Fijo, pu?.Ciudad, pu?.Direccion,
                    u.CoordinaTerapias, u.CoordinaEnfermeria, u.CoordinaConsultas, u.CoordinaEquipos);
            })
            .ToList();
    }

    public async Task<UsuarioDto?> GetPorProfesionalIdAsync(Guid profesionalId, CancellationToken ct = default)
    {
        // Busca el TenantUser por FK y reusa la logica de listado para armar el DTO.
        // Justificacion: matchear por documento fallaba en usuarios legacy con
        // platform_users.documento NULL (ej. demo-admin). El vinculo autoritativo
        // es TenantUser.ProfesionalId.
        var tu = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ProfesionalId == profesionalId, ct);
        if (tu is null) { return null; }
        return (await ListAsync(ct)).FirstOrDefault(u => u.Id == tu.Id);
    }

    public async Task<UsuarioDto?> ActualizarPerfilAsync(Guid tenantUserId, ActualizarPerfilUsuarioRequest req, Guid actor, CancellationToken ct = default)
    {
        var tu = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, ct);
        if (tu is null) { return null; }
        var pu = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == tu.PlatformUserId, ct);
        if (pu is null) { return null; }

        // Si el username viene no vacio, validar unicidad (excepto el propio usuario).
        var usernameTrim = req.Username?.Trim();
        if (!string.IsNullOrWhiteSpace(usernameTrim))
        {
            var exists = await _db.PlatformUsers.IgnoreQueryFilters()
                .AnyAsync(p => p.Id != pu.Id && p.Username == usernameTrim, ct);
            if (exists) { throw new InvalidOperationException($"El nombre de usuario '{usernameTrim}' ya esta en uso."); }
        }

        // Si documento viene no vacio, validar unicidad tambien.
        var docTrim = req.Documento?.Trim();
        if (!string.IsNullOrWhiteSpace(docTrim))
        {
            var exists = await _db.PlatformUsers.IgnoreQueryFilters()
                .AnyAsync(p => p.Id != pu.Id && p.Documento == docTrim, ct);
            if (exists) { throw new InvalidOperationException($"El documento '{docTrim}' ya esta en uso por otro usuario."); }
        }

        // Update PlatformUser (datos personales).
        pu.DisplayName = req.DisplayName?.Trim();
        pu.Username = string.IsNullOrWhiteSpace(usernameTrim) ? null : usernameTrim;
        pu.Documento = string.IsNullOrWhiteSpace(docTrim) ? null : docTrim;
        pu.PrimerNombre = req.PrimerNombre?.Trim();
        pu.SegundoNombre = req.SegundoNombre?.Trim();
        pu.PrimerApellido = req.PrimerApellido?.Trim();
        pu.SegundoApellido = req.SegundoApellido?.Trim();
        pu.Celular = req.Celular?.Trim();
        pu.Fijo = req.Fijo?.Trim();
        pu.Ciudad = req.Ciudad?.Trim();
        pu.Direccion = req.Direccion?.Trim();

        // Inhabilitar = poner el TenantUser en Blocked. El PlatformUser sigue Active
        // (por si tiene otro tenant). La pagina de login del tenant respeta este flag.
        tu.Status = req.Inhabilitado ? PlatformUserStatus.Blocked : PlatformUserStatus.Active;

        await _db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).FirstOrDefault(u => u.Id == tenantUserId);
    }

    public async Task<UsuarioDto?> ActualizarPermisosCoordinacionAsync(Guid tenantUserId, ActualizarPermisosCoordinacionRequest req, Guid actor, CancellationToken ct = default)
    {
        var tu = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, ct);
        if (tu is null) { return null; }
        tu.CoordinaTerapias = req.CoordinaTerapias;
        tu.CoordinaEnfermeria = req.CoordinaEnfermeria;
        tu.CoordinaConsultas = req.CoordinaConsultas;
        tu.CoordinaEquipos = req.CoordinaEquipos;
        await _db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).FirstOrDefault(u => u.Id == tenantUserId);
    }

    public async Task<IReadOnlyList<string>> GetModulosCoordinacionAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var tu = await _db.TenantUsers.AsNoTracking().FirstOrDefaultAsync(u => u.PlatformUserId == platformUserId, ct);
        if (tu is null) { return Array.Empty<string>(); }
        var lista = new List<string>();
        if (tu.CoordinaTerapias) { lista.Add("TERAPIAS"); }
        if (tu.CoordinaEnfermeria) { lista.Add("ENFERMERIA"); }
        if (tu.CoordinaConsultas) { lista.Add("CONSULTAS"); }
        if (tu.CoordinaEquipos) { lista.Add("EQUIPOS"); }
        return lista;
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

    public async Task<UsuarioDto?> CrearUsuarioDesdeProfesionalAsync(Guid profesionalId, string email, string password, Guid? rolId, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var prof = await _db.Profesionales.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profesionalId, ct)
            ?? throw new InvalidOperationException("Profesional no encontrado.");

        // Validar que el profesional no tenga ya un usuario vinculado.
        var existeVinculo = await _db.TenantUsers.AnyAsync(u => u.ProfesionalId == profesionalId, ct);
        if (existeVinculo) { throw new InvalidOperationException($"El profesional '{prof.NombreCompleto}' ya tiene un usuario asociado."); }

        email = (email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0) { throw new InvalidOperationException("El correo es obligatorio."); }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6) { throw new InvalidOperationException("La clave debe tener al menos 6 caracteres."); }

        // Reutilizar PlatformUser si ya existe por email o por documento. Sin
        // el segundo lookup, un documento preexistente con OTRO email (fila
        // creada por otro flujo, o re-import parcial) viola el indice unico
        // ix_platform_users_documento cuando el INSERT llega a Postgres.
        var pu = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Email == email, ct);
        if (pu is null && !string.IsNullOrWhiteSpace(prof.NumeroDocumento))
        {
            pu = await _db.PlatformUsers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Documento == prof.NumeroDocumento, ct);
        }
        if (pu is null)
        {
            pu = new PlatformUser
            {
                Email = email,
                DisplayName = prof.NombreCompleto,
                EmailVerified = true,
                AuthProvider = "local",
                PasswordHash = _hasher.Hash(password),
                Status = PlatformUserStatus.Active,
                EsGlobal = false,
                Documento = prof.NumeroDocumento,
                PrimerNombre = prof.PrimerNombre,
                SegundoNombre = prof.SegundoNombre,
                PrimerApellido = prof.PrimerApellido,
                SegundoApellido = prof.SegundoApellido,
                Celular = prof.Celular,
                Ciudad = prof.Ciudad
            };
            _db.PlatformUsers.Add(pu);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(pu.PasswordHash)) { pu.PasswordHash = _hasher.Hash(password); }
            if (string.IsNullOrWhiteSpace(pu.Documento)) { pu.Documento = prof.NumeroDocumento; }
            if (string.IsNullOrWhiteSpace(pu.DisplayName)) { pu.DisplayName = prof.NombreCompleto; }
        }

        if (await _db.TenantUsers.AnyAsync(u => u.PlatformUserId == pu.Id, ct))
        {
            throw new InvalidOperationException("Ya existe un usuario para este correo en la entidad.");
        }

        var tu = new TenantUser
        {
            TenantId = tid,
            PlatformUserId = pu.Id,
            Email = email,
            TenantRole = TenantRole.Advisor,
            Status = PlatformUserStatus.Active,
            ProfesionalId = profesionalId,
            RolId = rolId
        };
        _db.TenantUsers.Add(tu);
        await _db.SaveChangesAsync(ct);

        return (await ListAsync(ct)).FirstOrDefault(u => u.Id == tu.Id);
    }
}
