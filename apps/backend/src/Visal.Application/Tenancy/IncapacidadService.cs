using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class IncapacidadService(
    IApplicationDbContextFactory dbFactory,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : IIncapacidadService
{
    public async Task<IReadOnlyList<IncapacidadItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        return await db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new IncapacidadItemDto(
                x.Id, x.HistoriaClinicaId, x.Motivo,
                x.FechaDesde, x.FechaHasta, x.Dias, x.Tipo, x.Orden,
                x.InicialOProrroga, x.Retroactiva))
            .ToListAsync(ct);
    }

    public async Task<IncapacidadItemDto> AgregarAsync(
        Guid historiaId, AgregarIncapacidadRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Motivo))
        {
            throw new InvalidOperationException("El motivo de la incapacidad es obligatorio.");
        }
        await db.EnsureAbiertaAsync(historiaId, ct);

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
            InicialOProrroga = Trim(req.InicialOProrroga),
            Retroactiva = req.Retroactiva,
            Orden = siguiente
        };
        db.HistoriaClinicaIncapacidades.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new IncapacidadItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.Motivo,
            entity.FechaDesde, entity.FechaHasta, entity.Dias, entity.Tipo, entity.Orden,
            entity.InicialOProrroga, entity.Retroactiva);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarIncapacidadRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var entity = await db.HistoriaClinicaIncapacidades.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        await db.EnsureAbiertaAsync(entity.HistoriaClinicaId, ct);
        if (string.IsNullOrWhiteSpace(req.Motivo))
        {
            throw new InvalidOperationException("El motivo de la incapacidad es obligatorio.");
        }
        entity.Motivo = req.Motivo.Trim();
        entity.FechaDesde = req.FechaDesde;
        entity.FechaHasta = req.FechaHasta;
        entity.Dias = req.Dias;
        entity.Tipo = Trim(req.Tipo);
        entity.InicialOProrroga = Trim(req.InicialOProrroga);
        entity.Retroactiva = req.Retroactiva;
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var entity = await db.HistoriaClinicaIncapacidades.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
        db.HistoriaClinicaIncapacidades.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        return await db.HistoriaClinicaIncapacidades
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
