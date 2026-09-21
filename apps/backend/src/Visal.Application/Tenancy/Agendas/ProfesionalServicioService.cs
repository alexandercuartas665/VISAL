using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Agendas;

public sealed class ProfesionalServicioService(IApplicationDbContext db, ITenantContext tenant) : IProfesionalServicioService
{
    public async Task<IReadOnlyList<ProfesionalServicioDto>> ListarPorProfesionalAsync(Guid profesionalId, CancellationToken ct = default)
    {
        var rows = await db.ProfesionalServicios.AsNoTracking()
            .Where(s => s.ProfesionalId == profesionalId)
            .OrderBy(s => s.Nombre)
            .ToListAsync(ct);
        return rows.Select(s => new ProfesionalServicioDto(s.Id, s.Codigo, s.Nombre)).ToList();
    }

    public async Task<IReadOnlyList<CatalogoServicioBusquedaDto>> BuscarCatalogoAsync(Guid profesionalId, string? termino, int take = 20, CancellationToken ct = default)
    {
        var t = (termino ?? "").Trim().ToLowerInvariant();
        if (t.Length < 2) { return Array.Empty<CatalogoServicioBusquedaDto>(); }

        var yaTiene = await db.ProfesionalServicios.AsNoTracking()
            .Where(s => s.ProfesionalId == profesionalId).Select(s => s.Codigo).ToListAsync(ct);
        var yaSet = yaTiene.ToHashSet();

        // Busca en los 4 tipos del catalogo (codigo o nombre).
        var rows = await db.CatalogosServicioReferencia.AsNoTracking()
            .Where(c => c.Activo && (c.Codigo.ToLower().Contains(t) || c.Nombre.ToLower().Contains(t)))
            .OrderBy(c => c.Codigo)
            .Take(take + yaSet.Count)
            .ToListAsync(ct);

        return rows.Where(c => !yaSet.Contains(c.Codigo))
            .Take(take)
            .Select(c => new CatalogoServicioBusquedaDto(c.Id, c.Codigo, c.Nombre, c.Tipo.ToString()))
            .ToList();
    }

    public async Task<ProfesionalServicioDto> AgregarAsync(Guid profesionalId, Guid catalogoServicioReferenciaId, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var cat = await db.CatalogosServicioReferencia.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == catalogoServicioReferenciaId, ct)
            ?? throw new InvalidOperationException("Servicio del catalogo no encontrado.");

        var existente = await db.ProfesionalServicios
            .FirstOrDefaultAsync(s => s.ProfesionalId == profesionalId && s.Codigo == cat.Codigo, ct);
        if (existente is not null)
        {
            return new ProfesionalServicioDto(existente.Id, existente.Codigo, existente.Nombre);
        }

        var entity = new ProfesionalServicio
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            ProfesionalId = profesionalId,
            Codigo = cat.Codigo,
            Nombre = cat.Nombre,
            CatalogoServicioReferenciaId = cat.Id
        };
        db.ProfesionalServicios.Add(entity);
        await db.SaveChangesAsync(ct);
        return new ProfesionalServicioDto(entity.Id, entity.Codigo, entity.Nombre);
    }

    public async Task<bool> QuitarAsync(Guid profesionalServicioId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.ProfesionalServicios.FirstOrDefaultAsync(s => s.Id == profesionalServicioId, ct);
        if (entity is null) { return false; }
        db.ProfesionalServicios.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
