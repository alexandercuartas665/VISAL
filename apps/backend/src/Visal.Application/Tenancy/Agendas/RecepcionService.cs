using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy.Agendas;

public sealed class RecepcionService(
    IApplicationDbContext db,
    IAsignacionAgendasService agendas) : IRecepcionService
{
    public Task<IReadOnlyList<CitaRecepcionDto>> ListarCitasDelDiaAsync(DateOnly fecha, Guid? sucursalId, CancellationToken ct = default)
        => ListarCitasRangoAsync(fecha, fecha, sucursalId, ct);

    public async Task<IReadOnlyList<CitaRecepcionDto>> ListarCitasRangoAsync(DateOnly desde, DateOnly hasta, Guid? sucursalId, CancellationToken ct = default)
    {
        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.FechaInicio != null && t.FechaInicio >= desde && t.FechaInicio <= hasta && t.HoraInicio != null)
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
        foreach (var t in turnos.OrderBy(t => t.FechaInicio).ThenBy(t => t.HoraInicio))
        {
            if (!asigById.TryGetValue(t.AsignacionId, out var a)) { continue; }
            if (sedeNombre is not null && !string.Equals(a.Sucursal, sedeNombre, StringComparison.OrdinalIgnoreCase)) { continue; }
            var pac = pacById.TryGetValue(a.PacienteId, out var p) ? p : null;
            result.Add(new CitaRecepcionDto(
                t.Id, t.ProfesionalId, t.FechaInicio!.Value, t.HoraInicio,
                pac?.NombreCompleto ?? "(sin paciente)", pac?.NumeroDocumento ?? "",
                profNombre.TryGetValue(t.ProfesionalId, out var pn) ? pn : "",
                a.NombreServicio, a.Estado.ToString(),
                t.LlegoEn != null, t.LlegoEn, t.LlegadaTarde));
        }
        return result;
    }

    public async Task<bool> MarcarLlegadaAsync(Guid asignacionTurnoId, bool llego, bool tarde, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct);
        if (turno is null) { return false; }
        turno.LlegoEn = llego ? DateTimeOffset.UtcNow : null;
        turno.LlegadaTarde = llego && tarde; // solo marca tarde si efectivamente llego
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ContactosCitaDto?> ObtenerContactosCitaAsync(Guid asignacionTurnoId, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.Id == asignacionTurnoId)
            .Select(t => new { t.AsignacionId })
            .FirstOrDefaultAsync(ct);
        if (turno is null) { return null; }

        var pacienteId = await db.Asignaciones.AsNoTracking()
            .Where(a => a.Id == turno.AsignacionId).Select(a => a.PacienteId).FirstOrDefaultAsync(ct);
        if (pacienteId == Guid.Empty) { return null; }

        var pac = await db.Pacientes.AsNoTracking().Where(p => p.Id == pacienteId)
            .Select(p => new { p.NombreCompleto, p.NumeroDocumento, p.Telefono, p.TelefonoEmergencia })
            .FirstOrDefaultAsync(ct);
        if (pac is null) { return null; }

        var contactos = (await db.PacienteContactosEmergencia.AsNoTracking()
            .Where(c => c.PacienteId == pacienteId)
            .Select(c => new { c.Nombre, c.Parentesco, c.CodigoPais, c.Telefono, c.Orden })
            .ToListAsync(ct))
            .OrderBy(x => x.Orden)
            .Take(5)
            .Select(x => new ContactoAfectadoDto(x.Nombre, x.Parentesco,
                string.IsNullOrWhiteSpace(x.Telefono) ? null : $"{x.CodigoPais} {x.Telefono}".Trim()))
            .ToList();

        return new ContactosCitaDto(pac.NombreCompleto, pac.NumeroDocumento,
            pac.Telefono, pac.TelefonoEmergencia, contactos);
    }

    public async Task<IReadOnlyList<TimeOnly>> SlotsParaReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct);
        if (turno is null) { return Array.Empty<TimeOnly>(); }
        return await agendas.SlotsDisponiblesAsync(turno.ProfesionalId, nuevaFecha, ct);
    }

    public Task ReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, TimeOnly nuevaHora, Guid actor, CancellationToken ct = default)
        => ReprogramarConDoctorAsync(asignacionTurnoId, null, nuevaFecha, nuevaHora, actor, ct);

    public async Task ReprogramarConDoctorAsync(Guid asignacionTurnoId, Guid? nuevoProfesionalId, DateOnly nuevaFecha, TimeOnly nuevaHora, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct)
            ?? throw new InvalidOperationException("La cita no existe.");
        // Doctor destino: el nuevo elegido, o el mismo si no se cambia.
        var profDestino = nuevoProfesionalId is Guid np && np != Guid.Empty ? np : turno.ProfesionalId;

        if (nuevaFecha < DateOnly.FromDateTime(DateTime.Today))
        { throw new InvalidOperationException("No se puede reprogramar a una fecha anterior a hoy."); }

        // Slot libre del DOCTOR DESTINO en la nueva fecha/hora.
        var ocupado = await db.AsignacionTurnos.AsNoTracking()
            .AnyAsync(t => t.Id != asignacionTurnoId && t.ProfesionalId == profDestino
                        && t.FechaInicio == nuevaFecha && t.HoraInicio == nuevaHora, ct);
        if (ocupado) { throw new InvalidOperationException("Ese horario ya esta ocupado para el doctor destino."); }

        // El paciente no puede tener otra cita a esa fecha/hora (con cualquier doctor).
        var pacienteId = await db.Asignaciones.AsNoTracking()
            .Where(a => a.Id == turno.AsignacionId).Select(a => a.PacienteId).FirstAsync(ct);
        var choquePac = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.Id != asignacionTurnoId && t.FechaInicio == nuevaFecha && t.HoraInicio == nuevaHora)
            .Join(db.Asignaciones.AsNoTracking(), t => t.AsignacionId, a => a.Id, (t, a) => a.PacienteId)
            .AnyAsync(pid => pid == pacienteId, ct);
        if (choquePac) { throw new InvalidOperationException("El paciente ya tiene una cita a esa fecha y hora."); }

        turno.ProfesionalId = profDestino;
        turno.FechaInicio = nuevaFecha;
        turno.HoraInicio = nuevaHora;
        turno.MesAsignar = (short)nuevaFecha.Month;
        turno.LlegoEn = null; // reprogramada -> no ha llegado
        turno.LlegadaTarde = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AfectadoRecepcionDto>> ListarAfectadosAsync(Guid profesionalId, DateOnly fecha, TimeOnly? horaDesde, TimeOnly? horaHasta, CancellationToken ct = default)
    {
        // Citas de ese doctor ese dia, filtradas por la franja de la novedad (si se dio).
        var turnos = (await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.ProfesionalId == profesionalId && t.FechaInicio == fecha && t.HoraInicio != null)
            .ToListAsync(ct))
            .Where(t => (horaDesde is null || t.HoraInicio >= horaDesde) && (horaHasta is null || t.HoraInicio <= horaHasta))
            .ToList();
        if (turnos.Count == 0) { return Array.Empty<AfectadoRecepcionDto>(); }

        var asigIds = turnos.Select(t => t.AsignacionId).Distinct().ToList();
        var asigs = await db.Asignaciones.AsNoTracking().Where(a => asigIds.Contains(a.Id))
            .Select(a => new { a.Id, a.PacienteId, a.NombreServicio }).ToListAsync(ct);
        var asigById = asigs.ToDictionary(a => a.Id);

        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking().Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.NumeroDocumento, p.Telefono, p.TelefonoEmergencia })
            .ToListAsync(ct);
        var pacById = pacs.ToDictionary(p => p.Id);

        var contactos = (await db.PacienteContactosEmergencia.AsNoTracking()
            .Where(c => pacIds.Contains(c.PacienteId))
            .Select(c => new { c.PacienteId, c.Nombre, c.Parentesco, c.CodigoPais, c.Telefono, c.Orden })
            .ToListAsync(ct))
            .GroupBy(c => c.PacienteId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Orden)
                .Take(5) // tope para que la tarjeta no crezca demasiado
                .Select(x => new ContactoAfectadoDto(x.Nombre, x.Parentesco,
                    string.IsNullOrWhiteSpace(x.Telefono) ? null : $"{x.CodigoPais} {x.Telefono}".Trim()))
                .ToList());

        var profNombre = await db.Profesionales.AsNoTracking().Where(p => p.Id == profesionalId)
            .Select(p => p.NombreCompleto).FirstOrDefaultAsync(ct) ?? "";

        var result = new List<AfectadoRecepcionDto>();
        foreach (var t in turnos.OrderBy(t => t.HoraInicio))
        {
            if (!asigById.TryGetValue(t.AsignacionId, out var a)) { continue; }
            var pac = pacById.TryGetValue(a.PacienteId, out var p) ? p : null;
            var conts = contactos.TryGetValue(a.PacienteId, out var cs) ? (IReadOnlyList<ContactoAfectadoDto>)cs : Array.Empty<ContactoAfectadoDto>();
            result.Add(new AfectadoRecepcionDto(
                t.Id, t.ProfesionalId, a.PacienteId,
                pac?.NombreCompleto ?? "(sin paciente)", pac?.NumeroDocumento ?? "",
                t.FechaInicio!.Value, t.HoraInicio, profNombre, a.NombreServicio,
                t.LlegoEn != null, pac?.Telefono, pac?.TelefonoEmergencia, conts));
        }
        return result;
    }

    public Task<bool> CancelarCitaAsync(Guid asignacionTurnoId, Guid actor, CancellationToken ct = default)
        => agendas.CancelarCitaAsync(asignacionTurnoId, actor, ct);

    public async Task<IReadOnlyList<NovedadResumenDto>> ListarNovedadesConAfectadosAsync(CancellationToken ct = default)
    {
        var desde = DateOnly.FromDateTime(DateTime.Today).AddDays(-30);
        var novs = await db.NovedadesProfesional.AsNoTracking()
            .Where(n => n.FechaHasta >= desde)
            .OrderByDescending(n => n.FechaDesde)
            .Take(50)
            .ToListAsync(ct);
        if (novs.Count == 0) { return Array.Empty<NovedadResumenDto>(); }

        var profIds = novs.Select(n => n.ProfesionalId).Distinct().ToList();
        var profNombre = await db.Profesionales.AsNoTracking().Where(p => profIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        var result = new List<NovedadResumenDto>();
        foreach (var n in novs)
        {
            var afect = await db.AsignacionTurnos.AsNoTracking()
                .CountAsync(t => t.ProfesionalId == n.ProfesionalId && t.HoraInicio != null
                    && t.FechaInicio != null && t.FechaInicio >= n.FechaDesde && t.FechaInicio <= n.FechaHasta
                    && (n.HoraDesde == null || t.HoraInicio >= n.HoraDesde)
                    && (n.HoraHasta == null || t.HoraInicio <= n.HoraHasta), ct);
            result.Add(new NovedadResumenDto(
                n.Id, n.ProfesionalId, profNombre.TryGetValue(n.ProfesionalId, out var pn) ? pn : "",
                n.FechaDesde, n.FechaHasta, n.HoraDesde, n.HoraHasta, n.Tipo.ToString(), afect));
        }
        return result;
    }

    public Task<IReadOnlyList<TimeOnly>> SlotsDoctorAsync(Guid profesionalId, DateOnly fecha, CancellationToken ct = default)
        => agendas.SlotsDisponiblesAsync(profesionalId, fecha, ct);

    public async Task<IReadOnlyList<DoctorSimpleDto>> ListarDoctoresConAgendaAsync(CancellationToken ct = default)
    {
        var profIds = await db.AgendaProfesionalTurnos.AsNoTracking().Select(a => a.ProfesionalId).Distinct().ToListAsync(ct);
        if (profIds.Count == 0) { return Array.Empty<DoctorSimpleDto>(); }
        return await db.Profesionales.AsNoTracking()
            .Where(p => profIds.Contains(p.Id))
            .OrderBy(p => p.NombreCompleto)
            .Select(p => new DoctorSimpleDto(p.Id, p.NombreCompleto))
            .ToListAsync(ct);
    }
}
