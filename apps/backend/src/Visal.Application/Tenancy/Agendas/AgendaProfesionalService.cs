using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Agendas;

public sealed class AgendaProfesionalService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IProfesionalConfigService profesionales) : IAgendaProfesionalService
{
    public async Task<IReadOnlyList<ProfesionalAgendaResumenDto>> ListarResumenAsync(string? filtro, CancellationToken ct = default)
    {
        var profs = await profesionales.ListProfesionalesAsync(filtro, ct);
        // Todos los turnos del tenant en una sola consulta, agrupados por profesional.
        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking().ToListAsync(ct);
        var porProf = turnos.GroupBy(t => t.ProfesionalId)
            .ToDictionary(g => g.Key, g => (Turnos: g.Count(),
                Cupos: g.Sum(t => PlantillaAgendaCalculos.Cupos(t.HoraInicio, t.HoraFin, t.IntervaloMinutos))));

        return profs.Select(p =>
        {
            var r = porProf.TryGetValue(p.Id, out var v) ? v : (Turnos: 0, Cupos: 0);
            return new ProfesionalAgendaResumenDto(p.Id, p.NombreCompleto, p.TipoProfesional, r.Turnos, r.Cupos);
        }).ToList();
    }

    public async Task<AgendaProfesionalDto> ObtenerAsync(Guid profesionalId, CancellationToken ct = default)
    {
        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Where(t => t.ProfesionalId == profesionalId)
            .OrderBy(t => t.DiaSemana).ThenBy(t => t.HoraInicio)
            .ToListAsync(ct);
        var origen = turnos.Select(t => t.PlantillaOrigenId).FirstOrDefault(id => id != null);
        return new AgendaProfesionalDto(profesionalId, origen, turnos.Select(ToDto).ToList());
    }

    public async Task<int> ImportarPlantillaAsync(Guid profesionalId, Guid plantillaId, bool reemplazar, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var plantilla = await db.PlantillasAgenda.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plantillaId, ct)
            ?? throw new InvalidOperationException("Plantilla no encontrada.");
        var turnosPlantilla = await db.PlantillaAgendaTurnos.AsNoTracking()
            .Where(t => t.PlantillaAgendaId == plantillaId)
            .ToListAsync(ct);
        if (turnosPlantilla.Count == 0) { throw new InvalidOperationException($"La plantilla '{plantilla.Nombre}' no tiene turnos que importar."); }

        if (reemplazar)
        {
            var existentes = await db.AgendaProfesionalTurnos.Where(t => t.ProfesionalId == profesionalId).ToListAsync(ct);
            db.AgendaProfesionalTurnos.RemoveRange(existentes);
        }

        foreach (var t in turnosPlantilla)
        {
            db.AgendaProfesionalTurnos.Add(new AgendaProfesionalTurno
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                ProfesionalId = profesionalId,
                PlantillaOrigenId = plantillaId,
                DiaSemana = t.DiaSemana,
                HoraInicio = t.HoraInicio,
                HoraFin = t.HoraFin,
                IntervaloMinutos = t.IntervaloMinutos
            });
        }
        await db.SaveChangesAsync(ct);
        return turnosPlantilla.Count;
    }

    public async Task<AgendaProfesionalTurnoDto> GuardarTurnoAsync(Guid profesionalId, GuardarAgendaProfesionalTurnoCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (cmd.HoraFin <= cmd.HoraInicio) { throw new InvalidOperationException("La hora 'Hasta' debe ser mayor que 'Desde'."); }
        if (cmd.IntervaloMinutos <= 0) { throw new InvalidOperationException("El intervalo debe ser mayor a 0."); }
        if (cmd.IntervaloMinutos > 24 * 60) { throw new InvalidOperationException("El intervalo no puede exceder 1440 minutos."); }

        AgendaProfesionalTurno turno;
        if (cmd.Id is Guid id)
        {
            turno = await db.AgendaProfesionalTurnos.FirstOrDefaultAsync(t => t.Id == id && t.ProfesionalId == profesionalId, ct)
                ?? throw new InvalidOperationException("Turno no encontrado.");
            turno.DiaSemana = cmd.DiaSemana;
            turno.HoraInicio = cmd.HoraInicio;
            turno.HoraFin = cmd.HoraFin;
            turno.IntervaloMinutos = cmd.IntervaloMinutos;
        }
        else
        {
            turno = new AgendaProfesionalTurno
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                ProfesionalId = profesionalId,
                DiaSemana = cmd.DiaSemana,
                HoraInicio = cmd.HoraInicio,
                HoraFin = cmd.HoraFin,
                IntervaloMinutos = cmd.IntervaloMinutos
            };
            db.AgendaProfesionalTurnos.Add(turno);
        }
        await db.SaveChangesAsync(ct);
        return ToDto(turno);
    }

    public async Task<bool> EliminarTurnoAsync(Guid turnoId, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.AgendaProfesionalTurnos.FirstOrDefaultAsync(t => t.Id == turnoId, ct);
        if (turno is null) { return false; }
        db.AgendaProfesionalTurnos.Remove(turno);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> LimpiarAsync(Guid profesionalId, Guid actor, CancellationToken ct = default)
    {
        var existentes = await db.AgendaProfesionalTurnos.Where(t => t.ProfesionalId == profesionalId).ToListAsync(ct);
        if (existentes.Count == 0) { return 0; }
        db.AgendaProfesionalTurnos.RemoveRange(existentes);
        await db.SaveChangesAsync(ct);
        return existentes.Count;
    }

    private static AgendaProfesionalTurnoDto ToDto(AgendaProfesionalTurno t) =>
        new(t.Id, t.DiaSemana, t.HoraInicio, t.HoraFin, t.IntervaloMinutos);
}
