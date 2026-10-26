using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Reportes;

public sealed class PacienteIngresoService(
    IApplicationDbContext db,
    ITenantContext tenant) : IPacienteIngresoService
{
    public async Task<IReadOnlyList<PacienteIngresoDto>> ListarAsync(CancellationToken ct = default)
    {
        return await db.PacienteIngresos.AsNoTracking()
            .OrderByDescending(x => x.FechaComentan)
            .Select(x => new PacienteIngresoDto(
                x.Id, x.PacienteId,
                x.Paciente!.NombreCompleto, x.Paciente.NumeroDocumento,
                x.FechaComentan, x.FechaRespuesta, x.SlaMinutos,
                x.IpsComenta, x.ResponsableIngreso, x.Ingreso, x.MotivoNoIngreso, x.Observaciones))
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PacienteBusquedaDto>> BuscarPacientesAsync(string termino, CancellationToken ct = default)
    {
        termino = (termino ?? "").Trim();
        if (termino.Length < 2) { return []; }
        var t = termino.ToLower();
        // El query filter de tenant limita la busqueda al tenant actual.
        return await db.Pacientes.AsNoTracking()
            .Where(p => p.NombreCompleto.ToLower().Contains(t) || p.NumeroDocumento.Contains(termino))
            .OrderBy(p => p.NombreCompleto)
            .Take(20)
            .Select(p => new PacienteBusquedaDto(p.Id, p.NombreCompleto, p.NumeroDocumento))
            .ToListAsync(ct);
    }

    public async Task<Guid> GuardarAsync(Guid? id, GuardarIngresoRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }
        // Defensa: el paciente debe existir EN ESTE tenant (el filtro de tenant aplica).
        var existePaciente = await db.Pacientes.AnyAsync(p => p.Id == req.PacienteId, ct);
        if (!existePaciente)
        {
            throw new InvalidOperationException("Paciente no encontrado en el tenant.");
        }

        PacienteIngreso e;
        if (id is Guid gid)
        {
            e = await db.PacienteIngresos.FirstOrDefaultAsync(x => x.Id == gid, ct)
                ?? throw new InvalidOperationException("Registro de ingreso no encontrado.");
        }
        else
        {
            e = new PacienteIngreso { TenantId = tid };
            db.PacienteIngresos.Add(e);
        }

        e.PacienteId = req.PacienteId;
        e.FechaComentan = req.FechaComentan;
        e.FechaRespuesta = req.FechaRespuesta;
        e.IpsComenta = Limpio(req.IpsComenta);
        e.ResponsableIngreso = Limpio(req.ResponsableIngreso);
        e.Ingreso = req.Ingreso;
        e.MotivoNoIngreso = Limpio(req.MotivoNoIngreso);
        e.Observaciones = Limpio(req.Observaciones);
        // SLA en minutos: solo si estan las dos fechas.
        e.SlaMinutos = req.FechaComentan is DateTimeOffset c && req.FechaRespuesta is DateTimeOffset r
            ? (int)Math.Round((r - c).TotalMinutes)
            : null;

        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task<bool> EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.PacienteIngresos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.PacienteIngresos.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
