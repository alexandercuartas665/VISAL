using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class SeguimientoService(
    IApplicationDbContext db,
    ITenantContext tenant) : ISeguimientoService
{
    private static readonly string[] EstadosValidos = ["Pendiente", "Realizada", "NoContactado"];

    public async Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarPorMesAsync(int mes, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return []; }

        // Auto-materializar: cualquier paciente con HC creada en el mes recibe un registro Pendiente
        // si no existe. Regla simple para primer roll-out.
        var (desde, hasta) = MesToRango(mes);
        var pacientesActividad = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.TenantId == tid
                && h.FechaApertura >= desde
                && h.FechaApertura < hasta)
            .Select(h => h.PacienteId)
            .Distinct()
            .ToListAsync(ct);

        if (pacientesActividad.Count > 0)
        {
            var yaExistentes = await db.SeguimientoEncuestas.AsNoTracking()
                .Where(x => x.Mes == mes && pacientesActividad.Contains(x.PacienteId))
                .Select(x => x.PacienteId)
                .ToListAsync(ct);
            var faltantes = pacientesActividad.Except(yaExistentes).ToList();
            foreach (var pid in faltantes)
            {
                db.SeguimientoEncuestas.Add(new SeguimientoEncuesta
                {
                    TenantId = tid,
                    PacienteId = pid,
                    Mes = mes,
                    Estado = "Pendiente",
                    EstadoDesde = DateTimeOffset.UtcNow
                });
            }
            if (faltantes.Count > 0) { await db.SaveChangesAsync(ct); }
        }

        // OrderBy dentro del Select no lo traduce EF; ordenamos client-side despues.
        var filas = await db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => x.Mes == mes)
            .Join(db.Pacientes.AsNoTracking(),
                s => s.PacienteId,
                p => p.Id,
                (s, p) => new SeguimientoEncuestaDto(
                    s.Id, s.PacienteId,
                    p.NombreCompleto,
                    p.TipoDocumento, p.NumeroDocumento,
                    null, null,
                    s.Mes, s.Estado, s.FechaLlamada,
                    s.ResponsableLlamadaId, s.ResponsableLlamadaNombre,
                    s.Pregunta1, s.Pregunta2, s.Pregunta3, s.Pregunta4, s.Pregunta5,
                    s.PersonaAtiende, s.Observaciones,
                    (string?)null, (string?)null, (string?)null, (DateOnly?)null,
                    s.CreatedAt, s.EstadoDesde))
            .ToListAsync(ct);

        var ordenadas = filas
            .OrderBy(x => x.Estado == "Pendiente" ? 0 : x.Estado == "NoContactado" ? 1 : 2)
            .ThenBy(x => x.PacienteNombre)
            .ToList();
        return await EnriquecerAsync(tid, ordenadas, ct);
    }

    public async Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarPorRangoAsync(int desdeMes, int hastaMes, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return []; }
        if (hastaMes < desdeMes) { (desdeMes, hastaMes) = (hastaMes, desdeMes); }

        var filas = await db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => x.Mes >= desdeMes && x.Mes <= hastaMes)
            .Join(db.Pacientes.AsNoTracking(),
                s => s.PacienteId,
                p => p.Id,
                (s, p) => new SeguimientoEncuestaDto(
                    s.Id, s.PacienteId,
                    p.NombreCompleto,
                    p.TipoDocumento, p.NumeroDocumento,
                    null, null,
                    s.Mes, s.Estado, s.FechaLlamada,
                    s.ResponsableLlamadaId, s.ResponsableLlamadaNombre,
                    s.Pregunta1, s.Pregunta2, s.Pregunta3, s.Pregunta4, s.Pregunta5,
                    s.PersonaAtiende, s.Observaciones,
                    (string?)null, (string?)null, (string?)null, (DateOnly?)null,
                    s.CreatedAt, s.EstadoDesde))
            .ToListAsync(ct);

        var ordenadas = filas
            .OrderBy(x => x.Estado == "Pendiente" ? 0 : x.Estado == "NoContactado" ? 1 : 2)
            .ThenBy(x => x.PacienteNombre)
            .ToList();
        return await EnriquecerAsync(tid, ordenadas, ct);
    }

    public async Task<(int Creados, int Existentes)> TraerPacientesAsync(
        DateOnly desde, DateOnly hasta, Guid actor,
        Guid? sucursalId = null, string? servicio = null, string? profesional = null,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return (0, 0); }
        if (hasta < desde) { (desde, hasta) = (hasta, desde); }

        // Rango inclusivo por dia: [desde 00:00, (hasta+1) 00:00) en UTC.
        var desdeDt = new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var hastaDt = new DateTimeOffset(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        // Actividad de HC cerradas en el rango (con sede/servicio/profesional).
        var actividad = await CargarActividadAsync(tid, desdeDt, hastaDt, ct);
        if (actividad.Count == 0) { return (0, 0); }

        // Filtros opcionales: sede (por nombre de la sucursal), servicio (contiene),
        // profesional (por id). Solo pasan los que coinciden.
        string? sedeNombre = null;
        if (sucursalId is Guid sid && sid != Guid.Empty)
        {
            sedeNombre = await db.Sucursales.AsNoTracking()
                .Where(s => s.Id == sid).Select(s => s.Nombre).FirstOrDefaultAsync(ct);
        }
        var servFiltro = string.IsNullOrWhiteSpace(servicio) ? null : servicio.Trim();
        var profFiltro = string.IsNullOrWhiteSpace(profesional) ? null : profesional.Trim();
        var filtrada = actividad.Where(a =>
            (sedeNombre is null || string.Equals(a.Sede, sedeNombre, StringComparison.OrdinalIgnoreCase))
            && (servFiltro is null || (a.Servicio ?? "").Contains(servFiltro, StringComparison.OrdinalIgnoreCase))
            && (profFiltro is null || (a.Profesional ?? "").Contains(profFiltro, StringComparison.OrdinalIgnoreCase)));

        // Deseados: (paciente, mes de cierre) distintos.
        var deseados = filtrada
            .Select(a => (a.PacienteId, Mes: a.Mes))
            .Distinct()
            .ToList();
        if (deseados.Count == 0) { return (0, 0); }

        var meses = deseados.Select(d => d.Mes).Distinct().ToList();
        var yaExistentes = (await db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => meses.Contains(x.Mes))
            .Select(x => new { x.PacienteId, x.Mes })
            .ToListAsync(ct))
            .Select(x => (x.PacienteId, x.Mes))
            .ToHashSet();

        int creados = 0, existentes = 0;
        foreach (var d in deseados)
        {
            if (yaExistentes.Contains(d)) { existentes++; continue; }
            db.SeguimientoEncuestas.Add(new SeguimientoEncuesta
            {
                TenantId = tid,
                PacienteId = d.PacienteId,
                Mes = d.Mes,
                Estado = "Pendiente",
                EstadoDesde = DateTimeOffset.UtcNow
            });
            creados++;
        }
        if (creados > 0) { await db.SaveChangesAsync(ct); }
        return (creados, existentes);
    }

    private static int MesDe(DateTimeOffset f)
    {
        var local = f.ToOffset(TimeSpan.FromHours(-5)); // mes de cierre en hora Colombia
        return local.Year * 100 + local.Month;
    }

    public async Task<bool> GuardarEncuestaAsync(Guid id, GuardarEncuestaRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.SeguimientoEncuestas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }

        // Postgres timestamp with time zone requiere DateTimeKind.Utc con Npgsql;
        // la UI puede mandar Kind=Local (DateTime.Now) y romper el SaveChanges.
        var fecha = req.FechaLlamada ?? entity.FechaLlamada;
        if (fecha is DateTime f && f.Kind != DateTimeKind.Utc)
        {
            fecha = f.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(f, DateTimeKind.Utc)
                : f.ToUniversalTime();
        }
        entity.FechaLlamada = fecha;
        entity.Pregunta1 = ClampScore(req.Pregunta1);
        entity.Pregunta2 = ClampScore(req.Pregunta2);
        entity.Pregunta3 = ClampScore(req.Pregunta3);
        entity.Pregunta4 = ClampScore(req.Pregunta4);
        entity.Pregunta5 = ClampScore(req.Pregunta5);
        entity.PersonaAtiende = string.IsNullOrWhiteSpace(req.PersonaAtiende) ? null : req.PersonaAtiende.Trim();
        entity.Observaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim();

        var actorId = tenant.UserId ?? actor;
        if (actorId != Guid.Empty)
        {
            entity.ResponsableLlamadaId = actorId;
            var usr = await db.PlatformUsers.AsNoTracking()
                .Where(u => u.Id == actorId)
                .Select(u => new { u.DisplayName, u.PrimerNombre, u.SegundoNombre, u.Username })
                .FirstOrDefaultAsync(ct);
            entity.ResponsableLlamadaNombre = ComponerNombre(usr?.DisplayName, usr?.PrimerNombre, usr?.SegundoNombre, usr?.Username);
        }

        // Realizada si al menos una respuesta fue dada.
        var tieneRespuestas =
            entity.Pregunta1.HasValue || entity.Pregunta2.HasValue ||
            entity.Pregunta3.HasValue || entity.Pregunta4.HasValue ||
            entity.Pregunta5.HasValue || !string.IsNullOrWhiteSpace(entity.Observaciones);
        if (tieneRespuestas && entity.Estado != "Realizada")
        {
            entity.Estado = "Realizada";
            entity.EstadoDesde = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarHistorialRealizadasAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return []; }
        var filas = await db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => x.TenantId == tid && x.Estado == "Realizada")
            .Join(db.Pacientes.AsNoTracking(),
                s => s.PacienteId,
                p => p.Id,
                (s, p) => new SeguimientoEncuestaDto(
                    s.Id, s.PacienteId,
                    p.NombreCompleto,
                    p.TipoDocumento, p.NumeroDocumento,
                    null, null,
                    s.Mes, s.Estado, s.FechaLlamada,
                    s.ResponsableLlamadaId, s.ResponsableLlamadaNombre,
                    s.Pregunta1, s.Pregunta2, s.Pregunta3, s.Pregunta4, s.Pregunta5,
                    s.PersonaAtiende, s.Observaciones,
                    (string?)null, (string?)null, (string?)null, (DateOnly?)null,
                    s.CreatedAt, s.EstadoDesde))
            .ToListAsync(ct);
        return filas
            .OrderByDescending(x => x.FechaLlamada ?? DateTime.MinValue)
            .ToList();
    }

    public async Task<bool> CambiarEstadoAsync(Guid id, string estado, Guid actor, CancellationToken ct = default)
    {
        if (!EstadosValidos.Contains(estado)) { throw new InvalidOperationException($"Estado invalido: {estado}"); }
        var entity = await db.SeguimientoEncuestas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        if (entity.Estado != estado) { entity.EstadoDesde = DateTimeOffset.UtcNow; }
        entity.Estado = estado;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static (DateTime desde, DateTime hasta) MesToRango(int mes)
    {
        var y = mes / 100;
        var m = mes % 100;
        var desde = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddMonths(1);
        return (desde, hasta);
    }

    // ===== Enriquecimiento sede/servicio/profesional/fecha de atencion =====
    private sealed record ActividadHc(
        Guid PacienteId, int Mes, string? Sede, string? Servicio,
        string? Profesional, Guid? ProfesionalId, DateOnly? FechaAtencion);

    /// <summary>HC cerradas del tenant cuyo cierre cae en [desdeDt, hastaDt), con la
    /// sede/servicio de su asignacion (via pivote), el profesional y la fecha de
    /// atencion. Una fila por HC. Base para filtrar (traer) y enriquecer (listar).</summary>
    private async Task<List<ActividadHc>> CargarActividadAsync(
        Guid tid, DateTimeOffset desdeDt, DateTimeOffset hastaDt, CancellationToken ct)
    {
        var hcs = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.TenantId == tid
                && h.Estado == HistoriaClinicaEstado.Cerrada
                && h.FechaCierre != null && h.FechaCierre >= desdeDt && h.FechaCierre < hastaDt)
            .Select(h => new { h.Id, h.PacienteId, h.FechaCierre, h.EspecialistaNombre, h.ProfesionalId, h.FechaAtencion })
            .ToListAsync(ct);
        if (hcs.Count == 0) { return new(); }

        var hcIds = hcs.Select(h => h.Id).ToList();
        // Sede + servicio desde la asignacion (HC -> pivote -> sesion -> turno -> asignacion).
        var asig = await (from pv in db.AsignacionTurnoSesionHcs.AsNoTracking()
                          where hcIds.Contains(pv.HistoriaClinicaId)
                          join s in db.AsignacionTurnoSesiones.AsNoTracking() on pv.SesionId equals s.Id
                          join t in db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
                          join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
                          select new { pv.HistoriaClinicaId, a.Sucursal, a.NombreServicio })
                         .ToListAsync(ct);
        var asigByHc = asig.GroupBy(x => x.HistoriaClinicaId).ToDictionary(g => g.Key, g => g.First());

        return hcs.Select(h =>
        {
            asigByHc.TryGetValue(h.Id, out var a);
            DateOnly? fa = h.FechaAtencion is DateTimeOffset dt
                ? DateOnly.FromDateTime(dt.ToOffset(TimeSpan.FromHours(-5)).DateTime) : null;
            return new ActividadHc(h.PacienteId, MesDe(h.FechaCierre!.Value),
                a?.Sucursal, a?.NombreServicio,
                string.IsNullOrWhiteSpace(h.EspecialistaNombre) ? null : h.EspecialistaNombre,
                h.ProfesionalId, fa);
        }).ToList();
    }

    /// <summary>Rellena Sede/Servicio/Profesional/FechaAtencion de cada tarjeta con la
    /// actividad clinica del paciente en ese mes (distintos, unidos por coma; fecha de
    /// atencion = la mas reciente).</summary>
    private async Task<IReadOnlyList<SeguimientoEncuestaDto>> EnriquecerAsync(
        Guid tid, List<SeguimientoEncuestaDto> dtos, CancellationToken ct)
    {
        if (dtos.Count == 0) { return dtos; }
        var meses = dtos.Select(d => d.Mes).ToList();
        var (desdeDt, _) = MesToRango(meses.Min());
        var (_, hastaDt) = MesToRango(meses.Max());
        var act = await CargarActividadAsync(tid,
            new DateTimeOffset(desdeDt, TimeSpan.Zero), new DateTimeOffset(hastaDt, TimeSpan.Zero), ct);
        if (act.Count == 0) { return dtos; }

        var porClave = act.GroupBy(a => (a.PacienteId, a.Mes)).ToDictionary(g => g.Key, g =>
        {
            var fechas = g.Where(x => x.FechaAtencion.HasValue).Select(x => x.FechaAtencion!.Value).ToList();
            return (
                Sede: Unir(g.Select(x => x.Sede)),
                Servicio: Unir(g.Select(x => x.Servicio)),
                Profesional: Unir(g.Select(x => x.Profesional)),
                FechaAtencion: fechas.Count > 0 ? fechas.Max() : (DateOnly?)null);
        });

        return dtos.Select(d => porClave.TryGetValue((d.PacienteId, d.Mes), out var e)
            ? d with { Sede = e.Sede, Servicio = e.Servicio, Profesional = e.Profesional, FechaAtencion = e.FechaAtencion }
            : d).ToList();
    }

    private static string? Unir(IEnumerable<string?> vals)
    {
        var d = vals.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return d.Count == 0 ? null : string.Join(", ", d);
    }

    private static int? ClampScore(int? v) => v is null ? null : Math.Clamp(v.Value, 1, 5);

    private static string? ComponerNombre(string? display, string? p1, string? p2, string? user)
    {
        if (!string.IsNullOrWhiteSpace(display)) { return display.Trim(); }
        var partes = new[] { p1, p2 }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim());
        var compuesto = string.Join(' ', partes);
        return !string.IsNullOrWhiteSpace(compuesto) ? compuesto : (string.IsNullOrWhiteSpace(user) ? null : user!.Trim());
    }
}
