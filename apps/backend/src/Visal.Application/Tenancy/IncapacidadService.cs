using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class IncapacidadService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : IIncapacidadService
{
    public async Task<IReadOnlyList<IncapacidadItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new IncapacidadItemDto(
                x.Id, x.HistoriaClinicaId, x.Motivo,
                x.FechaDesde, x.FechaHasta, x.Dias, x.Tipo, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<IncapacidadItemDto> AgregarAsync(
        Guid historiaId, AgregarIncapacidadRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Motivo))
        {
            throw new InvalidOperationException("El motivo de la incapacidad es obligatorio.");
        }

        var siguiente = 1 + await db.HistoriaClinicaIncapacidades
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaIncapacidad
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            Motivo = req.Motivo.Trim(),
            FechaDesde = req.FechaDesde,
            FechaHasta = req.FechaHasta,
            Dias = req.Dias,
            Tipo = Trim(req.Tipo),
            Orden = siguiente
        };
        db.HistoriaClinicaIncapacidades.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new IncapacidadItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.Motivo,
            entity.FechaDesde, entity.FechaHasta, entity.Dias, entity.Tipo, entity.Orden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarIncapacidadRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaIncapacidades.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        if (string.IsNullOrWhiteSpace(req.Motivo))
        {
            throw new InvalidOperationException("El motivo de la incapacidad es obligatorio.");
        }
        entity.Motivo = req.Motivo.Trim();
        entity.FechaDesde = req.FechaDesde;
        entity.FechaHasta = req.FechaHasta;
        entity.Dias = req.Dias;
        entity.Tipo = Trim(req.Tipo);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaIncapacidades.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        db.HistoriaClinicaIncapacidades.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaIncapacidades
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
