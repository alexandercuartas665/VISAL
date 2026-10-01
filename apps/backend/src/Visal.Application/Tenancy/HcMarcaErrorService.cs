using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class HcMarcaErrorService(IApplicationDbContext db, ITenantContext tenant) : IHcMarcaErrorService
{
    public async Task<Guid> MarcarAsync(
        Guid asignacionId,
        string codigoAsignacion,
        string? pacienteNombre,
        string? pacienteDoc,
        string observacion,
        string? marcadoPorNombre,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        observacion = (observacion ?? "").Trim();

        // Si ya hay una marca Pendiente para esta asignacion, actualizamos en vez de
        // duplicar: el usuario puede re-marcar para corregir/ampliar la observacion.
        var existente = await db.HcMarcasError
            .FirstOrDefaultAsync(m => m.AsignacionId == asignacionId
                                   && m.Estado == HcMarcaErrorEstado.Pendiente, ct);
        if (existente is not null)
        {
            existente.Observacion = observacion;
            existente.CodigoAsignacion = codigoAsignacion;
            existente.PacienteNombre = pacienteNombre;
            existente.PacienteDoc = pacienteDoc;
            existente.MarcadoPorNombre = marcadoPorNombre;
            await db.SaveChangesAsync(ct);
            return existente.Id;
        }

        var marca = new HcMarcaError
        {
            TenantId = tid,
            AsignacionId = asignacionId,
            CodigoAsignacion = codigoAsignacion,
            PacienteNombre = pacienteNombre,
            PacienteDoc = pacienteDoc,
            Observacion = observacion,
            Estado = HcMarcaErrorEstado.Pendiente,
            MarcadoPorNombre = marcadoPorNombre
        };
        db.HcMarcasError.Add(marca);
        await db.SaveChangesAsync(ct);
        return marca.Id;
    }

    public async Task<IReadOnlyList<HcMarcaErrorDto>> ListarAsync(
        HcMarcaErrorEstado? estado,
        CancellationToken ct = default)
    {
        var q = db.HcMarcasError.AsNoTracking();
        if (estado is HcMarcaErrorEstado e) { q = q.Where(m => m.Estado == e); }
        return await q
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new HcMarcaErrorDto(
                m.Id, m.AsignacionId, m.CodigoAsignacion,
                m.PacienteNombre, m.PacienteDoc,
                m.Observacion, m.Estado,
                m.MarcadoPorNombre, m.CreatedAt,
                m.ReparadoPorNombre, m.ReparadoEn, m.ObservacionReparacion))
            .ToListAsync(ct);
    }

    public async Task<int> ContarPendientesAsync(CancellationToken ct = default)
        => await db.HcMarcasError.AsNoTracking()
            .CountAsync(m => m.Estado == HcMarcaErrorEstado.Pendiente, ct);

    public async Task<bool> RepararAsync(
        Guid id,
        string? observacionReparacion,
        string? reparadoPorNombre,
        CancellationToken ct = default)
    {
        var marca = await db.HcMarcasError
            .FirstOrDefaultAsync(m => m.Id == id && m.Estado == HcMarcaErrorEstado.Pendiente, ct);
        if (marca is null) { return false; }
        marca.Estado = HcMarcaErrorEstado.Reparado;
        marca.ObservacionReparacion = string.IsNullOrWhiteSpace(observacionReparacion) ? null : observacionReparacion.Trim();
        marca.ReparadoPorNombre = reparadoPorNombre;
        marca.ReparadoEn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
