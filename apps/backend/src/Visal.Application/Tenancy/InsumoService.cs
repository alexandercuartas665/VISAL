using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class InsumoService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : IInsumoService
{
    public async Task<IReadOnlyList<InsumoItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaInsumos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new InsumoItemDto(
                x.Id, x.HistoriaClinicaId, x.Codigo, x.Descripcion,
                x.Cantidad, x.Observaciones, x.MipresUrl, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<InsumoItemDto> AgregarAsync(
        Guid historiaId, AgregarInsumoRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Descripcion))
        {
            throw new InvalidOperationException("La descripcion del insumo es obligatoria.");
        }
        await db.EnsureAbiertaAsync(historiaId, ct);

        var siguiente = 1 + await db.HistoriaClinicaInsumos
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaInsumo
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            Codigo = Trim(req.Codigo),
            Descripcion = req.Descripcion.Trim(),
            Cantidad = Trim(req.Cantidad),
            Observaciones = Trim(req.Observaciones),
            MipresUrl = Trim(req.MipresUrl),
            Orden = siguiente
        };
        db.HistoriaClinicaInsumos.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new InsumoItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.Codigo, entity.Descripcion,
            entity.Cantidad, entity.Observaciones, entity.MipresUrl, entity.Orden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarInsumoRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaInsumos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        await db.EnsureAbiertaAsync(entity.HistoriaClinicaId, ct);
        entity.Cantidad = Trim(req.Cantidad);
        entity.Observaciones = Trim(req.Observaciones);
        entity.MipresUrl = Trim(req.MipresUrl);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaInsumos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
        db.HistoriaClinicaInsumos.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaInsumos
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
