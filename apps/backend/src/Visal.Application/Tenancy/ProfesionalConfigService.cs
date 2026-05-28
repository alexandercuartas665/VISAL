using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class ProfesionalConfigService : IProfesionalConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ProfesionalConfigService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
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

    public async Task<ProfesionalDetailDto?> GetProfesionalAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.Profesionales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return null; }
        var subs = await _db.ProfesionalSubCategorias.AsNoTracking().Where(x => x.ProfesionalId == id).Select(x => x.SubCategoriaId).ToListAsync(ct);
        var ags = await _db.ProfesionalAgencias.AsNoTracking().Where(x => x.ProfesionalId == id).OrderBy(x => x.Agencia).Select(x => x.Agencia).ToListAsync(ct);
        return new ProfesionalDetailDto(p.Id, p.NumeroDocumento, p.TipoDocumento, p.PrimerNombre, p.SegundoNombre,
            p.PrimerApellido, p.SegundoApellido, p.NombreCompleto, p.TipoProfesionalId, p.RegistroMedico, p.Ciudad, p.Celular, p.FirmaUrl, subs, ags);
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
        var existingAgs = await _db.ProfesionalAgencias.Where(x => x.ProfesionalId == p.Id).ToListAsync(ct);
        _db.ProfesionalAgencias.RemoveRange(existingAgs);
        foreach (var ag in req.Agencias.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct())
        {
            _db.ProfesionalAgencias.Add(new ProfesionalAgencia { TenantId = tenant, ProfesionalId = p.Id, Agencia = ag });
        }
        await _db.SaveChangesAsync(ct);

        return await GetProfesionalAsync(p.Id, ct);
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
