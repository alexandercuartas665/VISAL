using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class TipologiaArchivoService : ITipologiaArchivoService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TipologiaArchivoService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TipologiaArchivoDto>> ListAsync(bool soloActivos = false, CancellationToken ct = default)
    {
        var q = _db.TipologiaArchivos.AsNoTracking();
        if (soloActivos) { q = q.Where(t => t.Activo); }
        return await q.OrderBy(t => t.Nombre)
            .Select(t => new TipologiaArchivoDto(t.Id, t.Nombre, t.Color, t.Activo, t.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<TipologiaArchivoDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.TipologiaArchivos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : new TipologiaArchivoDto(t.Id, t.Nombre, t.Color, t.Activo, t.CreatedAt);
    }

    public async Task<TipologiaArchivoDto?> SaveAsync(SaveTipologiaArchivoRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var nombre = (req.Nombre ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nombre)) { throw new InvalidOperationException("El nombre es obligatorio."); }
        var color = NormalizeColor(req.Color);

        TipologiaArchivo? entity;
        if (req.Id is Guid id)
        {
            entity = await _db.TipologiaArchivos.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null) { return null; }
            // Unique check al renombrar.
            if (!string.Equals(entity.Nombre, nombre, StringComparison.OrdinalIgnoreCase))
            {
                if (await _db.TipologiaArchivos.AnyAsync(x => x.Id != id && x.Nombre == nombre, ct))
                {
                    throw new InvalidOperationException($"Ya existe una tipologia con el nombre '{nombre}'.");
                }
            }
        }
        else
        {
            if (await _db.TipologiaArchivos.AnyAsync(x => x.Nombre == nombre, ct))
            {
                throw new InvalidOperationException($"Ya existe una tipologia con el nombre '{nombre}'.");
            }
            entity = new TipologiaArchivo { TenantId = tenantId };
            _db.TipologiaArchivos.Add(entity);
        }
        entity.Nombre = nombre;
        entity.Color = color;
        entity.Activo = req.Activo;

        await _db.SaveChangesAsync(ct);
        return new TipologiaArchivoDto(entity.Id, entity.Nombre, entity.Color, entity.Activo, entity.CreatedAt);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var entity = await _db.TipologiaArchivos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        _db.TipologiaArchivos.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SeedDefaultsAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return 0; }
        if (await _db.TipologiaArchivos.AnyAsync(ct)) { return 0; }
        var seeds = new[]
        {
            ("Lista de firmas",   "#10b981"), // verde
            ("Escala",            "#3b82f6"), // azul
            ("Formato",           "#8b5cf6"), // violeta
            ("Examen",            "#ef4444"), // rojo
            ("Firma del Paciente","#0d9488"), // teal (igual que el WhatsApp)
            ("Otros",             "#64748b"), // gris
        };
        foreach (var (n, c) in seeds)
        {
            _db.TipologiaArchivos.Add(new TipologiaArchivo
            {
                TenantId = tenantId,
                Nombre = n,
                Color = c,
                Activo = true
            });
        }
        await _db.SaveChangesAsync(ct);
        return seeds.Length;
    }

    private static string NormalizeColor(string? raw)
    {
        var c = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(c)) { return "#64748b"; }
        // Validar #RRGGBB o #RGB.
        if (c[0] != '#') { c = "#" + c; }
        if (c.Length is not (4 or 7)) { return "#64748b"; }
        for (var i = 1; i < c.Length; i++)
        {
            var ch = c[i];
            var hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
            if (!hex) { return "#64748b"; }
        }
        return c.ToLowerInvariant();
    }
}
