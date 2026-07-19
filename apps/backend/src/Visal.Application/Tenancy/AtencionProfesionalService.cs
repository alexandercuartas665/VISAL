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
        // Datos del usuario logueado: nivel de tenant (Owner/Advisor) + Rol con permisos.
        var tu = await db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PlatformUserId == platformUserId, ct);
        if (tu is null) { return Array.Empty<MiServicioAsignadoDto>(); }

        // Admin = TenantRole.Owner o Rol.Nombre = "Administrador". Los admin ven TODOS los
        // turnos del tenant (no se restringe por profesional). Los demas (especialistas) ven
        // solo lo coordinado a su propio profesional vinculado.
        var rolNombre = tu.RolId is Guid rolId
            ? await db.Roles.AsNoTracking().Where(r => r.Id == rolId).Select(r => r.Nombre).FirstOrDefaultAsync(ct)
            : null;
        var esAdmin = tu.TenantRole == TenantRole.Owner
                    || string.Equals(rolNombre, "Administrador", StringComparison.OrdinalIgnoreCase);

        // Construimos la query base (filtrada por tenant via el global filter de EF).
        var turnosQ = db.AsignacionTurnos.AsNoTracking().AsQueryable();
        if (!esAdmin)
        {
            // Especialista: solo sus propios turnos. Sin profesional vinculado -> grid vacio.
            if (tu.ProfesionalId is not Guid profId) { return Array.Empty<MiServicioAsignadoDto>(); }
            turnosQ = turnosQ.Where(t => t.ProfesionalId == profId);
        }

        var turnos = await turnosQ.OrderBy(t => t.CreatedAt).ToListAsync(ct);
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

        // Profesionales asignados a los turnos (Id -> Nombre). Solo los turnos con
        // ProfesionalId asignado (los pendientes en Coordinacion vienen con null).
        var profIds = turnos.Where(t => t.ProfesionalId != Guid.Empty).Select(t => t.ProfesionalId).Distinct().ToList();
        var profs = profIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await db.Profesionales.AsNoTracking()
                .Where(p => profIds.Contains(p.Id))
                .Select(p => new { p.Id, p.PrimerNombre, p.PrimerApellido })
                .ToListAsync(ct))
                .ToDictionary(p => p.Id, p => ((p.PrimerNombre ?? "") + " " + (p.PrimerApellido ?? "")).Trim());

        // Nombre del paquete (por Codigo) para las asignaciones que nacieron de aplicar
        // un paquete. Codigo esta denormalizado en Asignacion.PaqueteCodigo; el Nombre
        // vive solo en la entidad Paquete asi que se resuelve con un JOIN por codigo.
        var paqueteCodigos = asigs.Where(a => a.PaqueteCodigo != null).Select(a => a.PaqueteCodigo!).Distinct().ToList();
        var paqueteNombres = paqueteCodigos.Count == 0
            ? new Dictionary<string, string>()
            : (await db.Paquetes.AsNoTracking()
                .Where(p => paqueteCodigos.Contains(p.Codigo))
                .Select(p => new { p.Codigo, p.Nombre })
                .ToListAsync(ct))
                .GroupBy(p => p.Codigo)
                .ToDictionary(g => g.Key, g => g.First().Nombre);

        // Sesiones ya registradas.
        var sesiones = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => turnoIds.Contains(s.AsignacionTurnoId))
            .ToListAsync(ct);
        var sesionesDict = sesiones
            .GroupBy(s => s.AsignacionTurnoId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.SessionNo));

        // Capa 08 Ola 3 — Chip de revision por fila del grid.
        // Para cada paciente traemos la HC MAS RECIENTE (Abierta o Cerrada) y su
        // revision viva (si existe). El chip resume el estado del ciclo — util
        // para que el profesional vea de un vistazo cuales HCs suyas ya fueron
        // aprobadas/rechazadas por el revisor. Terminales (ArchivadaOk/Inactivada)
        // se dejan pasar tambien para que el rojo/verde/negro se refleje incluso
        // despues de archivar.
        var hcsPacientes = pacIds.Count == 0
            ? new List<HistoriaClinica>()
            : await db.HistoriasClinicas.AsNoTracking()
                .Where(h => pacIds.Contains(h.PacienteId)
                            && h.Estado != HistoriaClinicaEstado.Inactiva)
                .ToListAsync(ct);
        // Escogemos la HC mas reciente por paciente (por FechaCierre o FechaApertura como fallback).
        var hcMasRecientePorPaciente = hcsPacientes
            .GroupBy(h => h.PacienteId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(h => h.FechaCierre ?? h.FechaApertura).First());
        var hcIds = hcMasRecientePorPaciente.Values.Select(h => h.Id).ToList();

        var revisionesDic = hcIds.Count == 0
            ? new Dictionary<Guid, RevisionClinica>()
            : (await db.RevisionesClinica.AsNoTracking()
                .Where(r => hcIds.Contains(r.HistoriaClinicaId))
                .ToListAsync(ct))
                .ToDictionary(r => r.HistoriaClinicaId);

        // Ultimo motivo de rechazo por revision — solo poblado si estado es Rechazada.
        var revisionesRechazadas = revisionesDic.Values
            .Where(r => r.EstadoAgregado == RevisionEstadoAgregado.Rechazada)
            .Select(r => r.Id)
            .ToList();
        var motivosDic = revisionesRechazadas.Count == 0
            ? new Dictionary<Guid, string?>()
            : (await db.RevisionClinicaEventos.AsNoTracking()
                .Where(e => revisionesRechazadas.Contains(e.RevisionClinicaId)
                            && e.Tipo == RevisionTipoEvento.Rechazado)
                .GroupBy(e => e.RevisionClinicaId)
                .Select(g => new
                {
                    Rid = g.Key,
                    Motivo = g.OrderByDescending(x => x.OcurridoEn).First().Motivo
                })
                .ToListAsync(ct))
                .ToDictionary(x => x.Rid, x => x.Motivo);

        // Orden corrido para la columna "Orden" del grid, ordenado por created_at de la asignacion madre.
        var ordenMap = asigs
            .OrderByDescending(a => a.CreatedAt)
            .Select((a, idx) => new { a.Id, Orden = idx + 1 })
            .ToDictionary(x => x.Id, x => x.Orden);

        // Total de sesiones por asignacion = suma(Cantidad) de todos sus turnos.
        // Se muestra al lado del NumeroSesionMostrar como "sesion 1 / 3".
        var totalPorAsignacion = turnos
            .GroupBy(t => t.AsignacionId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Cantidad));

        // Contador GLOBAL de sesion por asignacion — incrementa a medida que
        // recorremos los turnos ordenados por CreatedAt asc. Cada fila expandida
        // (n=1..Cantidad) toma el siguiente numero. Esto corrige el bug de que 2
        // turnos con Cantidad=1 mostraban ambos SessionNo=1 (cada uno era la
        // sesion #1 de SU turno pero no de la asignacion completa).
        var contadorPorAsignacion = new Dictionary<Guid, int>();

        var result = new List<MiServicioAsignadoDto>();
        foreach (var t in turnos)
        {
            if (!asigDict.TryGetValue(t.AsignacionId, out var a)) { continue; }
            pacs.TryGetValue(a.PacienteId, out var p);
            var sesionesT = sesionesDict.TryGetValue(t.Id, out var dict) ? dict : new Dictionary<int, AsignacionTurnoSesion>();
            var totalAsig = totalPorAsignacion.TryGetValue(t.AsignacionId, out var tot) ? tot : t.Cantidad;

            for (int n = 1; n <= t.Cantidad; n++)
            {
                var sesion = sesionesT.TryGetValue(n, out var s) ? s : null;
                var completado = sesion is not null;
                if (!incluirCompletados && completado) { continue; }

                // Incremento del numero de sesion GLOBAL por asignacion.
                var nGlobal = contadorPorAsignacion.TryGetValue(t.AsignacionId, out var c) ? c + 1 : 1;
                contadorPorAsignacion[t.AsignacionId] = nGlobal;

                // Capa 08 Ola 3 — resolver chip de revision para esta fila del grid.
                RevisionEstadoAgregado? revEstado = null;
                DateTimeOffset? revUltima = null;
                string? revMotivo = null;
                if (hcMasRecientePorPaciente.TryGetValue(a.PacienteId, out var hcRel)
                    && revisionesDic.TryGetValue(hcRel.Id, out var rev))
                {
                    revEstado = rev.EstadoAgregado;
                    revUltima = rev.UltimaAccionEn;
                    if (rev.EstadoAgregado == RevisionEstadoAgregado.Rechazada)
                    {
                        motivosDic.TryGetValue(rev.Id, out revMotivo);
                    }
                }

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
                    a.FormatoHistoria,
                    nGlobal,
                    totalAsig,
                    profs.TryGetValue(t.ProfesionalId, out var np) ? np : "",
                    a.PaqueteCodigo,
                    a.PaqueteCodigo != null && paqueteNombres.TryGetValue(a.PaqueteCodigo, out var pn) ? pn : null,
                    revEstado,
                    revUltima,
                    revMotivo));
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
