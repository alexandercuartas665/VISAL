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
                x.Id, x.HistoriaClinicaId,
                x.EspecialidadCodigo, x.EspecialidadNombre,
                x.Cantidad, x.Motivo, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<RemisionItemDto> AgregarAsync(
        Guid historiaId, AgregarRemisionRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.EspecialidadNombre))
        { throw new InvalidOperationException("La descripcion de la remision es obligatoria."); }
        await db.EnsureAbiertaAsync(historiaId, ct);

        var siguiente = 1 + await db.HistoriaClinicaRemisiones
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaRemision
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            Capitulo = "", // legacy, no se usa en el flujo nuevo
            EspecialidadCodigo = Trim(req.EspecialidadCodigo),
            EspecialidadNombre = req.EspecialidadNombre.Trim(),
            Cantidad = Trim(req.Cantidad),
            Motivo = Trim(req.Motivo),
            Orden = siguiente
        };
        db.HistoriaClinicaRemisiones.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new RemisionItemDto(
            entity.Id, entity.HistoriaClinicaId,
            entity.EspecialidadCodigo, entity.EspecialidadNombre,
            entity.Cantidad, entity.Motivo, entity.Orden);
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaRemisiones.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
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
