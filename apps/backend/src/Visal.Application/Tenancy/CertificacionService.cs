using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class CertificacionService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : ICertificacionService
{
    public async Task<IReadOnlyList<CertificacionItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaCertificaciones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new CertificacionItemDto(
                x.Id, x.HistoriaClinicaId, x.Titulo, x.Contenido, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<CertificacionItemDto> AgregarAsync(
        Guid historiaId, AgregarCertificacionRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Titulo))
        {
            throw new InvalidOperationException("El titulo de la certificacion es obligatorio.");
        }
        await db.EnsureAbiertaAsync(historiaId, ct);

        var siguiente = 1 + await db.HistoriaClinicaCertificaciones
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaCertificacion
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            Titulo = req.Titulo.Trim(),
            Contenido = req.Contenido?.Trim() ?? "",
            Orden = siguiente
        };
        db.HistoriaClinicaCertificaciones.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new CertificacionItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.Titulo, entity.Contenido, entity.Orden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarCertificacionRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaCertificaciones.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        await db.EnsureAbiertaAsync(entity.HistoriaClinicaId, ct);
        if (string.IsNullOrWhiteSpace(req.Titulo))
        {
            throw new InvalidOperationException("El titulo de la certificacion es obligatorio.");
        }
        entity.Titulo = req.Titulo.Trim();
        entity.Contenido = req.Contenido?.Trim() ?? "";
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaCertificaciones.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
        db.HistoriaClinicaCertificaciones.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaCertificaciones
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }
}
