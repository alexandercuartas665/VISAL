using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion.Selectors;

/// <summary>
/// Implementacion actual (v3) del selector.
///
/// Motivo del cambio v2 -> v3: la operativa real de prod NO registra sesiones
/// de atencion (la tabla <c>asignacion_turno_sesiones</c> quedo vacia porque
/// el metodo RegistrarSesionAsync nunca se cablea a la UI). Prod si produce
/// HistoriasClinicas con estado Cerrada, y esa es la evidencia real de que
/// una atencion sucedio.
///
/// Contrato v3:
/// - Unidad base: <see cref="HistoriaClinica"/> con Estado = Cerrada.
/// - Filtro fecha: <c>fecha_cierre</c> en [FechaInicio, FechaFin].
/// - Filtro EPS: el paciente pertenece a la aseguradora filtrada por alguno
///   de sus 3 contratos (<c>Paciente.Contrato1Id/2/3</c>). Si el paciente no
///   tiene ninguno de esos 3, se descarta.
/// - Filtro sucursal: <c>Paciente.SedeAtencionId</c> debe estar en la lista;
///   lista vacia = todas.
/// - Gate HC-revisada-por-sede: si la sede exige revision (Sucursal.
///   ExigirHcRevisadaParaFacturar), la HC especifica debe tener
///   RevisionClinica en estado Aprobada/ArchivadaOk. Aqui es mas preciso
///   que en v2: se valida la MISMA HC (no cualquier HC del paciente).
///
/// Todo con lookups en memoria por PK — no hay joins EF Core en cadena tras
/// query filters (patron heredado).
/// </summary>
public sealed class RelacionFacturasSelector(IApplicationDbContext db) : IRelacionFacturasSelector
{
    public async Task<IReadOnlyList<RelacionFacturasHecho>> SelectAsync(
        RelacionFacturasFiltros filtros,
        CancellationToken ct = default)
    {
        // 1) Aseguradora + sus contratos.
        var aseguradora = await db.Aseguradoras.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == filtros.AseguradoraId, ct);
        if (aseguradora is null) { return Array.Empty<RelacionFacturasHecho>(); }

        var contratos = await db.ContratosAseguradora.AsNoTracking()
            .Where(c => c.AseguradoraId == filtros.AseguradoraId)
            .ToListAsync(ct);
        if (contratos.Count == 0) { return Array.Empty<RelacionFacturasHecho>(); }
        var contratoPorId = contratos.ToDictionary(c => c.Id);
        var contratoIdsSet = contratos.Select(c => c.Id).ToHashSet();

        // 2) HCs cerradas en el rango.
        //    fecha_cierre es DateTimeOffset? — filtramos por not null y por
        //    conversion a DateOnly del componente UTC (simple y estable).
        var inicio = new DateTimeOffset(filtros.FechaInicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(filtros.FechaFin.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hcs = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Estado == HistoriaClinicaEstado.Cerrada
                     && h.FechaCierre != null
                     && h.FechaCierre >= inicio
                     && h.FechaCierre < fin)
            .OrderBy(h => h.FechaCierre)
            .ToListAsync(ct);
        if (hcs.Count == 0) { return Array.Empty<RelacionFacturasHecho>(); }

        // 3) Pacientes referenciados.
        var pacienteIds = hcs.Select(h => h.PacienteId).Distinct().ToList();
        var pacientes = await db.Pacientes.AsNoTracking()
            .Where(p => pacienteIds.Contains(p.Id))
            .ToListAsync(ct);
        var pacientePorId = pacientes.ToDictionary(p => p.Id);

        // 4) Sucursales + prep del gate de revision.
        var sucursales = await db.Sucursales.AsNoTracking().ToListAsync(ct);
        var sucursalPorId = sucursales.ToDictionary(s => s.Id);
        var sedesExigenRevision = sucursales
            .Where(s => s.ExigirHcRevisadaParaFacturar)
            .Select(s => s.Id)
            .ToHashSet();
        HashSet<Guid> hcsRevisadasSet = new();
        if (sedesExigenRevision.Count > 0)
        {
            var hcIds = hcs.Select(h => h.Id).ToList();
            var revisadas = await db.RevisionesClinica.AsNoTracking()
                .Where(r => hcIds.Contains(r.HistoriaClinicaId)
                         && (r.EstadoAgregado == RevisionEstadoAgregado.Aprobada
                          || r.EstadoAgregado == RevisionEstadoAgregado.ArchivadaOk))
                .Select(r => r.HistoriaClinicaId)
                .ToListAsync(ct);
            hcsRevisadasSet = revisadas.ToHashSet();
        }

        // 5) Prefiltro: HC valida (paciente conocido) + aseguradora del paciente
        //    coincide + sucursal filtrada + gate revision.
        var hechosPrefiltrados = new List<(HistoriaClinica Hc, Paciente Paciente, ContratoAseguradora Contrato, Sucursal? Sucursal)>();
        foreach (var hc in hcs)
        {
            if (!pacientePorId.TryGetValue(hc.PacienteId, out var paciente)) { continue; }

            // Aseguradora del paciente: uno de los 3 contratos debe apuntar a la
            // aseguradora filtrada. Sin match = no facturable a esta EPS.
            var contratoIdPaciente = ResolverContratoDelPaciente(paciente, contratoIdsSet);
            if (contratoIdPaciente is not Guid cid || !contratoPorId.TryGetValue(cid, out var contrato)) { continue; }

            // Sucursal del paciente.
            Sucursal? sucursal = null;
            if (paciente.SedeAtencionId is Guid sedeId && sucursalPorId.TryGetValue(sedeId, out var s))
            {
                sucursal = s;
            }
            if (filtros.SucursalIds is { Count: > 0 })
            {
                if (sucursal is null || !filtros.SucursalIds.Contains(sucursal.Id)) { continue; }
            }

            // Gate HC-revisada-por-sede (exclusion silenciosa).
            if (sedesExigenRevision.Count > 0
                && sucursal is not null
                && sedesExigenRevision.Contains(sucursal.Id)
                && !hcsRevisadasSet.Contains(hc.Id))
            {
                continue;
            }

            hechosPrefiltrados.Add((hc, paciente, contrato, sucursal));
        }
        if (hechosPrefiltrados.Count == 0) { return Array.Empty<RelacionFacturasHecho>(); }

        // 6) Cargas complementarias (solo sobre lo que sobrevivio).
        var profesionalIds = hechosPrefiltrados
            .Where(x => x.Hc.ProfesionalId is Guid pid && pid != Guid.Empty)
            .Select(x => x.Hc.ProfesionalId!.Value).Distinct().ToList();
        var profesionales = profesionalIds.Count == 0
            ? new Dictionary<Guid, Profesional>()
            : await db.Profesionales.AsNoTracking()
                .Where(p => profesionalIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        // 6.a) Codigo de habilitacion por sede — lo maneja Config Interoperabilidad
        //      (una fila por sede x ambiente). Sin config activo, fallback al
        //      campo directo Sucursal.CodigoHabilitacion.
        var interopConfig = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var ambienteActivo = interopConfig?.AmbienteActivo ?? AmbienteIhce.Sandbox;
        var sucursalIds = hechosPrefiltrados
            .Where(x => x.Sucursal is not null)
            .Select(x => x.Sucursal!.Id).Distinct().ToList();
        var credencialesSede = sucursalIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.InteroperabilidadCredencialesSede.AsNoTracking()
                .Where(c => c.Ambiente == ambienteActivo && sucursalIds.Contains(c.SucursalId))
                .ToDictionaryAsync(c => c.SucursalId, c => c.CodigoHabilitacion, ct);

        // 6.b) Asignacion mas relevante por HC — buscamos por (paciente, contrato)
        //      y elegimos la mas cercana (por fecha) al cierre de la HC. Sirve
        //      para resolver Autorizacion y modulo/TipoArchivoRips que hoy vienen
        //      del flujo de asignacion.
        var pacienteIdsPrefil = hechosPrefiltrados.Select(x => x.Paciente.Id).Distinct().ToList();
        var asignacionesPorPac = pacienteIdsPrefil.Count == 0
            ? new Dictionary<Guid, List<Asignacion>>()
            : (await db.Asignaciones.AsNoTracking()
                .Where(a => pacienteIdsPrefil.Contains(a.PacienteId))
                .ToListAsync(ct))
                .GroupBy(a => a.PacienteId)
                .ToDictionary(g => g.Key, g => g.ToList());

        // 6.c) TipoArchivoRips desde el catalogo de tipos de servicio — mapeamos
        //      por codigo del modulo de la asignacion (CONSULTA/TERAPIA/...).
        var catalogoTipos = await db.CatalogosTipoServicio.AsNoTracking()
            .Where(c => c.Activo)
            .ToDictionaryAsync(c => c.Codigo, c => c.TipoArchivoRips, StringComparer.OrdinalIgnoreCase, ct);

        // 6.d) Tarifa unitaria del ServicioContrato (mapeamos por Guid ya que
        //      Asignacion.ServicioId guarda el Guid del servicio contratado
        //      como string — ver AsignacionService linea 557 patron heredado).
        var servicioContratoIds = asignacionesPorPac.Values
            .SelectMany(l => l)
            .Where(a => Guid.TryParse(a.ServicioId, out _))
            .Select(a => Guid.Parse(a.ServicioId))
            .Distinct().ToList();
        var serviciosContrato = servicioContratoIds.Count == 0
            ? new Dictionary<Guid, ServicioContrato>()
            : await db.ServiciosContrato.AsNoTracking()
                .Where(s => servicioContratoIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

        // Descripciones CUPS via catalogo — usamos el diagnostico principal del
        // paciente como proxy cuando no hay servicio contrato claro. Es lo mas
        // que podemos derivar sin cablear turno/asignacion.
        var codigosCups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in hechosPrefiltrados)
        {
            if (!string.IsNullOrEmpty(x.Paciente.Cie10Codigo)) { codigosCups.Add(x.Paciente.Cie10Codigo); }
        }
        var codigosList = codigosCups.ToList();
        var catalogo = codigosList.Count == 0
            ? new Dictionary<string, string>()
            : await db.CatalogosServicioReferencia.AsNoTracking()
                .Where(c => codigosList.Contains(c.Codigo))
                .ToDictionaryAsync(c => c.Codigo, c => c.Nombre, StringComparer.OrdinalIgnoreCase, ct);

        // Geografia del paciente.
        var depIds = hechosPrefiltrados.Where(x => x.Paciente.DepartamentoId is not null)
            .Select(x => x.Paciente.DepartamentoId!.Value).Distinct().ToList();
        var munIds = hechosPrefiltrados.Where(x => x.Paciente.MunicipioId is not null)
            .Select(x => x.Paciente.MunicipioId!.Value).Distinct().ToList();
        var paisIds = hechosPrefiltrados.Where(x => x.Paciente.PaisResidenciaId is not null)
            .Select(x => x.Paciente.PaisResidenciaId!.Value).Distinct().ToList();
        var depts = depIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Departamentos.AsNoTracking()
            .Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Nombre, ct);
        var muns = munIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Municipios.AsNoTracking()
            .Where(m => munIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);
        var paisesNombre = paisIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Paises.AsNoTracking()
            .Where(p => paisIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        // 7) Construccion de hechos finales.
        var hechos = new List<RelacionFacturasHecho>(hechosPrefiltrados.Count);
        foreach (var (hc, paciente, contrato, sucursal) in hechosPrefiltrados)
        {
            Profesional? profesional = null;
            if (hc.ProfesionalId is Guid pid) { profesionales.TryGetValue(pid, out profesional); }

            string? cupsCodigo = paciente.Cie10Codigo;
            string? cupsDescripcion = null;
            if (!string.IsNullOrEmpty(cupsCodigo) && catalogo.TryGetValue(cupsCodigo, out var cn))
            {
                cupsDescripcion = cn;
            }

            string? deptoNombre = paciente.DepartamentoId is Guid dId && depts.TryGetValue(dId, out var dn) ? dn : null;
            string? munNombre = paciente.MunicipioId is Guid mId && muns.TryGetValue(mId, out var mn) ? mn : null;
            string? nacNombre = paciente.PaisResidenciaId is Guid pId && paisesNombre.TryGetValue(pId, out var pn) ? pn : null;

            // Codigo habilitacion: credencial de interop (ambiente activo) ganan
            // sobre el campo directo de la sede.
            string? codHabResuelto = null;
            if (sucursal is not null)
            {
                if (credencialesSede.TryGetValue(sucursal.Id, out var chIhce) && !string.IsNullOrWhiteSpace(chIhce))
                {
                    codHabResuelto = chIhce;
                }
                else
                {
                    codHabResuelto = sucursal.CodigoHabilitacion;
                }
            }

            // Asignacion mas relevante: preferimos misma sede + mismo contrato
            // codigo, y de esas priorizamos las que tengan TipoPago poblado
            // (para no perder cuota/copago), luego la mas cercana en fecha al
            // cierre de la HC.
            Asignacion? asigRelevante = null;
            if (asignacionesPorPac.TryGetValue(paciente.Id, out var listaAsig))
            {
                var fechaCierre = hc.FechaCierre?.LocalDateTime ?? hc.UpdatedAt?.LocalDateTime ?? hc.CreatedAt.LocalDateTime;
                asigRelevante = listaAsig
                    .Where(a => a.ContratoCodigo == contrato.CodigoContrato)
                    .OrderByDescending(a => !string.IsNullOrEmpty(a.TipoPago))
                    .ThenBy(a => Math.Abs((a.FechaInicio.ToDateTime(TimeOnly.MinValue) - fechaCierre).TotalDays))
                    .FirstOrDefault()
                    ?? listaAsig
                        .OrderByDescending(a => !string.IsNullOrEmpty(a.TipoPago))
                        .ThenBy(a => Math.Abs((a.FechaInicio.ToDateTime(TimeOnly.MinValue) - fechaCierre).TotalDays))
                        .FirstOrDefault();
            }

            // TipoArchivoRips desde el modulo de la asignacion via catalogo.
            string? tipoArchivoRips = null;
            if (!string.IsNullOrEmpty(asigRelevante?.Modulo)
                && catalogoTipos.TryGetValue(asigRelevante.Modulo, out var tar))
            {
                tipoArchivoRips = tar;
            }
            else if (!string.IsNullOrEmpty(asigRelevante?.TipoServicio)
                && catalogoTipos.TryGetValue(asigRelevante.TipoServicio, out var tar2))
            {
                tipoArchivoRips = tar2;
            }

            string? codigoAutorizacion = asigRelevante?.CodigoAutorizacion;

            // Nombre del servicio y tarifa unitaria — vienen del par
            // (Asignacion, ServicioContrato). NombreServicio esta denormalizado
            // en la asignacion; la tarifa vive en el contrato de servicio.
            string? nombreServicio = asigRelevante?.NombreServicio;
            decimal? valorUnitario = null;
            if (asigRelevante is not null
                && Guid.TryParse(asigRelevante.ServicioId, out var sid)
                && serviciosContrato.TryGetValue(sid, out var sc))
            {
                valorUnitario = sc.Tarifa;
            }

            // Cuota moderadora / copago — mutuamente excluyentes segun TipoPago
            // de la asignacion. Preferimos el valor real que pago el paciente;
            // si no se registro, caemos al sugerido por el catalogo de cuotas.
            decimal? cuotaModeradora = null;
            decimal? copago = null;
            if (asigRelevante is not null)
            {
                var valorPago = asigRelevante.ValorPagoReal ?? asigRelevante.ValorPagoSugerido;
                if (string.Equals(asigRelevante.TipoPago, "CUOTA", StringComparison.OrdinalIgnoreCase))
                {
                    cuotaModeradora = valorPago;
                }
                else if (string.Equals(asigRelevante.TipoPago, "COPAGO", StringComparison.OrdinalIgnoreCase))
                {
                    copago = valorPago;
                }
            }

            hechos.Add(new RelacionFacturasHecho(
                hc, paciente, contrato, aseguradora, sucursal, profesional,
                cupsCodigo, cupsDescripcion,
                deptoNombre, munNombre, nacNombre,
                codHabResuelto, tipoArchivoRips, codigoAutorizacion,
                nombreServicio, valorUnitario, cuotaModeradora, copago));
        }
        return hechos;
    }

    /// <summary>
    /// Devuelve el ContratoAseguradora.Id del paciente que apunta a la aseguradora
    /// filtrada (representada por <paramref name="contratoIdsPermitidos"/>).
    /// Prioridad: Contrato1 -> Contrato2 -> Contrato3. Devuelve null si ninguno
    /// coincide (el paciente no factura a esa EPS).
    /// </summary>
    private static Guid? ResolverContratoDelPaciente(Paciente p, HashSet<Guid> contratoIdsPermitidos)
    {
        if (p.Contrato1Id is Guid c1 && contratoIdsPermitidos.Contains(c1)) { return c1; }
        if (p.Contrato2Id is Guid c2 && contratoIdsPermitidos.Contains(c2)) { return c2; }
        if (p.Contrato3Id is Guid c3 && contratoIdsPermitidos.Contains(c3)) { return c3; }
        return null;
    }
}
