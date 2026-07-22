using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class CatalogoTipoServicioService(IApplicationDbContext db, ITenantContext tenant)
    : ICatalogoTipoServicioService
{
    private static string NormalizarCodigo(string? c)
    {
        if (string.IsNullOrWhiteSpace(c)) { throw new InvalidOperationException("Codigo es obligatorio."); }
        return c.Trim().ToUpperInvariant();
    }

    public async Task<IReadOnlyList<CatalogoTipoServicioDto>> ListarAsync(bool incluirInactivos = false, CancellationToken ct = default)
    {
        var q = db.CatalogosTipoServicio.AsNoTracking();
        if (!incluirInactivos) { q = q.Where(x => x.Activo); }
        var rows = await q.ToListAsync(ct);
        return rows
            .OrderBy(r => r.Orden).ThenBy(r => r.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(r => new CatalogoTipoServicioDto(r.Id, r.Codigo, r.Nombre, r.Orden, r.Activo, r.TipoArchivoRips))
            .ToList();
    }

    public async Task<CatalogoTipoServicioDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.CatalogosTipoServicio.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : new CatalogoTipoServicioDto(r.Id, r.Codigo, r.Nombre, r.Orden, r.Activo, r.TipoArchivoRips);
    }

    public async Task<CatalogoTipoServicioDto> GuardarAsync(GuardarTipoServicioRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var codigo = NormalizarCodigo(req.Codigo);
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { throw new InvalidOperationException("Nombre es obligatorio."); }

        CatalogoTipoServicio? entity;
        if (req.Id is Guid gid)
        {
            entity = await db.CatalogosTipoServicio.FirstOrDefaultAsync(x => x.Id == gid, ct);
            if (entity is null) { throw new InvalidOperationException("Tipo de servicio no encontrado."); }
        }
        else
        {
            var duplicado = await db.CatalogosTipoServicio.AsNoTracking()
                .AnyAsync(x => x.Codigo == codigo, ct);
            if (duplicado) { throw new InvalidOperationException($"Ya existe un tipo con codigo '{codigo}'."); }
            entity = new CatalogoTipoServicio { TenantId = tid };
            db.CatalogosTipoServicio.Add(entity);
        }
        entity.Codigo = codigo;
        entity.Nombre = nombre;
        entity.Orden = req.Orden;
        entity.Activo = req.Activo;
        entity.TipoArchivoRips = string.IsNullOrWhiteSpace(req.TipoArchivoRips) ? null : req.TipoArchivoRips.Trim().ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return new CatalogoTipoServicioDto(entity.Id, entity.Codigo, entity.Nombre, entity.Orden, entity.Activo, entity.TipoArchivoRips);
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.CatalogosTipoServicio.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        // Verificar que no este referenciado en servicios_contrato ni asignaciones.
        var usado = await db.ServiciosContrato.AnyAsync(s => s.Modulo == e.Codigo, ct)
                 || await db.Asignaciones.AnyAsync(a => a.Modulo == e.Codigo || a.TipoServicio == e.Codigo, ct);
        if (usado)
        { throw new InvalidOperationException("No se puede borrar: hay servicios o asignaciones que usan este tipo. Desactivalo en su lugar."); }
        db.CatalogosTipoServicio.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
