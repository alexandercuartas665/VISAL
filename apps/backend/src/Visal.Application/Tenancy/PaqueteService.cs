using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class PaqueteService(IApplicationDbContext db, ITenantContext tenant) : IPaqueteService
{
    public async Task<IReadOnlyList<PaqueteDto>> ListarAsync(string? filtro = null, bool soloActivos = false, CancellationToken ct = default)
    {
        var q = db.Paquetes.AsNoTracking();
        if (soloActivos) { q = q.Where(p => p.Activo); }
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim().ToLower();
            q = q.Where(p => p.Codigo.ToLower().Contains(f) || p.Nombre.ToLower().Contains(f));
        }
        return await q.OrderBy(p => p.Codigo)
            .Select(p => new PaqueteDto(p.Id, p.Codigo, p.Nombre, p.Activo, p.Precio))
            .ToListAsync(ct);
    }

    public async Task<PaqueteDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Paquetes.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PaqueteDto(p.Id, p.Codigo, p.Nombre, p.Activo, p.Precio))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PaqueteDto?> SaveAsync(SavePaqueteRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0 || nombre.Length == 0)
        {
            throw new InvalidOperationException("El codigo y el nombre del paquete son obligatorios.");
        }
        if (req.Precio is decimal p && p < 0)
        {
            throw new InvalidOperationException("El precio no puede ser negativo.");
        }

        Paquete entity;
        if (req.Id is Guid id)
        {
            entity = await db.Paquetes.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Paquete no encontrado.");
            if (await db.Paquetes.AnyAsync(x => x.Codigo == codigo && x.Id != id, ct))
            {
                throw new InvalidOperationException($"Ya existe otro paquete con el codigo '{codigo}'.");
            }
        }
        else
        {
            if (tenant.TenantId is not Guid tid) { return null; }
            if (await db.Paquetes.AnyAsync(x => x.Codigo == codigo, ct))
            {
                throw new InvalidOperationException($"Ya existe un paquete con el codigo '{codigo}'.");
            }
            entity = new Paquete { TenantId = tid };
            db.Paquetes.Add(entity);
        }

        entity.Codigo = codigo;
        entity.Nombre = nombre;
        entity.Activo = req.Activo;
        entity.Precio = req.Precio;

        await db.SaveChangesAsync(ct);
        return new PaqueteDto(entity.Id, entity.Codigo, entity.Nombre, entity.Activo, entity.Precio);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.Paquetes.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (e is null) { return false; }
        db.Paquetes.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SembrarAsync(IReadOnlyList<SavePaqueteRequest> items, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return 0; }
        if (items.Count == 0) { return 0; }

        var codigos = items.Select(i => (i.Codigo ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existentes = await db.Paquetes
            .Where(p => codigos.Contains(p.Codigo))
            .Select(p => p.Codigo)
            .ToListAsync(ct);
        var existentesSet = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);

        var n = 0;
        foreach (var it in items)
        {
            var codigo = (it.Codigo ?? "").Trim();
            var nombre = (it.Nombre ?? "").Trim();
            if (codigo.Length == 0 || nombre.Length == 0) { continue; }
            if (existentesSet.Contains(codigo)) { continue; }
            db.Paquetes.Add(new Paquete
            {
                TenantId = tid,
                Codigo = codigo,
                Nombre = nombre,
                Activo = it.Activo,
                Precio = it.Precio
            });
            existentesSet.Add(codigo);
            n++;
        }
        if (n > 0) { await db.SaveChangesAsync(ct); }
        return n;
    }

    // ---------------- Detalle de servicios del paquete ----------------

    public async Task<IReadOnlyList<PaqueteServicioDto>> ListarServiciosAsync(Guid paqueteId, CancellationToken ct = default)
    {
        // Join left al catalogo para traer el nombre. Si el catalogo se borro/desactivo,
        // el codigo queda visible con nombre null (marcado en UI como "(codigo suelto)").
        return await (
            from ps in db.PaqueteServicios.AsNoTracking().Where(x => x.PaqueteId == paqueteId)
            join c in db.CatalogosServicioReferencia.AsNoTracking()
                on ps.CatalogoServicioReferenciaId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            orderby ps.Codigo
            select new PaqueteServicioDto(
                ps.Id, ps.PaqueteId, ps.Codigo,
                c != null ? c.Nombre : null,
                ps.Cantidad,
                ps.CatalogoServicioReferenciaId))
            .ToListAsync(ct);
    }

    public async Task<PaqueteServicioDto> AgregarServicioAsync(AgregarPaqueteServicioRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }
        var codigo = (req.Codigo ?? "").Trim();
        if (codigo.Length == 0) { throw new InvalidOperationException("El codigo del servicio es obligatorio."); }
        if (req.Cantidad <= 0) { throw new InvalidOperationException("La cantidad debe ser mayor a cero."); }
        if (!await db.Paquetes.AnyAsync(p => p.Id == req.PaqueteId, ct))
        {
            throw new InvalidOperationException("Paquete no encontrado.");
        }
        if (await db.PaqueteServicios.AnyAsync(x => x.PaqueteId == req.PaqueteId && x.Codigo == codigo, ct))
        {
            throw new InvalidOperationException($"El servicio con codigo '{codigo}' ya esta en este paquete. Edita su cantidad en vez de agregarlo de nuevo.");
        }

        var entity = new PaqueteServicio
        {
            TenantId = tid,
            PaqueteId = req.PaqueteId,
            Codigo = codigo,
            Cantidad = req.Cantidad,
            CatalogoServicioReferenciaId = req.CatalogoServicioReferenciaId
        };
        db.PaqueteServicios.Add(entity);
        await db.SaveChangesAsync(ct);

        // Nombre lookup para devolver el DTO completo.
        string? nombre = null;
        if (entity.CatalogoServicioReferenciaId is Guid cid)
        {
            nombre = await db.CatalogosServicioReferencia.AsNoTracking()
                .Where(c => c.Id == cid).Select(c => c.Nombre).FirstOrDefaultAsync(ct);
        }
        return new PaqueteServicioDto(entity.Id, entity.PaqueteId, entity.Codigo, nombre, entity.Cantidad, entity.CatalogoServicioReferenciaId);
    }

    public async Task<PaqueteServicioDto?> ActualizarCantidadServicioAsync(Guid servicioId, int cantidad, Guid actor, CancellationToken ct = default)
    {
        if (cantidad <= 0) { throw new InvalidOperationException("La cantidad debe ser mayor a cero."); }
        var e = await db.PaqueteServicios.FirstOrDefaultAsync(x => x.Id == servicioId, ct);
        if (e is null) { return null; }
        e.Cantidad = cantidad;
        await db.SaveChangesAsync(ct);
        string? nombre = null;
        if (e.CatalogoServicioReferenciaId is Guid cid)
        {
            nombre = await db.CatalogosServicioReferencia.AsNoTracking()
                .Where(c => c.Id == cid).Select(c => c.Nombre).FirstOrDefaultAsync(ct);
        }
        return new PaqueteServicioDto(e.Id, e.PaqueteId, e.Codigo, nombre, e.Cantidad, e.CatalogoServicioReferenciaId);
    }

    public async Task<bool> QuitarServicioAsync(Guid servicioId, Guid actor, CancellationToken ct = default)
    {
        var e = await db.PaqueteServicios.FirstOrDefaultAsync(x => x.Id == servicioId, ct);
        if (e is null) { return false; }
        db.PaqueteServicios.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CatalogoServicioAutocompleteDto>> BuscarCatalogoAsync(string filtro, int limite = 20, CancellationToken ct = default)
    {
        var f = (filtro ?? "").Trim().ToLower();
        if (f.Length < 2) { return Array.Empty<CatalogoServicioAutocompleteDto>(); }
        if (limite <= 0 || limite > 100) { limite = 20; }
        return await db.CatalogosServicioReferencia.AsNoTracking()
            .Where(c => c.Activo && (c.Codigo.ToLower().Contains(f) || c.Nombre.ToLower().Contains(f)))
            .OrderBy(c => c.Codigo)
            .Take(limite)
            .Select(c => new CatalogoServicioAutocompleteDto(c.Id, c.Codigo, c.Nombre, c.Tipo.ToString()))
            .ToListAsync(ct);
    }
}
