using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>Fila del resumen de un cierre/previsualizacion: un servicio con sesiones pendientes.</summary>
public sealed record CierrePeriodoFilaDto(
    Guid PacienteId, string PacienteNombre, string? PacienteDocumento,
    string ServicioNombre, string? CodigoServicio, int CantidadPendiente);

/// <summary>Previsualizacion del cierre de un periodo (sin mutar): que quedaria anulado.
/// Filas abarca TODO el periodo (todos los pacientes con pendientes); la UI filtra por
/// paciente del lado cliente y acota el cierre pasando el PacienteId a CerrarAsync.</summary>
public sealed record CierrePeriodoPreviewDto(
    int Anio, int Mes, bool YaCerrado,
    IReadOnlyList<CierrePeriodoFilaDto> Filas, int TotalServicios, int TotalSesiones);

/// <summary>Cabecera de un cierre ya realizado (para el historial). PacienteNombre != null
/// cuando el cierre fue parcial (acotado a un paciente).</summary>
public sealed record CierrePeriodoDto(
    Guid Id, int Anio, int Mes, DateTimeOffset CerradoEn,
    int TotalServicios, int TotalSesiones,
    Guid? PacienteId = null, string? PacienteNombre = null,
    // Cantidad de pacientes distintos del cierre y usuario (email) que lo cerro.
    int TotalPacientes = 0, string? UsuarioCerro = null);

public interface ICierrePeriodoService
{
    /// <summary>Resumen de sesiones PENDIENTES que se anularian al cerrar (anio, mes), de TODO
    /// el periodo. No muta. La UI filtra por paciente localmente.</summary>
    Task<CierrePeriodoPreviewDto> PrevisualizarAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Cierra el periodo: anula las sesiones pendientes (marca sus turnos) y guarda el
    /// resumen. Si <paramref name="pacienteId"/> viene, el cierre es PARCIAL (solo ese paciente).
    /// Lanza si no hay sesiones pendientes que cerrar. Devuelve el cierre creado.</summary>
    Task<CierrePeriodoDto> CerrarAsync(int anio, int mes, Guid? pacienteId, Guid actor, CancellationToken ct = default);

    /// <summary>Reabre un periodo cerrado: devuelve las sesiones a pendientes (quita la marca) y
    /// borra el cierre + su resumen.</summary>
    Task<bool> ReabrirAsync(Guid cierreId, Guid actor, CancellationToken ct = default);

    /// <summary>Historial de periodos cerrados (mas reciente primero).</summary>
    Task<IReadOnlyList<CierrePeriodoDto>> ListarCierresAsync(CancellationToken ct = default);

    /// <summary>Detalle (resumen guardado) de un cierre.</summary>
    Task<IReadOnlyList<CierrePeriodoFilaDto>> GetDetalleAsync(Guid cierreId, CancellationToken ct = default);
}

public sealed class CierrePeriodoService : ICierrePeriodoService
{
    private readonly IApplicationDbContext _db;

    public CierrePeriodoService(IApplicationDbContext db) => _db = db;

    public async Task<CierrePeriodoPreviewDto> PrevisualizarAsync(int anio, int mes, CancellationToken ct = default)
    {
        var yaCerrado = await _db.CierrePeriodos.AsNoTracking()
            .AnyAsync(c => c.Anio == anio && c.Mes == mes, ct);

        var filas = await CalcularPendientesAsync(anio, mes, ct);
        return new CierrePeriodoPreviewDto(
            anio, mes, yaCerrado, filas,
            filas.Count, filas.Sum(f => f.CantidadPendiente));
    }

    public async Task<CierrePeriodoDto> CerrarAsync(int anio, int mes, Guid? pacienteId, Guid actor, CancellationToken ct = default)
    {
        // Turnos pendientes en el periodo (acotados al paciente si viene). Cerrar es idempotente:
        // los turnos ya anulados por un cierre previo salen del calculo de pendientes, asi que un
        // cierre parcial no choca con otros del mismo periodo (indice no unico).
        var turnosPend = await TurnosPendientesAsync(anio, mes, pacienteId, ct);
        if (turnosPend.Count == 0)
        {
            var quien = pacienteId is null ? "" : " para el paciente seleccionado";
            throw new InvalidOperationException(
                $"No hay sesiones pendientes que cerrar en {mes:00}/{anio}{quien}.");
        }
        var filas = await ResumirAsync(turnosPend, ct);

        // Nombre del paciente (denormalizado) cuando el cierre es parcial.
        string? pacNombre = null;
        if (pacienteId is Guid pid)
        {
            pacNombre = await _db.Pacientes.AsNoTracking()
                .Where(p => p.Id == pid)
                .Select(p => ((p.PrimerNombre ?? "") + " " + (p.PrimerApellido ?? "")).Trim())
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(pacNombre)) { pacNombre = filas.FirstOrDefault()?.PacienteNombre; }
        }

        var cierre = new CierrePeriodo
        {
            Anio = anio,
            Mes = mes,
            PacienteId = pacienteId,
            PacienteNombre = pacNombre,
            CerradoEn = DateTimeOffset.UtcNow,
            TotalServicios = filas.Count,
            TotalSesiones = filas.Sum(f => f.CantidadPendiente),
            Detalles = filas.Select(f => new CierrePeriodoDetalle
            {
                PacienteNombre = f.PacienteNombre,
                PacienteDocumento = f.PacienteDocumento,
                ServicioNombre = f.ServicioNombre,
                CodigoServicio = f.CodigoServicio,
                CantidadPendiente = f.CantidadPendiente
            }).ToList()
        };
        _db.CierrePeriodos.Add(cierre);           // el interceptor estampa TenantId/audit
        await _db.SaveChangesAsync(ct);           // necesitamos el Id para marcar los turnos

        // Marcar los turnos pendientes como anulados por este cierre (salen de Atencion).
        var turnoIds = turnosPend.Select(t => t.TurnoId).ToList();
        if (turnoIds.Count > 0)
        {
            var turnos = await _db.AsignacionTurnos.Where(t => turnoIds.Contains(t.Id)).ToListAsync(ct);
            foreach (var t in turnos) { t.CierrePeriodoId = cierre.Id; }
            await _db.SaveChangesAsync(ct);
        }

        return new CierrePeriodoDto(cierre.Id, cierre.Anio, cierre.Mes, cierre.CerradoEn,
            cierre.TotalServicios, cierre.TotalSesiones, cierre.PacienteId, cierre.PacienteNombre);
    }

    public async Task<bool> ReabrirAsync(Guid cierreId, Guid actor, CancellationToken ct = default)
    {
        var cierre = await _db.CierrePeriodos.FirstOrDefaultAsync(c => c.Id == cierreId, ct);
        if (cierre is null) { return false; }

        // Devolver las sesiones a pendientes.
        var turnos = await _db.AsignacionTurnos.Where(t => t.CierrePeriodoId == cierreId).ToListAsync(ct);
        foreach (var t in turnos) { t.CierrePeriodoId = null; }

        _db.CierrePeriodos.Remove(cierre);        // cascade borra los detalles
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CierrePeriodoDto>> ListarCierresAsync(CancellationToken ct = default)
    {
        var cierres = await _db.CierrePeriodos.AsNoTracking()
            .OrderByDescending(c => c.Anio).ThenByDescending(c => c.Mes).ThenByDescending(c => c.CerradoEn)
            .Select(c => new
            {
                c.Id, c.Anio, c.Mes, c.CerradoEn, c.TotalServicios, c.TotalSesiones,
                c.PacienteId, c.PacienteNombre, c.CreatedBy
            })
            .ToListAsync(ct);
        if (cierres.Count == 0) { return Array.Empty<CierrePeriodoDto>(); }

        var ids = cierres.Select(c => c.Id).ToList();

        // Cantidad de pacientes DISTINTOS por cierre, desde el detalle guardado (nombre+documento).
        var pacPorCierre = (await _db.CierrePeriodoDetalles.AsNoTracking()
                .Where(d => ids.Contains(d.CierrePeriodoId))
                .Select(d => new { d.CierrePeriodoId, d.PacienteNombre, d.PacienteDocumento })
                .ToListAsync(ct))
            .GroupBy(d => d.CierrePeriodoId)
            .ToDictionary(g => g.Key,
                g => g.Select(x => (x.PacienteNombre + "|" + (x.PacienteDocumento ?? "")).Trim())
                      .Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Usuario que cerro: email del tenant_user por CreatedBy (= platform_user_id del interceptor).
        var userIds = cierres.Where(c => c.CreatedBy is not null).Select(c => c.CreatedBy!.Value).Distinct().ToList();
        var usuarios = new Dictionary<Guid, string>();
        if (userIds.Count > 0)
        {
            var tus = await _db.TenantUsers.AsNoTracking()
                .Where(tu => userIds.Contains(tu.PlatformUserId))
                .Select(tu => new { tu.PlatformUserId, tu.Email })
                .ToListAsync(ct);
            foreach (var tu in tus) { usuarios[tu.PlatformUserId] = tu.Email; }
        }

        return cierres.Select(c => new CierrePeriodoDto(
            c.Id, c.Anio, c.Mes, c.CerradoEn, c.TotalServicios, c.TotalSesiones,
            c.PacienteId, c.PacienteNombre,
            pacPorCierre.TryGetValue(c.Id, out var np) ? np : 0,
            c.CreatedBy is Guid cb && usuarios.TryGetValue(cb, out var em) ? em : null)).ToList();
    }

    public async Task<IReadOnlyList<CierrePeriodoFilaDto>> GetDetalleAsync(Guid cierreId, CancellationToken ct = default)
        => await _db.CierrePeriodoDetalles.AsNoTracking()
            .Where(d => d.CierrePeriodoId == cierreId)
            .OrderBy(d => d.PacienteNombre)
            .Select(d => new CierrePeriodoFilaDto(Guid.Empty, d.PacienteNombre, d.PacienteDocumento, d.ServicioNombre, d.CodigoServicio, d.CantidadPendiente))
            .ToListAsync(ct);

    // ── Internos ────────────────────────────────────────────────────────────

    private sealed record TurnoPendiente(Guid TurnoId, Guid AsignacionId, int Cantidad);

    // Turnos pendientes del periodo: asignaciones cuya vigencia TERMINA en (anio, mes)
    // [(MesFinal ?? MesVigencia) == mes && AnioServicio == anio], turnos no anulados por un
    // cierre previo, y SIN ninguna sesion completada (atendida y cerrada). Si viene pacienteId,
    // acota a las asignaciones de ese paciente (cierre parcial).
    private async Task<List<TurnoPendiente>> TurnosPendientesAsync(int anio, int mes, Guid? pacienteId, CancellationToken ct)
    {
        short anioS = (short)anio, mesS = (short)mes;

        var asigQ = _db.Asignaciones.AsNoTracking()
            .Where(a => a.AnioServicio == anioS && (a.MesFinal ?? a.MesVigencia) == mesS);
        if (pacienteId is Guid pid) { asigQ = asigQ.Where(a => a.PacienteId == pid); }
        var asigIds = await asigQ.Select(a => a.Id).ToListAsync(ct);
        if (asigIds.Count == 0) { return new List<TurnoPendiente>(); }

        var turnos = await _db.AsignacionTurnos.AsNoTracking()
            .Where(t => asigIds.Contains(t.AsignacionId) && t.CierrePeriodoId == null)
            .Select(t => new { t.Id, t.AsignacionId, t.Cantidad })
            .ToListAsync(ct);
        if (turnos.Count == 0) { return new List<TurnoPendiente>(); }

        var turnoIds = turnos.Select(t => t.Id).ToList();
        // Turnos que YA tienen al menos una sesion completada (atendida) -> no son pendientes.
        var turnosConCompletada = (await _db.AsignacionTurnoSesiones.AsNoTracking()
                .Where(s => turnoIds.Contains(s.AsignacionTurnoId) && s.Completado)
                .Select(s => s.AsignacionTurnoId)
                .ToListAsync(ct))
            .ToHashSet();

        return turnos
            .Where(t => !turnosConCompletada.Contains(t.Id))
            .Select(t => new TurnoPendiente(t.Id, t.AsignacionId, t.Cantidad))
            .ToList();
    }

    private async Task<IReadOnlyList<CierrePeriodoFilaDto>> CalcularPendientesAsync(int anio, int mes, CancellationToken ct)
        => await ResumirAsync(await TurnosPendientesAsync(anio, mes, null, ct), ct);

    // Agrupa los turnos pendientes por asignacion y resuelve paciente + servicio.
    private async Task<IReadOnlyList<CierrePeriodoFilaDto>> ResumirAsync(List<TurnoPendiente> turnos, CancellationToken ct)
    {
        if (turnos.Count == 0) { return Array.Empty<CierrePeriodoFilaDto>(); }

        var porAsig = turnos.GroupBy(t => t.AsignacionId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));
        var asigIds = porAsig.Keys.ToList();

        var asigs = await (
            from a in _db.Asignaciones.AsNoTracking()
            where asigIds.Contains(a.Id)
            join p in _db.Pacientes.AsNoTracking() on a.PacienteId equals p.Id
            select new
            {
                a.Id, a.PacienteId, a.NombreServicio,
                p.PrimerNombre, p.PrimerApellido, p.TipoDocumento, p.NumeroDocumento
            }).ToListAsync(ct);

        return asigs.Select(a =>
            {
                var nombre = ((a.PrimerNombre ?? "") + " " + (a.PrimerApellido ?? "")).Trim();
                var doc = (a.TipoDocumento + " " + a.NumeroDocumento).Trim();
                return new CierrePeriodoFilaDto(
                    a.PacienteId,
                    string.IsNullOrWhiteSpace(nombre) ? "(sin paciente)" : nombre,
                    string.IsNullOrWhiteSpace(doc) ? null : doc,
                    a.NombreServicio ?? "(sin servicio)",
                    null,
                    porAsig.TryGetValue(a.Id, out var c) ? c : 0);
            })
            .OrderBy(f => f.PacienteNombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
