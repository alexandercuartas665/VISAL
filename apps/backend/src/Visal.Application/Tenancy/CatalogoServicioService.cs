using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class CatalogoServicioService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit) : ICatalogoServicioService
{
    public async Task<(IReadOnlyList<CatalogoServicioDto> rows, int total)> SearchAsync(
        TipoCatalogoServicio tipo, string? termino, int skip, int take, CancellationToken ct = default)
    {
        var q = db.CatalogosServicioReferencia.AsNoTracking().Where(c => c.Tipo == tipo);
        if (!string.IsNullOrWhiteSpace(termino))
        {
            var t = termino.Trim().ToLowerInvariant();
            q = q.Where(c => c.Codigo.ToLower().Contains(t) || c.Nombre.ToLower().Contains(t));
        }

        var total = await q.CountAsync(ct);
        if (take <= 0) { take = 50; }
        if (take > 500) { take = 500; }

        var rows = await q
            .OrderBy(c => c.Codigo)
            .Skip(skip).Take(take)
            .Select(c => new CatalogoServicioDto(c.Id, c.Codigo, c.Nombre, c.Activo))
            .ToListAsync(ct);
        return (rows, total);
    }

    public async Task<CatalogoServicioDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.CatalogosServicioReferencia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : new CatalogoServicioDto(c.Id, c.Codigo, c.Nombre, c.Activo);
    }

    public async Task<CatalogoServicioDto?> SaveAsync(SaveCatalogoServicioRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) { return null; }
        if (string.IsNullOrWhiteSpace(req.Codigo) || string.IsNullOrWhiteSpace(req.Nombre)) { return null; }

        var codigo = req.Codigo.Trim();
        var nombre = req.Nombre.Trim();

        CatalogoServicioReferencia? entity;
        if (req.Id is Guid id)
        {
            entity = await db.CatalogosServicioReferencia.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null) { return null; }
            entity.Codigo = codigo;
            entity.Nombre = nombre;
            entity.Activo = req.Activo;
        }
        else
        {
            // Upsert por (tipo, codigo): si ya existe, actualizar el nombre.
            var existente = await db.CatalogosServicioReferencia
                .FirstOrDefaultAsync(x => x.Tipo == req.Tipo && x.Codigo == codigo, ct);
            if (existente is not null)
            {
                existente.Nombre = nombre;
                existente.Activo = req.Activo;
                entity = existente;
            }
            else
            {
                entity = new CatalogoServicioReferencia
                {
                    TenantId = tenantId,
                    Tipo = req.Tipo,
                    Codigo = codigo,
                    Nombre = nombre,
                    Activo = req.Activo
                };
                db.CatalogosServicioReferencia.Add(entity);
            }
        }

        await db.SaveChangesAsync(ct);
        audit.Write(actorUserId, "catalogo-servicio.save", nameof(CatalogoServicioReferencia), entity.Id,
            previousValue: null, newValue: new { entity.Tipo, entity.Codigo, entity.Nombre, entity.Activo },
            tenantId: entity.TenantId);
        return new CatalogoServicioDto(entity.Id, entity.Codigo, entity.Nombre, entity.Activo);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var e = await db.CatalogosServicioReferencia.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.CatalogosServicioReferencia.Remove(e);
        await db.SaveChangesAsync(ct);
        audit.Write(actorUserId, "catalogo-servicio.delete", nameof(CatalogoServicioReferencia), e.Id,
            previousValue: new { e.Tipo, e.Codigo, e.Nombre }, newValue: null, tenantId: e.TenantId);
        return true;
    }

    public async Task<int> ImportAsync(TipoCatalogoServicio tipo,
        IReadOnlyList<CatalogoServicioImportRow> rows, Guid actorUserId,
        IProgress<CatalogoServicioImportProgress>? progress = null, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) { return 0; }
        var total = rows.Count;
        progress?.Report(new CatalogoServicioImportProgress("Validando", 0, total));

        // Cache de codigos existentes de ese tipo (para upsert eficiente).
        var codigosExistentes = await db.CatalogosServicioReferencia
            .Where(c => c.Tipo == tipo)
            .Select(c => new { c.Id, c.Codigo })
            .ToListAsync(ct);
        var indice = codigosExistentes
            .GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var procesados = 0;
        const int lote = 500;
        var buffer = new List<CatalogoServicioReferencia>(lote);
        var updates = new List<(Guid Id, string Nombre)>();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var cod = row.Codigo?.Trim();
            var nom = row.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(cod) || string.IsNullOrWhiteSpace(nom))
            {
                procesados++;
                continue;
            }
            if (indice.TryGetValue(cod, out var existingId))
            {
                updates.Add((existingId, nom));
            }
            else
            {
                buffer.Add(new CatalogoServicioReferencia
                {
                    TenantId = tenantId,
                    Tipo = tipo,
                    Codigo = cod,
                    Nombre = nom,
                    Activo = true
                });
                indice[cod] = Guid.NewGuid();
            }
            procesados++;

            if (buffer.Count >= lote)
            {
                db.CatalogosServicioReferencia.AddRange(buffer);
                await db.SaveChangesAsync(ct);
                buffer.Clear();
                progress?.Report(new CatalogoServicioImportProgress("Insertando", procesados, total));
            }
        }
        if (buffer.Count > 0)
        {
            db.CatalogosServicioReferencia.AddRange(buffer);
            await db.SaveChangesAsync(ct);
        }
        // Aplicar updates de nombre a los ya existentes en un solo batch.
        if (updates.Count > 0)
        {
            var ids = updates.Select(u => u.Id).ToHashSet();
            var trackables = await db.CatalogosServicioReferencia.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
            var byId = trackables.ToDictionary(x => x.Id);
            foreach (var (uid, nom) in updates)
            {
                if (byId.TryGetValue(uid, out var e)) { e.Nombre = nom; }
            }
            await db.SaveChangesAsync(ct);
        }
        progress?.Report(new CatalogoServicioImportProgress("Listo", total, total));

        audit.Write(actorUserId, "catalogo-servicio.import", nameof(CatalogoServicioReferencia), Guid.Empty,
            previousValue: null, newValue: new { tipo = tipo.ToString(), total = procesados },
            tenantId: tenantId);
        return procesados;
    }

    public async Task<int> ClearAllAsync(TipoCatalogoServicio tipo, Guid actorUserId, CancellationToken ct = default)
    {
        var eliminados = await db.CatalogosServicioReferencia
            .Where(c => c.Tipo == tipo)
            .ExecuteDeleteAsync(ct);
        audit.Write(actorUserId, "catalogo-servicio.clear-all", nameof(CatalogoServicioReferencia), Guid.Empty,
            previousValue: null, newValue: new { tipo = tipo.ToString(), eliminados },
            tenantId: tenant.TenantId ?? Guid.Empty);
        return eliminados;
    }
}
