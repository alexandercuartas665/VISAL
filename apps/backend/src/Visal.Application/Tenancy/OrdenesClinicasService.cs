using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Consulta read-only del modulo "Ordenes Clinicas". No expone metodos de
/// edicion porque el modulo es de consulta + reimpresion.
/// </summary>
public sealed class OrdenesClinicasService(IApplicationDbContext db) : IOrdenesClinicasService
{
    public async Task<IReadOnlyList<OrdenClinicaItemDto>> BuscarAsync(
        OrdenesClinicasFiltro filtro, CancellationToken ct = default)
    {
        var q = db.HistoriasClinicas.AsNoTracking().AsQueryable();

        if (filtro.SoloCerradas)
        {
            q = q.Where(h => h.Estado == HistoriaClinicaEstado.Cerrada);
        }

        if (filtro.Desde is DateOnly d)
        {
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(h => (h.FechaCierre ?? h.FechaApertura) >= dStart);
        }
        if (filtro.Hasta is DateOnly h2)
        {
            var dEnd = new DateTimeOffset(h2.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(h => (h.FechaCierre ?? h.FechaApertura) <= dEnd);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Especialista))
        {
            var esp = filtro.Especialista.Trim().ToLower();
            q = q.Where(h => h.EspecialistaNombre != null && h.EspecialistaNombre.ToLower().Contains(esp));
        }

        // LEFT JOIN a `revisiones_clinica` para traer el estado agregado + veredicto
        // agente sin romper filas de HCs que aun no entraron al ciclo (Capa 08 Ola 2).
        // La EPS del paciente se resuelve DESPUES via lookup en memoria (los
        // GroupJoin en cascada tras query filters rompen el traductor EF Core).
        var joined = q
            .Join(db.Pacientes.AsNoTracking(), h => h.PacienteId, p => p.Id, (h, p) => new { h, p })
            .Join(db.FormDefinitions.AsNoTracking(), x => x.h.FormDefinitionId, f => f.Id, (x, f) => new { x.h, x.p, f })
            .GroupJoin(db.RevisionesClinica.AsNoTracking(),
                x => x.h.Id, r => r.HistoriaClinicaId,
                (x, rs) => new { x.h, x.p, x.f, rs })
            .SelectMany(x => x.rs.DefaultIfEmpty(), (x, r) => new { x.h, x.p, x.f, r });

        if (!string.IsNullOrWhiteSpace(filtro.PacienteTexto))
        {
            var t = filtro.PacienteTexto.Trim().ToLower();
            joined = joined.Where(x =>
                x.p.NombreCompleto.ToLower().Contains(t) ||
                x.p.NumeroDocumento.ToLower().Contains(t));
        }

        // Filtro EPS: paciente tiene que tener AL MENOS un contrato en la EPS
        // filtrada (via paciente_contratos, no via los slots viejos). Se
        // resuelve subquery: contratos_de_esa_ase -> pacientes con al menos
        // uno de esos contratos en paciente_contratos.
        if (filtro.AseguradoraId is Guid aseFiltro)
        {
            var pacientesConEsaAse = db.PacienteContratos.AsNoTracking()
                .Where(pc => db.ContratosAseguradora.AsNoTracking()
                    .Any(c => c.Id == pc.ContratoAseguradoraId && c.AseguradoraId == aseFiltro))
                .Select(pc => pc.PacienteId);
            joined = joined.Where(x => pacientesConEsaAse.Contains(x.p.Id));
        }

        // Filtro Sede: la sucursal esta directamente en el paciente
        // (SedeAtencionId), traducible sin trucos.
        if (filtro.SucursalId is Guid sedeFiltro)
        {
            joined = joined.Where(x => x.p.SedeAtencionId == sedeFiltro);
        }

        // Orden: paciente alfabetico ascendente, secundario por fecha de cierre desc
        // (las mas recientes arriba dentro del mismo paciente). El usuario pidio "orden
        // alfabetico por la fecha de cierre" — interpretamos: alfabetico por paciente,
        // y fecha de cierre como criterio secundario.
        var rows = await joined
            .OrderBy(x => x.p.NombreCompleto)
            .ThenByDescending(x => x.h.FechaCierre ?? x.h.FechaApertura)
            .Take(500)
            .Select(x => new
            {
                Hc = x.h,
                Pa = x.p,
                Fo = x.f,
                Rv = x.r
            })
            .ToListAsync(ct);

        if (rows.Count == 0) { return Array.Empty<OrdenClinicaItemDto>(); }

        // Conteos por HC en una sola pasada por tabla.
        var hcIds = rows.Select(r => r.Hc.Id).ToList();
        var medCounts = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var srvCounts = await db.HistoriaClinicaOrdenesServicio.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var remCounts = await db.HistoriaClinicaRemisiones.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var incCounts = await db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var certCounts = await db.HistoriaClinicaCertificaciones.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var insCounts = await db.HistoriaClinicaInsumos.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        // Ordenes externas: agrupamos por HC y por tipo para no traer las 3 en
        // 3 queries separadas. Filtro por hcIds en una sola pasada.
        var extCounts = await db.HistoriaClinicaOrdenesExternas.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => new { x.HistoriaClinicaId, x.Tipo })
            .Select(g => new { g.Key.HistoriaClinicaId, g.Key.Tipo, N = g.Count() })
            .ToListAsync(ct);
        var escCounts = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var docCounts = await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => new { x.HistoriaClinicaId, x.Tipo })
            .Select(g => new { g.Key.HistoriaClinicaId, g.Key.Tipo, N = g.Count() })
            .ToListAsync(ct);

        var med = medCounts.ToDictionary(x => x.Id, x => x.N);
        var srv = srvCounts.ToDictionary(x => x.Id, x => x.N);
        var rem = remCounts.ToDictionary(x => x.Id, x => x.N);
        var inc = incCounts.ToDictionary(x => x.Id, x => x.N);
        var cert = certCounts.ToDictionary(x => x.Id, x => x.N);
        var ins = insCounts.ToDictionary(x => x.Id, x => x.N);
        var esc = escCounts.ToDictionary(x => x.Id, x => x.N);
        var rxImag = extCounts
            .Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.RxImagenologia)
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var labExt = extCounts
            .Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.Laboratorio)
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var insExt = extCounts
            .Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.Insumo)
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var evo = docCounts
            .Where(x => x.Tipo == "EVOLUCION")
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var con = docCounts
            .Where(x => x.Tipo == "CONSENTIMIENTO")
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);

        // Capa 08 Ola 2 — resumen del ultimo veredicto del agente por revision.
        // Solo se trae el ultimo evento tipo `PreRevisionAgente` de cada revision:
        // sirve para popular el tooltip del chip "Pre-revision agente" en el grid.
        var revisionIds = rows.Where(r => r.Rv != null).Select(r => r.Rv!.Id).ToList();
        var agenteResumenes = new Dictionary<Guid, string?>();
        if (revisionIds.Count > 0)
        {
            var eventosAgente = await db.RevisionClinicaEventos.AsNoTracking()
                .Where(e => revisionIds.Contains(e.RevisionClinicaId)
                            && e.Tipo == RevisionTipoEvento.PreRevisionAgente)
                .GroupBy(e => e.RevisionClinicaId)
                .Select(g => new
                {
                    RevisionClinicaId = g.Key,
                    Ultimo = g.OrderByDescending(x => x.OcurridoEn).First()
                })
                .ToListAsync(ct);
            agenteResumenes = eventosAgente.ToDictionary(
                x => x.RevisionClinicaId,
                x => x.Ultimo.Nota ?? x.Ultimo.Motivo);
        }

        // Lookup EPS por paciente: primer contrato del paciente por Orden en
        // paciente_contratos -> Contrato -> Aseguradora. Post-PC4 no existen
        // slots fijos; el contrato "principal" es el orden=1.
        var pacienteIds = rows.Select(r => r.Pa.Id).Distinct().ToList();
        var pacienteToContratoPrincipal = pacienteIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await db.PacienteContratos.AsNoTracking()
                .Where(pc => pacienteIds.Contains(pc.PacienteId))
                .OrderBy(pc => pc.PacienteId).ThenBy(pc => pc.Orden)
                .Select(pc => new { pc.PacienteId, pc.ContratoAseguradoraId })
                .ToListAsync(ct))
                .GroupBy(x => x.PacienteId)
                .ToDictionary(g => g.Key, g => g.First().ContratoAseguradoraId);
        var contrato1Ids = pacienteToContratoPrincipal.Values.Distinct().ToList();
        var contratoToAse = contrato1Ids.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.ContratosAseguradora.AsNoTracking()
                .Where(c => contrato1Ids.Contains(c.Id))
                .Select(c => new { c.Id, c.AseguradoraId })
                .ToDictionaryAsync(x => x.Id, x => x.AseguradoraId, ct);
        var aseIds = contratoToAse.Values.Distinct().ToList();
        var aseIdToNombre = aseIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Aseguradoras.AsNoTracking()
                .Where(a => aseIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Nombre })
                .ToDictionaryAsync(x => x.Id, x => x.Nombre, ct);

        // Lookup Sede por paciente: Paciente.SedeAtencionId -> Sucursal.Nombre.
        var sedeIds = rows
            .Where(r => r.Pa.SedeAtencionId.HasValue)
            .Select(r => r.Pa.SedeAtencionId!.Value)
            .Distinct()
            .ToList();
        var sedeIdToNombre = sedeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Sucursales.AsNoTracking()
                .Where(s => sedeIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Nombre })
                .ToDictionaryAsync(x => x.Id, x => x.Nombre, ct);

        return rows.Select(r =>
        {
            Guid? aseId = null;
            string? aseNombre = null;
            if (pacienteToContratoPrincipal.TryGetValue(r.Pa.Id, out var c1)
                && contratoToAse.TryGetValue(c1, out var aId))
            {
                aseId = aId;
                aseIdToNombre.TryGetValue(aId, out aseNombre);
            }
            Guid? sedeId = r.Pa.SedeAtencionId;
            string? sedeNombre = null;
            if (sedeId is Guid sid) { sedeIdToNombre.TryGetValue(sid, out sedeNombre); }
            return new OrdenClinicaItemDto(
                r.Hc.Id,
                r.Pa.Id,
                r.Pa.NombreCompleto,
                r.Pa.TipoDocumento,
                r.Pa.NumeroDocumento,
                r.Hc.Estado.ToString(),
                r.Hc.FechaApertura,
                r.Hc.FechaCierre,
                r.Fo.Nombre,
                r.Hc.EspecialistaNombre,
                med.GetValueOrDefault(r.Hc.Id, 0),
                srv.GetValueOrDefault(r.Hc.Id, 0),
                rem.GetValueOrDefault(r.Hc.Id, 0),
                inc.GetValueOrDefault(r.Hc.Id, 0),
                cert.GetValueOrDefault(r.Hc.Id, 0),
                ins.GetValueOrDefault(r.Hc.Id, 0),
                rxImag.GetValueOrDefault(r.Hc.Id, 0),
                labExt.GetValueOrDefault(r.Hc.Id, 0),
                insExt.GetValueOrDefault(r.Hc.Id, 0),
                esc.GetValueOrDefault(r.Hc.Id, 0),
                evo.GetValueOrDefault(r.Hc.Id, 0),
                con.GetValueOrDefault(r.Hc.Id, 0),
                r.Rv?.Id,
                r.Rv?.EstadoAgregado,
                r.Rv?.EstadoAgente,
                r.Rv?.IteracionActual,
                r.Rv is null ? null : agenteResumenes.GetValueOrDefault(r.Rv.Id),
                aseNombre,
                aseId,
                sedeNombre,
                sedeId
            );
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> ListarEspecialistasAsync(CancellationToken ct = default)
    {
        return await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.EspecialistaNombre != null && h.EspecialistaNombre != "")
            .Select(h => h.EspecialistaNombre!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AseguradoraOpcionDto>> ListarAseguradorasAsync(CancellationToken ct = default)
    {
        // Solo aseguradoras que realmente aparecen en el listado — HCs ->
        // Paciente -> paciente_contratos -> Contrato -> Aseguradora. Evita
        // ensuciar el filtro con EPSes que el tenant configuro pero que
        // nadie usa clinicamente.
        //
        // Partido en dos queries porque EF Core no traduce Distinct()+Join
        // en cadena tras aplicar query filters de tenant (falla en runtime).
        // Con lista N: un mismo paciente puede tener N contratos y por lo
        // tanto contribuir N EPSes al filtro — todas se incluyen.
        var pacienteIdsConHc = await db.HistoriasClinicas.AsNoTracking()
            .Select(h => h.PacienteId).Distinct().ToListAsync(ct);
        if (pacienteIdsConHc.Count == 0) { return Array.Empty<AseguradoraOpcionDto>(); }

        var contratoIdValues = await db.PacienteContratos.AsNoTracking()
            .Where(pc => pacienteIdsConHc.Contains(pc.PacienteId))
            .Select(pc => pc.ContratoAseguradoraId)
            .Distinct()
            .ToListAsync(ct);
        if (contratoIdValues.Count == 0) { return Array.Empty<AseguradoraOpcionDto>(); }

        var aseguradoraIds = await db.ContratosAseguradora.AsNoTracking()
            .Where(c => contratoIdValues.Contains(c.Id))
            .Select(c => c.AseguradoraId)
            .Distinct()
            .ToListAsync(ct);
        if (aseguradoraIds.Count == 0) { return Array.Empty<AseguradoraOpcionDto>(); }

        return await db.Aseguradoras.AsNoTracking()
            .Where(a => aseguradoraIds.Contains(a.Id))
            .OrderBy(a => a.Nombre)
            .Select(a => new AseguradoraOpcionDto(a.Id, a.Nombre))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SucursalOpcionDto>> ListarSucursalesAsync(CancellationToken ct = default)
    {
        // Solo sucursales que aparecen como SedeAtencionId de pacientes con HCs.
        // Partido en dos queries por la misma razon que Aseguradoras: EF Core
        // no traduce Distinct()+Join en cadena tras query filters.
        var sedeIds = await db.HistoriasClinicas.AsNoTracking()
            .Join(db.Pacientes.AsNoTracking(), h => h.PacienteId, p => p.Id,
                (h, p) => p.SedeAtencionId)
            .Where(s => s != null)
            .Distinct()
            .ToListAsync(ct);
        if (sedeIds.Count == 0) { return Array.Empty<SucursalOpcionDto>(); }

        var sedeIdValues = sedeIds.Select(s => s!.Value).ToList();
        return await db.Sucursales.AsNoTracking()
            .Where(s => sedeIdValues.Contains(s.Id))
            .OrderBy(s => s.Nombre)
            .Select(s => new SucursalOpcionDto(s.Id, s.Nombre))
            .ToListAsync(ct);
    }

    public async Task<OrdenesArchivoExportado> ExportarExcelAsync(OrdenesClinicasFiltro filtro, CancellationToken ct = default)
    {
        var rows = await BuscarAsync(filtro, ct);

        using var wb = new XLWorkbook();
        var hoja = wb.Worksheets.Add("Ordenes clinicas");

        // Headers (linea 1): mismos titulos que la tabla en pantalla.
        string[] headers = {
            "Paciente", "Documento", "Formato", "Especialista", "Aseguradora", "Sede",
            "Estado", "Fecha", "Total ordenes", "Revision", "Agente IA",
            "Medicamentos", "Servicios", "Insumos", "Remisiones", "Incapacidades",
            "Certificaciones", "RxImag", "Laboratorios", "Insumos externos",
            "Escalas", "Evoluciones", "Consentimientos",
        };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = hoja.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        }

        // Filas
        for (var r = 0; r < rows.Count; r++)
        {
            var row = r + 2;
            var it = rows[r];
            var totalOrdenes =
                it.MedicamentosCount + it.ServiciosCount + it.InsumosCount + it.RemisionesCount +
                it.IncapacidadesCount + it.CertificacionesCount + it.RxImagCount + it.LabExtCount +
                it.InsExtCount + it.EscalasCount + it.EvolucionesCount + it.ConsentimientosCount;
            var fecha = (it.FechaCierre ?? it.FechaApertura).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            hoja.Cell(row, 1).Value = it.PacienteNombre;
            hoja.Cell(row, 2).Value = $"{it.PacienteTipoDoc} {it.PacienteDoc}".Trim();
            hoja.Cell(row, 3).Value = it.FormatoNombre;
            hoja.Cell(row, 4).Value = it.EspecialistaNombre ?? "";
            hoja.Cell(row, 5).Value = it.AseguradoraNombre ?? "";
            hoja.Cell(row, 6).Value = it.SedeNombre ?? "";
            hoja.Cell(row, 7).Value = it.Estado;
            hoja.Cell(row, 8).Value = fecha;
            hoja.Cell(row, 9).Value = totalOrdenes;
            hoja.Cell(row, 10).Value = it.RevisionEstado?.ToString() ?? "";
            hoja.Cell(row, 11).Value = it.RevisionAgente?.ToString() ?? "";
            hoja.Cell(row, 12).Value = it.MedicamentosCount;
            hoja.Cell(row, 13).Value = it.ServiciosCount;
            hoja.Cell(row, 14).Value = it.InsumosCount;
            hoja.Cell(row, 15).Value = it.RemisionesCount;
            hoja.Cell(row, 16).Value = it.IncapacidadesCount;
            hoja.Cell(row, 17).Value = it.CertificacionesCount;
            hoja.Cell(row, 18).Value = it.RxImagCount;
            hoja.Cell(row, 19).Value = it.LabExtCount;
            hoja.Cell(row, 20).Value = it.InsExtCount;
            hoja.Cell(row, 21).Value = it.EscalasCount;
            hoja.Cell(row, 22).Value = it.EvolucionesCount;
            hoja.Cell(row, 23).Value = it.ConsentimientosCount;
        }

        hoja.Columns().AdjustToContents(1, Math.Max(1, rows.Count + 1));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        // No usamos Date.Now directo — el nombre lleva la fecha local del server
        // como suffix legible; los caracteres invalidos ya no aparecen porque
        // el prefijo es fijo.
        var nombre = $"ordenes-clinicas-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        return new OrdenesArchivoExportado(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombre);
    }
}
