using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy.Agendas;

public sealed class RecepcionService(
    IApplicationDbContext db,
    IAsignacionAgendasService agendas) : IRecepcionService
{
    public async Task<IReadOnlyList<CitaRecepcionDto>> ListarCitasDelDiaAsync(DateOnly fecha, Guid? sucursalId, CancellationToken ct = default)
    {
        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.FechaInicio == fecha && t.HoraInicio != null)
            .ToListAsync(ct);
        if (turnos.Count == 0) { return Array.Empty<CitaRecepcionDto>(); }

        var asigIds = turnos.Select(t => t.AsignacionId).Distinct().ToList();
        var asigs = await db.Asignaciones.AsNoTracking()
            .Where(a => asigIds.Contains(a.Id))
            .Select(a => new { a.Id, a.PacienteId, a.NombreServicio, a.Estado, a.Sucursal })
            .ToListAsync(ct);
        var asigById = asigs.ToDictionary(a => a.Id);

        // Filtro por sede (Asignacion.Sucursal es el NOMBRE de la sede).
        string? sedeNombre = null;
        if (sucursalId is Guid sid && sid != Guid.Empty)
        {
            sedeNombre = await db.Sucursales.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Nombre).FirstOrDefaultAsync(ct);
        }

        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking().Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.NumeroDocumento }).ToListAsync(ct);
        var pacById = pacs.ToDictionary(p => p.Id);

        var profIds = turnos.Select(t => t.ProfesionalId).Distinct().ToList();
        var profNombre = await db.Profesionales.AsNoTracking().Where(p => profIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        var result = new List<CitaRecepcionDto>();
        foreach (var t in turnos.OrderBy(t => t.HoraInicio))
        {
            if (!asigById.TryGetValue(t.AsignacionId, out var a)) { continue; }
            if (sedeNombre is not null && !string.Equals(a.Sucursal, sedeNombre, StringComparison.OrdinalIgnoreCase)) { continue; }
            var pac = pacById.TryGetValue(a.PacienteId, out var p) ? p : null;
            result.Add(new CitaRecepcionDto(
                t.Id, t.ProfesionalId, t.FechaInicio!.Value, t.HoraInicio,
                pac?.NombreCompleto ?? "(sin paciente)", pac?.NumeroDocumento ?? "",
                profNombre.TryGetValue(t.ProfesionalId, out var pn) ? pn : "",
                a.NombreServicio, a.Estado.ToString(),
                t.LlegoEn != null, t.LlegoEn));
        }
        return result;
    }

    public async Task<bool> MarcarLlegadaAsync(Guid asignacionTurnoId, bool llego, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct);
        if (turno is null) { return false; }
        turno.LlegoEn = llego ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TimeOnly>> SlotsParaReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct);
        if (turno is null) { return Array.Empty<TimeOnly>(); }
        return await agendas.SlotsDisponiblesAsync(turno.ProfesionalId, nuevaFecha, ct);
    }

    public async Task ReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, TimeOnly nuevaHora, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct)
            ?? throw new InvalidOperationException("La cita no existe.");

        if (nuevaFecha < DateOnly.FromDateTime(DateTime.Today))
        { throw new InvalidOperationException("No se puede reprogramar a una fecha anterior a hoy."); }

        // Slot libre del doctor en la nueva fecha (excluye el propio si es la misma fecha/hora).
        var ocupado = await db.AsignacionTurnos.AsNoTracking()
            .AnyAsync(t => t.Id != asignacionTurnoId && t.ProfesionalId == turno.ProfesionalId
                        && t.FechaInicio == nuevaFecha && t.HoraInicio == nuevaHora, ct);
        if (ocupado) { throw new InvalidOperationException("Ese horario ya esta ocupado para el doctor."); }

        // El paciente no puede tener otra cita a esa fecha/hora (con cualquier doctor).
        var pacienteId = await db.Asignaciones.AsNoTracking()
            .Where(a => a.Id == turno.AsignacionId).Select(a => a.PacienteId).FirstAsync(ct);
        var choquePac = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.Id != asignacionTurnoId && t.FechaInicio == nuevaFecha && t.HoraInicio == nuevaHora)
            .Join(db.Asignaciones.AsNoTracking(), t => t.AsignacionId, a => a.Id, (t, a) => a.PacienteId)
            .AnyAsync(pid => pid == pacienteId, ct);
        if (choquePac) { throw new InvalidOperationException("El paciente ya tiene una cita a esa fecha y hora."); }

        turno.FechaInicio = nuevaFecha;
        turno.HoraInicio = nuevaHora;
        turno.MesAsignar = (short)nuevaFecha.Month;
        turno.LlegoEn = null; // reprogramada -> no ha llegado
        await db.SaveChangesAsync(ct);
    }
}
