using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class RemisionService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : IRemisionService
{
    public async Task<IReadOnlyList<RemisionItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaRemisiones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new RemisionItemDto(
                x.Id, x.HistoriaClinicaId, x.Capitulo,
                x.EspecialidadCodigo, x.EspecialidadNombre, x.Motivo, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CapituloCupDto>> ListarCapitulosAsync(CancellationToken ct = default)
    {
        // El "capitulo" CUPS vive en Cup.Descripcion (ej: "CapItulo 03 SISTEMA VISUAL").
        // Devolvemos la lista distinta ordenada alfabeticamente con su conteo de procedimientos.
        // NOTA: el OrderBy se hace despues de materializar porque EF Core 9 no puede traducir
        // OrderBy(x => x.Nombre) sobre un GroupBy proyectado a un record (CapituloCupDto).
        var rows = await db.Cups.AsNoTracking()
            .Where(c => c.Descripcion != null && c.Descripcion != "")
            .GroupBy(c => c.Descripcion!)
            .Select(g => new { Nombre = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(r => new CapituloCupDto(r.Nombre, r.Total))
            .ToList();
    }

    public async Task<IReadOnlyList<EspecialidadCupDto>> BuscarEspecialidadesAsync(
        string? capitulo, string? termino, int take = 30, CancellationToken ct = default)
    {
        if (take <= 0) { take = 30; }
        if (take > 200) { take = 200; }

        var q = db.Cups.AsNoTracking().Where(c => c.Nombre != null);

        if (!string.IsNullOrWhiteSpace(capitulo))
        {
            q = q.Where(c => c.Descripcion == capitulo);
        }

        if (!string.IsNullOrWhiteSpace(termino))
        {
            var t = termino.Trim().ToLowerInvariant();
            q = q.Where(c =>
                (c.Nombre != null && c.Nombre.ToLower().Contains(t)) ||
                (c.Codigo != null && c.Codigo.ToLower().Contains(t)));
        }

        return await q
            .OrderBy(c => c.Nombre)
            .Take(take)
            .Select(c => new EspecialidadCupDto(c.Id, c.Codigo, c.Nombre!))
            .ToListAsync(ct);
    }

    public async Task<RemisionItemDto> AgregarAsync(
        Guid historiaId, AgregarRemisionRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Capitulo))
        { throw new InvalidOperationException("El capitulo es obligatorio."); }
        if (string.IsNullOrWhiteSpace(req.EspecialidadNombre))
        { throw new InvalidOperationException("La especialidad es obligatoria."); }

        var siguiente = 1 + await db.HistoriaClinicaRemisiones
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaRemision
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            Capitulo = req.Capitulo.Trim(),
            EspecialidadCodigo = Trim(req.EspecialidadCodigo),
            EspecialidadNombre = req.EspecialidadNombre.Trim(),
            Motivo = Trim(req.Motivo),
            Orden = siguiente
        };
        db.HistoriaClinicaRemisiones.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new RemisionItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.Capitulo,
            entity.EspecialidadCodigo, entity.EspecialidadNombre, entity.Motivo, entity.Orden);
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaRemisiones.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        db.HistoriaClinicaRemisiones.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaRemisiones
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
