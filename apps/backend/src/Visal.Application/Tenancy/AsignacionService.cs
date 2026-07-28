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

        // Contratos: unica fuente de verdad es la tabla N:M paciente_contratos
        // ordenada por Orden. El orden=1 es el default que auto-selecciona
        // la UI de /asignacion. Post-PC4 los slots viejos ya no existen.
        var idsOrdenados = await db.PacienteContratos.AsNoTracking()
            .Where(pc => pc.PacienteId == p.Id)
            .OrderBy(pc => pc.Orden)
            .Select(pc => pc.ContratoAseguradoraId)
            .ToArrayAsync(ct);
        var contratos = new List<ContratoMiniDto>();
        if (idsOrdenados.Length > 0)
        {
            var lookup = await db.ContratosAseguradora.AsNoTracking()
                .Where(c => idsOrdenados.Contains(c.Id))
                .Join(db.Aseguradoras.AsNoTracking(), c => c.AseguradoraId, a => a.Id,
                    (c, a) => new ContratoMiniDto(c.Id, a.Id, a.Nombre, c.CodigoContrato, c.Estado, c.RequierePdfAutorizacion))
                .ToDictionaryAsync(c => c.ContratoId, ct);
            // Preserva el orden real (orden 1..N de paciente_contratos, o slots 1/2/3).
            foreach (var cid in idsOrdenados)
            {
                if (lookup.TryGetValue(cid, out var dto)) { contratos.Add(dto); }
            }
        }

        // EPS principal: el nombre de la aseguradora vinculada al paciente.
        string? epsNombre = null;
        if (p.AseguradoraId is Guid aid)
        {
            epsNombre = await db.Aseguradoras.AsNoTracking()
                .Where(a => a.Id == aid)
                .Select(a => a.Nombre)
                .FirstOrDefaultAsync(ct);
        }

        // Resolver los 5 FK a catalogos_paciente en 1 sola query batch. Cada uno
        // apunta a la misma tabla, solo cambia el Tipo — con un IN sobre los ids
        // no-nulos + un lookup Dictionary evitamos 5 idas separadas a la BD.
        var catalogoIds = new[] {
                p.TipoUsuarioId, p.ClasificacionPacienteId, p.ClasificacionGrupoPatologiaId,
                p.TipoTutelaId, p.MedContratadoId
            }
            .Where(g => g is not null).Select(g => g!.Value).ToArray();
        var catalogoLookup = catalogoIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.CatalogosPaciente.AsNoTracking()
                .Where(c => catalogoIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        string? ResolverCatalogo(Guid? id) =>
            id is Guid g && catalogoLookup.TryGetValue(g, out var nombre) ? nombre : null;

        return new PacienteAsignacionDto(p.Id, p.NumeroDocumento, p.TipoDocumento, p.NombreCompleto,
            sedeNombre, p.Ciudad, contratos,
            p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido,
            p.FechaNacimiento,
            p.Sexo, p.EstadoCivil,
            p.Telefono, p.CodigoPaisTelefono, p.Email,
            p.Direccion, p.Zona,
            p.Ocupacion, p.Regimen,
            p.ContactoEmergencia, p.Parentesco, p.TelefonoEmergencia,
            epsNombre,
            p.EstadoAdmision,
            // Campos ampliados: raw + fechas + FKs resueltos.
            p.Barrio, p.Incapacidad, p.GrupoRh, p.Estado, p.EstratoSocial, p.Tutela,
            p.CodigoAceptacion, p.Cie10Codigo, p.DiagnosticoPrincipal,
            p.FechaComentan, p.FechaIngresoPad, p.FechaEgresoPad,
            ResolverCatalogo(p.TipoUsuarioId),
            ResolverCatalogo(p.ClasificacionPacienteId),
            ResolverCatalogo(p.ClasificacionGrupoPatologiaId),
            ResolverCatalogo(p.TipoTutelaId),
            ResolverCatalogo(p.MedContratadoId));
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
                (c, a) => new { c.Id, c.AseguradoraId, AseguradoraNombre = a.Nombre, c.CodigoContrato, c.Estado, c.RequierePdfAutorizacion })
            .OrderBy(x => x.AseguradoraNombre).ThenBy(x => x.CodigoContrato)
            .Select(x => new ContratoMiniDto(x.Id, x.AseguradoraId, x.AseguradoraNombre, x.CodigoContrato, x.Estado, x.RequierePdfAutorizacion))
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
                s.CodigoInterno, s.Historia, s.Clasificacion, s.Modalidad,
                s.PaqueteId,
                s.PaqueteId != null ? db.Paquetes.Where(p => p.Id == s.PaqueteId).Select(p => p.Codigo).FirstOrDefault() : null))
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
        // Validacion de PDF obligatorio segun contrato.
        var contrato = await db.ContratosAseguradora.FirstOrDefaultAsync(
            c => c.CodigoContrato == req.ContratoCodigo, ct);
        if (contrato is not null && contrato.RequierePdfAutorizacion
            && string.IsNullOrWhiteSpace(req.PdfAutorizacionUrl))
        {
            throw new InvalidOperationException(
                $"El contrato {req.ContratoCodigo} exige adjuntar el PDF de autorizacion antes de guardar.");
        }

        // Defensa server-side de la regla "una sola fila lleva el valor pactado":
        // El frontend YA arma el request con el valor solo en el primer chip con
        // Cantidad>0 por PaqueteInstanciaId. Aqui re-validamos y forzamos la regla
        // por si algun cliente enviase datos inconsistentes (bug o UI vieja).
        var yaAsignadoValor = new HashSet<Guid>();
        foreach (var it in req.Items)
        {
            var valorPactado = it.PaqueteValorPactado;
            if (it.PaqueteInstanciaId is Guid pid)
            {
                if (yaAsignadoValor.Contains(pid) || it.Cantidad <= 0)
                {
                    valorPactado = null; // solo el primero con Cantidad>0 conserva el valor
                }
                else if (valorPactado is not null)
                {
                    yaAsignadoValor.Add(pid);
                }
            }
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
                PdfAutorizacionUrl = req.PdfAutorizacionUrl,
                TipoPago = req.TipoPago,
                CategoriaCopago = req.CategoriaCopago,
                ValorPagoSugerido = req.ValorPagoSugerido,
                ValorPagoReal = req.ValorPagoReal,
                Estado = AsignacionEstado.Pendiente,
                PaqueteInstanciaId = it.PaqueteInstanciaId,
                PaqueteCodigo = it.PaqueteCodigo,
                PaqueteValorPactado = valorPactado
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

    public async Task<bool> ActualizarAsignacionAsync(ActualizarAsignacionRequest req, Guid actor, CancellationToken ct = default)
    {
        var a = await db.Asignaciones.FirstOrDefaultAsync(x => x.Id == req.AsignacionId, ct);
        if (a is null) { return false; }

        // Solo se puede editar mientras esta Pendiente (no tomada por Coordinacion ni cerrada).
        // Mismo criterio que EliminarAsignacionAsync para mantener el modelo simple.
        if (a.Estado == AsignacionEstado.Asignado)
        {
            throw new InvalidOperationException(
                "No se puede editar: la asignacion ya esta tomada por Coordinacion.");
        }
        if (a.Estado == AsignacionEstado.Cerrado)
        {
            throw new InvalidOperationException(
                "No se puede editar: la asignacion esta cerrada.");
        }
        var tieneTurnos = await db.AsignacionTurnos.AnyAsync(t => t.AsignacionId == a.Id, ct);
        if (tieneTurnos)
        {
            throw new InvalidOperationException(
                "No se puede editar: la asignacion tiene turnos creados por Coordinacion.");
        }

        // Validaciones basicas (mismo criterio que CrearLote).
        if (req.Cantidad <= 0) { throw new InvalidOperationException("La cantidad debe ser mayor a cero."); }
        if (req.MesVigencia < 1 || req.MesVigencia > 12) { throw new InvalidOperationException("Mes de vigencia invalido."); }
        if (req.MesFinal is short mf && (mf < 1 || mf > 12)) { throw new InvalidOperationException("Mes final invalido."); }

        a.ServicioId = req.ServicioId;
        a.NombreServicio = req.NombreServicio;
        a.TipoServicio = req.TipoServicio;
        a.Modulo = req.Modulo;
        a.Cantidad = req.Cantidad;
        a.ContratoCodigo = req.ContratoCodigo;
        a.CodigoAutorizacion = req.CodigoAutorizacion;
        a.AnioServicio = req.AnioServicio;
        a.MesVigencia = req.MesVigencia;
        a.MesFinal = req.MesFinal;
        a.FechaInicio = req.FechaInicio;
        a.FechaFinal = req.FechaFinal;
        a.Observaciones = req.Observaciones;
        a.FormatoHistoria = req.FormatoHistoria;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AsignacionListadoDto>> ListarAsignacionesAsync(AsignacionListadoFiltro filtro, CancellationToken ct = default)
    {
        // Join principal: asignaciones + paciente (in-tenant por query filter global).
        var q = from a in db.Asignaciones.AsNoTracking()
                join p in db.Pacientes.AsNoTracking() on a.PacienteId equals p.Id
                select new { a, p };

        if (filtro.FechaInicial is DateOnly fi) { q = q.Where(x => x.a.FechaInicio >= fi); }
        if (filtro.FechaFinal is DateOnly ff) { q = q.Where(x => x.a.FechaInicio <= ff); }
        if (filtro.PacienteId is Guid pid) { q = q.Where(x => x.p.Id == pid); }
        if (!string.IsNullOrWhiteSpace(filtro.ContratoCodigo))
        {
            var cc = filtro.ContratoCodigo.Trim().ToLower();
            q = q.Where(x => x.a.ContratoCodigo.ToLower() == cc);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Modulo))
        {
            // Match tolerante a plural/singular final "s" (TS8). El catalogo
            // canonico es singular (TERAPIA, SERVICIO...) pero registros
            // legacy y del import Excel pueden traer plural. Comparo ambas
            // formas quitando la "s" final para que el filtro no pierda
            // datos por diferencia trivial de sufijo.
            var m = filtro.Modulo.Trim().ToLower();
            var mSin = m.EndsWith("s") ? m[..^1] : m;
            q = q.Where(x =>
                (x.a.Modulo != null && (
                    x.a.Modulo.ToLower() == m ||
                    x.a.Modulo.ToLower() == mSin ||
                    (x.a.Modulo.ToLower().EndsWith("s") ? x.a.Modulo.ToLower().Substring(0, x.a.Modulo.Length - 1) : x.a.Modulo.ToLower()) == mSin))
                || x.a.TipoServicio.ToLower() == m
                || x.a.TipoServicio.ToLower() == mSin
                || (x.a.TipoServicio.ToLower().EndsWith("s") ? x.a.TipoServicio.ToLower().Substring(0, x.a.TipoServicio.Length - 1) : x.a.TipoServicio.ToLower()) == mSin);
        }
        if (!string.IsNullOrWhiteSpace(filtro.NombreServicio))
        {
            var ns = filtro.NombreServicio.Trim().ToLower();
            q = q.Where(x => x.a.NombreServicio.ToLower().Contains(ns));
        }

        // Filtro por aseguradora: se hace via contrato_codigo -> ContratoAseguradora -> AseguradoraId.
        if (filtro.AseguradoraId is Guid ase)
        {
            var codigosContrato = db.ContratosAseguradora.AsNoTracking()
                .Where(c => c.AseguradoraId == ase)
                .Select(c => c.CodigoContrato.ToLower());
            q = q.Where(x => codigosContrato.Contains(x.a.ContratoCodigo.ToLower()));
        }

        var rows = await q
            .OrderByDescending(x => x.a.CreatedAt)
            .Take(2000)
            .Select(x => new
            {
                x.a.Id,
                x.a.CreatedAt,
                Documento = x.p.NumeroDocumento,
                PacienteNombre = x.p.NombreCompleto,
                x.a.ContratoCodigo,
                x.a.NombreServicio,
                x.a.TipoServicio,
                x.a.Modulo,
                x.a.Cantidad,
                Estado = x.a.Estado.ToString(),
                x.a.FechaInicio,
                x.a.FechaFinal,
                x.a.AnioServicio,
                x.a.MesVigencia,
                x.a.MesFinal,
                x.a.CodigoAutorizacion,
                x.a.Observaciones,
                x.a.Sucursal
            })
            .ToListAsync(ct);

        // Resolver nombre de aseguradora en un segundo pase (evita join que no puede traducir EF Core en algunos casos).
        var codigos = rows.Select(r => r.ContratoCodigo).Distinct().ToList();
        var mapaAseguradora = await (from c in db.ContratosAseguradora.AsNoTracking()
                                     join a in db.Aseguradoras.AsNoTracking() on c.AseguradoraId equals a.Id
                                     where codigos.Contains(c.CodigoContrato)
                                     select new { c.CodigoContrato, AseguradoraNombre = a.Nombre })
                                    .ToListAsync(ct);
        var mapa = mapaAseguradora
            .GroupBy(x => x.CodigoContrato)
            .ToDictionary(g => g.Key, g => g.First().AseguradoraNombre, StringComparer.OrdinalIgnoreCase);

        return rows.Select(r => new AsignacionListadoDto(
            r.Id, r.CreatedAt,
            r.Documento, r.PacienteNombre,
            r.ContratoCodigo, mapa.TryGetValue(r.ContratoCodigo, out var an) ? an : null,
            r.NombreServicio, r.TipoServicio, r.Modulo,
            r.Cantidad, r.Estado,
            r.FechaInicio, r.FechaFinal,
            r.AnioServicio, r.MesVigencia, r.MesFinal,
            r.CodigoAutorizacion, r.Observaciones,
            r.Sucursal)).ToList();
    }

    public async Task<IReadOnlyList<AsignacionPendienteDto>> ListarPendientesAsync(
        IReadOnlyList<string> modulosPermitidos,
        AsignacionEstadoFiltro estado = AsignacionEstadoFiltro.Pendientes,
        int? anio = null, int? mesVigencia = null,
        string? noOrden = null, string? documentoPaciente = null,
        string? sucursalNombre = null,
        CancellationToken ct = default)
    {
        // Sin modulos permitidos -> grid vacio (el usuario no es coordinador de ningun modulo).
        if (modulosPermitidos is null || modulosPermitidos.Count == 0)
        {
            return Array.Empty<AsignacionPendienteDto>();
        }

        var permisos = ExpandirSinonimosModulo(modulosPermitidos);

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
        if (!string.IsNullOrWhiteSpace(sucursalNombre))
        {
            // El coordinador solo debe ver pacientes asignados en SU sede. Las
            // asignaciones guardan la sucursal como string (varchar 40); el caller
            // resuelve el nombre desde el claim sucursal_id. Si pasa null, no se filtra
            // (admin global, vista historica, etc.).
            var s = sucursalNombre.Trim();
            q = q.Where(a => a.Sucursal == s);
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

        // Suma de turnos ya creados por asignacion -> para mostrar "Parcial" en el grid
        // cuando hay turnos coordinados pero aun no completan la cantidad pedida.
        var asigIds = asigs.Select(a => a.Id).ToList();
        var turnosPorAsig = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => asigIds.Contains(t.AsignacionId))
            .GroupBy(t => t.AsignacionId)
            .Select(g => new { AsignacionId = g.Key, Total = g.Sum(t => t.Cantidad) })
            .ToDictionaryAsync(x => x.AsignacionId, x => x.Total, ct);

        // Resolver Especialidad del catalogo ServicioContrato. Asignacion.ServicioId guarda
        // el GUID del ServicioContrato (como string), asi que el lookup es por Guid -> Especialidad.
        var servicioGuids = asigs
            .Select(a => Guid.TryParse(a.ServicioId, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();
        var espDict = await db.ServiciosContrato.AsNoTracking()
            .Where(sc => servicioGuids.Contains(sc.Id))
            .Select(sc => new { sc.Id, sc.Especialidad })
            .ToDictionaryAsync(x => x.Id, x => x.Especialidad, ct);

        // El "Orden" visible en la grilla es un numero corrido por created_at (mas reciente primero -> 1, 2, ...).
        var orderedById = asigs
            .Select((a, idx) => new { a.Id, Orden = idx + 1 })
            .ToDictionary(x => x.Id, x => x.Orden);

        return asigs.Select(a =>
        {
            pacs.TryGetValue(a.PacienteId, out var p);
            turnosPorAsig.TryGetValue(a.Id, out var coordinados);
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
                a.Estado.ToString(),
                coordinados,
                Guid.TryParse(a.ServicioId, out var sgid) && espDict.TryGetValue(sgid, out var esp) ? esp : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<EspecialistaDto>> ListarEspecialistasPorModuloAsync(string modulo, CancellationToken ct = default)
    {
        // Filtro por tipo de profesional cuyo Nombre haga match (case-insensitive) con el modulo.
        var moduloUpper = (modulo ?? "").Trim().ToUpperInvariant();
        if (moduloUpper.Length == 0) { return Array.Empty<EspecialistaDto>(); }

        // 1) tipos que matchean por nombre. Match tolerante plural/singular
        // final "s" (TS8): "TERAPIAS" del filtro debe encontrar tipos
        // catalogo "TERAPIA" y viceversa.
        var moduloSin = moduloUpper.EndsWith("S") ? moduloUpper[..^1] : moduloUpper;
        var tipoIdsMatch = await db.TiposProfesional.AsNoTracking()
            .Where(t => t.Activo)
            .Where(t =>
                t.Nombre.ToUpper() == moduloUpper ||
                t.Nombre.ToUpper() == moduloSin ||
                (t.Nombre.ToUpper().EndsWith("S") ? t.Nombre.ToUpper().Substring(0, t.Nombre.Length - 1) : t.Nombre.ToUpper()) == moduloSin)
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

    public async Task<decimal?> ObtenerTarifaServicioAsync(string contratoCodigo, string codigoServicio, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contratoCodigo) || string.IsNullOrWhiteSpace(codigoServicio)) { return null; }
        var c = contratoCodigo.Trim();
        var s = codigoServicio.Trim();
        // Join ServicioContrato -> ContratoAseguradora por CodigoContrato. La asignacion
        // guarda el ContratoCodigo (texto) y el CodigoServicio (texto), no los GUID, por
        // eso resolvemos por codigos.
        return await db.ServiciosContrato.AsNoTracking()
            .Join(db.ContratosAseguradora.AsNoTracking(), sv => sv.ContratoId, ct2 => ct2.Id, (sv, ct2) => new { sv, ct2 })
            .Where(x => x.ct2.CodigoContrato == c && x.sv.CodigoServicio == s)
            .Select(x => x.sv.Tarifa)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PaqueteAsignadoDto>> ListarPaquetesDelPacienteAsync(
        Guid pacienteId, CancellationToken ct = default)
    {
        // Agrupamos por PaqueteInstanciaId — cada lote es un "paquete asignado".
        // Traemos totalServicios (COUNT), el valor pactado (SUM ya que solo una fila lo lleva)
        // y la fecha de creacion mas temprana del lote para orden descendente.
        // OrderBy client-side: EF Core no traduce OrderByDescending sobre propiedad
        // de DTO despues del Select.
        var filas = await db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == pacienteId && a.PaqueteInstanciaId != null && a.PaqueteCodigo != null)
            .GroupBy(a => new { InstanciaId = a.PaqueteInstanciaId!.Value, Codigo = a.PaqueteCodigo! })
            .Select(g => new
            {
                g.Key.InstanciaId,
                g.Key.Codigo,
                Total = g.Count(),
                Valor = g.Sum(x => x.PaqueteValorPactado),
                CreadoEn = g.Min(x => x.CreatedAt)
            })
            .ToListAsync(ct);
        return filas
            .OrderByDescending(x => x.CreadoEn)
            .Select(x => new PaqueteAsignadoDto(x.InstanciaId, x.Codigo, x.Total, x.Valor, x.CreadoEn))
            .ToList();
    }

    public async Task<PaqueteDeServicioDto?> GetPaqueteDeLaAsignacionAsync(
        Guid asignacionId, CancellationToken ct = default)
    {
        var datos = await db.Asignaciones.AsNoTracking()
            .Where(a => a.Id == asignacionId
                     && a.PaqueteInstanciaId != null
                     && a.PaqueteCodigo != null)
            .Select(a => new { InstanciaId = a.PaqueteInstanciaId!.Value, Codigo = a.PaqueteCodigo! })
            .FirstOrDefaultAsync(ct);
        if (datos is null) { return null; }

        var nombre = await db.Paquetes.AsNoTracking()
            .Where(p => p.Codigo == datos.Codigo)
            .Select(p => p.Nombre)
            .FirstOrDefaultAsync(ct);
        return new PaqueteDeServicioDto(datos.InstanciaId, datos.Codigo, nombre ?? datos.Codigo);
    }

    public async Task<PaqueteExpansionDto?> ObtenerPaqueteExpansionAsync(
        Guid paqueteId, string contratoCodigo, CancellationToken ct = default)
    {
        var pkg = await db.Paquetes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
        if (pkg is null) { return null; }

        // Fuente de verdad: los servicios que la aseguradora realmente pacto con
        // este paquete en el contrato del paciente (ServicioContrato.PaqueteId).
        // NO usamos paquete_servicios (catalogo global) como fuente de "esperados"
        // porque el operador solo puede agregar al carrito servicios que existen
        // en su contrato — si el catalogo global tuviera 14 codigos pero la EPS
        // solo pacto 3, el mensaje "faltantes" mencionaria codigos imposibles de
        // agregar. Ver diagnostico usuario 2026-07-24.
        //
        // Enriquecemos con Cantidad canonica del catalogo global (paquete_servicios)
        // cuando el codigo coincide; si no hay entrada canonica caemos a Cantidad=1.
        var contratados = await db.ServiciosContrato.AsNoTracking()
            .Join(db.ContratosAseguradora.AsNoTracking(), s => s.ContratoId, c => c.Id, (s, c) => new { s, c })
            .Where(x => x.c.CodigoContrato == contratoCodigo
                     && x.s.PaqueteId == paqueteId
                     && !string.IsNullOrEmpty(x.s.CodigoServicio))
            .OrderBy(x => x.s.CodigoServicio)
            .Select(x => new { x.s.Id, x.s.CodigoServicio, x.s.Descripcion, x.s.Modulo, x.s.Tarifa })
            .ToListAsync(ct);
        if (contratados.Count == 0)
        {
            return new PaqueteExpansionDto(pkg.Id, pkg.Codigo, pkg.Nombre, pkg.Precio, Array.Empty<PaqueteExpansionItemDto>());
        }

        var codigosContratados = contratados.Select(x => x.CodigoServicio!).ToList();
        var cantidadCanonicaPorCodigo = await db.PaqueteServicios.AsNoTracking()
            .Where(x => x.PaqueteId == paqueteId && codigosContratados.Contains(x.Codigo))
            .Select(x => new { x.Codigo, x.Cantidad })
            .ToDictionaryAsync(x => x.Codigo, x => x.Cantidad, ct);

        var items = new List<PaqueteExpansionItemDto>(contratados.Count);
        foreach (var sc in contratados)
        {
            var cantidad = cantidadCanonicaPorCodigo.TryGetValue(sc.CodigoServicio!, out var c) ? c : 1;
            items.Add(new PaqueteExpansionItemDto(
                sc.CodigoServicio!,
                sc.Descripcion ?? sc.CodigoServicio!,
                sc.Modulo ?? "",
                sc.Modulo,
                cantidad,
                sc.Id,
                sc.Tarifa));
        }
        return new PaqueteExpansionDto(pkg.Id, pkg.Codigo, pkg.Nombre, pkg.Precio, items);
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
            t.Cantidad, t.HorasPorTurno, t.FechaInicio, t.MesAsignar, t.Tarifa)).ToList();
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

        // Turnos con TurnoProgramacionId requieren cargar el grid una vez para
        // materializar las sesiones. Cacheamos por id para no re-parsear varias veces.
        var progIds = req.Turnos
            .Where(x => x.TurnoProgramacionId is Guid)
            .Select(x => x.TurnoProgramacionId!.Value)
            .Distinct().ToList();
        var progsMap = progIds.Count == 0
            ? new Dictionary<Guid, TurnoProgramacion>()
            : await db.TurnoProgramaciones.AsNoTracking()
                .Where(p => progIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p, ct);

        // Insertar los nuevos turnos. Modelo de negocio: cada turno individual produce
        // UN registro independiente en asignacion_turnos. Los turnos que traen
        // TurnoProgramacionId materializan sesiones con tipo/horas del grid;
        // el resto se guardan como turnos vacios (flujo manual).
        foreach (var t in req.Turnos)
        {
            if (t.TurnoProgramacionId is Guid progId && !string.IsNullOrWhiteSpace(t.TurnoRowNombre))
            {
                if (!progsMap.TryGetValue(progId, out var prog))
                {
                    throw new InvalidOperationException("Programacion no encontrada al guardar.");
                }
                var diaArranque = t.DiaArranque ?? 1;
                CrearTurnoDesdeGrid(prog, asig, t.ProfesionalId, t.TurnoRowNombre!, diaArranque, tid, t.Tarifa);
            }
            else
            {
                for (int i = 0; i < t.Cantidad; i++)
                {
                    db.AsignacionTurnos.Add(new AsignacionTurno
                    {
                        TenantId = tid,
                        AsignacionId = req.AsignacionId,
                        ProfesionalId = t.ProfesionalId,
                        Cantidad = 1,
                        HorasPorTurno = t.HorasPorTurno,
                        FechaInicio = t.FechaInicio,
                        MesAsignar = t.MesAsignar,
                        Tarifa = t.Tarifa,
                        // PQ6: denormalizar campos de paquete desde la Asignacion
                        // para poder GROUP BY paquete_instancia_id en reportes.
                        PaqueteInstanciaId = asig.PaqueteInstanciaId,
                        PaqueteCodigo = asig.PaqueteCodigo,
                        PaqueteValorPactado = asig.PaqueteValorPactado
                    });
                }
            }
        }

        // Si la suma total iguala la cantidad del servicio, o si se aplico una
        // programacion (que ignora Cantidad por decision explicita), marcar
        // como Asignado.
        if (totalProyectado == asig.Cantidad || progIds.Count > 0)
        {
            asig.Estado = AsignacionEstado.Asignado;
        }

        await db.SaveChangesAsync(ct);
        return req.Turnos.Count;
    }

    /// <summary>Materializa un asignacion_turno + sus sesiones desde una fila del
    /// grid de una TurnoProgramacion. Se usa desde AsignarServicioAsync (via carrito
    /// con turnos de programacion). Muta el DbContext; el SaveChanges lo llama el
    /// caller.</summary>
    private void CrearTurnoDesdeGrid(
        TurnoProgramacion prog, Asignacion asig, Guid profesionalId, string rowNombre,
        int diaArranque, Guid tid, decimal? tarifa)
    {
        var grid = System.Text.Json.JsonDocument.Parse(prog.GridDataJson).RootElement;
        int diasEnMes = DateTime.DaysInMonth(prog.Anio, prog.Mes);
        if (diaArranque < 1 || diaArranque > diasEnMes)
        {
            throw new InvalidOperationException($"Dia de arranque fuera del mes ({diasEnMes} dias).");
        }

        var at = new AsignacionTurno
        {
            TenantId = tid,
            AsignacionId = asig.Id,
            ProfesionalId = profesionalId,
            Cantidad = 1,
            MesAsignar = (short)prog.Mes,
            FechaInicio = new DateOnly(prog.Anio, prog.Mes, diaArranque),
            TurnoProgramacionId = prog.Id,
            TurnoRowNombre = rowNombre,
            Tarifa = tarifa,
            // PQ6: denormalizar campos de paquete desde la Asignacion.
            PaqueteInstanciaId = asig.PaqueteInstanciaId,
            PaqueteCodigo = asig.PaqueteCodigo,
            PaqueteValorPactado = asig.PaqueteValorPactado
        };
        db.AsignacionTurnos.Add(at);

        if (grid.TryGetProperty("dias", out var diasProp)
            && diasProp.ValueKind == System.Text.Json.JsonValueKind.Object
            && diasProp.TryGetProperty(rowNombre, out var celdasRow)
            && celdasRow.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            int sessionNo = 1;
            for (int d = diaArranque; d <= diasEnMes; d++)
            {
                if (!celdasRow.TryGetProperty(d.ToString(), out var celda)) { continue; }
                string? tipoCodigo = celda.TryGetProperty("tipo", out var t) ? t.GetString() : null;
                decimal? horas = celda.TryGetProperty("horas", out var h) && h.TryGetDecimal(out var hv) ? hv : (decimal?)null;
                if (string.IsNullOrEmpty(tipoCodigo)) { continue; }
                db.AsignacionTurnoSesiones.Add(new AsignacionTurnoSesion
                {
                    TenantId = tid,
                    AsignacionTurno = at,
                    SessionNo = sessionNo++,
                    FechaAtencion = new DateOnly(prog.Anio, prog.Mes, d),
                    TipoTurnoCodigo = tipoCodigo,
                    Horas = horas
                });
            }
        }
    }

    // ---------------- Aplicar programacion (Turnos) ----------------

    public async Task<IReadOnlyList<TurnoProgramacionCardDto>> ListarProgramacionesElegiblesAsync(
        Guid asignacionId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return Array.Empty<TurnoProgramacionCardDto>(); }

        var asig = await db.Asignaciones.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == asignacionId, ct);
        if (asig is null) { return Array.Empty<TurnoProgramacionCardDto>(); }

        // Filtrar por (Anio, Mes) de la asignacion. Sede N:N y TipoServicio se filtran
        // en memoria porque el nombre viene de otras tablas y queremos matching flexible.
        int? anio = asig.AnioServicio is short a ? a : null;
        int? mes = asig.MesVigencia is short m ? m : null;

        var q = db.TurnoProgramaciones.AsNoTracking().Where(p => p.Activa);
        if (anio is int ay) { q = q.Where(p => p.Anio == ay); }
        if (mes is int mv) { q = q.Where(p => p.Mes == mv); }

        var raw = await q
            .Include(p => p.Sucursales)
            .OrderBy(p => p.Nombre).ToListAsync(ct);

        // Resolver nombres de sedes vinculadas (todas las N:N presentes).
        var sedeIds = raw.SelectMany(p => p.Sucursales.Select(s => s.SucursalId)).Distinct().ToList();
        var sedesMap = await db.Sucursales.AsNoTracking()
            .Where(s => sedeIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Nombre })
            .ToDictionaryAsync(s => s.Id, s => s.Nombre, ct);

        var tipoIds = raw.Where(p => p.TipoServicioId.HasValue).Select(p => p.TipoServicioId!.Value).Distinct().ToList();
        var tiposMap = await db.CatalogosTipoServicio.AsNoTracking()
            .Where(t => tipoIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Codigo })
            .ToDictionaryAsync(t => t.Id, t => t.Codigo, ct);

        // Sede de la asignacion (para filtrar N:N). asig.Sucursal es un string
        // (varchar 40) — buscamos el Id resolviendo por nombre. Si el paciente no
        // tiene sede en la asignacion, no filtramos por sede.
        Guid? asigSedeId = null;
        if (!string.IsNullOrWhiteSpace(asig.Sucursal))
        {
            var s = asig.Sucursal.Trim();
            // Matching tolerante: exacto primero, luego contains bidireccional
            // (para casos legacy donde asignacion tiene "CALI" y sucursal es
            // "SANTIAGO DE CALI"). Todo case-insensitive.
            var todas = await db.Sucursales.AsNoTracking()
                .Select(x => new { x.Id, x.Nombre })
                .ToListAsync(ct);
            var exact = todas.FirstOrDefault(x =>
                string.Equals(x.Nombre, s, StringComparison.OrdinalIgnoreCase));
            asigSedeId = exact?.Id
                ?? todas.FirstOrDefault(x =>
                    x.Nombre.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || s.Contains(x.Nombre, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        var asigTipo = asig.TipoServicio?.ToUpperInvariant();

        var result = new List<TurnoProgramacionCardDto>();
        foreach (var p in raw)
        {
            var tipo = p.TipoServicioId is Guid tsid ? tiposMap.GetValueOrDefault(tsid) : null;
            // Matching tolerante de tipo: normaliza plural/singular
            // ("TERAPIAS" == "TERAPIA", "CONSULTAS" == "CONSULTA", etc.).
            if (!string.IsNullOrEmpty(tipo) && !string.IsNullOrEmpty(asigTipo)
                && !TiposCoinciden(tipo, asigTipo))
            {
                continue;
            }
            // Regla dura N:N: la programacion es elegible si su lista de sedes
            // contiene la sede de la asignacion. Si la asignacion no tiene sede
            // resuelta, permitimos cualquier programacion (fallback conservador).
            if (asigSedeId is Guid aSid && !p.Sucursales.Any(s => s.SucursalId == aSid))
            {
                continue;
            }
            var nombresSedes = p.Sucursales
                .Select(s => sedesMap.TryGetValue(s.SucursalId, out var n) ? n : "?")
                .OrderBy(n => n).ToList();
            var sedeNombreDisplay = nombresSedes.Count switch
            {
                0 => null,
                1 => nombresSedes[0],
                2 => $"{nombresSedes[0]}, {nombresSedes[1]}",
                _ => $"{nombresSedes.Count} sedes"
            };
            int numTurnos = ContarTurnosEnGrid(p.GridDataJson);
            result.Add(new TurnoProgramacionCardDto(
                p.Id, p.Nombre, p.Anio, p.Mes, sedeNombreDisplay, tipo, numTurnos, p.GridDataJson));
        }
        return result;
    }

    public async Task<AplicarProgramacionResult> AplicarProgramacionAsync(
        AplicarProgramacionRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (req.DiaArranque < 1 || req.DiaArranque > 31)
        {
            throw new InvalidOperationException("El dia de arranque debe estar entre 1 y 31.");
        }
        if (req.ProfesionalPorFila is null || req.ProfesionalPorFila.Count == 0)
        {
            throw new InvalidOperationException("Debe asignar al menos un profesional a una fila.");
        }

        var asig = await db.Asignaciones.FirstOrDefaultAsync(a => a.Id == req.AsignacionId, ct)
            ?? throw new InvalidOperationException("Asignacion no encontrada.");
        var prog = await db.TurnoProgramaciones.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.ProgramacionId, ct)
            ?? throw new InvalidOperationException("Programacion no encontrada.");

        var grid = System.Text.Json.JsonDocument.Parse(prog.GridDataJson).RootElement;
        var turnos = grid.TryGetProperty("turnos", out var tArr) && tArr.ValueKind == System.Text.Json.JsonValueKind.Array
            ? tArr.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : new List<string>();
        if (turnos.Count == 0) { throw new InvalidOperationException("La programacion no tiene turnos configurados."); }

        int diasEnMes = DateTime.DaysInMonth(prog.Anio, prog.Mes);
        if (req.DiaArranque > diasEnMes)
        {
            throw new InvalidOperationException($"El dia de arranque supera los dias del mes ({diasEnMes}).");
        }

        var diasProp = grid.TryGetProperty("dias", out var dObj) && dObj.ValueKind == System.Text.Json.JsonValueKind.Object
            ? dObj : default;

        int turnosCreados = 0, sesionesCreadas = 0, sesionesDescanso = 0;

        foreach (var rowNombre in turnos)
        {
            if (!req.ProfesionalPorFila.TryGetValue(rowNombre, out var profId) || profId == Guid.Empty)
            {
                // Fila sin profesional asignado: se salta (no obligamos a cubrir todas).
                continue;
            }

            var at = new AsignacionTurno
            {
                TenantId = tid,
                AsignacionId = req.AsignacionId,
                ProfesionalId = profId,
                Cantidad = 1,
                MesAsignar = (short)prog.Mes,
                FechaInicio = new DateOnly(prog.Anio, prog.Mes, req.DiaArranque),
                TurnoProgramacionId = prog.Id,
                TurnoRowNombre = rowNombre,
                // PQ6: denormalizar campos de paquete desde la Asignacion.
                PaqueteInstanciaId = asig.PaqueteInstanciaId,
                PaqueteCodigo = asig.PaqueteCodigo,
                PaqueteValorPactado = asig.PaqueteValorPactado
            };
            db.AsignacionTurnos.Add(at);
            turnosCreados++;

            if (diasProp.ValueKind == System.Text.Json.JsonValueKind.Object
                && diasProp.TryGetProperty(rowNombre, out var celdasRow)
                && celdasRow.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                int sessionNo = 1;
                for (int d = req.DiaArranque; d <= diasEnMes; d++)
                {
                    if (!celdasRow.TryGetProperty(d.ToString(), out var celda)) { continue; }
                    string? tipoCodigo = celda.TryGetProperty("tipo", out var t) ? t.GetString() : null;
                    decimal? horas = celda.TryGetProperty("horas", out var h) && h.TryGetDecimal(out var hv) ? hv : (decimal?)null;
                    if (string.IsNullOrEmpty(tipoCodigo)) { continue; }
                    db.AsignacionTurnoSesiones.Add(new AsignacionTurnoSesion
                    {
                        TenantId = tid,
                        AsignacionTurno = at,
                        SessionNo = sessionNo++,
                        FechaAtencion = new DateOnly(prog.Anio, prog.Mes, d),
                        TipoTurnoCodigo = tipoCodigo,
                        Horas = horas
                    });
                    if (string.Equals(tipoCodigo, "L", StringComparison.OrdinalIgnoreCase)) { sesionesDescanso++; }
                    else { sesionesCreadas++; }
                }
            }
        }

        if (turnosCreados == 0)
        {
            throw new InvalidOperationException("Debe asignar al menos un profesional a alguna fila del grid.");
        }

        // Al aplicar programacion consideramos la asignacion Asignada aunque el # de
        // sesiones supere Cantidad — el usuario decidio ignorar el limite explicitamente.
        asig.Estado = AsignacionEstado.Asignado;

        await db.SaveChangesAsync(ct);
        return new AplicarProgramacionResult(turnosCreados, sesionesCreadas, sesionesDescanso);
    }

    /// <summary>Compara dos codigos de tipo de servicio tolerando plural/singular.
    /// Ejemplos: "TERAPIA" == "TERAPIAS", "CONSULTA" == "CONSULTAS". Case-insensitive.
    /// Sirve para asignaciones legacy con datos plurales que el catalogo tiene en
    /// singular (o viceversa).</summary>
    private static bool TiposCoinciden(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) { return true; }
        var au = a.ToUpperInvariant();
        var bu = b.ToUpperInvariant();
        // Normaliza terminacion "S" para comparar plural/singular.
        var aSing = au.EndsWith('S') ? au[..^1] : au;
        var bSing = bu.EndsWith('S') ? bu[..^1] : bu;
        return string.Equals(aSing, bSing, StringComparison.OrdinalIgnoreCase);
    }

    private static int ContarTurnosEnGrid(string gridJson)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(gridJson);
            if (doc.RootElement.TryGetProperty("turnos", out var arr) &&
                arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return arr.GetArrayLength();
            }
        }
        catch { }
        return 0;
    }

    public async Task<IReadOnlyList<AsignacionTableroKanbanDto>> ListarTableroKanbanAsync(
        IReadOnlyList<string> modulosPermitidos,
        int anio, int? mesVigencia = null,
        string? documentoPaciente = null,
        string? sucursalNombre = null,
        CancellationToken ct = default)
    {
        if (modulosPermitidos is null || modulosPermitidos.Count == 0)
        {
            return Array.Empty<AsignacionTableroKanbanDto>();
        }

        var permisos = ExpandirSinonimosModulo(modulosPermitidos);

        var q = db.Asignaciones.AsNoTracking()
            .Where(a => a.AnioServicio == (short)anio)
            .Where(a => (a.Modulo != null && permisos.Contains(a.Modulo.ToUpper()))
                     || permisos.Contains(a.TipoServicio.ToUpper()));

        if (mesVigencia is int mv && mv >= 1 && mv <= 12) { q = q.Where(a => a.MesVigencia == (short)mv); }
        if (!string.IsNullOrWhiteSpace(documentoPaciente))
        {
            var d = documentoPaciente.Trim();
            q = q.Where(a => a.Paciente != null && a.Paciente.NumeroDocumento.Contains(d));
        }
        if (!string.IsNullOrWhiteSpace(sucursalNombre))
        {
            var s = sucursalNombre.Trim();
            q = q.Where(a => a.Sucursal == s);
        }

        var asigs = await q.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        if (asigs.Count == 0) { return Array.Empty<AsignacionTableroKanbanDto>(); }

        var asigIds = asigs.Select(a => a.Id).ToList();
        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();

        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NumeroDocumento, p.NombreCompleto })
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        // Turnos por asignacion: cantidad + profesional_id.
        var turnosData = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => asigIds.Contains(t.AsignacionId))
            .Select(t => new { t.Id, t.AsignacionId, t.ProfesionalId, t.Cantidad })
            .ToListAsync(ct);

        var turnoIds = turnosData.Select(t => t.Id).ToList();

        // Sesiones por asignacion_turno_id.
        var sesionesData = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => turnoIds.Contains(s.AsignacionTurnoId))
            .Select(s => new { s.AsignacionTurnoId, s.TipoTurnoCodigo })
            .ToListAsync(ct);

        // Notas por asignacion_turno_id.
        var notasData = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.AsignacionTurnoId != null && turnoIds.Contains(n.AsignacionTurnoId.Value))
            .Select(n => new { AsignacionTurnoId = n.AsignacionTurnoId!.Value, n.Estado })
            .ToListAsync(ct);

        // Profesionales para mostrar en la card (max 2 nombres + "...").
        var profIds = turnosData.Select(t => t.ProfesionalId).Distinct().ToList();
        var profs = await db.Profesionales.AsNoTracking()
            .Where(p => profIds.Contains(p.Id))
            .Select(p => new { p.Id, Nombre = ((p.PrimerNombre ?? "") + " " + (p.PrimerApellido ?? "")).Trim() })
            .ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        // Agrupar por asignacion.
        var turnosPorAsig = turnosData.GroupBy(t => t.AsignacionId).ToDictionary(g => g.Key, g => g.ToList());
        var sesionesPorTurno = sesionesData.GroupBy(s => s.AsignacionTurnoId).ToDictionary(g => g.Key, g => g.ToList());
        var notasPorTurno = notasData.GroupBy(n => n.AsignacionTurnoId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<AsignacionTableroKanbanDto>(asigs.Count);
        foreach (var a in asigs)
        {
            pacs.TryGetValue(a.PacienteId, out var p);
            turnosPorAsig.TryGetValue(a.Id, out var turnos);

            int turnosCount = turnos?.Count ?? 0;
            int sesionesTotales = 0, notasCreadas = 0, notasDefinitivas = 0;
            var especialistas = new List<string>();

            if (turnos != null)
            {
                foreach (var t in turnos)
                {
                    if (sesionesPorTurno.TryGetValue(t.Id, out var ss)) { sesionesTotales += ss.Count; }
                    if (notasPorTurno.TryGetValue(t.Id, out var ns))
                    {
                        notasCreadas += ns.Count;
                        notasDefinitivas += ns.Count(n => n.Estado == NotaMedicaEstado.Definitivo);
                    }
                    if (profs.TryGetValue(t.ProfesionalId, out var nom) && !string.IsNullOrWhiteSpace(nom))
                    {
                        if (!especialistas.Contains(nom)) { especialistas.Add(nom); }
                    }
                }
            }

            // "Total esperado" para determinar Terminado = suma de Cantidad de los turnos.
            // Si hay sesiones creadas up-front (Aplicar Programacion), usamos ese conteo.
            int totalEsperado = sesionesTotales > 0 ? sesionesTotales : (turnos?.Sum(t => t.Cantidad) ?? 0);

            var estado = CalcularEstadoTablero(turnosCount, sesionesTotales, notasCreadas, notasDefinitivas, totalEsperado);

            string? espNombres = especialistas.Count switch
            {
                0 => null,
                1 => especialistas[0],
                2 => $"{especialistas[0]}, {especialistas[1]}",
                _ => $"{especialistas[0]}, {especialistas[1]} + {especialistas.Count - 2}"
            };

            result.Add(new AsignacionTableroKanbanDto(
                a.Id, estado,
                p?.NombreCompleto ?? "?", p?.NumeroDocumento ?? "",
                a.NombreServicio, a.TipoServicio, a.ContratoCodigo,
                a.Cantidad, sesionesTotales, notasDefinitivas,
                turnosCount, espNombres,
                a.FechaInicio, a.FechaFinal));
        }
        return result;
    }

    private static EstadoTablero CalcularEstadoTablero(
        int turnosCount, int sesionesTotales, int notasCreadas, int notasDefinitivas, int totalEsperado)
    {
        // Sin turnos = viene de /asignacion pero nadie le puso profesional aun.
        if (turnosCount == 0) { return EstadoTablero.Asignado; }
        if (totalEsperado > 0 && notasDefinitivas >= totalEsperado) { return EstadoTablero.Terminado; }
        if (notasDefinitivas > 0) { return EstadoTablero.EnProgreso; }
        if (notasCreadas > 0) { return EstadoTablero.Atendido; }
        if (sesionesTotales > 0) { return EstadoTablero.Programado; }
        return EstadoTablero.Coordinado;
    }

    public async Task<IReadOnlyList<SesionCalendarioDto>> ListarTableroCalendarioAsync(
        IReadOnlyList<string> modulosPermitidos,
        int anio, int mes,
        string? sucursalNombre = null,
        CancellationToken ct = default)
    {
        if (modulosPermitidos is null || modulosPermitidos.Count == 0)
        {
            return Array.Empty<SesionCalendarioDto>();
        }

        var permisos = ExpandirSinonimosModulo(modulosPermitidos);

        // Rango del mes.
        var desde = new DateOnly(anio, mes, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);

        // Asignaciones cuyos turnos tienen sesiones en el mes + modulo permitido + (opcional) sede.
        var asigsQ = db.Asignaciones.AsNoTracking()
            .Where(a => (a.Modulo != null && permisos.Contains(a.Modulo.ToUpper()))
                     || permisos.Contains(a.TipoServicio.ToUpper()));
        if (!string.IsNullOrWhiteSpace(sucursalNombre))
        {
            var s = sucursalNombre.Trim();
            asigsQ = asigsQ.Where(a => a.Sucursal == s);
        }

        var asigs = await asigsQ.Select(a => new { a.Id, a.PacienteId, a.NombreServicio }).ToListAsync(ct);
        if (asigs.Count == 0) { return Array.Empty<SesionCalendarioDto>(); }

        var asigIds = asigs.Select(a => a.Id).ToList();
        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => asigIds.Contains(t.AsignacionId))
            .Select(t => new { t.Id, t.AsignacionId, t.ProfesionalId, t.Cantidad })
            .ToListAsync(ct);

        var turnoIds = turnos.Select(t => t.Id).ToList();
        var sesiones = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => turnoIds.Contains(s.AsignacionTurnoId))
            .Where(s => s.FechaAtencion >= desde && s.FechaAtencion <= hasta)
            .Select(s => new
            {
                s.Id, s.AsignacionTurnoId, s.SessionNo, s.FechaAtencion,
                s.TipoTurnoCodigo, s.Horas
            })
            .ToListAsync(ct);

        if (sesiones.Count == 0) { return Array.Empty<SesionCalendarioDto>(); }

        // Notas por (turno, session_no).
        var notas = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.AsignacionTurnoId != null && turnoIds.Contains(n.AsignacionTurnoId.Value))
            .Where(n => n.FechaNota >= desde && n.FechaNota <= hasta)
            .Select(n => new { AsignacionTurnoId = n.AsignacionTurnoId!.Value, n.SessionNo, n.Estado })
            .ToListAsync(ct);

        // Notas por asignacion_turno_id para calcular estado agregado del turno.
        var notasPorTurno = notas.GroupBy(n => n.AsignacionTurnoId).ToDictionary(g => g.Key, g => g.ToList());

        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto })
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        var profIds = turnos.Select(t => t.ProfesionalId).Distinct().ToList();
        var profs = await db.Profesionales.AsNoTracking()
            .Where(p => profIds.Contains(p.Id))
            .Select(p => new { p.Id, Nombre = ((p.PrimerNombre ?? "") + " " + (p.PrimerApellido ?? "")).Trim() })
            .ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        var asigDict = asigs.ToDictionary(a => a.Id, a => a);
        var turnoDict = turnos.ToDictionary(t => t.Id, t => t);

        var result = new List<SesionCalendarioDto>(sesiones.Count);
        foreach (var s in sesiones)
        {
            if (!turnoDict.TryGetValue(s.AsignacionTurnoId, out var t)) { continue; }
            if (!asigDict.TryGetValue(t.AsignacionId, out var a)) { continue; }

            pacs.TryGetValue(a.PacienteId, out var pacNom);
            profs.TryGetValue(t.ProfesionalId, out var profNom);

            // Estado por sesion: si tiene nota Definitivo -> Terminado (para pintar verde).
            // Si tiene nota Parcial -> Atendido. Si no tiene nota -> Programado.
            bool tieneNota = false, notaDefinitiva = false;
            if (notasPorTurno.TryGetValue(t.Id, out var ns))
            {
                var propia = ns.FirstOrDefault(n => n.SessionNo == s.SessionNo);
                if (propia != null)
                {
                    tieneNota = true;
                    notaDefinitiva = propia.Estado == NotaMedicaEstado.Definitivo;
                }
            }
            var estado = notaDefinitiva ? EstadoTablero.Terminado
                        : tieneNota ? EstadoTablero.Atendido
                        : EstadoTablero.Programado;

            result.Add(new SesionCalendarioDto(
                s.Id, s.FechaAtencion,
                s.AsignacionTurnoId, t.AsignacionId,
                estado,
                pacNom ?? "?",
                string.IsNullOrWhiteSpace(profNom) ? "?" : profNom!,
                a.NombreServicio,
                s.TipoTurnoCodigo, s.Horas,
                s.SessionNo, tieneNota, notaDefinitiva));
        }
        return result;
    }

    /// <summary>
    /// Expande cada modulo permitido a todas sus variantes conocidas plural/singular.
    /// El catalogo TS4 normalizo a singular (TERAPIA, INSUMO, etc.) pero en prod
    /// conviven asignaciones antiguas en plural (TERAPIAS, INSUMOS) y coordinadores
    /// que pueden tener registrada cualquiera de las dos formas. Match tolerante
    /// evita que un desfase de una letra oculte 65+ pendientes en /coordinacion.
    /// </summary>
    private static List<string> ExpandirSinonimosModulo(IReadOnlyList<string> modulosPermitidos)
    {
        var salida = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in modulosPermitidos)
        {
            var key = (m ?? "").Trim().ToUpperInvariant();
            if (key.Length == 0) { continue; }
            salida.Add(key);
            if (_sinonimosModulo.TryGetValue(key, out var extras))
            {
                foreach (var e in extras) { salida.Add(e); }
            }
        }
        return salida.ToList();
    }

    /// <summary>Aliases plural &lt;-&gt; singular por modulo. Ambas direcciones para
    /// que el orden de entrada no importe. Ampliar aqui si aparecen mas colisiones.</summary>
    private static readonly Dictionary<string, string[]> _sinonimosModulo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TERAPIA"] = new[] { "TERAPIAS" },
        ["TERAPIAS"] = new[] { "TERAPIA" },
        ["INSUMO"] = new[] { "INSUMOS" },
        ["INSUMOS"] = new[] { "INSUMO" },
        ["EQUIPO"] = new[] { "EQUIPOS" },
        ["EQUIPOS"] = new[] { "EQUIPO" },
    };

    // ================== TAB "COORDINACIONES" ==================
    // Lista de asignaciones ya coordinadas pero SIN ningun artefacto clinico
    // downstream (HC/nota). La UI las agrupa por asignacion_id y ofrece un
    // boton eliminar. La eliminacion re-valida antes de tocar la BD.

    public async Task<IReadOnlyList<CoordinacionEliminableDto>> ListarCoordinacionesEliminablesAsync(
        IReadOnlyList<string> modulosPermitidos,
        string? sucursalNombre = null,
        CancellationToken ct = default)
    {
        if (modulosPermitidos is null || modulosPermitidos.Count == 0)
        {
            return Array.Empty<CoordinacionEliminableDto>();
        }
        var permisos = ExpandirSinonimosModulo(modulosPermitidos);

        // Ids de asignaciones que YA tienen algun artefacto clinico downstream
        // (HC ligada por pivote, o nota medica ligada al turno). Se calculan
        // por separado y luego se restan de la lista de "con turnos".
        // NotaMedica no tiene navigation a AsignacionTurno; se usa join manual.
        var asigsConHc = db.AsignacionTurnoSesionHcs.AsNoTracking()
            .Select(pv => pv.Sesion!.AsignacionTurno!.AsignacionId)
            .Distinct();
        var turnosConNota = db.NotasMedicas.AsNoTracking()
            .Where(n => n.AsignacionTurnoId != null)
            .Select(n => n.AsignacionTurnoId!.Value)
            .Distinct();
        var asigsConNota = db.AsignacionTurnos.AsNoTracking()
            .Where(t => turnosConNota.Contains(t.Id))
            .Select(t => t.AsignacionId)
            .Distinct();

        // Base: asignaciones con turnos coordinados que NO estan en las dos listas anteriores.
        var q = db.Asignaciones.AsNoTracking()
            .Where(a => (a.Modulo != null && permisos.Contains(a.Modulo.ToUpper()))
                     || permisos.Contains(a.TipoServicio.ToUpper()))
            .Where(a => a.Estado != AsignacionEstado.Pendiente)
            .Where(a => db.AsignacionTurnos.Any(t => t.AsignacionId == a.Id))
            .Where(a => !asigsConHc.Contains(a.Id))
            .Where(a => !asigsConNota.Contains(a.Id));

        if (!string.IsNullOrWhiteSpace(sucursalNombre))
        {
            var s = sucursalNombre.Trim();
            q = q.Where(a => a.Sucursal == s);
        }

        var asigs = await q
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        if (asigs.Count == 0) { return Array.Empty<CoordinacionEliminableDto>(); }

        var asigIds = asigs.Select(a => a.Id).ToList();
        var pacIds = asigs.Select(a => a.PacienteId).Distinct().ToList();

        // Enriquecimiento: paciente + turnos agregados (count + especialistas + fechas + sesiones).
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NumeroDocumento, p.NombreCompleto })
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        var turnos = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => asigIds.Contains(t.AsignacionId))
            .Select(t => new { t.Id, t.AsignacionId, t.ProfesionalId, t.Cantidad })
            .ToListAsync(ct);

        var profIds = turnos.Where(t => t.ProfesionalId != Guid.Empty).Select(t => t.ProfesionalId).Distinct().ToList();
        var profs = await db.Profesionales.AsNoTracking()
            .Where(p => profIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto })
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        // Sesiones + fechas min/max por asignacion (una query, groupby).
        var turnoIds = turnos.Select(t => t.Id).ToList();
        var sesionesInfo = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => turnoIds.Contains(s.AsignacionTurnoId))
            .Select(s => new { s.AsignacionTurnoId, s.FechaAtencion })
            .ToListAsync(ct);

        // Agrupar turnos por asignacion y sesiones por asignacion
        var turnosPorAsig = turnos.GroupBy(t => t.AsignacionId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var turnoToAsig = turnos.ToDictionary(t => t.Id, t => t.AsignacionId);
        var sesionesPorAsig = sesionesInfo
            .GroupBy(s => turnoToAsig[s.AsignacionTurnoId])
            .ToDictionary(g => g.Key, g => g.Select(x => x.FechaAtencion).ToList());

        var result = new List<CoordinacionEliminableDto>(asigs.Count);
        foreach (var a in asigs)
        {
            pacs.TryGetValue(a.PacienteId, out var pac);
            var tList = turnosPorAsig.TryGetValue(a.Id, out var tl) ? tl : new();
            var sesList = sesionesPorAsig.TryGetValue(a.Id, out var sl) ? sl : new();
            var espNombres = tList
                .Select(t => profs.TryGetValue(t.ProfesionalId, out var n) ? n : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToArray();

            result.Add(new CoordinacionEliminableDto(
                AsignacionId: a.Id,
                PacienteDoc: pac?.NumeroDocumento ?? "-",
                PacienteNombre: pac?.NombreCompleto ?? "-",
                ContratoCodigo: a.ContratoCodigo,
                TipoServicio: a.TipoServicio,
                NombreServicio: a.NombreServicio,
                Sucursal: a.Sucursal,
                Cantidad: a.Cantidad,
                TurnosCoordinados: tList.Count,
                SesionesProgramadas: sesList.Count,
                EspecialistasNombres: string.Join(", ", espNombres),
                PrimeraFecha: sesList.Count > 0 ? sesList.Min() : null,
                UltimaFecha: sesList.Count > 0 ? sesList.Max() : null,
                CreadoEn: a.CreatedAt));
        }
        return result;
    }

    public async Task<bool> EliminarCoordinacionAsync(Guid asignacionId, Guid actor, CancellationToken ct = default)
    {
        // Helper local: chequea si la asignacion tiene HC o nota downstream.
        // Se usa dos veces: al entrar (rechazo temprano) y justo antes del
        // delete (guarda contra carreras entre lista y borrado).
        async Task<string?> ValidarAsync()
        {
            var tieneHc = await db.AsignacionTurnoSesionHcs.AsNoTracking()
                .AnyAsync(pv => pv.Sesion!.AsignacionTurno!.AsignacionId == asignacionId, ct);
            if (tieneHc) { return "No se puede eliminar: alguna sesion de esta coordinacion ya tiene historia clinica registrada."; }

            var turnoIdsLocal = await db.AsignacionTurnos.AsNoTracking()
                .Where(t => t.AsignacionId == asignacionId)
                .Select(t => t.Id)
                .ToListAsync(ct);
            var tieneNota = turnoIdsLocal.Count > 0 && await db.NotasMedicas.AsNoTracking()
                .AnyAsync(n => n.AsignacionTurnoId != null && turnoIdsLocal.Contains(n.AsignacionTurnoId!.Value), ct);
            if (tieneNota) { return "No se puede eliminar: algun turno de esta coordinacion ya tiene notas medicas."; }
            return null;
        }

        // PASO 1 - Validacion previa (feedback rapido antes de cargar todo).
        var err1 = await ValidarAsync();
        if (err1 is not null) { throw new InvalidOperationException(err1); }

        var asig = await db.Asignaciones.FirstOrDefaultAsync(a => a.Id == asignacionId, ct);
        if (asig is null) { return false; }

        // PASO 2 - Re-validacion justo antes del delete: entre listar y borrar
        // pudo haberse creado una HC/nota; sin re-check quedaria un delete
        // parcial huerfano de la HC.
        var err2 = await ValidarAsync();
        if (err2 is not null)
        {
            throw new InvalidOperationException(
                "No se puede eliminar: durante el proceso aparecio actividad clinica en esta coordinacion. Recarga la lista e intenta de nuevo.");
        }

        // Cascada explicita: sesiones -> turnos -> asignacion. Todo va en un
        // solo SaveChangesAsync (transaccion implicita de EF Core).
        var turnoIds = await db.AsignacionTurnos
            .Where(t => t.AsignacionId == asignacionId)
            .Select(t => t.Id)
            .ToListAsync(ct);
        if (turnoIds.Count > 0)
        {
            var sesiones = await db.AsignacionTurnoSesiones
                .Where(s => turnoIds.Contains(s.AsignacionTurnoId))
                .ToListAsync(ct);
            db.AsignacionTurnoSesiones.RemoveRange(sesiones);

            var turnos = await db.AsignacionTurnos
                .Where(t => turnoIds.Contains(t.Id))
                .ToListAsync(ct);
            db.AsignacionTurnos.RemoveRange(turnos);
        }
        db.Asignaciones.Remove(asig);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
