using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Agendas;

public sealed class NovedadProfesionalService(IApplicationDbContext db, ITenantContext tenant) : INovedadProfesionalService
{
    public async Task<IReadOnlyList<NovedadProfesionalDto>> ListarPorProfesionalAsync(Guid profesionalId, CancellationToken ct = default)
    {
        var rows = await db.NovedadesProfesional.AsNoTracking()
            .Where(n => n.ProfesionalId == profesionalId)
            .OrderByDescending(n => n.FechaDesde).ThenByDescending(n => n.FechaHasta)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<NovedadProfesionalDto> GuardarAsync(Guid profesionalId, GuardarNovedadProfesionalCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (profesionalId == Guid.Empty) { throw new InvalidOperationException("Selecciona un profesional."); }
        Validar(cmd);

        NovedadProfesional entity;
        if (cmd.Id is Guid id)
        {
            entity = await db.NovedadesProfesional.FirstOrDefaultAsync(n => n.Id == id && n.ProfesionalId == profesionalId, ct)
                ?? throw new InvalidOperationException("Novedad no encontrada.");
            entity.Tipo = cmd.Tipo;
            entity.FechaDesde = cmd.FechaDesde;
            entity.FechaHasta = cmd.FechaHasta;
            entity.HoraDesde = cmd.HoraDesde;
            entity.HoraHasta = cmd.HoraHasta;
            entity.Nota = string.IsNullOrWhiteSpace(cmd.Nota) ? null : cmd.Nota.Trim();
        }
        else
        {
            entity = new NovedadProfesional
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                ProfesionalId = profesionalId,
                Tipo = cmd.Tipo,
                FechaDesde = cmd.FechaDesde,
                FechaHasta = cmd.FechaHasta,
                HoraDesde = cmd.HoraDesde,
                HoraHasta = cmd.HoraHasta,
                Nota = string.IsNullOrWhiteSpace(cmd.Nota) ? null : cmd.Nota.Trim()
            };
            db.NovedadesProfesional.Add(entity);
        }
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> EliminarAsync(Guid novedadId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.NovedadesProfesional.FirstOrDefaultAsync(n => n.Id == novedadId, ct);
        if (entity is null) { return false; }
        db.NovedadesProfesional.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Validar(GuardarNovedadProfesionalCmd cmd)
    {
        if (cmd.FechaHasta < cmd.FechaDesde) { throw new InvalidOperationException("La fecha 'Hasta' no puede ser anterior a 'Desde'."); }
        // Las horas son opcionales, pero si viene una debe venir la otra y ser coherentes.
        var tieneDesde = cmd.HoraDesde is not null;
        var tieneHasta = cmd.HoraHasta is not null;
        if (tieneDesde != tieneHasta) { throw new InvalidOperationException("Indica la hora 'Desde' y 'Hasta', o deja ambas vacias para dia completo."); }
        if (tieneDesde && tieneHasta && cmd.HoraHasta <= cmd.HoraDesde)
        { throw new InvalidOperationException("La hora 'Hasta' debe ser mayor que 'Desde'."); }
        if (cmd.Nota is { Length: > 300 }) { throw new InvalidOperationException("La nota no puede exceder 300 caracteres."); }
    }

    private static NovedadProfesionalDto ToDto(NovedadProfesional n) =>
        new(n.Id, n.ProfesionalId, n.Tipo, n.FechaDesde, n.FechaHasta, n.HoraDesde, n.HoraHasta, n.Nota);
}
