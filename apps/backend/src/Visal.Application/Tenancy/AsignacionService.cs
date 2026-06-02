using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class AsignacionService(IApplicationDbContext db, ITenantContext tenant) : IAsignacionService
{
    public async Task<PacienteAsignacionDto?> GetPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        var p = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null) { return null; }

        // Sede del paciente (nombre)
        string? sedeNombre = null;
        if (p.SedeAtencionId is Guid sid)
        {
            sedeNombre = await db.Sucursales.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Nombre).FirstOrDefaultAsync(ct);
        }

        // Contratos: si tiene aseguradora, sus contratos. Si no, vacio.
        var contratos = new List<ContratoMiniDto>();
        if (p.AseguradoraId is Guid aid)
        {
            contratos = await db.ContratosAseguradora.AsNoTracking()
                .Where(c => c.AseguradoraId == aid)
                .Join(db.Aseguradoras.AsNoTracking(), c => c.AseguradoraId, a => a.Id,
                    (c, a) => new ContratoMiniDto(c.Id, a.Id, a.Nombre, c.CodigoContrato, c.Estado))
                .ToListAsync(ct);
        }

        return new PacienteAsignacionDto(p.Id, p.NumeroDocumento, p.TipoDocumento, p.NombreCompleto,
            sedeNombre, p.Ciudad, contratos,
            p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido,
            p.FechaNacimiento,
            p.Sexo, p.EstadoCivil,
            p.Telefono, p.Email,
            p.Direccion, p.Zona,
            p.Ocupacion, p.Regimen,
            p.ContactoEmergencia, p.Parentesco, p.TelefonoEmergencia);
    }

    public async Task<IReadOnlyList<PacienteAsignacionDto>> BuscarPacientesAsync(string? texto, Guid? contratoId, CancellationToken ct = default)
    {
        var q = db.Pacientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var f = texto.Trim().ToLower();
            q = q.Where(p => p.NumeroDocumento.ToLower().Contains(f) || p.NombreCompleto.ToLower().Contains(f) || (p.Telefono != null && p.Telefono.Contains(f)));
        }
        if (contratoId is Guid cid)
        {
            // Filtrar por aseguradora del contrato.
            var aseguradoraId = await db.ContratosAseguradora.AsNoTracking()
                .Where(c => c.Id == cid)
                .Select(c => (Guid?)c.AseguradoraId)
                .FirstOrDefaultAsync(ct);
            if (aseguradoraId is Guid a)
            {
                q = q.Where(p => p.AseguradoraId == a);
            }
        }
        var lista = await q.OrderBy(p => p.NombreCompleto).Take(50).ToListAsync(ct);
        var result = new List<PacienteAsignacionDto>(lista.Count);
        foreach (var p in lista) { result.Add((await GetPacienteAsync(p.Id, ct))!); }
        return result;
    }

    public async Task<IReadOnlyList<ContratoMiniDto>> ListContratosDisponiblesAsync(CancellationToken ct = default)
    {
        return await db.ContratosAseguradora.AsNoTracking()
            .Where(c => c.Estado == "ACTIVO")
            .Join(db.Aseguradoras.AsNoTracking(), c => c.AseguradoraId, a => a.Id,
                (c, a) => new { c.Id, c.AseguradoraId, AseguradoraNombre = a.Nombre, c.CodigoContrato, c.Estado })
            .OrderBy(x => x.AseguradoraNombre).ThenBy(x => x.CodigoContrato)
            .Select(x => new ContratoMiniDto(x.Id, x.AseguradoraId, x.AseguradoraNombre, x.CodigoContrato, x.Estado))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PacienteFiltroResultadoDto>> BuscarPacientesAvanzadoAsync(BusquedaPacienteFiltro filtro, CancellationToken ct = default)
    {
        var q = db.Pacientes.AsNoTracking().AsQueryable();

        // Filtro por contratos: trae todas las aseguradoras de esos contratos y filtra pacientes por aseguradora.
        if (filtro.ContratoIds is { Count: > 0 } contIds)
        {
            var aseIds = await db.ContratosAseguradora.AsNoTracking()
                .Where(c => contIds.Contains(c.Id))
                .Select(c => c.AseguradoraId).Distinct().ToListAsync(ct);
            if (aseIds.Count == 0) { return Array.Empty<PacienteFiltroResultadoDto>(); }
            q = q.Where(p => p.AseguradoraId != null && aseIds.Contains(p.AseguradoraId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            var d = filtro.Documento.Trim();
            q = q.Where(p => p.NumeroDocumento.Contains(d));
        }
        if (!string.IsNullOrWhiteSpace(filtro.Telefono))
        {
            var t = filtro.Telefono.Trim();
            q = q.Where(p => p.Telefono != null && p.Telefono.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(filtro.Correo))
        {
            var c = filtro.Correo.Trim().ToLower();
            q = q.Where(p => p.Email != null && p.Email.ToLower().Contains(c));
        }
        if (!string.IsNullOrWhiteSpace(filtro.Nombre))
        {
            // Split por espacios: cada token AND LIKE sobre el nombre completo (mismo patron del legacy).
            var tokens = filtro.Nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                var t = raw.ToLower();
                q = q.Where(p => p.NombreCompleto.ToLower().Contains(t));
            }
        }

        var lista = await q.OrderBy(p => p.NombreCompleto).Take(100).ToListAsync(ct);

        // Resolver nombre aseguradora (contrato visible) en una segunda query.
        var aseIdsR = lista.Where(p => p.AseguradoraId is Guid).Select(p => p.AseguradoraId!.Value).Distinct().ToList();
        var aseguradoras = aseIdsR.Count > 0
            ? await db.Aseguradoras.AsNoTracking().Where(a => aseIdsR.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Nombre, ct)
            : new Dictionary<Guid, string>();

        return lista.Select(p => new PacienteFiltroResultadoDto(
            p.Id, p.NumeroDocumento, p.NombreCompleto,
            p.AseguradoraId is Guid aid && aseguradoras.TryGetValue(aid, out var an) ? an : null,
            p.Telefono, p.Email)).ToList();
    }

    public async Task<IReadOnlyList<string>> TiposServicioPorContratoAsync(Guid contratoId, CancellationToken ct = default)
    {
        return await db.ServiciosContrato.AsNoTracking()
            .Where(s => s.ContratoId == contratoId && s.Modulo != null && s.Modulo != "")
            .Select(s => s.Modulo!)
            .Distinct()
            .OrderBy(m => m)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ServicioCatalogoDto>> ServiciosPorContratoAsync(Guid contratoId, string? tipo, CancellationToken ct = default)
    {
        var q = db.ServiciosContrato.AsNoTracking().Where(s => s.ContratoId == contratoId);
        if (!string.IsNullOrWhiteSpace(tipo)) { q = q.Where(s => s.Modulo == tipo); }
        return await q.OrderBy(s => s.Descripcion)
            .Select(s => new ServicioCatalogoDto(
                s.Id, s.CodigoServicio,
                s.Descripcion ?? s.CodigoServicio ?? "(sin descripcion)",
                s.Modulo, s.Especialidad, s.Tarifa,
                s.CodigoInterno, s.Historia, s.Clasificacion, s.Modalidad))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AsignacionMiniDto>> UltimasAsignacionesAsync(Guid pacienteId, int n, CancellationToken ct = default)
    {
        if (n <= 0) { n = 10; }
        return await db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == pacienteId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(n)
            .Select(a => new AsignacionMiniDto(
                a.Id, a.NombreServicio, a.TipoServicio, a.Cantidad,
                a.FechaInicio, a.FechaFinal, a.Estado.ToString(), a.ContratoCodigo, a.CreatedAt,
                a.CodigoAutorizacion, a.AnioServicio, a.MesVigencia, a.MesFinal, a.Observaciones,
                a.ServicioId, a.Modulo))
            .ToListAsync(ct);
    }

    public async Task<LoteCreadoDto> CrearLoteAsync(CrearLoteRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (req.Items is null || req.Items.Count == 0) { throw new InvalidOperationException("El lote no tiene servicios."); }
        foreach (var it in req.Items)
        {
            if (it.Cantidad <= 0) { throw new InvalidOperationException("La cantidad debe ser mayor a cero."); }
            if (it.MesVigencia < 1 || it.MesVigencia > 12) { throw new InvalidOperationException("Mes de vigencia invalido."); }
            if (it.MesFinal is short mf && (mf < 1 || mf > 12)) { throw new InvalidOperationException("Mes final invalido."); }
        }
        // Validar que el paciente exista en el tenant.
        var paciente = await db.Pacientes.FirstOrDefaultAsync(p => p.Id == req.PacienteId, ct)
            ?? throw new InvalidOperationException("Paciente no encontrado en el tenant activo.");

        var lote = new AsignacionLote
        {
            TenantId = tid,
            PacienteId = paciente.Id,
            Sucursal = req.Sucursal,
            ContratoCodigo = req.ContratoCodigo
        };
        db.AsignacionLotes.Add(lote);
        foreach (var it in req.Items)
        {
            db.Asignaciones.Add(new Asignacion
            {
                TenantId = tid,
                Lote = lote,
                PacienteId = paciente.Id,
                Sucursal = req.Sucursal,
                ServicioId = it.ServicioId,
                NombreServicio = it.NombreServicio,
                TipoServicio = it.TipoServicio,
                Modulo = it.Modulo,
                Cantidad = it.Cantidad,
                ContratoCodigo = req.ContratoCodigo,
                CodigoAutorizacion = it.CodigoAutorizacion,
                AnioServicio = it.AnioServicio,
                MesVigencia = it.MesVigencia,
                MesFinal = it.MesFinal,
                FechaInicio = it.FechaInicio,
                FechaFinal = it.FechaFinal,
                Observaciones = it.Observaciones,
                FormatoHistoria = it.FormatoHistoria,
                Estado = AsignacionEstado.Pendiente
            });
        }
        await db.SaveChangesAsync(ct);
        return new LoteCreadoDto(lote.Id, req.Items.Count);
    }

    public async Task<bool> EliminarAsignacionAsync(Guid asignacionId, Guid actor, CancellationToken ct = default)
    {
        var a = await db.Asignaciones.FirstOrDefaultAsync(x => x.Id == asignacionId, ct);
        if (a is null) { return false; }

        // Guarda contra borrar asignaciones que ya estan en uso por Coordinacion. Una
        // asignacion entra en estado Asignado cuando el coordinador le crea turnos; no
        // tiene sentido eliminarla desde /asignacion porque dejaria turnos huerfanos.
        // Tambien evitamos tocar las Cerradas (ya facturadas o terminadas).
        if (a.Estado == AsignacionEstado.Asignado)
        {
            throw new InvalidOperationException(
                "No se puede eliminar: la asignacion ya esta tomada por Coordinacion. Quitala desde alli primero.");
        }
        if (a.Estado == AsignacionEstado.Cerrado)
        {
            throw new InvalidOperationException(
                "No se puede eliminar: la asignacion esta cerrada.");
        }
        // Defensa adicional: si por alguna razon hay turnos asociados pero el estado
        // sigue Pendiente, tampoco la dejamos borrar.
        var tieneTurnos = await db.AsignacionTurnos
            .AnyAsync(t => t.AsignacionId == asignacionId, ct);
        if (tieneTurnos)
        {
            throw new InvalidOperationException(
                "No se puede eliminar: la asignacion tiene turnos creados por Coordinacion.");
        }

        db.Asignaciones.Remove(a);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AsignacionPendienteDto>> ListarPendientesAsync(
        IReadOnlyList<string> modulosPermitidos,
        AsignacionEstadoFiltro estado = AsignacionEstadoFiltro.Pendientes,
        int? anio = null, int? mesVigencia = null,
        string? noOrden = null, string? documentoPaciente = null,
        CancellationToken ct = default)
    {
        // Sin modulos permitidos -> grid vacio (el usuario no es coordinador de ningun modulo).
        if (modulosPermitidos is null || modulosPermitidos.Count == 0)
        {
            return Array.Empty<AsignacionPendienteDto>();
        }

        var permisos = modulosPermitidos.Select(m => m.ToUpperInvariant()).ToList();

        // Asignaciones cuyo Modulo (o TipoServicio como fallback) esta entre los permitidos.
        var q = db.Asignaciones.AsNoTracking()
            .Where(a => (a.Modulo != null && permisos.Contains(a.Modulo.ToUpper()))
                     || permisos.Contains(a.TipoServicio.ToUpper()));

        // Filtro por estado equivalente al cmbEstado del legacy (ctrlCoordinador.ascx.vb).
        switch (estado)
        {
            case AsignacionEstadoFiltro.Pendientes:
                q = q.Where(a => a.Estado == AsignacionEstado.Pendiente);
                break;
            case AsignacionEstadoFiltro.Asignados:
                q = q.Where(a => a.Estado != AsignacionEstado.Pendiente);
                break;
            case AsignacionEstadoFiltro.Todos:
                // sin filtro adicional
                break;
        }

        if (anio is int ay) { q = q.Where(a => a.AnioServicio == (short)ay); }
        if (mesVigencia is int mv && mv >= 1 && mv <= 12) { q = q.Where(a => a.MesVigencia == (short)mv); }
        if (!string.IsNullOrWhiteSpace(noOrden))
        {
            var n = noOrden.Trim();
            q = q.Where(a => a.CodigoAutorizacion != null && a.CodigoAutorizacion.Contains(n));
        }
        if (!string.IsNullOrWhiteSpace(documentoPaciente))
        {
            var d = documentoPaciente.Trim();
            q = q.Where(a => a.Paciente != null && a.Paciente.NumeroDocumento.Contains(d));
        }

        var asigs = await q
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        // Resolver paciente (nombre + documento + tipoDoc) en una segunda query.
        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NumeroDocumento, p.NombreCompleto, p.TipoDocumento })
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        // El "Orden" visible en la grilla es un numero corrido por created_at (mas reciente primero -> 1, 2, ...).
        var orderedById = asigs
            .Select((a, idx) => new { a.Id, Orden = idx + 1 })
            .ToDictionary(x => x.Id, x => x.Orden);

        return asigs.Select(a =>
        {
            pacs.TryGetValue(a.PacienteId, out var p);
            return new AsignacionPendienteDto(
                a.Id,
                orderedById[a.Id],
                p?.NombreCompleto ?? "(sin paciente)",
                p?.NumeroDocumento ?? "",
                p?.TipoDocumento ?? "",
                a.NombreServicio,
                a.Cantidad,
                a.Observaciones,
                a.TipoServicio,
                a.ContratoCodigo,
                a.ServicioId,
                a.FechaInicio,
                a.FechaFinal,
                a.CodigoAutorizacion,
                a.CreatedAt,
                a.Estado.ToString());
        }).ToList();
    }

    public async Task<IReadOnlyList<EspecialistaDto>> ListarEspecialistasPorModuloAsync(string modulo, CancellationToken ct = default)
    {
        // Filtro por tipo de profesional cuyo Nombre haga match (case-insensitive) con el modulo.
        var moduloUpper = (modulo ?? "").Trim().ToUpperInvariant();
        if (moduloUpper.Length == 0) { return Array.Empty<EspecialistaDto>(); }

        // 1) tipos que matchean por nombre
        var tipoIdsMatch = await db.TiposProfesional.AsNoTracking()
            .Where(t => t.Activo)
            .Where(t => t.Nombre.ToUpper() == moduloUpper)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var qProf = db.Profesionales.AsNoTracking().AsQueryable();

        // Si hay tipo coincidente, filtrar; si no, devolver TODOS los profesionales del tenant.
        if (tipoIdsMatch.Count > 0)
        {
            qProf = qProf.Where(p => p.TipoProfesionalId != null && tipoIdsMatch.Contains(p.TipoProfesionalId.Value));
        }

        var lista = await qProf.OrderBy(p => p.NombreCompleto).Take(500).ToListAsync(ct);

        // Resolver nombre del tipo para la columna del dropdown.
        var tipoIds = lista.Where(p => p.TipoProfesionalId != null).Select(p => p.TipoProfesionalId!.Value).Distinct().ToList();
        var tiposDict = tipoIds.Count > 0
            ? await db.TiposProfesional.AsNoTracking().Where(t => tipoIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct)
            : new Dictionary<Guid, string>();

        return lista.Select(p => new EspecialistaDto(
            p.Id,
            p.NumeroDocumento,
            p.NombreCompleto,
            p.TipoProfesionalId is Guid tid && tiposDict.TryGetValue(tid, out var tn) ? tn : null)).ToList();
    }

    public async Task<IReadOnlyList<TurnoCoordinadoDto>> ListarTurnosAsync(Guid asignacionId, CancellationToken ct = default)
    {
        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.AsignacionId == asignacionId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        if (turnos.Count == 0) { return Array.Empty<TurnoCoordinadoDto>(); }

        var profIds = turnos.Select(t => t.ProfesionalId).Distinct().ToList();
        var profDict = await db.Profesionales.AsNoTracking()
            .Where(p => profIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        return turnos.Select(t => new TurnoCoordinadoDto(
            t.Id, t.ProfesionalId,
            profDict.TryGetValue(t.ProfesionalId, out var n) ? n : "(desconocido)",
            t.Cantidad, t.HorasPorTurno, t.FechaInicio, t.MesAsignar)).ToList();
    }

    public async Task<int> AsignarServicioAsync(AsignarServicioRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (req.Turnos is null || req.Turnos.Count == 0) { throw new InvalidOperationException("Debe agregar al menos un turno antes de asignar."); }

        var asig = await db.Asignaciones.FirstOrDefaultAsync(a => a.Id == req.AsignacionId, ct)
            ?? throw new InvalidOperationException("Asignacion no encontrada.");

        // Validaciones por turno + suma total.
        var sumaNueva = 0;
        foreach (var t in req.Turnos)
        {
            if (t.Cantidad <= 0) { throw new InvalidOperationException("Cada turno debe tener cantidad > 0."); }
            if (t.ProfesionalId == Guid.Empty) { throw new InvalidOperationException("Cada turno debe tener profesional."); }
            sumaNueva += t.Cantidad;
        }

        // Sumar turnos ya existentes para no exceder la cantidad de la asignacion.
        var sumaExistente = await db.AsignacionTurnos
            .Where(x => x.AsignacionId == req.AsignacionId)
            .SumAsync(x => (int?)x.Cantidad, ct) ?? 0;

        var totalProyectado = sumaExistente + sumaNueva;
        if (totalProyectado > asig.Cantidad)
        {
            throw new InvalidOperationException(
                $"La suma de turnos ({totalProyectado}) supera la cantidad del servicio ({asig.Cantidad}).");
        }

        // Insertar los nuevos turnos.
        foreach (var t in req.Turnos)
        {
            db.AsignacionTurnos.Add(new AsignacionTurno
            {
                TenantId = tid,
                AsignacionId = req.AsignacionId,
                ProfesionalId = t.ProfesionalId,
                Cantidad = t.Cantidad,
                HorasPorTurno = t.HorasPorTurno,
                FechaInicio = t.FechaInicio,
                MesAsignar = t.MesAsignar
            });
        }

        // Si la suma total iguala la cantidad del servicio, marcar como Asignado.
        if (totalProyectado == asig.Cantidad)
        {
            asig.Estado = AsignacionEstado.Asignado;
        }

        await db.SaveChangesAsync(ct);
        return req.Turnos.Count;
    }
}
