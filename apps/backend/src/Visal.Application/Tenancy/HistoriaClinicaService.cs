using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class HistoriaClinicaService(IApplicationDbContext db, ITenantContext tenant) : IHistoriaClinicaService
{
    public async Task<IReadOnlyList<HistoriaClinicaResumenDto>> ListarPorPacienteAsync(
        Guid pacienteId,
        DateOnly? desde = null, DateOnly? hasta = null,
        Guid? formDefinitionId = null,
        CancellationToken ct = default)
    {
        var q = db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId);

        if (desde is DateOnly d)
        {
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(h => h.FechaApertura >= dStart);
        }
        if (hasta is DateOnly h2)
        {
            var dEnd = new DateTimeOffset(h2.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(h => h.FechaApertura <= dEnd);
        }
        if (formDefinitionId is Guid fid)
        {
            q = q.Where(h => h.FormDefinitionId == fid);
        }

        var rows = await q
            .Join(db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id, (h, f) => new { h, f })
            .OrderByDescending(x => x.h.FechaApertura)
            .Take(200)
            .Select(x => new HistoriaClinicaResumenDto(
                x.h.Id, x.f.Id, x.f.Codigo, x.f.Nombre,
                x.h.Estado.ToString(), x.h.FechaApertura, x.h.FechaCierre,
                x.h.EspecialistaNombre, x.h.MotivoInactivacion))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<HistoriaClinicaDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == id)
            .Join(db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id, (h, f) => new { h, f })
            .Select(x => new HistoriaClinicaDetailDto(
                x.h.Id, x.h.PacienteId, x.f.Id, x.f.Codigo, x.f.Nombre, x.f.Version,
                x.f.SchemaJson, x.f.PrefillRoutesJson, x.h.ValoresJson,
                x.h.Estado.ToString(), x.h.FechaApertura, x.h.FechaCierre,
                x.h.EspecialistaNombre, x.h.MotivoInactivacion))
            .FirstOrDefaultAsync(ct);
        return row;
    }

    public async Task<HistoriaClinicaDetailDto> CrearAsync(CrearHistoriaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var formato = await db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == req.FormDefinitionId, ct)
            ?? throw new InvalidOperationException("Formato de historia no encontrado.");

        var paciente = await db.Pacientes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.PacienteId, ct)
            ?? throw new InvalidOperationException("Paciente no encontrado.");

        var entity = new HistoriaClinica
        {
            TenantId = tid,
            PacienteId = req.PacienteId,
            FormDefinitionId = req.FormDefinitionId,
            ValoresJson = string.IsNullOrWhiteSpace(req.ValoresJson) ? "{}" : req.ValoresJson,
            Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = DateTimeOffset.UtcNow,
            EspecialistaNombre = req.EspecialistaNombre
        };
        db.HistoriasClinicas.Add(entity);
        await db.SaveChangesAsync(ct);

        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado != HistoriaClinicaEstado.Abierta)
        {
            throw new InvalidOperationException("Solo se pueden actualizar valores de historias abiertas.");
        }
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? "{}" : valoresJson;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado == HistoriaClinicaEstado.Inactiva)
        {
            throw new InvalidOperationException("No se puede cerrar una historia inactiva.");
        }
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? e.ValoresJson : valoresJson;
        e.Estado = HistoriaClinicaEstado.Cerrada;
        e.FechaCierre = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DescartarAsync(Guid id, string? motivo, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        e.Estado = HistoriaClinicaEstado.Inactiva;
        e.MotivoInactivacion = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        e.FechaCierre = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid?> BuscarUltimaAbiertaPorPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        // Si hay varias abiertas (raro pero posible si el profesional cerro sesion
        // sin cerrar la HC), tomamos la mas reciente.
        return await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId && h.Estado == HistoriaClinicaEstado.Abierta)
            .OrderByDescending(h => h.FechaApertura)
            .Select(h => (Guid?)h.Id)
            .FirstOrDefaultAsync(ct);
    }
}
