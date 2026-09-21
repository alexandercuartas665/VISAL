using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

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
        var hoy = DateOnly.FromDateTime(DateTime.Today);

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
                // Dias anteriores a hoy no se pueden agendar.
                if (f < hoy) { dias.Add(new DiaDisponibilidadDto(f, EstadoDiaAgenda.SinTurno, 0, "Pasado")); continue; }
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

    public async Task<IReadOnlyList<ServicioContratoAgendaDto>> ListarServiciosContratoConAgendaAsync(Guid contratoId, string? filtro, CancellationToken ct = default)
    {
        if (contratoId == Guid.Empty) { return Array.Empty<ServicioContratoAgendaDto>(); }

        var profIdsConAgenda = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Select(t => t.ProfesionalId).Distinct().ToListAsync(ct);
        if (profIdsConAgenda.Count == 0) { return Array.Empty<ServicioContratoAgendaDto>(); }

        // CUPS que presta cada doctor con agenda -> doctores por codigo.
        var prov = await db.ProfesionalServicios.AsNoTracking()
            .Where(s => profIdsConAgenda.Contains(s.ProfesionalId))
            .Select(s => new { s.Codigo, s.ProfesionalId })
            .ToListAsync(ct);
        if (prov.Count == 0) { return Array.Empty<ServicioContratoAgendaDto>(); }
        var doctoresPorCodigo = prov.GroupBy(x => x.Codigo)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProfesionalId).Distinct().Count());
        var codigos = doctoresPorCodigo.Keys.ToList();

        var f = (filtro ?? "").Trim().ToLowerInvariant();
        var q = db.ServiciosContrato.AsNoTracking()
            .Where(s => s.ContratoId == contratoId && s.CodigoServicio != null && codigos.Contains(s.CodigoServicio));
        if (f.Length > 0)
        {
            q = q.Where(s => s.CodigoServicio!.ToLower().Contains(f) || s.Descripcion.ToLower().Contains(f));
        }
        var rows = await q.ToListAsync(ct);

        // Deduplicar por CUPS: el contrato suele traer muchas filas con el mismo
        // codigo_servicio; para el explorador basta una por codigo.
        return rows
            .GroupBy(s => s.CodigoServicio)
            .Select(g => g.OrderBy(s => s.Descripcion).First())
            .OrderBy(s => s.Descripcion)
            .Take(200)
            .Select(s => new ServicioContratoAgendaDto(
                s.Id, s.CodigoServicio, s.Descripcion, s.Modulo, s.Especialidad,
                s.CodigoServicio != null && doctoresPorCodigo.TryGetValue(s.CodigoServicio, out var d) ? d : 0))
            .ToList();
    }

    public async Task<IReadOnlyList<DoctorConAgendaDto>> ListarDoctoresPorServicioContratoAsync(Guid servicioContratoId, CancellationToken ct = default)
    {
        var sc = await db.ServiciosContrato.AsNoTracking().FirstOrDefaultAsync(s => s.Id == servicioContratoId, ct);
        if (sc?.CodigoServicio is not string cups || cups.Length == 0) { return Array.Empty<DoctorConAgendaDto>(); }

        var profIdsConAgenda = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Select(t => t.ProfesionalId).Distinct().ToListAsync(ct);
        var proveedores = await db.ProfesionalServicios.AsNoTracking()
            .Where(s => s.Codigo == cups && profIdsConAgenda.Contains(s.ProfesionalId))
            .Select(s => s.ProfesionalId).Distinct().ToListAsync(ct);
        if (proveedores.Count == 0) { return Array.Empty<DoctorConAgendaDto>(); }

        var profs = await db.Profesionales.AsNoTracking()
            .Where(p => proveedores.Contains(p.Id))
            .OrderBy(p => p.NombreCompleto)
            .Select(p => new { p.Id, p.NombreCompleto, p.TipoProfesionalId })
            .ToListAsync(ct);
        var tipoNombre = await db.TiposProfesional.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Where(t => proveedores.Contains(t.ProfesionalId)).ToListAsync(ct);
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

    public async Task<IReadOnlyList<DoctorDisponibilidadDto>> ListarDoctoresConDisponibilidadAsync(
        Guid servicioContratoId, Guid sucursalId, int anioInicio, int mesInicio, int meses = 2, CancellationToken ct = default)
    {
        if (meses < 1) { meses = 1; }
        var doctores = await ListarDoctoresPorServicioContratoAsync(servicioContratoId, ct);
        if (doctores.Count == 0) { return Array.Empty<DoctorDisponibilidadDto>(); }
        var ids = doctores.Select(d => d.ProfesionalId).ToList();

        var primerDia = new DateOnly(anioInicio, mesInicio, 1);
        var ultimoDia = primerDia.AddMonths(meses).AddDays(-1);
        var hoyCard = DateOnly.FromDateTime(DateTime.Today);

        // Datos en bloque para todos los doctores.
        var turnos = await db.AgendaProfesionalTurnos.AsNoTracking()
            .Where(t => ids.Contains(t.ProfesionalId)).ToListAsync(ct);
        var cuposPorProfDow = turnos.GroupBy(t => t.ProfesionalId)
            .ToDictionary(g => g.Key, g => g.GroupBy(t => t.DiaSemana)
                .ToDictionary(gg => gg.Key, gg => gg.Sum(t => PlantillaAgendaCalculos.Cupos(t.HoraInicio, t.HoraFin, t.IntervaloMinutos))));

        var novedades = await db.NovedadesProfesional.AsNoTracking()
            .Where(n => ids.Contains(n.ProfesionalId) && n.HoraDesde == null && n.HoraHasta == null
                     && n.FechaDesde <= ultimoDia && n.FechaHasta >= primerDia)
            .ToListAsync(ct);
        var novPorProf = novedades.GroupBy(n => n.ProfesionalId).ToDictionary(g => g.Key, g => g.ToList());

        var asignados = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => ids.Contains(t.ProfesionalId) && t.FechaInicio != null
                     && t.FechaInicio >= primerDia && t.FechaInicio <= ultimoDia)
            .GroupBy(t => new { t.ProfesionalId, Fecha = t.FechaInicio!.Value })
            .Select(g => new { g.Key.ProfesionalId, g.Key.Fecha, Ocupados = g.Sum(x => x.Cantidad) })
            .ToListAsync(ct);
        var ocupPorProfFecha = asignados.ToDictionary(x => (x.ProfesionalId, x.Fecha), x => x.Ocupados);

        var festivosMapa = new Dictionary<DateOnly, string>();
        for (var anio = primerDia.Year; anio <= ultimoDia.Year; anio++)
        {
            foreach (var kv in festivos.MapaDeAnio(anio)) { festivosMapa[kv.Key] = kv.Value; }
        }
        var inactivos = sucursalId == Guid.Empty
            ? new HashSet<DateOnly>()
            : (await db.DiasInactivosSede.AsNoTracking()
                .Where(d => d.SucursalId == sucursalId && d.Fecha >= primerDia && d.Fecha <= ultimoDia)
                .Select(d => d.Fecha).ToListAsync(ct)).ToHashSet();

        var res = new List<DoctorDisponibilidadDto>();
        foreach (var d in doctores)
        {
            var dow = cuposPorProfDow.TryGetValue(d.ProfesionalId, out var m) ? m : new();
            var novs = novPorProf.TryGetValue(d.ProfesionalId, out var nl) ? nl : new List<NovedadProfesional>();
            int diasDisp = 0, cuposLibres = 0;
            for (var f = primerDia; f <= ultimoDia; f = f.AddDays(1))
            {
                if (f < hoyCard) { continue; }
                if (festivosMapa.ContainsKey(f) || inactivos.Contains(f)) { continue; }
                if (novs.Any(n => n.FechaDesde <= f && n.FechaHasta >= f)) { continue; }
                var cupos = dow.TryGetValue(f.DayOfWeek, out var c) ? c : 0;
                if (cupos <= 0) { continue; }
                var ocup = ocupPorProfFecha.TryGetValue((d.ProfesionalId, f), out var o) ? o : 0;
                var rest = cupos - ocup;
                if (rest > 0) { diasDisp++; cuposLibres += rest; }
            }
            res.Add(new DoctorDisponibilidadDto(d.ProfesionalId, d.NombreCompleto, d.TipoProfesional,
                diasDisp, cuposLibres, diasDisp > 0));
        }
        return res;
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

        // No se puede agendar en el pasado.
        if (req.Fecha < DateOnly.FromDateTime(DateTime.Today))
        { throw new InvalidOperationException("No se puede agendar en una fecha anterior a hoy."); }

        // El paciente no puede tener otra cita a la misma fecha y hora (con cualquier doctor).
        var choquePaciente = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.FechaInicio == req.Fecha && t.HoraInicio == req.HoraInicio)
            .Join(db.Asignaciones.AsNoTracking(), t => t.AsignacionId, a => a.Id, (t, a) => a.PacienteId)
            .AnyAsync(pid => pid == req.PacienteId, ct);
        if (choquePaciente)
        { throw new InvalidOperationException($"El paciente ya tiene una cita el {req.Fecha:dd/MM/yyyy} a las {req.HoraInicio:HH\\:mm}."); }

        var cantidad = req.Cantidad <= 0 ? 1 : req.Cantidad;
        var cantTurnos = req.CantidadTurnos <= 0 ? 1 : req.CantidadTurnos;
        if (cantTurnos > cantidad) { cantTurnos = cantidad; }
        var anio = req.AnioServicio ?? (short)req.Fecha.Year;
        var mesVig = req.MesVigencia ?? (short)req.Fecha.Month;

        // 1) Crear la Asignacion (lote de 1 item) con todas las piezas, reusando el flujo estandar.
        var item = new AsignacionItemRequest(
            req.ServicioContratoId, req.NombreServicio, req.TipoServicio, req.Modulo,
            cantidad, string.IsNullOrWhiteSpace(req.CodigoAutorizacion) ? null : req.CodigoAutorizacion.Trim(),
            anio, mesVig, req.MesFinal,
            req.Fecha, req.FechaFinal,
            req.Observaciones, null,
            RipsViaIngresoCodigo: req.ViaIngresoCodigo, RipsViaIngresoNombre: req.ViaIngresoNombre);
        var lote = await asignaciones.CrearLoteAsync(
            new CrearLoteRequest(req.PacienteId, req.ContratoCodigo, req.Sucursal, new[] { item },
                PdfAutorizacionUrl: null,
                TipoPago: string.IsNullOrWhiteSpace(req.TipoPago) ? null : req.TipoPago,
                CategoriaCopago: string.IsNullOrWhiteSpace(req.CategoriaCopago) ? null : req.CategoriaCopago,
                ValorPagoSugerido: req.ValorPagoSugerido, ValorPagoReal: req.ValorPagoReal,
                AutorizacionPendiente: req.AutorizacionPendiente), actor, ct);

        // 2) Recuperar la Asignacion recien creada del lote.
        var asigId = await db.Asignaciones.AsNoTracking()
            .Where(a => a.LoteId == lote.LoteId)
            .Select(a => a.Id).FirstAsync(ct);

        // 3) Coordinar el/los turno(s) del doctor con la fecha y la hora del slot. Si los turnos
        //    igualan la cantidad, la Asignacion queda Asignado (como en Coordinacion).
        await asignaciones.AsignarServicioAsync(
            new AsignarServicioRequest(asigId, new[]
            {
                new TurnoCoordinadoRequest(req.ProfesionalId, cantTurnos, null, req.Fecha, mesVig, HoraInicio: req.HoraInicio)
            }), actor, ct);

        return asigId;
    }

    public async Task<IReadOnlyList<CitaDelDiaDto>> ListarCitasDelDiaAsync(Guid profesionalId, DateOnly fecha, CancellationToken ct = default)
    {
        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.ProfesionalId == profesionalId && t.FechaInicio == fecha)
            .ToListAsync(ct);
        if (turnos.Count == 0) { return Array.Empty<CitaDelDiaDto>(); }

        var asigIds = turnos.Select(t => t.AsignacionId).Distinct().ToList();
        var asigs = await db.Asignaciones.AsNoTracking()
            .Where(a => asigIds.Contains(a.Id))
            .Select(a => new { a.Id, a.PacienteId, a.NombreServicio, a.Estado })
            .ToListAsync(ct);
        var asigById = asigs.ToDictionary(a => a.Id);
        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();
        var pacNombre = await db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        return turnos
            .OrderBy(t => t.HoraInicio ?? TimeOnly.MaxValue)
            .Select(t =>
            {
                asigById.TryGetValue(t.AsignacionId, out var a);
                var pac = a is not null && pacNombre.TryGetValue(a.PacienteId, out var n) ? n : null;
                return new CitaDelDiaDto(t.Id, t.HoraInicio, pac,
                    a?.NombreServicio ?? "", a?.Estado.ToString() ?? "");
            }).ToList();
    }

    public async Task<bool> CancelarCitaAsync(Guid asignacionTurnoId, Guid actor, CancellationToken ct = default)
    {
        var turno = await db.AsignacionTurnos.FirstOrDefaultAsync(t => t.Id == asignacionTurnoId, ct);
        if (turno is null) { return false; }
        var asigId = turno.AsignacionId;
        db.AsignacionTurnos.Remove(turno);
        await db.SaveChangesAsync(ct);

        var restantes = await db.AsignacionTurnos.CountAsync(t => t.AsignacionId == asigId, ct);
        if (restantes == 0)
        {
            var asig = await db.Asignaciones.FirstOrDefaultAsync(a => a.Id == asigId, ct);
            if (asig is not null)
            {
                var loteId = asig.LoteId;
                db.Asignaciones.Remove(asig);
                await db.SaveChangesAsync(ct);
                var enLote = await db.Asignaciones.CountAsync(a => a.LoteId == loteId, ct);
                if (enLote == 0)
                {
                    var lote = await db.AsignacionLotes.FirstOrDefaultAsync(l => l.Id == loteId, ct);
                    if (lote is not null) { db.AsignacionLotes.Remove(lote); await db.SaveChangesAsync(ct); }
                }
            }
        }
        else
        {
            // Aun quedan turnos: la asignacion ya no esta completa -> vuelve a Pendiente.
            var asig = await db.Asignaciones.FirstOrDefaultAsync(a => a.Id == asigId, ct);
            if (asig is not null && asig.Estado == AsignacionEstado.Asignado)
            {
                asig.Estado = AsignacionEstado.Pendiente;
                await db.SaveChangesAsync(ct);
            }
        }
        return true;
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
