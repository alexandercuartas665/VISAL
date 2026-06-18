using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Lecturas y acciones de mantenimiento sobre la tabla rda_eventos. NO construye Bundles
/// (eso es del RdaBuilderService) ni envia (eso sera del IhceSenderService en Ola 5).
/// </summary>
public sealed class RdaConsoleService(
    IApplicationDbContext db,
    ILogger<RdaConsoleService> log) : IRdaConsoleService
{
    public async Task<IReadOnlyList<RdaEventoRowDto>> ListarAsync(RdaConsoleFiltro filtro, CancellationToken ct = default)
    {
        // Join contra paciente, profesional y sucursal para sacar nombres legibles.
        var q = from e in db.RdaEventos.AsNoTracking()
                join p in db.Pacientes.AsNoTracking() on e.PacienteId equals p.Id
                join s in db.Sucursales.AsNoTracking() on e.SucursalId equals s.Id
                join pr in db.Profesionales.AsNoTracking() on e.ProfesionalId equals pr.Id into prGroup
                from pr in prGroup.DefaultIfEmpty()
                select new
                {
                    e.Id,
                    e.FechaGeneracion,
                    PacienteNombre = p.NombreCompleto,
                    PacienteDocumento = p.NumeroDocumento,
                    ProfesionalNombre = pr != null ? pr.NombreCompleto : "(sin firmante)",
                    SucursalNombre = s.Nombre,
                    e.Modalidad,
                    e.Ambiente,
                    e.Estado,
                    e.Intentos,
                    e.FechaEnvio,
                    e.ReferenciaMinsalud,
                    e.BundleHash
                };

        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            var doc = filtro.Documento.Trim();
            q = q.Where(x => x.PacienteDocumento.Contains(doc));
        }
        if (filtro.Estado is EstadoRdaEvento est) { q = q.Where(x => x.Estado == est); }
        if (filtro.Ambiente is AmbienteIhce amb) { q = q.Where(x => x.Ambiente == amb); }
        if (filtro.Desde is DateOnly desde)
        {
            var dt = desde.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(x => x.FechaGeneracion >= dt);
        }
        if (filtro.Hasta is DateOnly hasta)
        {
            var dt = hasta.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            q = q.Where(x => x.FechaGeneracion <= dt);
        }

        var rows = await q.OrderByDescending(x => x.FechaGeneracion).Take(500).ToListAsync(ct);
        return rows.Select(r => new RdaEventoRowDto(
            r.Id, r.FechaGeneracion, r.PacienteNombre, r.PacienteDocumento,
            r.ProfesionalNombre, r.SucursalNombre, r.Modalidad, r.Ambiente,
            r.Estado, r.Intentos, r.FechaEnvio, r.ReferenciaMinsalud, r.BundleHash)).ToList();
    }

    public async Task<RdaEventoDetailDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.RdaEventos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return null; }
        return new RdaEventoDetailDto(e.Id, e.BundleJson, e.BundleHash, e.Estado,
            e.Intentos, e.ErroresJson, e.ReferenciaMinsalud, e.FechaGeneracion, e.FechaEnvio);
    }

    public async Task<IReadOnlyList<HcCandidataRdaDto>> ListarHcCandidatasAsync(string? buscar, CancellationToken ct = default)
    {
        var q = from h in db.HistoriasClinicas.AsNoTracking()
                join p in db.Pacientes.AsNoTracking() on h.PacienteId equals p.Id
                join fd in db.FormDefinitions.AsNoTracking() on h.FormDefinitionId equals fd.Id into fdg
                from fd in fdg.DefaultIfEmpty()
                select new
                {
                    h.Id,
                    PacienteNombre = p.NombreCompleto,
                    PacienteDocumento = p.NumeroDocumento,
                    h.FechaApertura,
                    h.FechaCierre,
                    FormatoCodigo = fd != null ? fd.Codigo : null,
                    Estado = h.Estado
                };
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim();
            q = q.Where(x => x.PacienteNombre.Contains(b) || x.PacienteDocumento.Contains(b));
        }
        var rows = await q.OrderByDescending(x => x.FechaApertura).Take(50).ToListAsync(ct);
        return rows.Select(r => new HcCandidataRdaDto(r.Id, r.PacienteNombre, r.PacienteDocumento,
            r.FechaApertura, r.FechaCierre, r.FormatoCodigo, r.Estado.ToString())).ToList();
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.RdaEventos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado != EstadoRdaEvento.Borrador)
        {
            // Auditoria inmutable: una vez validado / enviado / aceptado no se borra.
            throw new InvalidOperationException(
                $"No se puede eliminar el evento {id}: estado {e.Estado}. Solo se permiten borrar Borradores.");
        }
        db.RdaEventos.Remove(e);
        await db.SaveChangesAsync(ct);
        log.LogInformation("RdaEvento {Id} borrado por {Actor}", id, actor);
        return true;
    }
}
