using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Agendas;

public sealed class PlantillaAgendaService(IApplicationDbContext db, ITenantContext tenant) : IPlantillaAgendaService
{
    public async Task<IReadOnlyList<PlantillaAgendaDto>> ListarAsync(bool incluirInactivas = false, CancellationToken ct = default)
    {
        var q = db.PlantillasAgenda.AsNoTracking();
        if (!incluirInactivas) { q = q.Where(p => p.Activa); }
        var rows = await q.OrderBy(p => p.Nombre).ToListAsync(ct);
        return rows.Select(p => new PlantillaAgendaDto(p.Id, p.Nombre, p.Descripcion, p.Activa,
            Array.Empty<PlantillaAgendaTurnoDto>())).ToList();
    }

    public async Task<PlantillaAgendaDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.PlantillasAgenda.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return null; }
        var turnos = await db.PlantillaAgendaTurnos.AsNoTracking()
            .Where(t => t.PlantillaAgendaId == id)
            .OrderBy(t => t.DiaSemana).ThenBy(t => t.HoraInicio)
            .ToListAsync(ct);
        return new PlantillaAgendaDto(p.Id, p.Nombre, p.Descripcion, p.Activa, turnos.Select(ToDto).ToList());
    }

    public async Task<PlantillaAgendaDto> GuardarAsync(GuardarPlantillaAgendaCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var nombre = (cmd.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0) { throw new InvalidOperationException("El nombre es obligatorio."); }
        if (nombre.Length > 120) { throw new InvalidOperationException("El nombre no puede exceder 120 caracteres."); }
        if (cmd.Descripcion is { Length: > 300 }) { throw new InvalidOperationException("La descripcion no puede exceder 300 caracteres."); }

        // Unicidad de nombre por tenant, respetando el propio Id al editar.
        var duplicado = await db.PlantillasAgenda
            .Where(p => p.Nombre == nombre && (cmd.Id == null || p.Id != cmd.Id))
            .AnyAsync(ct);
        if (duplicado) { throw new InvalidOperationException($"Ya existe una plantilla llamada '{nombre}'."); }

        PlantillaAgenda entity;
        if (cmd.Id is Guid id)
        {
            entity = await db.PlantillasAgenda.FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new InvalidOperationException("Plantilla no encontrada.");
            entity.Nombre = nombre;
            entity.Descripcion = string.IsNullOrWhiteSpace(cmd.Descripcion) ? null : cmd.Descripcion.Trim();
            entity.Activa = cmd.Activa;
        }
        else
        {
            entity = new PlantillaAgenda
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                Nombre = nombre,
                Descripcion = string.IsNullOrWhiteSpace(cmd.Descripcion) ? null : cmd.Descripcion.Trim(),
                Activa = cmd.Activa
            };
            db.PlantillasAgenda.Add(entity);
        }
        await db.SaveChangesAsync(ct);
        return new PlantillaAgendaDto(entity.Id, entity.Nombre, entity.Descripcion, entity.Activa,
            Array.Empty<PlantillaAgendaTurnoDto>());
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.PlantillasAgenda.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) { return false; }
        db.PlantillasAgenda.Remove(entity); // los turnos caen por cascade
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PlantillaAgendaTurnoDto> GuardarTurnoAsync(Guid plantillaId, GuardarPlantillaAgendaTurnoCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        Validar(cmd);

        var plantilla = await db.PlantillasAgenda.FirstOrDefaultAsync(p => p.Id == plantillaId, ct)
            ?? throw new InvalidOperationException("Plantilla no encontrada.");

        PlantillaAgendaTurno turno;
        if (cmd.Id is Guid id)
        {
            turno = await db.PlantillaAgendaTurnos.FirstOrDefaultAsync(t => t.Id == id && t.PlantillaAgendaId == plantillaId, ct)
                ?? throw new InvalidOperationException("Turno no encontrado.");
            turno.DiaSemana = cmd.DiaSemana;
            turno.HoraInicio = cmd.HoraInicio;
            turno.HoraFin = cmd.HoraFin;
            turno.IntervaloMinutos = cmd.IntervaloMinutos;
        }
        else
        {
            turno = new PlantillaAgendaTurno
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                PlantillaAgendaId = plantilla.Id,
                DiaSemana = cmd.DiaSemana,
                HoraInicio = cmd.HoraInicio,
                HoraFin = cmd.HoraFin,
                IntervaloMinutos = cmd.IntervaloMinutos
            };
            db.PlantillaAgendaTurnos.Add(turno);
        }
        await db.SaveChangesAsync(ct);
        return ToDto(turno);
    }

    public async Task<bool> EliminarTurnoAsync(Guid turnoId, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.PlantillaAgendaTurnos.FirstOrDefaultAsync(t => t.Id == turnoId, ct);
        if (turno is null) { return false; }
        db.PlantillaAgendaTurnos.Remove(turno);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Validar(GuardarPlantillaAgendaTurnoCmd cmd)
    {
        if (cmd.HoraFin <= cmd.HoraInicio) { throw new InvalidOperationException("La hora 'Hasta' debe ser mayor que 'Desde'."); }
        if (cmd.IntervaloMinutos <= 0) { throw new InvalidOperationException("El intervalo debe ser mayor a 0."); }
        if (cmd.IntervaloMinutos > 24 * 60) { throw new InvalidOperationException("El intervalo no puede exceder 1440 minutos."); }
    }

    private static PlantillaAgendaTurnoDto ToDto(PlantillaAgendaTurno t) =>
        new(t.Id, t.DiaSemana, t.HoraInicio, t.HoraFin, t.IntervaloMinutos);
}
