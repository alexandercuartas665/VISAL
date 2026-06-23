using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class OrdenServicioService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : IOrdenServicioService
{
    public async Task<IReadOnlyList<ServicioSugerenciaDto>> BuscarSugerenciasAsync(
        string termino, int take = 12, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(termino) || termino.Trim().Length < 2)
        {
            return Array.Empty<ServicioSugerenciaDto>();
        }

        var t = termino.Trim().ToLowerInvariant();
        if (take <= 0) { take = 12; }
        if (take > 50) { take = 50; }

        // Buscamos contra Descripcion + CodigoServicio + CodigoInterno del catalogo
        // de servicios de contratos. La query filtra por tenant via el global filter
        // de EF (ServicioContrato hereda de TenantEntity).
        // Hacemos un join opcional con Contrato/Aseguradora para mostrar de donde
        // viene el servicio en el dropdown (ej: "EPS SURA - CTR-001").
        var q = from s in db.ServiciosContrato.AsNoTracking()
                where (s.Descripcion != null && s.Descripcion.ToLower().Contains(t)) ||
                      (s.CodigoServicio != null && s.CodigoServicio.ToLower().Contains(t)) ||
                      (s.CodigoInterno != null && s.CodigoInterno.ToLower().Contains(t))
                join c in db.ContratosAseguradora.AsNoTracking()
                     on s.ContratoId equals c.Id into cj
                from c in cj.DefaultIfEmpty()
                join a in db.Aseguradoras.AsNoTracking()
                     on c.AseguradoraId equals a.Id into aj
                from a in aj.DefaultIfEmpty()
                orderby s.Descripcion
                select new ServicioSugerenciaDto(
                    s.Id, s.CodigoServicio, s.Descripcion ?? "(sin descripcion)",
                    s.Modulo, s.Especialidad,
                    c != null ? c.CodigoContrato : null,
                    a != null ? a.Nombre : null);

        return await q.Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrdenServicioItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaOrdenesServicio.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new OrdenServicioItemDto(
                x.Id, x.HistoriaClinicaId, x.ServicioContratoId,
                x.CodigoServicio, x.Descripcion, x.Cantidad, x.Observaciones, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<OrdenServicioItemDto> AgregarAsync(
        Guid historiaId, AgregarServicioRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Descripcion))
        {
            throw new InvalidOperationException("La descripcion del servicio es obligatoria.");
        }

        // Calcular siguiente orden en la historia.
        var siguiente = 1 + await db.HistoriaClinicaOrdenesServicio
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaOrdenServicio
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            ServicioContratoId = req.ServicioContratoId,
            CodigoServicio = Trim(req.CodigoServicio),
            Descripcion = req.Descripcion.Trim(),
            Cantidad = Trim(req.Cantidad),
            Observaciones = Trim(req.Observaciones),
            Orden = siguiente
        };
        db.HistoriaClinicaOrdenesServicio.Add(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new OrdenServicioItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.ServicioContratoId,
            entity.CodigoServicio, entity.Descripcion, entity.Cantidad,
            entity.Observaciones, entity.Orden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarServicioRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaOrdenesServicio.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        entity.Cantidad = Trim(req.Cantidad);
        entity.Observaciones = Trim(req.Observaciones);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaOrdenesServicio.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        db.HistoriaClinicaOrdenesServicio.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaOrdenesServicio
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
