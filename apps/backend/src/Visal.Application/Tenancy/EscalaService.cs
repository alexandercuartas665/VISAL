using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class EscalaService(IApplicationDbContext db, ITenantContext tenant) : IEscalaService
{
    public async Task<IReadOnlyList<EscalaFormatoDto>> ListarFormatosAsync(CancellationToken ct = default)
    {
        // Materializamos primero para poder filtrar por substring case-insensitive
        // de forma estable y luego mapear al DTO (evita el problema clasico de
        // OrderBy sobre props de un record proyectado por joins).
        var rows = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Activo && f.Tipo != null)
            .Select(f => new { f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo })
            .ToListAsync(ct);
        return rows
            .Where(r => r.Tipo!.Contains("escala", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(r => new EscalaFormatoDto(r.Id, r.Codigo, r.Nombre, r.Version, r.Tipo, r.Activo))
            .ToList();
    }

    public async Task<IReadOnlyList<EscalaItemDto>> ListarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(e => e.HistoriaClinicaId == historiaId)
            .Join(db.FormDefinitions.AsNoTracking(), e => e.FormDefinitionId, f => f.Id, (e, f) => new EscalaItemDto(
                e.Id, e.HistoriaClinicaId, e.FormDefinitionId,
                f.Codigo, f.Nombre,
                e.Estado.ToString(), e.FechaApertura, e.FechaCierre,
                e.EspecialistaNombre))
            .OrderByDescending(x => x.FechaApertura)
            .ToListAsync(ct);
    }

    public async Task<EscalaDetailDto> IniciarAsync(IniciarEscalaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var hc = await db.HistoriasClinicas.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == req.HistoriaClinicaId, ct)
            ?? throw new InvalidOperationException("Historia clinica no encontrada.");
        if (hc.Estado == HistoriaClinicaEstado.Inactiva)
        { throw new InvalidOperationException("La historia clinica esta inactiva; no se pueden iniciar escalas."); }

        var formato = await db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == req.FormDefinitionId, ct)
            ?? throw new InvalidOperationException("Formato de escala no encontrado.");

        var entity = new HistoriaClinicaEscala
        {
            TenantId = tid,
            HistoriaClinicaId = req.HistoriaClinicaId,
            FormDefinitionId = req.FormDefinitionId,
            ValoresJson = string.IsNullOrWhiteSpace(req.ValoresJson) ? "{}" : req.ValoresJson,
            Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = DateTimeOffset.UtcNow,
            EspecialistaNombre = req.EspecialistaNombre
        };
        db.HistoriaClinicaEscalas.Add(entity);
        await db.SaveChangesAsync(ct);

        return new EscalaDetailDto(
            entity.Id, entity.HistoriaClinicaId, entity.FormDefinitionId,
            formato.Codigo, formato.Nombre, formato.Version,
            formato.SchemaJson, formato.PrefillRoutesJson, entity.ValoresJson,
            entity.Estado.ToString(), entity.FechaApertura, entity.FechaCierre,
            entity.EspecialistaNombre);
    }

    public async Task<EscalaDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(e => e.Id == id)
            .Join(db.FormDefinitions.AsNoTracking(), e => e.FormDefinitionId, f => f.Id, (e, f) => new EscalaDetailDto(
                e.Id, e.HistoriaClinicaId, e.FormDefinitionId,
                f.Codigo, f.Nombre, f.Version,
                f.SchemaJson, f.PrefillRoutesJson, e.ValoresJson,
                e.Estado.ToString(), e.FechaApertura, e.FechaCierre,
                e.EspecialistaNombre))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaEscalas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? "{}" : valoresJson;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaEscalas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? e.ValoresJson : valoresJson;
        e.Estado = HistoriaClinicaEstado.Cerrada;
        e.FechaCierre = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaEscalas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.HistoriaClinicaEscalas.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaEscalas.CountAsync(e => e.HistoriaClinicaId == historiaId, ct);
    }
}
