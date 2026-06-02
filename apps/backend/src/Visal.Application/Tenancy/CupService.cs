using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class CupService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit) : ICupService
{
    public async Task<(IReadOnlyList<CupDto> rows, int total)> SearchAsync(
        string? termino, int skip, int take, CancellationToken ct = default)
    {
        var q = db.Cups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(termino))
        {
            var t = termino.Trim().ToLowerInvariant();
            q = q.Where(c =>
                (c.Codigo != null && c.Codigo.ToLower().Contains(t)) ||
                (c.Nombre != null && c.Nombre.ToLower().Contains(t)) ||
                (c.Descripcion != null && c.Descripcion.ToLower().Contains(t)) ||
                (c.ExtraIV != null && c.ExtraIV.ToLower().Contains(t)) ||
                (c.ExtraV != null && c.ExtraV.ToLower().Contains(t)));
        }

        var total = await q.CountAsync(ct);
        if (take <= 0) { take = 50; }
        if (take > 500) { take = 500; }

        var rows = await q
            .OrderBy(c => c.Codigo)
            .Skip(skip).Take(take)
            .Select(c => new CupDto(
                c.Id, c.Codigo, c.Nombre, c.Descripcion, c.Habilitado,
                c.ExtraIV, c.ExtraV, c.IsStandardGEL, c.IsStandardMSPS))
            .ToListAsync(ct);
        return (rows, total);
    }

    public async Task<CupDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Cups.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CupDetailDto(
                c.Id, c.Tabla, c.Codigo, c.Nombre, c.Descripcion,
                c.Habilitado, c.Aplicacion, c.IsStandardGEL, c.IsStandardMSPS,
                c.ExtraI, c.ExtraII, c.ExtraIII, c.ExtraIV, c.ExtraV,
                c.ExtraVI, c.ExtraVII, c.ExtraVIII, c.ExtraIX, c.ExtraX,
                c.ValorRegistro, c.UsuarioResponsable, c.FechaActualizacion,
                c.IsPublicPrivate))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CupDetailDto?> SaveAsync(SaveCupRequest r, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        Cup entity;
        if (r.Id is Guid id)
        {
            entity = await db.Cups.FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new InvalidOperationException("CUP no encontrado.");
        }
        else
        {
            entity = new Cup { TenantId = tid };
            db.Cups.Add(entity);
        }

        entity.Tabla = Norm(r.Tabla);
        entity.Codigo = Norm(r.Codigo);
        entity.Nombre = Norm(r.Nombre);
        entity.Descripcion = Norm(r.Descripcion);
        entity.Habilitado = Norm(r.Habilitado);
        entity.Aplicacion = Norm(r.Aplicacion);
        entity.IsStandardGEL = Norm(r.IsStandardGEL);
        entity.IsStandardMSPS = Norm(r.IsStandardMSPS);
        entity.ExtraI = Norm(r.ExtraI);
        entity.ExtraII = Norm(r.ExtraII);
        entity.ExtraIII = Norm(r.ExtraIII);
        entity.ExtraIV = Norm(r.ExtraIV);
        entity.ExtraV = Norm(r.ExtraV);
        entity.ExtraVI = Norm(r.ExtraVI);
        entity.ExtraVII = Norm(r.ExtraVII);
        entity.ExtraVIII = Norm(r.ExtraVIII);
        entity.ExtraIX = Norm(r.ExtraIX);
        entity.ExtraX = Norm(r.ExtraX);
        entity.ValorRegistro = Norm(r.ValorRegistro);
        entity.UsuarioResponsable = Norm(r.UsuarioResponsable);
        entity.FechaActualizacion = r.FechaActualizacion;
        entity.IsPublicPrivate = Norm(r.IsPublicPrivate);

        audit.Write(actor,
            r.Id is null ? "cup.create" : "cup.update",
            nameof(Cup), entity.Id,
            previousValue: null, newValue: new { entity.Codigo, entity.Nombre }, tenantId: tid);

        await db.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.Cups.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) { return false; }
        db.Cups.Remove(e);
        audit.Write(actor, "cup.delete", nameof(Cup), e.Id,
            previousValue: new { e.Codigo, e.Nombre }, newValue: null, tenantId: e.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ImportAsync(
        IReadOnlyList<CupImportRow> rows,
        Guid actor,
        IProgress<CupImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (rows.Count == 0) { return 0; }

        // Fase 1: validar y mapear a entidades en memoria.
        var insert = new List<Cup>(rows.Count);
        const int chunkValidacion = 1000;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            // Saltar filas vacias (sin codigo ni nombre).
            if (string.IsNullOrWhiteSpace(r.Codigo) && string.IsNullOrWhiteSpace(r.Nombre))
            { continue; }

            insert.Add(new Cup
            {
                TenantId = tid,
                Tabla = Norm(r.Tabla),
                Codigo = Norm(r.Codigo),
                Nombre = Norm(r.Nombre),
                Descripcion = Norm(r.Descripcion),
                Habilitado = Norm(r.Habilitado),
                Aplicacion = Norm(r.Aplicacion),
                IsStandardGEL = Norm(r.IsStandardGEL),
                IsStandardMSPS = Norm(r.IsStandardMSPS),
                ExtraI = Norm(r.ExtraI),
                ExtraII = Norm(r.ExtraII),
                ExtraIII = Norm(r.ExtraIII),
                ExtraIV = Norm(r.ExtraIV),
                ExtraV = Norm(r.ExtraV),
                ExtraVI = Norm(r.ExtraVI),
                ExtraVII = Norm(r.ExtraVII),
                ExtraVIII = Norm(r.ExtraVIII),
                ExtraIX = Norm(r.ExtraIX),
                ExtraX = Norm(r.ExtraX),
                ValorRegistro = Norm(r.ValorRegistro),
                UsuarioResponsable = Norm(r.UsuarioResponsable),
                FechaActualizacion = ParseDateTime(r.FechaActualizacion),
                IsPublicPrivate = Norm(r.IsPublicPrivate)
            });

            if (progress is not null && (i + 1) % chunkValidacion == 0)
            {
                progress.Report(new CupImportProgress("Validando", i + 1, rows.Count));
            }
            ct.ThrowIfCancellationRequested();
        }
        progress?.Report(new CupImportProgress("Validando", rows.Count, rows.Count));

        // Fase 2: insertar en lotes para mantener viable un archivo grande.
        const int chunkInsercion = 500;
        var insertados = 0;
        for (int offset = 0; offset < insert.Count; offset += chunkInsercion)
        {
            var batch = insert.Skip(offset).Take(chunkInsercion).ToList();
            db.Cups.AddRange(batch);
            await db.SaveChangesAsync(ct);
            insertados += batch.Count;
            progress?.Report(new CupImportProgress("Insertando", insertados, insert.Count));
            ct.ThrowIfCancellationRequested();
        }

        audit.Write(actor, "cup.import", nameof(Cup), Guid.Empty,
            previousValue: null, newValue: new { count = insertados }, tenantId: tid);

        progress?.Report(new CupImportProgress("Listo", insertados, insertados));
        return insertados;
    }

    public async Task<int> ClearAllAsync(Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var n = await db.Cups.ExecuteDeleteAsync(ct);
        audit.Write(actor, "cup.clear-all", nameof(Cup), Guid.Empty,
            previousValue: new { count = n }, newValue: null, tenantId: tid);
        return n;
    }

    private static string? Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Parsea fechas/timestamps del archivo CUPS. El muestra trae cosas como
    /// "2026-02-20 01:36:23 PM". Toleramos formatos comunes ISO y locales.
    /// </summary>
    private static DateTimeOffset? ParseDateTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var t = s.Trim();
        string[] formats = {
            "yyyy-MM-dd hh:mm:ss tt", "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd hh:mm tt", "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy"
        };
        if (DateTimeOffset.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            return dt;
        }
        if (DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
        {
            return dt;
        }
        return null;
    }
}
