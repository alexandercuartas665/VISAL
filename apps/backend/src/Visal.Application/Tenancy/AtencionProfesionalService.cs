using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class AtencionProfesionalService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IConfiguracionClinicaService clinica) : IAtencionProfesionalService
{
    public async Task<IReadOnlyList<MiServicioAsignadoDto>> GetMisServiciosAsync(Guid platformUserId, bool incluirCompletados = true, CancellationToken ct = default)
    {
        // El profesional vinculado al usuario logueado.
        var tu = await db.TenantUsers.AsNoTracking().FirstOrDefaultAsync(u => u.PlatformUserId == platformUserId, ct);
        if (tu is null || tu.ProfesionalId is not Guid profId) { return Array.Empty<MiServicioAsignadoDto>(); }

        // Turnos coordinados a este profesional.
        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.ProfesionalId == profId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        if (turnos.Count == 0) { return Array.Empty<MiServicioAsignadoDto>(); }

        var turnoIds = turnos.Select(t => t.Id).ToList();
        var asigIds = turnos.Select(t => t.AsignacionId).Distinct().ToList();

        // Asignaciones madre + pacientes para enriquecer cada fila.
        var asigs = await db.Asignaciones.AsNoTracking()
            .Where(a => asigIds.Contains(a.Id))
            .ToListAsync(ct);
        var asigDict = asigs.ToDictionary(a => a.Id);

        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NumeroDocumento, p.NombreCompleto, p.TipoDocumento })
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        // Sesiones ya registradas.
        var sesiones = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => turnoIds.Contains(s.AsignacionTurnoId))
            .ToListAsync(ct);
        var sesionesDict = sesiones
            .GroupBy(s => s.AsignacionTurnoId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.SessionNo));

        // Orden corrido para la columna "Orden" del grid, ordenado por created_at de la asignacion madre.
        var ordenMap = asigs
            .OrderByDescending(a => a.CreatedAt)
            .Select((a, idx) => new { a.Id, Orden = idx + 1 })
            .ToDictionary(x => x.Id, x => x.Orden);

        var result = new List<MiServicioAsignadoDto>();
        foreach (var t in turnos)
        {
            if (!asigDict.TryGetValue(t.AsignacionId, out var a)) { continue; }
            pacs.TryGetValue(a.PacienteId, out var p);
            var sesionesT = sesionesDict.TryGetValue(t.Id, out var dict) ? dict : new Dictionary<int, AsignacionTurnoSesion>();

            for (int n = 1; n <= t.Cantidad; n++)
            {
                var sesion = sesionesT.TryGetValue(n, out var s) ? s : null;
                var completado = sesion is not null;
                if (!incluirCompletados && completado) { continue; }

                result.Add(new MiServicioAsignadoDto(
                    t.Id, a.Id,
                    n, t.Cantidad,
                    a.TipoServicio,
                    a.NombreServicio,
                    a.Id.ToString()[..8],
                    a.CodigoAutorizacion ?? "",
                    DateOnly.FromDateTime(a.CreatedAt.LocalDateTime),
                    ordenMap[a.Id],
                    p?.TipoDocumento ?? "",
                    p?.NumeroDocumento ?? "",
                    p?.NombreCompleto ?? "(sin paciente)",
                    a.PacienteId,
                    completado,
                    sesion?.FechaAtencion,
                    a.FormatoHistoria));
            }
        }
        return result;
    }

    public async Task<RegistrarSesionResult> RegistrarSesionAsync(Guid asignacionTurnoId, int sessionNo, string? nota, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            return new RegistrarSesionResult(false, "Sin tenant activo.", false, false);
        }
        var turno = await db.AsignacionTurnos.FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct);
        if (turno is null) { return new RegistrarSesionResult(false, "Turno no encontrado.", false, false); }
        if (sessionNo < 1 || sessionNo > turno.Cantidad)
        {
            return new RegistrarSesionResult(false, $"Numero de sesion fuera de rango (1..{turno.Cantidad}).", false, false);
        }

        // No permitir saltarse: la sesion N-1 debe existir.
        if (sessionNo > 1)
        {
            var prevExiste = await db.AsignacionTurnoSesiones
                .AnyAsync(s => s.AsignacionTurnoId == asignacionTurnoId && s.SessionNo == sessionNo - 1, ct);
            if (!prevExiste)
            {
                return new RegistrarSesionResult(false,
                    $"Debes atender primero la sesion {sessionNo - 1}.",
                    false, true);
            }
        }

        // Validar HC vigente: que el paciente haya tenido alguna sesion atendida
        // dentro de los N meses configurados. Si nunca ha sido atendido, se considera
        // que su HC necesita ser creada (no bloquea para la primera atencion ya que
        // esta es justamente la HC inicial). Asi que solo bloquea si tiene HC pero
        // ya vencida.
        var asig = await db.Asignaciones.AsNoTracking().FirstOrDefaultAsync(a => a.Id == turno.AsignacionId, ct);
        if (asig is not null)
        {
            var mesesValidez = await clinica.GetMesesValidezHistoriaClinicaAsync(ct);
            var corte = DateOnly.FromDateTime(DateTime.Today.AddMonths(-mesesValidez));

            var pacienteId = asig.PacienteId;
            var ultimaAtencion = await db.AsignacionTurnoSesiones.AsNoTracking()
                .Join(db.AsignacionTurnos.AsNoTracking(), s => s.AsignacionTurnoId, t => t.Id, (s, t) => new { s, t })
                .Join(db.Asignaciones.AsNoTracking(), st => st.t.AsignacionId, a => a.Id, (st, a) => new { st.s, a })
                .Where(x => x.a.PacienteId == pacienteId)
                .OrderByDescending(x => x.s.FechaAtencion)
                .Select(x => (DateOnly?)x.s.FechaAtencion)
                .FirstOrDefaultAsync(ct);

            // Si tiene atenciones previas pero la ultima es anterior a la fecha de corte,
            // se exige una nueva HC.
            if (ultimaAtencion is DateOnly fechaUltima && fechaUltima < corte)
            {
                return new RegistrarSesionResult(false,
                    $"El paciente no tiene historia clinica vigente. Ultima atencion: {fechaUltima:dd/MM/yyyy}. " +
                    $"Validez configurada: {mesesValidez} mes(es). Crea una nueva historia clinica antes de continuar.",
                    true, false);
            }
        }

        // Idempotencia: si ya existe, no duplicar.
        var yaExiste = await db.AsignacionTurnoSesiones
            .AnyAsync(s => s.AsignacionTurnoId == asignacionTurnoId && s.SessionNo == sessionNo, ct);
        if (yaExiste) { return new RegistrarSesionResult(false, "Esta sesion ya fue registrada.", false, false); }

        db.AsignacionTurnoSesiones.Add(new AsignacionTurnoSesion
        {
            TenantId = tid,
            AsignacionTurnoId = asignacionTurnoId,
            SessionNo = sessionNo,
            FechaAtencion = DateOnly.FromDateTime(DateTime.Today),
            NotaTexto = nota?.Trim()
        });

        // Si esta es la ultima sesion del turno y ya todas las anteriores estan, dejar el turno listo.
        // (El estado de la Asignacion madre se mantiene Asignado; el cierre total cuando todas las
        // sesiones de todos los turnos completen se gestiona en un evento futuro.)
        await db.SaveChangesAsync(ct);
        return new RegistrarSesionResult(true, "Sesion registrada.", false, false);
    }
}
