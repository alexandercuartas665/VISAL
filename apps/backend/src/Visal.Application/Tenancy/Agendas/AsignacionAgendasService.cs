using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Agendas;

public sealed class AsignacionAgendasService(
    IApplicationDbContext db,
    IFestivosColombiaService festivos,
    IAsignacionService asignaciones) : IAsignacionAgendasService
{
    public async Task<IReadOnlyList<ServicioConAgendaDto>> ListarServiciosConAgendaAsync(CancellationToken ct = default)
    {
        // Profesionales que tienen agenda propia.
        var profIdsConAgenda = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Select(t => t.ProfesionalId).Distinct().ToListAsync(ct);
        if (profIdsConAgenda.Count == 0) { return Array.Empty<ServicioConAgendaDto>(); }

        // Su tipo de profesional (los que no tienen tipo no cuelgan de ningun servicio).
        var tiposDeProfesConAgenda = await db.Profesionales.AsNoTracking()
            .Where(p => profIdsConAgenda.Contains(p.Id) && p.TipoProfesionalId != null)
            .GroupBy(p => p.TipoProfesionalId!.Value)
            .Select(g => new { TipoId = g.Key, Doctores = g.Count() })
            .ToListAsync(ct);
        if (tiposDeProfesConAgenda.Count == 0) { return Array.Empty<ServicioConAgendaDto>(); }

        var tipoIds = tiposDeProfesConAgenda.Select(x => x.TipoId).ToList();
        var tipoNombres = await db.TiposProfesional.AsNoTracking()
            .Where(t => tipoIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);
        // (nombre de tipo -> doctores con agenda de ese tipo)
        var doctoresPorTipoNombre = tiposDeProfesConAgenda
            .Where(x => tipoNombres.ContainsKey(x.TipoId))
            .GroupBy(x => tipoNombres[x.TipoId])
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Doctores));

        var modulos = await db.CatalogosTipoServicio.AsNoTracking()
            .Where(m => m.Activo).OrderBy(m => m.Orden).ThenBy(m => m.Nombre).ToListAsync(ct);

        var result = new List<ServicioConAgendaDto>();
        foreach (var m in modulos)
        {
            var doctores = doctoresPorTipoNombre
                .Where(kv => TipoMatchModulo(kv.Key, m.Codigo))
                .Sum(kv => kv.Value);
            if (doctores > 0) { result.Add(new ServicioConAgendaDto(m.Codigo, m.Nombre, doctores)); }
        }
        return result;
    }

    public async Task<IReadOnlyList<DoctorConAgendaDto>> ListarDoctoresConAgendaAsync(string moduloCodigo, CancellationToken ct = default)
    {
        var modulo = (moduloCodigo ?? "").Trim();
        if (modulo.Length == 0) { return Array.Empty<DoctorConAgendaDto>(); }

        // Tipos que matchean el modulo (mismo criterio que Coordinacion; sin fallback a "todos").
        var tipos = await db.TiposProfesional.AsNoTracking().Where(t => t.Activo)
            .Select(t => new { t.Id, t.Nombre }).ToListAsync(ct);
        var tipoIdsMatch = tipos.Where(t => TipoMatchModulo(t.Nombre, modulo)).Select(t => t.Id).ToHashSet();
        if (tipoIdsMatch.Count == 0) { return Array.Empty<DoctorConAgendaDto>(); }
        var tipoNombre = tipos.ToDictionary(t => t.Id, t => t.Nombre);

        var profIdsConAgenda = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Select(t => t.ProfesionalId).Distinct().ToListAsync(ct);
        if (profIdsConAgenda.Count == 0) { return Array.Empty<DoctorConAgendaDto>(); }

        var profs = await db.Profesionales.AsNoTracking()
            .Where(p => p.TipoProfesionalId != null && profIdsConAgenda.Contains(p.Id))
            .OrderBy(p => p.NombreCompleto)
            .Select(p => new { p.Id, p.NombreCompleto, p.TipoProfesionalId })
            .ToListAsync(ct);
        profs = profs.Where(p => tipoIdsMatch.Contains(p.TipoProfesionalId!.Value)).ToList();
        if (profs.Count == 0) { return Array.Empty<DoctorConAgendaDto>(); }

        var idsFinal = profs.Select(p => p.Id).ToList();
        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Where(t => idsFinal.Contains(t.ProfesionalId)).ToListAsync(ct);
        var turnosPorProf = turnos.GroupBy(t => t.ProfesionalId)
            .ToDictionary(g => g.Key, g => (Turnos: g.Count(),
                Cupos: g.Sum(t => PlantillaAgendaCalculos.Cupos(t.HoraInicio, t.HoraFin, t.IntervaloMinutos))));

        return profs.Select(p =>
        {
            var r = turnosPorProf.TryGetValue(p.Id, out var v) ? v : (Turnos: 0, Cupos: 0);
            return new DoctorConAgendaDto(p.Id, p.NombreCompleto,
                p.TipoProfesionalId is Guid tid && tipoNombre.TryGetValue(tid, out var tn) ? tn : null,
                r.Turnos, r.Cupos);
        }).ToList();
    }

    public async Task<DisponibilidadAgendaDto> ObtenerDisponibilidadAsync(
        Guid profesionalId, Guid sucursalId, int anioInicio, int mesInicio, int meses = 2, CancellationToken ct = default)
    {
        if (meses < 1) { meses = 1; }
        var primerDia = new DateOnly(anioInicio, mesInicio, 1);
        var ultimoDia = primerDia.AddMonths(meses).AddDays(-1);

        // Cupos por dia de la semana desde la agenda del profesional.
        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Where(t => t.ProfesionalId == profesionalId).ToListAsync(ct);
        var cuposPorDow = turnos.GroupBy(t => t.DiaSemana)
            .ToDictionary(g => g.Key, g => g.Sum(t => PlantillaAgendaCalculos.Cupos(t.HoraInicio, t.HoraFin, t.IntervaloMinutos)));

        // Festivos (por cada anio que toque el rango).
        var festivosMapa = new Dictionary<DateOnly, string>();
        for (var anio = primerDia.Year; anio <= ultimoDia.Year; anio++)
        {
            foreach (var kv in festivos.MapaDeAnio(anio)) { festivosMapa[kv.Key] = kv.Value; }
        }

        // Dias inactivos de la sede.
        var inactivos = sucursalId == Guid.Empty
            ? new Dictionary<DateOnly, string?>()
            : (await db.DiasInactivosSede.AsNoTracking()
                .Where(d => d.SucursalId == sucursalId && d.Fecha >= primerDia && d.Fecha <= ultimoDia)
                .ToListAsync(ct)).ToDictionary(d => d.Fecha, d => d.Motivo);

        // Novedades del profesional que solapan el rango.
        var novedades = await db.NovedadesProfesional.AsNoTracking()
            .Where(n => n.ProfesionalId == profesionalId && n.FechaDesde <= ultimoDia && n.FechaHasta >= primerDia)
            .ToListAsync(ct);

        // Turnos ya asignados al doctor por fecha (para descontar cupos ocupados).
        var asignadosPorFecha = (await db.AsignacionTurnos.AsNoTracking()
                .Where(t => t.ProfesionalId == profesionalId && t.FechaInicio != null
                         && t.FechaInicio >= primerDia && t.FechaInicio <= ultimoDia)
                .GroupBy(t => t.FechaInicio!.Value)
                .Select(g => new { Fecha = g.Key, Ocupados = g.Sum(x => x.Cantidad) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Fecha, x => x.Ocupados);

        string? NovedadDiaCompleto(DateOnly f)
        {
            var n = novedades.FirstOrDefault(x => x.HoraDesde == null && x.HoraHasta == null
                && x.FechaDesde <= f && x.FechaHasta >= f);
            return n is null ? null : (string.IsNullOrWhiteSpace(n.Nota) ? n.Tipo.ToString() : $"{n.Tipo}: {n.Nota}");
        }
        string? NovedadParcial(DateOnly f)
        {
            var n = novedades.FirstOrDefault(x => (x.HoraDesde != null || x.HoraHasta != null)
                && x.FechaDesde <= f && x.FechaHasta >= f);
            return n is null ? null : $"{n.Tipo} {n.HoraDesde:hh\\:mm}-{n.HoraHasta:hh\\:mm}";
        }

        var mesesOut = new List<MesDisponibilidadDto>();
        var cursor = primerDia;
        for (int i = 0; i < meses; i++)
        {
            var anio = cursor.Year; var mes = cursor.Month;
            var finMes = cursor.AddMonths(1).AddDays(-1);
            var dias = new List<DiaDisponibilidadDto>();
            for (var f = cursor; f <= finMes; f = f.AddDays(1))
            {
                var cupos = cuposPorDow.TryGetValue(f.DayOfWeek, out var c) ? c : 0;
                EstadoDiaAgenda estado; int cuposDia = 0; string? detalle = null;
                if (festivosMapa.TryGetValue(f, out var fest)) { estado = EstadoDiaAgenda.Festivo; detalle = fest; }
                else if (inactivos.TryGetValue(f, out var mot)) { estado = EstadoDiaAgenda.Inactivo; detalle = string.IsNullOrWhiteSpace(mot) ? "Dia inactivo" : mot; }
                else if (NovedadDiaCompleto(f) is string nd) { estado = EstadoDiaAgenda.Novedad; detalle = nd; }
                else if (cupos > 0)
                {
                    var ocupados = asignadosPorFecha.TryGetValue(f, out var oc) ? oc : 0;
                    var restantes = cupos - ocupados;
                    if (restantes > 0) { estado = EstadoDiaAgenda.Disponible; cuposDia = restantes; detalle = NovedadParcial(f); }
                    else { estado = EstadoDiaAgenda.Completo; detalle = $"{ocupados}/{cupos} cupos ocupados"; }
                }
                else { estado = EstadoDiaAgenda.SinTurno; }
                dias.Add(new DiaDisponibilidadDto(f, estado, cuposDia, detalle));
            }
            mesesOut.Add(new MesDisponibilidadDto(anio, mes, dias));
            cursor = cursor.AddMonths(1);
        }
        return new DisponibilidadAgendaDto(profesionalId, sucursalId, mesesOut);
    }

    public async Task<IReadOnlyList<TimeOnly>> SlotsDisponiblesAsync(Guid profesionalId, DateOnly fecha, CancellationToken ct = default)
    {
        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Where(t => t.ProfesionalId == profesionalId && t.DiaSemana == fecha.DayOfWeek)
            .OrderBy(t => t.HoraInicio).ToListAsync(ct);
        if (turnos.Count == 0) { return Array.Empty<TimeOnly>(); }

        // Slots ya ocupados por turnos existentes con hora en esa fecha.
        var ocupados = (await db.AsignacionTurnos.AsNoTracking()
                .Where(t => t.ProfesionalId == profesionalId && t.FechaInicio == fecha && t.HoraInicio != null)
                .Select(t => t.HoraInicio!.Value).ToListAsync(ct))
            .ToHashSet();

        var slots = new List<TimeOnly>();
        foreach (var tu in turnos)
        {
            if (tu.IntervaloMinutos <= 0) { continue; }
            var finSpan = tu.HoraFin.ToTimeSpan();
            for (var t = tu.HoraInicio;
                 t.ToTimeSpan().Add(TimeSpan.FromMinutes(tu.IntervaloMinutos)) <= finSpan;
                 t = t.AddMinutes(tu.IntervaloMinutos))
            {
                if (!ocupados.Contains(t)) { slots.Add(t); }
            }
        }
        return slots.Distinct().OrderBy(t => t).ToList();
    }

    public async Task<Guid> AgendarAsync(AgendarDesdeAgendaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.ContratoCodigo)) { throw new InvalidOperationException("Selecciona el contrato del paciente."); }
        if (string.IsNullOrWhiteSpace(req.ServicioContratoId)) { throw new InvalidOperationException("Selecciona el servicio del contrato."); }
        if (string.IsNullOrWhiteSpace(req.ViaIngresoCodigo)) { throw new InvalidOperationException("Selecciona la via de ingreso RIPS."); }
        if (string.IsNullOrWhiteSpace(req.Sucursal)) { throw new InvalidOperationException("Selecciona la sede."); }

        // 1) Crear la Asignacion (lote de 1 item), reusando el flujo estandar.
        var item = new AsignacionItemRequest(
            req.ServicioContratoId, req.NombreServicio, req.TipoServicio, req.Modulo,
            1, null,
            (short)req.Fecha.Year, (short)req.Fecha.Month, null,
            req.Fecha, null,
            req.Observaciones, null,
            RipsViaIngresoCodigo: req.ViaIngresoCodigo, RipsViaIngresoNombre: req.ViaIngresoNombre);
        var lote = await asignaciones.CrearLoteAsync(
            new CrearLoteRequest(req.PacienteId, req.ContratoCodigo, req.Sucursal, new[] { item }), actor, ct);

        // 2) Recuperar la Asignacion recien creada del lote.
        var asigId = await db.Asignaciones.AsNoTracking()
            .Where(a => a.LoteId == lote.LoteId)
            .Select(a => a.Id).FirstAsync(ct);

        // 3) Asignar el doctor con la fecha y la hora del slot (queda Asignado).
        await asignaciones.AsignarServicioAsync(
            new AsignarServicioRequest(asigId, new[]
            {
                new TurnoCoordinadoRequest(req.ProfesionalId, 1, null, req.Fecha, (short)req.Fecha.Month, HoraInicio: req.HoraInicio)
            }), actor, ct);

        return asigId;
    }

    /// <summary>Match tolerante plural/singular entre el nombre de un tipo de profesional
    /// y un codigo de modulo (mismo criterio que AsignacionService).</summary>
    private static bool TipoMatchModulo(string tipoNombre, string moduloCodigo)
    {
        var m = (moduloCodigo ?? "").Trim().ToUpperInvariant();
        var t = (tipoNombre ?? "").Trim().ToUpperInvariant();
        if (m.Length == 0 || t.Length == 0) { return false; }
        var mSin = m.EndsWith("S") ? m[..^1] : m;
        var tSin = t.EndsWith("S") ? t[..^1] : t;
        return t == m || tSin == mSin;
    }
}
