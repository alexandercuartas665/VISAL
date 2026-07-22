using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion.Selectors;

/// <summary>
/// Implementacion actual (v2) del selector:
/// - Unidad base: <see cref="AsignacionTurnoSesion"/> — cada sesion que el
///   profesional efectivamente atendio genera una fila potencial.
/// - Filtro fecha: <see cref="AsignacionTurnoSesion.FechaAtencion"/> en el rango.
/// - Filtro EPS: Sesion -> Turno -> Asignacion -> Contrato.AseguradoraId.
/// - Requisito adicional: el paciente del turno debe tener AL MENOS una
///   HistoriaClinica en estado Cerrada (evidencia de que la atencion ya se
///   documento). No exigimos que la HC este ligada al turno especifico porque
///   el modelo actual no soporta ese vinculo directo.
/// - Regla paquete: cuando varios turnos comparten <c>PaqueteInstanciaId</c>,
///   solo emitimos una fila (la de la asignacion cuyo <c>PaqueteValorPactado</c>
///   esta definido — la fila-ancla del paquete). Las demas se descartan.
///
/// Toda la resolucion se hace con lookups en memoria por PK: nada de joins
/// EF Core en cadena tras query filters (patron ya adoptado en Ordenes).
/// </summary>
public sealed class RelacionFacturasSelector(IApplicationDbContext db) : IRelacionFacturasSelector
{
    public async Task<IReadOnlyList<RelacionFacturasHecho>> SelectAsync(
        RelacionFacturasFiltros filtros,
        CancellationToken ct = default)
    {
        // 1) Sesiones en rango. Ordenadas por fecha/hora para dar estabilidad al output.
        var sesiones = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => s.FechaAtencion >= filtros.FechaInicio
                     && s.FechaAtencion <= filtros.FechaFin)
            .OrderBy(s => s.FechaAtencion).ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
        if (sesiones.Count == 0) { return Array.Empty<RelacionFacturasHecho>(); }

        // 2) Turnos + asignaciones (lookups por PK).
        var turnos = await CargarPorIdAsync(db.AsignacionTurnos,
            sesiones.Select(s => s.AsignacionTurnoId).Distinct().ToList(), ct);
        var asigIds = turnos.Values.Select(t => t.AsignacionId).Distinct().ToList();
        var asignaciones = await CargarPorIdAsync(db.Asignaciones, asigIds, ct);

        // 3) Contratos por codigo (Asignacion guarda el codigo, no el Guid).
        var codigosContrato = asignaciones.Values.Select(a => a.ContratoCodigo).Distinct().ToList();
        var contratos = await db.ContratosAseguradora.AsNoTracking()
            .Where(c => codigosContrato.Contains(c.CodigoContrato))
            .ToListAsync(ct);
        var contratoPorCodigo = contratos.ToDictionary(c => c.CodigoContrato);

        // 4) Filtro aseguradora + sucursal aplicado ANTES de cargar el resto —
        //    reduce el fan-out de las queries siguientes.
        var aseguradora = await db.Aseguradoras.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == filtros.AseguradoraId, ct);
        if (aseguradora is null) { return Array.Empty<RelacionFacturasHecho>(); }

        var sucursales = await db.Sucursales.AsNoTracking().ToListAsync(ct);
        var sucursalPorCodigo = sucursales.ToDictionary(s => s.Codigo, s => s, StringComparer.OrdinalIgnoreCase);

        // 5) Pacientes con HC cerrada — el requisito adicional del criterio. En vez
        //    de una consulta N+1, traemos el conjunto de PacienteIds que aparecen
        //    en HistoriasClinicas con Estado=Cerrada y lo cruzamos en memoria.
        var pacienteIdsCandidatos = asignaciones.Values.Select(a => a.PacienteId).Distinct().ToList();
        var pacientesConHcCerrada = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Estado == HistoriaClinicaEstado.Cerrada
                     && pacienteIdsCandidatos.Contains(h.PacienteId))
            .Select(h => h.PacienteId)
            .Distinct()
            .ToListAsync(ct);
        var pacienteHasHcCerrada = pacientesConHcCerrada.ToHashSet();

        // 5b) Gate "HC revisada por sede" (FCC-R). Cada sucursal decide con el flag
        //     Sucursal.ExigirHcRevisadaParaFacturar si sus pacientes deben tener HC
        //     con RevisionClinica en estado terminal positivo (Aprobada o ArchivadaOk)
        //     para poder facturar. Si ninguna sede lo exige, este bloque se salta y
        //     no cuesta queries adicionales.
        var sedesExigenRevision = sucursales
            .Where(s => s.ExigirHcRevisadaParaFacturar)
            .Select(s => s.Id)
            .ToHashSet();
        HashSet<Guid> pacienteHasHcRevisada = new();
        Dictionary<Guid, Guid?> pacienteSedeMap = new();
        if (sedesExigenRevision.Count > 0)
        {
            pacienteSedeMap = await db.Pacientes.AsNoTracking()
                .Where(p => pacienteIdsCandidatos.Contains(p.Id))
                .Select(p => new { p.Id, p.SedeAtencionId })
                .ToDictionaryAsync(x => x.Id, x => x.SedeAtencionId, ct);

            // Cruce en 2 pasos (evita joins que EF Core no traduce bien tras query filters):
            //  a) Ids de HC de los pacientes candidatos.
            //  b) Ids de esas HC que tienen RevisionClinica.EstadoAgregado terminal positivo.
            var hcCandidatas = await db.HistoriasClinicas.AsNoTracking()
                .Where(h => pacienteIdsCandidatos.Contains(h.PacienteId))
                .Select(h => new { h.Id, h.PacienteId })
                .ToListAsync(ct);
            var hcIds = hcCandidatas.Select(x => x.Id).ToList();
            var hcRevisadasIds = hcIds.Count == 0
                ? new List<Guid>()
                : await db.RevisionesClinica.AsNoTracking()
                    .Where(r => hcIds.Contains(r.HistoriaClinicaId)
                             && (r.EstadoAgregado == RevisionEstadoAgregado.Aprobada
                              || r.EstadoAgregado == RevisionEstadoAgregado.ArchivadaOk))
                    .Select(r => r.HistoriaClinicaId)
                    .ToListAsync(ct);
            var hcRevisadasSet = hcRevisadasIds.ToHashSet();
            pacienteHasHcRevisada = hcCandidatas
                .Where(x => hcRevisadasSet.Contains(x.Id))
                .Select(x => x.PacienteId)
                .ToHashSet();
        }

        // 6) Prefiltrado: nos quedamos solo con las sesiones que pasan aseguradora +
        //    sucursal + HC-cerrada + gate HC-revisada-por-sede. Sesiones con
        //    turno/asig/contrato faltantes se descartan silenciosamente (datos
        //    corruptos, mismo comportamiento que el builder original).
        var sesionesFiltradas = new List<(AsignacionTurnoSesion Sesion, AsignacionTurno Turno, Asignacion Asignacion, ContratoAseguradora Contrato, Sucursal? Sucursal)>();
        foreach (var sesion in sesiones)
        {
            if (!turnos.TryGetValue(sesion.AsignacionTurnoId, out var turno)) { continue; }
            if (!asignaciones.TryGetValue(turno.AsignacionId, out var asig)) { continue; }
            if (!contratoPorCodigo.TryGetValue(asig.ContratoCodigo, out var contrato)) { continue; }
            if (contrato.AseguradoraId != filtros.AseguradoraId) { continue; }

            Sucursal? sucursal = null;
            if (!string.IsNullOrEmpty(asig.Sucursal))
            {
                sucursalPorCodigo.TryGetValue(asig.Sucursal, out sucursal);
            }
            if (filtros.SucursalIds is { Count: > 0 })
            {
                if (sucursal is null || !filtros.SucursalIds.Contains(sucursal.Id)) { continue; }
            }

            if (!pacienteHasHcCerrada.Contains(asig.PacienteId)) { continue; }

            // Gate HC-revisada-por-sede: si la sede del paciente exige revision,
            // el paciente debe tener al menos una HC con estado revision Aprobada
            // o ArchivadaOk. Exclusion silenciosa: la sesion simplemente no aparece.
            if (sedesExigenRevision.Count > 0
                && pacienteSedeMap.TryGetValue(asig.PacienteId, out var sedePaciente)
                && sedePaciente is Guid sedeId
                && sedesExigenRevision.Contains(sedeId)
                && !pacienteHasHcRevisada.Contains(asig.PacienteId))
            {
                continue;
            }

            sesionesFiltradas.Add((sesion, turno, asig, contrato, sucursal));
        }
        if (sesionesFiltradas.Count == 0) { return Array.Empty<RelacionFacturasHecho>(); }

        // 7) Cargas complementarias (solo sobre lo que sobrevivio el prefiltro).
        var pacientes = await CargarPorIdAsync(db.Pacientes,
            sesionesFiltradas.Select(x => x.Asignacion.PacienteId).Distinct().ToList(), ct);
        var profesionales = await CargarPorIdAsync(db.Profesionales,
            sesionesFiltradas.Select(x => x.Turno.ProfesionalId).Distinct().ToList(), ct);

        var contratoIds = sesionesFiltradas.Select(x => x.Contrato.Id).Distinct().ToList();
        var serviciosContrato = await db.ServiciosContrato.AsNoTracking()
            .Where(sc => contratoIds.Contains(sc.ContratoId))
            .ToListAsync(ct);
        var servicioPorCodigo = serviciosContrato
            .Where(sc => !string.IsNullOrEmpty(sc.CodigoServicio))
            .GroupBy(sc => (sc.ContratoId, sc.CodigoServicio!))
            .ToDictionary(g => g.Key, g => g.First());
        var servicioPorId = serviciosContrato.ToDictionary(sc => sc.Id, sc => sc);

        var paqueteCodigos = sesionesFiltradas
            .Where(x => !string.IsNullOrEmpty(x.Asignacion.PaqueteCodigo))
            .Select(x => x.Asignacion.PaqueteCodigo!).Distinct().ToList();
        var paquetes = paqueteCodigos.Count == 0
            ? new List<Paquete>()
            : await db.Paquetes.AsNoTracking()
                .Include(p => p.Servicios)
                .Where(p => paqueteCodigos.Contains(p.Codigo))
                .ToListAsync(ct);
        var paquetePorCodigo = paquetes.ToDictionary(p => p.Codigo, p => p);

        // Codigos CUPS que aparecen (para descripcion desde catalogo referencia).
        var codigosCups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sc in serviciosContrato) { if (!string.IsNullOrEmpty(sc.CodigoServicio)) { codigosCups.Add(sc.CodigoServicio); } }
        foreach (var p in paquetes) { foreach (var s in p.Servicios) { codigosCups.Add(s.Codigo); } }
        var codigosList = codigosCups.ToList();
        var catalogo = codigosList.Count == 0
            ? new List<CatalogoServicioReferencia>()
            : await db.CatalogosServicioReferencia.AsNoTracking()
                .Where(c => codigosList.Contains(c.Codigo))
                .ToListAsync(ct);
        var catalogoPorCodigo = catalogo.ToDictionary(c => c.Codigo, c => c, StringComparer.OrdinalIgnoreCase);

        // Geografia del paciente.
        var depIds = pacientes.Values.Where(p => p.DepartamentoId is not null).Select(p => p.DepartamentoId!.Value).Distinct().ToList();
        var munIds = pacientes.Values.Where(p => p.MunicipioId is not null).Select(p => p.MunicipioId!.Value).Distinct().ToList();
        var paisIds = pacientes.Values.Where(p => p.PaisResidenciaId is not null).Select(p => p.PaisResidenciaId!.Value).Distinct().ToList();
        var depts = depIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Departamentos.AsNoTracking()
            .Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Nombre, ct);
        var muns = munIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Municipios.AsNoTracking()
            .Where(m => munIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);
        var paisesNombre = paisIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Paises.AsNoTracking()
            .Where(p => paisIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        // 8) Construccion de hechos + regla paquete (dedup por lote-ancla).
        var hechos = new List<RelacionFacturasHecho>(sesionesFiltradas.Count);
        var lotesPaqueteYaEmitidos = new HashSet<Guid>();

        foreach (var (sesion, turno, asig, contrato, sucursal) in sesionesFiltradas)
        {
            if (asig.PaqueteInstanciaId is Guid loteId)
            {
                if (asig.PaqueteValorPactado is null) { continue; } // no-ancla
                if (!lotesPaqueteYaEmitidos.Add(loteId)) { continue; } // ancla ya emitida
            }

            if (!pacientes.TryGetValue(asig.PacienteId, out var paciente)) { continue; }
            profesionales.TryGetValue(turno.ProfesionalId, out var profesional);

            ServicioContrato? servicio = null;
            if (Guid.TryParse(asig.ServicioId, out var scGuid) && servicioPorId.TryGetValue(scGuid, out var scPorGuid))
            {
                servicio = scPorGuid;
            }
            else if (servicioPorCodigo.TryGetValue((contrato.Id, asig.ServicioId), out var scPorCod))
            {
                servicio = scPorCod;
            }

            string? cupsCodigo = servicio?.CodigoServicio;
            Paquete? paquete = null;
            if (asig.PaqueteInstanciaId is not null
                && !string.IsNullOrEmpty(asig.PaqueteCodigo)
                && paquetePorCodigo.TryGetValue(asig.PaqueteCodigo, out var p))
            {
                paquete = p;
                var pServ = ResolverPaqueteServicioRepresentativo(p);
                if (pServ is not null) { cupsCodigo = pServ.Codigo; }
            }
            string? cupsDescripcion = null;
            if (!string.IsNullOrEmpty(cupsCodigo) && catalogoPorCodigo.TryGetValue(cupsCodigo, out var cat))
            {
                cupsDescripcion = cat.Nombre;
            }

            string? deptoNombre = paciente.DepartamentoId is Guid dId && depts.TryGetValue(dId, out var dn) ? dn : null;
            string? munNombre = paciente.MunicipioId is Guid mId && muns.TryGetValue(mId, out var mn) ? mn : null;
            string? nacNombre = paciente.PaisResidenciaId is Guid pId && paisesNombre.TryGetValue(pId, out var pn) ? pn : null;

            hechos.Add(new RelacionFacturasHecho(
                sesion, turno, asig, paciente, contrato, aseguradora, sucursal, profesional,
                servicio, paquete, cupsCodigo, cupsDescripcion,
                deptoNombre, munNombre, nacNombre));
        }
        return hechos;
    }

    private static PaqueteServicio? ResolverPaqueteServicioRepresentativo(Paquete paquete)
    {
        if (paquete.CupsRepresentativoServicioId is Guid rid)
        {
            var explicito = paquete.Servicios.FirstOrDefault(s => s.Id == rid);
            if (explicito is not null) { return explicito; }
        }
        return paquete.Servicios.OrderBy(s => s.Codigo, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static async Task<Dictionary<Guid, T>> CargarPorIdAsync<T>(
        DbSet<T> set,
        List<Guid> ids,
        CancellationToken ct) where T : Visal.Domain.Common.BaseEntity
    {
        if (ids.Count == 0) { return new Dictionary<Guid, T>(); }
        var items = await set.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        return items.ToDictionary(x => x.Id, x => x);
    }
}
