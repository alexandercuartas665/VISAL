using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Facturacion;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>Paciente candidato para el filtro del cierre (busqueda ligera por nombre/documento).</summary>
public sealed record PacienteCierreBusquedaDto(Guid Id, string Nombre, string? Documento);

/// <summary>Fila del resumen de un cierre/previsualizacion: un servicio con sesiones pendientes.
/// TipoServicio es el tipo de asignacion (TERAPIA, ENFERMERIA, CONSULTA, ...); null en el detalle
/// historico (no se persiste en CierrePeriodoDetalle).</summary>
public sealed record CierrePeriodoFilaDto(
    Guid PacienteId, string PacienteNombre, string? PacienteDocumento,
    string ServicioNombre, string? CodigoServicio, int CantidadPendiente,
    string? TipoServicio = null);

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
    int TotalPacientes = 0, string? UsuarioCerro = null,
    // Sede (nombre) a la que se acoto el cierre (null = todas) y nombre del usuario que cerro.
    string? Sucursal = null, string? UsuarioNombre = null);

public interface ICierrePeriodoService
{
    /// <summary>Resumen de sesiones PENDIENTES que se anularian al cerrar (anio, mes). Si
    /// <paramref name="pacienteId"/>, <paramref name="sede"/> y/o <paramref name="tipo"/>
    /// (tipo de asignacion: TERAPIA, ENFERMERIA, ...) vienen, acota la vista previa
    /// (preparacion liviana); null = sin acotar por ese criterio. No muta.</summary>
    Task<CierrePeriodoPreviewDto> PrevisualizarAsync(int anio, int mes, Guid? pacienteId = null, string? sede = null, string? tipo = null, CancellationToken ct = default);

    /// <summary>Busqueda ligera de pacientes por nombre o documento (para el filtro del cierre,
    /// antes de preparar). Tenant-scoped. Devuelve hasta 15 coincidencias.</summary>
    Task<IReadOnlyList<PacienteCierreBusquedaDto>> BuscarPacientesAsync(string? texto, CancellationToken ct = default);

    /// <summary>Tipos de asignacion (TipoServicio) presentes en las asignaciones del tenant, para el
    /// filtro de cierre (un coordinador cierra solo su tipo). Distinct, no vacios, ordenados.</summary>
    Task<IReadOnlyList<string>> ListarTiposServicioAsync(CancellationToken ct = default);

    /// <summary>Genera el Excel de detalle de un cierre con TODOS los datos del paciente/servicio
    /// (identificacion, EPS, contrato, CUP, profesional, autorizacion, fechas, cantidades). Se
    /// arma en vivo desde los turnos que el cierre anulo. Null si el cierre no existe.</summary>
    Task<ArchivoExportado?> GenerarExcelDetalleAsync(Guid cierreId, CancellationToken ct = default);

    /// <summary>Cierra el periodo: anula las sesiones pendientes (marca sus turnos) y guarda el
    /// resumen. Si <paramref name="pacienteId"/>, <paramref name="sede"/> y/o <paramref name="tipo"/>
    /// vienen, el cierre es PARCIAL (acotado). Lanza si no hay sesiones pendientes que cerrar.
    /// Devuelve el cierre creado.</summary>
    Task<CierrePeriodoDto> CerrarAsync(int anio, int mes, Guid? pacienteId, string? sede, Guid actor, string? tipo = null, CancellationToken ct = default);

    /// <summary>Reabre un periodo cerrado: devuelve las sesiones a pendientes (quita la marca) y
    /// borra el cierre + su resumen.</summary>
    Task<bool> ReabrirAsync(Guid cierreId, Guid actor, CancellationToken ct = default);

    /// <summary>Historial de periodos cerrados (mas reciente primero).</summary>
    Task<IReadOnlyList<CierrePeriodoDto>> ListarCierresAsync(CancellationToken ct = default);

    /// <summary>Detalle (resumen guardado) de un cierre.</summary>
    Task<IReadOnlyList<CierrePeriodoFilaDto>> GetDetalleAsync(Guid cierreId, CancellationToken ct = default);
}

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class CierrePeriodoService : ICierrePeriodoService
{
    private readonly IApplicationDbContextFactory _dbFactory;

    public CierrePeriodoService(IApplicationDbContextFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<CierrePeriodoPreviewDto> PrevisualizarAsync(int anio, int mes, Guid? pacienteId = null, string? sede = null, string? tipo = null, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var yaCerrado = await db.CierrePeriodos.AsNoTracking()
            .AnyAsync(c => c.Anio == anio && c.Mes == mes, ct);

        var filas = await ResumirAsync(db, await TurnosPendientesAsync(db, anio, mes, pacienteId, sede, tipo, ct), ct);
        return new CierrePeriodoPreviewDto(
            anio, mes, yaCerrado, filas,
            filas.Count, filas.Sum(f => f.CantidadPendiente));
    }

    public async Task<IReadOnlyList<PacienteCierreBusquedaDto>> BuscarPacientesAsync(string? texto, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var q = db.Pacientes.AsNoTracking();
        var f = (texto ?? "").Trim().ToLower();
        if (!string.IsNullOrEmpty(f))
        {
            q = q.Where(p => p.NumeroDocumento.ToLower().Contains(f) || p.NombreCompleto.ToLower().Contains(f));
        }
        return await q.OrderBy(p => p.NombreCompleto).Take(15)
            .Select(p => new PacienteCierreBusquedaDto(
                p.Id, p.NombreCompleto,
                ((p.TipoDocumento ?? "") + " " + (p.NumeroDocumento ?? "")).Trim()))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListarTiposServicioAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var tipos = await db.Asignaciones.AsNoTracking()
            .Where(a => a.TipoServicio != null && a.TipoServicio != "")
            .Select(a => a.TipoServicio!)
            .Distinct()
            .ToListAsync(ct);
        return tipos
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CierrePeriodoDto> CerrarAsync(int anio, int mes, Guid? pacienteId, string? sede, Guid actor, string? tipo = null, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        sede = string.IsNullOrWhiteSpace(sede) ? null : sede.Trim();
        tipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim();
        // Turnos pendientes en el periodo (acotados al paciente, sede y/o tipo si vienen). Cerrar es
        // idempotente: los turnos ya anulados por un cierre previo salen del calculo de pendientes,
        // asi que un cierre parcial no choca con otros del mismo periodo (indice no unico).
        var turnosPend = await TurnosPendientesAsync(db, anio, mes, pacienteId, sede, tipo, ct);
        if (turnosPend.Count == 0)
        {
            var filtros = new List<string>();
            if (pacienteId is not null) { filtros.Add("el paciente seleccionado"); }
            if (sede is not null) { filtros.Add($"la sede {sede}"); }
            if (tipo is not null) { filtros.Add($"el tipo {tipo}"); }
            var quien = filtros.Count == 0 ? "" : " para " + string.Join(" y ", filtros);
            throw new InvalidOperationException(
                $"No hay sesiones pendientes que cerrar en {mes:00}/{anio}{quien}.");
        }
        var filas = await ResumirAsync(db, turnosPend, ct);

        // Nombre del paciente (denormalizado) cuando el cierre es parcial.
        string? pacNombre = null;
        if (pacienteId is Guid pid)
        {
            pacNombre = await db.Pacientes.AsNoTracking()
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
            Sucursal = sede,
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
        db.CierrePeriodos.Add(cierre);           // el interceptor estampa TenantId/audit
        await db.SaveChangesAsync(ct);           // necesitamos el Id para marcar los turnos

        // Marcar los turnos pendientes como anulados por este cierre (salen de Atencion).
        var turnoIds = turnosPend.Select(t => t.TurnoId).ToList();
        if (turnoIds.Count > 0)
        {
            var turnos = await db.AsignacionTurnos.Where(t => turnoIds.Contains(t.Id)).ToListAsync(ct);
            foreach (var t in turnos) { t.CierrePeriodoId = cierre.Id; }
            await db.SaveChangesAsync(ct);
        }

        return new CierrePeriodoDto(cierre.Id, cierre.Anio, cierre.Mes, cierre.CerradoEn,
            cierre.TotalServicios, cierre.TotalSesiones, cierre.PacienteId, cierre.PacienteNombre,
            Sucursal: cierre.Sucursal);
    }

    public async Task<bool> ReabrirAsync(Guid cierreId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cierre = await db.CierrePeriodos.FirstOrDefaultAsync(c => c.Id == cierreId, ct);
        if (cierre is null) { return false; }

        // Devolver las sesiones a pendientes.
        var turnos = await db.AsignacionTurnos.Where(t => t.CierrePeriodoId == cierreId).ToListAsync(ct);
        foreach (var t in turnos) { t.CierrePeriodoId = null; }

        db.CierrePeriodos.Remove(cierre);        // cascade borra los detalles
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CierrePeriodoDto>> ListarCierresAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cierres = await db.CierrePeriodos.AsNoTracking()
            .OrderByDescending(c => c.Anio).ThenByDescending(c => c.Mes).ThenByDescending(c => c.CerradoEn)
            .Select(c => new
            {
                c.Id, c.Anio, c.Mes, c.CerradoEn, c.TotalServicios, c.TotalSesiones,
                c.PacienteId, c.PacienteNombre, c.Sucursal, c.CreatedBy
            })
            .ToListAsync(ct);
        if (cierres.Count == 0) { return Array.Empty<CierrePeriodoDto>(); }

        var ids = cierres.Select(c => c.Id).ToList();

        // Cantidad de pacientes DISTINTOS por cierre, desde el detalle guardado (nombre+documento).
        var pacPorCierre = (await db.CierrePeriodoDetalles.AsNoTracking()
                .Where(d => ids.Contains(d.CierrePeriodoId))
                .Select(d => new { d.CierrePeriodoId, d.PacienteNombre, d.PacienteDocumento })
                .ToListAsync(ct))
            .GroupBy(d => d.CierrePeriodoId)
            .ToDictionary(g => g.Key,
                g => g.Select(x => (x.PacienteNombre + "|" + (x.PacienteDocumento ?? "")).Trim())
                      .Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Usuario que cerro: email del tenant_user + nombre del platform_user, por CreatedBy
        // (= platform_user_id del interceptor).
        var userIds = cierres.Where(c => c.CreatedBy is not null).Select(c => c.CreatedBy!.Value).Distinct().ToList();
        var emails = new Dictionary<Guid, string>();
        var nombres = new Dictionary<Guid, string>();
        if (userIds.Count > 0)
        {
            var tus = await db.TenantUsers.AsNoTracking()
                .Where(tu => userIds.Contains(tu.PlatformUserId))
                .Select(tu => new { tu.PlatformUserId, tu.Email })
                .ToListAsync(ct);
            foreach (var tu in tus) { emails[tu.PlatformUserId] = tu.Email; }

            // Nombre legible: DisplayName si existe, si no la concatenacion de nombres/apellidos.
            var pus = await db.PlatformUsers.AsNoTracking()
                .Where(p => userIds.Contains(p.Id))
                .Select(p => new { p.Id, p.DisplayName, p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido })
                .ToListAsync(ct);
            foreach (var p in pus)
            {
                var nombre = !string.IsNullOrWhiteSpace(p.DisplayName)
                    ? p.DisplayName!.Trim()
                    : string.Join(" ", new[] { p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido }
                        .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                if (!string.IsNullOrWhiteSpace(nombre)) { nombres[p.Id] = nombre; }
            }
        }

        return cierres.Select(c => new CierrePeriodoDto(
            c.Id, c.Anio, c.Mes, c.CerradoEn, c.TotalServicios, c.TotalSesiones,
            c.PacienteId, c.PacienteNombre,
            pacPorCierre.TryGetValue(c.Id, out var np) ? np : 0,
            c.CreatedBy is Guid cb && emails.TryGetValue(cb, out var em) ? em : null,
            c.Sucursal,
            c.CreatedBy is Guid cb2 && nombres.TryGetValue(cb2, out var nm) ? nm : null)).ToList();
    }

    public async Task<IReadOnlyList<CierrePeriodoFilaDto>> GetDetalleAsync(Guid cierreId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await db.CierrePeriodoDetalles.AsNoTracking()
            .Where(d => d.CierrePeriodoId == cierreId)
            .OrderBy(d => d.PacienteNombre)
            .Select(d => new CierrePeriodoFilaDto(Guid.Empty, d.PacienteNombre, d.PacienteDocumento, d.ServicioNombre, d.CodigoServicio, d.CantidadPendiente, null))
            .ToListAsync(ct);
    }

    // ── Internos ────────────────────────────────────────────────────────────

    private sealed record TurnoPendiente(Guid TurnoId, Guid AsignacionId, int Cantidad);

    // Turnos pendientes del periodo (anio, mes): asignaciones cuya FECHA DE ASIGNACION
    // (FechaInicio) cae en ese mes, turnos no anulados por un cierre previo, y SIN ninguna
    // sesion completada (atendida y cerrada). Si viene pacienteId y/o sede, acota a las
    // asignaciones de ese paciente / esa sucursal (cierre parcial).
    //
    // El periodo se define por FechaInicio (la columna "Fecha Asig" de la parrilla), el MISMO
    // criterio que usa el filtro de Mes del modulo Atencion. Antes se usaba AnioServicio +
    // (MesFinal ?? MesVigencia), que podia quedar desalineado con lo que Atencion muestra
    // (p.ej. mes_final capturado de mas): una asignacion se veia en un mes pero se cerraba en
    // otro. Se usa rango de fechas por traduccion limpia a SQL e indices.
    private async Task<List<TurnoPendiente>> TurnosPendientesAsync(IApplicationDbContext db, int anio, int mes, Guid? pacienteId, string? sede, string? tipo, CancellationToken ct)
    {
        sede = string.IsNullOrWhiteSpace(sede) ? null : sede.Trim();
        tipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim();
        var primerDia = new DateOnly(anio, mes, 1);
        var primerDiaSiguiente = primerDia.AddMonths(1);

        var asigQ = db.Asignaciones.AsNoTracking()
            .Where(a => a.FechaInicio >= primerDia && a.FechaInicio < primerDiaSiguiente);
        if (pacienteId is Guid pid) { asigQ = asigQ.Where(a => a.PacienteId == pid); }
        if (sede is not null) { asigQ = asigQ.Where(a => a.Sucursal == sede); }
        // Tipo de asignacion (TERAPIA, ENFERMERIA, ...): un coordinador cierra solo su tipo.
        if (tipo is not null) { asigQ = asigQ.Where(a => a.TipoServicio == tipo); }
        var asigIds = await asigQ.Select(a => a.Id).ToListAsync(ct);
        if (asigIds.Count == 0) { return new List<TurnoPendiente>(); }

        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => asigIds.Contains(t.AsignacionId) && t.CierrePeriodoId == null)
            .Select(t => new { t.Id, t.AsignacionId, t.Cantidad })
            .ToListAsync(ct);
        if (turnos.Count == 0) { return new List<TurnoPendiente>(); }

        var turnoIds = turnos.Select(t => t.Id).ToList();
        // Turnos que YA tienen al menos una sesion completada (atendida) -> no son pendientes.
        var turnosConCompletada = (await db.AsignacionTurnoSesiones.AsNoTracking()
                .Where(s => turnoIds.Contains(s.AsignacionTurnoId) && s.Completado)
                .Select(s => s.AsignacionTurnoId)
                .ToListAsync(ct))
            .ToHashSet();

        return turnos
            .Where(t => !turnosConCompletada.Contains(t.Id))
            .Select(t => new TurnoPendiente(t.Id, t.AsignacionId, t.Cantidad))
            .ToList();
    }

    // Agrupa los turnos pendientes por asignacion y resuelve paciente + servicio.
    private async Task<IReadOnlyList<CierrePeriodoFilaDto>> ResumirAsync(IApplicationDbContext db, List<TurnoPendiente> turnos, CancellationToken ct)
    {
        if (turnos.Count == 0) { return Array.Empty<CierrePeriodoFilaDto>(); }

        var porAsig = turnos.GroupBy(t => t.AsignacionId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));
        var asigIds = porAsig.Keys.ToList();

        var asigs = await (
            from a in db.Asignaciones.AsNoTracking()
            where asigIds.Contains(a.Id)
            join p in db.Pacientes.AsNoTracking() on a.PacienteId equals p.Id
            select new
            {
                a.Id, a.PacienteId, a.NombreServicio, a.TipoServicio,
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
                    porAsig.TryGetValue(a.Id, out var c) ? c : 0,
                    string.IsNullOrWhiteSpace(a.TipoServicio) ? null : a.TipoServicio);
            })
            .OrderBy(f => f.PacienteNombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly string[] Meses =
        { "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio",
          "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };

    public async Task<ArchivoExportado?> GenerarExcelDetalleAsync(Guid cierreId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cierre = await db.CierrePeriodos.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cierreId, ct);
        if (cierre is null) { return null; }

        // Detalle en vivo desde los turnos que ESTE cierre anulo (CierrePeriodoId == cierreId).
        // Join a asignacion/paciente/profesional/aseguradora para exponer todos los datos.
        var filas = await (
            from t in db.AsignacionTurnos.AsNoTracking()
            where t.CierrePeriodoId == cierreId
            join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
            join p in db.Pacientes.AsNoTracking() on a.PacienteId equals p.Id
            join prof in db.Profesionales.AsNoTracking() on t.ProfesionalId equals prof.Id into pj
            from prof in pj.DefaultIfEmpty()
            join aseg in db.Aseguradoras.AsNoTracking() on p.AseguradoraId equals aseg.Id into aj
            from aseg in aj.DefaultIfEmpty()
            select new
            {
                a.Id,
                a.ServicioId,
                PacienteNombre = p.NombreCompleto,
                p.TipoDocumento,
                p.NumeroDocumento,
                EpsAsig = a.AseguradoraNombre,
                EpsPac = aseg != null ? aseg.Nombre : null,
                a.ContratoCodigo,
                a.CodigoRips,
                a.NombreServicio,
                a.TipoServicio,
                a.Sucursal,
                a.CodigoAutorizacion,
                CantidadAsignacion = a.Cantidad,
                a.FechaInicio,
                a.FechaFinal,
                TurnoCantidad = t.Cantidad,
                t.NumeroSesion,
                ProfNombre = prof != null ? prof.NombreCompleto : null
            }).ToListAsync(ct);

        // El codigo del servicio (ej. "890105A") y el codigo real del contrato NO siempre viven
        // en la asignacion: en servicios por paquete, Asignacion.CodigoRips queda vacio y
        // ContratoCodigo trae "PAQUETES". Los resolvemos del catalogo (ServicioContrato ->
        // ContratoAseguradora) via Asignacion.ServicioId (GUID del ServicioContrato) y los usamos
        // como respaldo para que el Excel siempre muestre ambos codigos.
        var servGuids = filas
            .Select(x => x.ServicioId)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .Select(s => Guid.TryParse(s, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue).Select(g => g!.Value).ToList();
        var catServicio = new Dictionary<Guid, (string? CodigoServicio, Guid? ContratoId)>();
        var catContrato = new Dictionary<Guid, string?>();
        if (servGuids.Count > 0)
        {
            var scs = await db.ServiciosContrato.AsNoTracking()
                .Where(sc => servGuids.Contains(sc.Id))
                .Select(sc => new { sc.Id, sc.CodigoServicio, sc.ContratoId })
                .ToListAsync(ct);
            foreach (var sc in scs) { catServicio[sc.Id] = (sc.CodigoServicio, sc.ContratoId); }

            var contratoIds = scs.Where(sc => sc.ContratoId != Guid.Empty)
                .Select(sc => sc.ContratoId).Distinct().ToList();
            if (contratoIds.Count > 0)
            {
                var cas = await db.ContratosAseguradora.AsNoTracking()
                    .Where(ca => contratoIds.Contains(ca.Id))
                    .Select(ca => new { ca.Id, ca.CodigoContrato })
                    .ToListAsync(ct);
                foreach (var ca in cas) { catContrato[ca.Id] = ca.CodigoContrato; }
            }
        }

        string? CodServicioDe(string? servicioId, string? codigoRips)
        {
            if (!string.IsNullOrWhiteSpace(codigoRips)) { return codigoRips; }
            if (!string.IsNullOrWhiteSpace(servicioId) && Guid.TryParse(servicioId, out var g)
                && catServicio.TryGetValue(g, out var sc)) { return sc.CodigoServicio; }
            return null;
        }
        string? ContratoDe(string? servicioId, string? contratoCodigo)
        {
            if (!string.IsNullOrWhiteSpace(contratoCodigo)) { return contratoCodigo; }
            if (!string.IsNullOrWhiteSpace(servicioId) && Guid.TryParse(servicioId, out var g)
                && catServicio.TryGetValue(g, out var sc) && sc.ContratoId is Guid cid
                && catContrato.TryGetValue(cid, out var cod)) { return cod; }
            return null;
        }

        // Agrupar por servicio (asignacion) + profesional: una fila por combinacion, con las
        // sesiones inactivadas sumadas y la lista de numeros de sesion.
        var grupos = filas
            .GroupBy(x => new { x.Id, x.ProfNombre })
            .Select(g =>
            {
                var f0 = g.First();
                var nums = g.Where(x => x.NumeroSesion.HasValue)
                            .Select(x => x.NumeroSesion!.Value).OrderBy(n => n).ToList();
                return new
                {
                    f0.PacienteNombre, f0.TipoDocumento, f0.NumeroDocumento,
                    Eps = string.IsNullOrWhiteSpace(f0.EpsAsig) ? f0.EpsPac : f0.EpsAsig,
                    Contrato = ContratoDe(f0.ServicioId, f0.ContratoCodigo),
                    CodServicio = CodServicioDe(f0.ServicioId, f0.CodigoRips),
                    f0.NombreServicio, f0.TipoServicio, f0.Sucursal,
                    f0.CodigoAutorizacion, f0.CantidadAsignacion, f0.FechaInicio, f0.FechaFinal,
                    Profesional = f0.ProfNombre,
                    SesionesInactivadas = g.Sum(x => x.TurnoCantidad),
                    NumerosSesion = string.Join(", ", nums)
                };
            })
            .OrderBy(x => x.PacienteNombre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.NombreServicio, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mesNom = cierre.Mes >= 1 && cierre.Mes <= 12 ? Meses[cierre.Mes] : cierre.Mes.ToString();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Detalle cierre");

        ws.Cell(1, 1).Value = "Detalle del cierre de periodo";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(2, 1).Value = $"Periodo: {mesNom} {cierre.Anio}";
        var alcance = cierre.PacienteNombre is null ? "Todos los pacientes" : cierre.PacienteNombre;
        if (!string.IsNullOrWhiteSpace(cierre.Sucursal)) { alcance += $" · Sede: {cierre.Sucursal}"; }
        ws.Cell(3, 1).Value = $"Alcance: {alcance}";
        ws.Cell(4, 1).Value = $"Total: {grupos.Count} servicio(s) · {grupos.Sum(x => x.SesionesInactivadas)} sesion(es) inactivada(s)";
        ws.Cell(5, 1).Value = $"Cerrado: {cierre.CerradoEn.ToLocalTime():yyyy-MM-dd HH:mm}";
        ws.Cell(6, 1).Value = $"Generado: {DateTimeOffset.Now.ToLocalTime():yyyy-MM-dd HH:mm}";

        const int hdr = 8;
        string[] cols =
        {
            "#", "Paciente", "Tipo doc", "Documento", "EPS", "Contrato", "Cód. servicio",
            "Servicio", "Tipo servicio", "Sede", "Profesional", "Autorizacion",
            "Cant. asignada", "Sesiones inactivadas", "N° sesiones",
            "Vigencia desde", "Vigencia hasta"
        };
        for (var i = 0; i < cols.Length; i++)
        {
            var cell = ws.Cell(hdr, i + 1);
            cell.Value = cols[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var row = hdr + 1;
        var n = 0;
        foreach (var g in grupos)
        {
            n++;
            ws.Cell(row, 1).Value = n;
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(g.PacienteNombre) ? "-" : g.PacienteNombre;
            ws.Cell(row, 3).Value = g.TipoDocumento ?? "";
            ws.Cell(row, 4).Value = g.NumeroDocumento ?? "";
            ws.Cell(row, 4).Style.NumberFormat.Format = "@"; // documento como texto
            ws.Cell(row, 5).Value = g.Eps ?? "";
            ws.Cell(row, 6).Value = g.Contrato ?? "";
            ws.Cell(row, 7).Value = g.CodServicio ?? "";
            ws.Cell(row, 7).Style.NumberFormat.Format = "@"; // codigo como texto
            ws.Cell(row, 8).Value = g.NombreServicio ?? "";
            ws.Cell(row, 9).Value = g.TipoServicio ?? "";
            ws.Cell(row, 10).Value = g.Sucursal ?? "";
            ws.Cell(row, 11).Value = string.IsNullOrWhiteSpace(g.Profesional) ? "(sin profesional)" : g.Profesional;
            ws.Cell(row, 12).Value = g.CodigoAutorizacion ?? "";
            ws.Cell(row, 13).Value = g.CantidadAsignacion;
            ws.Cell(row, 14).Value = g.SesionesInactivadas;
            ws.Cell(row, 15).Value = g.NumerosSesion;
            ws.Cell(row, 15).Style.NumberFormat.Format = "@";
            ws.Cell(row, 16).Value = g.FechaInicio.ToString("yyyy-MM-dd");
            ws.Cell(row, 17).Value = g.FechaFinal.HasValue ? g.FechaFinal.Value.ToString("yyyy-MM-dd") : "";
            row++;
        }

        if (grupos.Count == 0)
        {
            ws.Cell(hdr + 1, 1).Value = "El cierre no tiene turnos anulados (pudo reabrirse).";
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var pacSuffix = cierre.PacienteNombre is null ? "" : "_" + SanitizarNombre(cierre.PacienteNombre);
        var nombre = $"CIERRE_{mesNom}_{cierre.Anio}{pacSuffix}.xlsx";
        return new ArchivoExportado(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombre);
    }

    private static string SanitizarNombre(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        var r = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(r) ? "cierre" : r;
    }
}
