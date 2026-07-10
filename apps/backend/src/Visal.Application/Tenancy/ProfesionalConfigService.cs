using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class ProfesionalConfigService : IProfesionalConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IUsuarioAdminService _usuarios;

    public ProfesionalConfigService(IApplicationDbContext db, ITenantContext tenant, IUsuarioAdminService usuarios)
    {
        _db = db;
        _tenant = tenant;
        _usuarios = usuarios;
    }

    // ── Tipos ──
    public async Task<IReadOnlyList<CatalogItemDto>> ListTiposAsync(bool soloActivos = false, CancellationToken ct = default)
    {
        var q = _db.TiposProfesional.AsNoTracking();
        if (soloActivos) { q = q.Where(t => t.Activo); }
        return await q.OrderBy(t => t.Orden).ThenBy(t => t.Nombre)
            .Select(t => new CatalogItemDto(t.Id, t.Nombre, t.Activo, t.Orden)).ToListAsync(ct);
    }

    public async Task<CatalogItemDto?> SaveTipoAsync(Guid? id, string nombre, bool activo, Guid actor, CancellationToken ct = default)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) { throw new InvalidOperationException("El nombre es obligatorio."); }
        TipoProfesional e;
        if (id is Guid gid)
        {
            e = await _db.TiposProfesional.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new InvalidOperationException("No encontrado.");
            if (await _db.TiposProfesional.AnyAsync(x => x.Nombre == nombre && x.Id != gid, ct)) { throw new InvalidOperationException($"Ya existe '{nombre}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.TiposProfesional.AnyAsync(x => x.Nombre == nombre, ct)) { throw new InvalidOperationException($"Ya existe '{nombre}'."); }
            e = new TipoProfesional { TenantId = tid };
            _db.TiposProfesional.Add(e);
        }
        e.Nombre = nombre; e.Activo = activo;
        await _db.SaveChangesAsync(ct);
        return new CatalogItemDto(e.Id, e.Nombre, e.Activo, e.Orden);
    }

    public async Task<bool> DeleteTipoAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.TiposProfesional.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.TiposProfesional.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Subcategorias ──
    public async Task<IReadOnlyList<CatalogItemDto>> ListSubcategoriasAsync(bool soloActivos = false, CancellationToken ct = default)
    {
        var q = _db.SubCategoriasProfesional.AsNoTracking();
        if (soloActivos) { q = q.Where(t => t.Activo); }
        return await q.OrderBy(t => t.Orden).ThenBy(t => t.Nombre)
            .Select(t => new CatalogItemDto(t.Id, t.Nombre, t.Activo, t.Orden)).ToListAsync(ct);
    }

    public async Task<CatalogItemDto?> SaveSubcategoriaAsync(Guid? id, string nombre, bool activo, Guid actor, CancellationToken ct = default)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) { throw new InvalidOperationException("El nombre es obligatorio."); }
        SubCategoriaProfesional e;
        if (id is Guid gid)
        {
            e = await _db.SubCategoriasProfesional.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new InvalidOperationException("No encontrado.");
            if (await _db.SubCategoriasProfesional.AnyAsync(x => x.Nombre == nombre && x.Id != gid, ct)) { throw new InvalidOperationException($"Ya existe '{nombre}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.SubCategoriasProfesional.AnyAsync(x => x.Nombre == nombre, ct)) { throw new InvalidOperationException($"Ya existe '{nombre}'."); }
            e = new SubCategoriaProfesional { TenantId = tid };
            _db.SubCategoriasProfesional.Add(e);
        }
        e.Nombre = nombre; e.Activo = activo;
        await _db.SaveChangesAsync(ct);
        return new CatalogItemDto(e.Id, e.Nombre, e.Activo, e.Orden);
    }

    public async Task<bool> DeleteSubcategoriaAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.SubCategoriasProfesional.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.SubCategoriasProfesional.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Profesionales ──
    public async Task<IReadOnlyList<ProfesionalDto>> ListProfesionalesAsync(string? filtro, CancellationToken ct = default)
    {
        var q = _db.Profesionales.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim().ToLower();
            q = q.Where(p => p.NombreCompleto.ToLower().Contains(f) || p.NumeroDocumento.ToLower().Contains(f));
        }
        return await q.OrderBy(p => p.NombreCompleto)
            .Select(p => new ProfesionalDto(p.Id, p.NumeroDocumento, p.NombreCompleto,
                p.TipoProfesional != null ? p.TipoProfesional.Nombre : null, p.Ciudad, p.RegistroMedico))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProfesionalTablaDto>> ListProfesionalesTablaAsync(CancellationToken ct = default)
    {
        // 1) Datos base + tipo profesional + rol (nombre) en un solo query.
        var basicos = await _db.Profesionales.AsNoTracking()
            .OrderBy(p => p.NombreCompleto)
            .Select(p => new
            {
                p.Id,
                p.NombreCompleto,
                p.TipoDocumento,
                p.NumeroDocumento,
                TipoProfesional = p.TipoProfesional != null ? p.TipoProfesional.Nombre : null,
                Rol = p.RolPredeterminadoId != null
                    ? _db.Roles.Where(r => r.Id == p.RolPredeterminadoId).Select(r => r.Nombre).FirstOrDefault()
                    : null,
                TieneFirma = p.FirmaUrl != null && p.FirmaUrl != ""
            })
            .ToListAsync(ct);

        if (basicos.Count == 0) { return Array.Empty<ProfesionalTablaDto>(); }

        // 2) Sedes por profesional (M:N via ProfesionalAgencias) en una sola pasada.
        var ids = basicos.Select(b => b.Id).ToList();
        var sedesFlat = await _db.ProfesionalAgencias.AsNoTracking()
            .Where(x => ids.Contains(x.ProfesionalId))
            .OrderBy(x => x.Agencia)
            .Select(x => new { x.ProfesionalId, x.Agencia })
            .ToListAsync(ct);
        var sedesPorProf = sedesFlat
            .GroupBy(x => x.ProfesionalId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Agencia).ToList());

        return basicos.Select(b => new ProfesionalTablaDto(
            b.Id,
            b.NombreCompleto,
            b.TipoDocumento,
            b.NumeroDocumento,
            b.TipoProfesional,
            b.Rol,
            sedesPorProf.TryGetValue(b.Id, out var s) ? s : Array.Empty<string>(),
            b.TieneFirma
        )).ToList();
    }

    public async Task<ProfesionalDetailDto?> GetProfesionalAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.Profesionales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return null; }
        var subs = await _db.ProfesionalSubCategorias.AsNoTracking().Where(x => x.ProfesionalId == id).Select(x => x.SubCategoriaId).ToListAsync(ct);
        var ags = await _db.ProfesionalAgencias.AsNoTracking().Where(x => x.ProfesionalId == id).OrderBy(x => x.Agencia).Select(x => x.Agencia).ToListAsync(ct);
        return new ProfesionalDetailDto(p.Id, p.NumeroDocumento, p.TipoDocumento, p.PrimerNombre, p.SegundoNombre,
            p.PrimerApellido, p.SegundoApellido, p.NombreCompleto, p.TipoProfesionalId, p.RegistroMedico, p.Ciudad, p.Celular, p.FirmaUrl, subs, ags,
            p.RolPredeterminadoId);
    }

    public async Task<ProfesionalDetailDto?> SaveProfesionalAsync(SaveProfesionalRequest req, Guid actor, CancellationToken ct = default)
    {
        var doc = (req.NumeroDocumento ?? "").Trim();
        if (doc.Length == 0) { throw new InvalidOperationException("El numero de documento es obligatorio."); }
        var nombre = string.IsNullOrWhiteSpace(req.NombreCompleto)
            ? string.Join(' ', new[] { req.PrimerNombre, req.SegundoNombre, req.PrimerApellido, req.SegundoApellido }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim()
            : req.NombreCompleto.Trim();
        if (nombre.Length == 0) { nombre = doc; }

        Profesional p;
        if (req.Id is Guid id)
        {
            p = await _db.Profesionales.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException("Profesional no encontrado.");
            if (await _db.Profesionales.AnyAsync(x => x.NumeroDocumento == doc && x.Id != id, ct)) { throw new InvalidOperationException($"Ya existe un profesional con documento '{doc}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.Profesionales.AnyAsync(x => x.NumeroDocumento == doc, ct)) { throw new InvalidOperationException($"Ya existe un profesional con documento '{doc}'."); }
            p = new Profesional { TenantId = tid };
            _db.Profesionales.Add(p);
        }

        p.NumeroDocumento = doc;
        p.TipoDocumento = string.IsNullOrWhiteSpace(req.TipoDocumento) ? "CC" : req.TipoDocumento.Trim();
        p.PrimerNombre = req.PrimerNombre?.Trim();
        p.SegundoNombre = req.SegundoNombre?.Trim();
        p.PrimerApellido = req.PrimerApellido?.Trim();
        p.SegundoApellido = req.SegundoApellido?.Trim();
        p.NombreCompleto = nombre;
        p.TipoProfesionalId = req.TipoProfesionalId;
        p.RegistroMedico = req.RegistroMedico?.Trim();
        p.Ciudad = req.Ciudad?.Trim();
        p.Celular = req.Celular?.Trim();
        p.FirmaUrl = req.FirmaUrl;
        p.RolPredeterminadoId = req.RolPredeterminadoId;
        await _db.SaveChangesAsync(ct);

        var tenant = p.TenantId;
        // Sincronizar subcategorias
        var existingSubs = await _db.ProfesionalSubCategorias.Where(x => x.ProfesionalId == p.Id).ToListAsync(ct);
        _db.ProfesionalSubCategorias.RemoveRange(existingSubs);
        foreach (var sid in req.SubCategoriaIds.Distinct())
        {
            _db.ProfesionalSubCategorias.Add(new ProfesionalSubCategoria { TenantId = tenant, ProfesionalId = p.Id, SubCategoriaId = sid });
        }
        // Sincronizar agencias
        var agenciasLimpias = req.Agencias.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct().ToList();
        var existingAgs = await _db.ProfesionalAgencias.Where(x => x.ProfesionalId == p.Id).ToListAsync(ct);
        _db.ProfesionalAgencias.RemoveRange(existingAgs);
        foreach (var ag in agenciasLimpias)
        {
            _db.ProfesionalAgencias.Add(new ProfesionalAgencia { TenantId = tenant, ProfesionalId = p.Id, Agencia = ag });
        }
        await _db.SaveChangesAsync(ct);

        // Propagar rol + sedes al TenantUser vinculado si existe. Es un no-op si el
        // profesional aun no tiene usuario de acceso — el rol queda persistido en
        // Profesional.RolPredeterminadoId y se aplica cuando se cree el usuario.
        await PropagarAlUsuarioVinculadoAsync(p.Id, req.RolPredeterminadoId, agenciasLimpias, actor, ct);

        return await GetProfesionalAsync(p.Id, ct);
    }

    /// <summary>Si existe TenantUser con ProfesionalId=<paramref name="profesionalId"/>,
    /// sincroniza al PlatformUser vinculado los datos personales del profesional
    /// (documento, nombres, apellidos, celular, ciudad, display) y llama a
    /// UsuarioAdminService.AsignarAsync para replicarle rol + sedes. Preserva
    /// el flag EsGlobal actual del usuario. Asi el operador no tiene que ir a
    /// /cfg-usuarios a poner al dia los datos cuando cambia algo del profesional.</summary>
    private async Task PropagarAlUsuarioVinculadoAsync(Guid profesionalId, Guid? rolId, IReadOnlyList<string> nombresSedes, Guid actor, CancellationToken ct)
    {
        var tu = await _db.TenantUsers.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.ProfesionalId == profesionalId, ct);
        if (tu is null) { return; }

        // Cargar el profesional actualizado (ya persistido en el Save previo)
        // para copiar sus datos al PlatformUser vinculado.
        var prof = await _db.Profesionales.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == profesionalId, ct);

        // Sincronizar datos personales del PlatformUser desde el profesional.
        // El correo NO se toca aqui — es identidad de login y se edita solo
        // desde /cfg-usuarios (el usuario loguea con documento, no con correo,
        // pero cambiar el correo puede romper flujos de recuperacion).
        var pu = await _db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == tu.PlatformUserId, ct);
        var esGlobal = pu?.EsGlobal ?? false;
        if (pu is not null && prof is not null)
        {
            pu.Documento = prof.NumeroDocumento;
            pu.PrimerNombre = prof.PrimerNombre;
            pu.SegundoNombre = prof.SegundoNombre;
            pu.PrimerApellido = prof.PrimerApellido;
            pu.SegundoApellido = prof.SegundoApellido;
            pu.DisplayName = prof.NombreCompleto;
            pu.Celular = prof.Celular;
            pu.Ciudad = prof.Ciudad;
            await _db.SaveChangesAsync(ct);
        }

        // Traducir nombres de sedes a IDs. Sedes que no matcheen se ignoran.
        Guid[] sucursalIds = Array.Empty<Guid>();
        if (nombresSedes.Count > 0)
        {
            sucursalIds = await _db.Sucursales.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.TenantId == tu.TenantId && nombresSedes.Contains(s.Nombre))
                .Select(s => s.Id)
                .ToArrayAsync(ct);
        }
        await _usuarios.AsignarAsync(tu.Id, rolId, sucursalIds, esGlobal, actor, ct);
    }

    public async Task<bool> DeleteProfesionalAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Profesionales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return false; }
        _db.Profesionales.Remove(p);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
