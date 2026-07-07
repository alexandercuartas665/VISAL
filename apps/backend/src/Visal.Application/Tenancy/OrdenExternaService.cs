using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class OrdenExternaService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    TimeProvider time,
    IHistoriaPrefillService prefill) : IOrdenExternaService
{
    public async Task<IReadOnlyList<OrdenExternaItemDto>> ListarPorHistoriaAsync(
        Guid historiaClinicaId, TipoCatalogoServicio tipo, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaOrdenesExternas.AsNoTracking()
            .Where(o => o.HistoriaClinicaId == historiaClinicaId && o.Tipo == tipo)
            .OrderBy(o => o.Orden)
            .Select(o => new OrdenExternaItemDto(o.Id, o.Orden, o.Codigo, o.Descripcion, o.Cantidad, o.Observaciones))
            .ToListAsync(ct);
    }

    public async Task<OrdenExternaItemDto> AgregarAsync(
        Guid historiaClinicaId, TipoCatalogoServicio tipo,
        AgregarOrdenExternaRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) { throw new InvalidOperationException("Sin tenant activo."); }
        await db.EnsureAbiertaAsync(historiaClinicaId, ct);
        var siguiente = await db.HistoriaClinicaOrdenesExternas
            .Where(o => o.HistoriaClinicaId == historiaClinicaId && o.Tipo == tipo)
            .Select(o => (int?)o.Orden).MaxAsync(ct) ?? 0;

        var entity = new HistoriaClinicaOrdenExterna
        {
            TenantId = tenantId,
            HistoriaClinicaId = historiaClinicaId,
            Tipo = tipo,
            Orden = siguiente + 1,
            Codigo = string.IsNullOrWhiteSpace(req.Codigo) ? null : req.Codigo.Trim(),
            Descripcion = req.Descripcion.Trim(),
            Cantidad = string.IsNullOrWhiteSpace(req.Cantidad) ? null : req.Cantidad.Trim(),
            Observaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim()
        };
        db.HistoriaClinicaOrdenesExternas.Add(entity);
        await db.SaveChangesAsync(ct);
        // Refresca las rutas prefill (rx_imagenologia / laboratorios /
        // insumos_externos) que apunten a esta HC. Sin esto el ValoresJson
        // del formulario no ve el item nuevo hasta que el usuario recargue.
        await prefill.ActualizarValoresAsync(historiaClinicaId, ct);
        audit.Write(actorUserId, "hc-orden-externa.add", nameof(HistoriaClinicaOrdenExterna), entity.Id,
            previousValue: null,
            newValue: new { historiaClinicaId, tipo = tipo.ToString(), entity.Descripcion, entity.Codigo },
            tenantId: tenantId);
        return new OrdenExternaItemDto(entity.Id, entity.Orden, entity.Codigo, entity.Descripcion, entity.Cantidad, entity.Observaciones);
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actorUserId, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaOrdenesExternas.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (e is null) { return false; }
        var hcId = e.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
        db.HistoriaClinicaOrdenesExternas.Remove(e);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        audit.Write(actorUserId, "hc-orden-externa.delete", nameof(HistoriaClinicaOrdenExterna), e.Id,
            previousValue: new { e.HistoriaClinicaId, tipo = e.Tipo.ToString(), e.Descripcion },
            newValue: null, tenantId: e.TenantId);
        return true;
    }
}
