using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Agendas;

public sealed class CalendarioService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IFestivosColombiaService festivos) : ICalendarioService
{
    public async Task<MesCalendarioDto> ObtenerMesAsync(Guid sucursalId, int anio, int mes, CancellationToken ct = default)
    {
        var primero = new DateOnly(anio, mes, 1);
        var ultimo = primero.AddMonths(1).AddDays(-1);

        var mapaFestivos = festivos.MapaDeAnio(anio);
        var inactivos = await db.DiasInactivosSede.AsNoTracking()
            .Where(d => d.SucursalId == sucursalId && d.Fecha >= primero && d.Fecha <= ultimo)
            .ToListAsync(ct);
        var inactivoPorFecha = inactivos.ToDictionary(d => d.Fecha);

        var dias = new List<DiaCalendarioDto>();
        for (var f = primero; f <= ultimo; f = f.AddDays(1))
        {
            var esFestivo = mapaFestivos.TryGetValue(f, out var nombreFest);
            var esInactivo = inactivoPorFecha.TryGetValue(f, out var inact);
            dias.Add(new DiaCalendarioDto(
                f,
                esFestivo, esFestivo ? nombreFest : null,
                esInactivo, esInactivo ? inact!.Id : null, esInactivo ? inact!.Motivo : null));
        }
        return new MesCalendarioDto(anio, mes, dias);
    }

    public async Task<Guid> MarcarInactivoAsync(Guid sucursalId, DateOnly fecha, string? motivo, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (sucursalId == Guid.Empty) { throw new InvalidOperationException("Selecciona una sede."); }

        // Idempotente: si el dia ya esta inactivo para la sede, actualiza el motivo.
        var existente = await db.DiasInactivosSede
            .FirstOrDefaultAsync(d => d.SucursalId == sucursalId && d.Fecha == fecha, ct);
        if (existente is not null)
        {
            existente.Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            await db.SaveChangesAsync(ct);
            return existente.Id;
        }

        var entity = new DiaInactivoSede
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            SucursalId = sucursalId,
            Fecha = fecha,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()
        };
        db.DiasInactivosSede.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> QuitarInactivoAsync(Guid diaInactivoId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.DiasInactivosSede.FirstOrDefaultAsync(d => d.Id == diaInactivoId, ct);
        if (entity is null) { return false; }
        db.DiasInactivosSede.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public IReadOnlyList<FestivoColombiaDto> FestivosDeAnio(int anio) => festivos.DeAnio(anio);
}
