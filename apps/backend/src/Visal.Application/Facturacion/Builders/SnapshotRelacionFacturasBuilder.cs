using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion.Builders;

/// <summary>
/// Builder del snapshot Relacion de Facturas (Tipo 1 del motor). Produce las 41
/// columnas EXACTAS del template EPS/MinSalud — nombres, orden, tildes rotas
/// incluidas (spec: "07. Facturacion / 2. Snapshot Relacion de Facturas").
///
/// Granularidad: 1 fila = 1 <see cref="NotaMedica"/> definitiva en el rango.
/// Excepcion paquete: notas provenientes del mismo <c>PaqueteInstanciaId</c>
/// producen UNA sola fila (la del turno que trae <c>PaqueteValorPactado</c>);
/// las demas se descartan. La fila del paquete usa el CUPS + descripcion +
/// datos del <see cref="Paquete.CupsRepresentativoServicio"/>. Si el paquete
/// no lo tiene definido, cae a <c>Servicios.OrderBy(Codigo).First()</c>.
///
/// Filtros del JSON: <c>aseguradoraId</c> (Guid, obligatorio), <c>sucursalIds</c>
/// (lista de Guids, opcional — vacio = todas), <c>fechaInicio</c>/<c>fechaFin</c>
/// (yyyy-MM-dd). Multi-EPS se resuelve en el motor generico como N snapshots.
/// </summary>
public sealed class SnapshotRelacionFacturasBuilder(IApplicationDbContext db) : ISnapshotBuilder
{
    public TipoSnapshot TipoAplicable => TipoSnapshot.RelacionFacturas;

    // Orden EXACTO del template EPS. NO reformatear los espacios ni las tildes.
    public IReadOnlyList<string> Columnas { get; } = new[]
    {
        "Consecutivo Factura",                        //  1
        "Orden",                                      //  2
        "Contrato",                                   //  3
        "codigo habilitacion ",                       //  4  (con espacio final del template)
        "Regimen",                                    //  5
        "Archivo json",                               //  6
        "Autorizacion",                               //  7
        "Tipo_Id",                                    //  8
        "Identificación",                             //  9
        "Primer Apellido",                            // 10
        "Segundo Apellido",                           // 11
        "Primer Nombre",                              // 12
        "Segundo Nombre",                             // 13
        "Fecha de Nacimiento",                        // 14
        "Sexo",                                       // 15
        "Fecha suministro de tecnologia",             // 16
        "Hora",                                       // 17
        "CUPS",                                       // 18
        "Codigo Externo (Factura)",                   // 19
        "Cantidad",                                   // 20
        "Descripción del procedimiento (Factura)",    // 21
        "Valor Unitario",                             // 22
        "Vr Cuota Moderadora ",                       // 23  (con espacio final)
        "Copago o Pago Compartido",                   // 24
        "Valor Total",                                // 25
        "Diagnóstico",                                // 26
        "TipoDocProfesional",                         // 27
        "DocumentoProf",                              // 28
        "NomProf",                                    // 29
        "Finalidad",                                  // 30
        "Causa Externa",                              // 31
        "Modalidad Atención",                         // 32
        "Vía de Ingreso",                             // 33
        "Grupo Servicios",                            // 34
        "Servicios",                                  // 35
        "Nacionalidad",                               // 36
        "Departamento",                               // 37
        "Municipio",                                  // 38
        "Dirección",                                  // 39
        "Telefono",                                   // 40
        "Correo electrónico"                          // 41
    };

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ConstruirAsync(
        string filtrosJson,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var filtros = ParsearFiltros(filtrosJson);

        // Notas definitivas en el rango. Traemos todo el arbol necesario en una
        // sola query para no hacer N+1 sobre 500+ filas de un mes.
        var notas = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.Estado == NotaMedicaEstado.Definitivo
                     && n.FechaNota >= filtros.FechaInicio
                     && n.FechaNota <= filtros.FechaFin)
            .OrderBy(n => n.FechaNota).ThenBy(n => n.HoraNota)
            .ToListAsync(ct);

        if (notas.Count == 0) { yield break; }

        // Indices para JOIN in-memory. Todo tenant-scoped por los global query
        // filters del DbContext — no exponen datos de otros tenants.
        var pacientes = await CargarDictAsync(db.Pacientes, notas.Select(n => n.PacienteId).Distinct().ToList(), ct);
        var turnos = await CargarDictAsync(
            db.AsignacionTurnos,
            notas.Where(n => n.AsignacionTurnoId is not null).Select(n => n.AsignacionTurnoId!.Value).Distinct().ToList(),
            ct);
        var asignaciones = await CargarDictAsync(db.Asignaciones, turnos.Values.Select(t => t.AsignacionId).Distinct().ToList(), ct);
        var profesionales = await CargarDictAsync(db.Profesionales, turnos.Values.Select(t => t.ProfesionalId).Distinct().ToList(), ct);

        // ContratoAseguradora se resuelve por CodigoContrato dentro del tenant.
        var codigosContrato = asignaciones.Values.Select(a => a.ContratoCodigo).Distinct().ToList();
        var contratos = await db.ContratosAseguradora.AsNoTracking()
            .Where(c => codigosContrato.Contains(c.CodigoContrato))
            .ToListAsync(ct);
        var contratoPorCodigo = contratos.ToDictionary(c => c.CodigoContrato, c => c);

        // Aseguradora (FILTRO PRINCIPAL) + Sucursal.
        var aseguradora = await db.Aseguradoras.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == filtros.AseguradoraId, ct);
        var sucursales = await db.Sucursales.AsNoTracking().ToListAsync(ct);
        var sucursalPorCodigo = sucursales.ToDictionary(s => s.Codigo, s => s, StringComparer.OrdinalIgnoreCase);
        var sucursalPorId = sucursales.ToDictionary(s => s.Id, s => s);

        // ServicioContrato indexado por (contratoId, codigoServicio) o por Guid.
        var contratoIds = contratos.Select(c => c.Id).Distinct().ToList();
        var serviciosContrato = await db.ServiciosContrato.AsNoTracking()
            .Where(sc => contratoIds.Contains(sc.ContratoId))
            .ToListAsync(ct);
        var servicioPorCodigo = serviciosContrato
            .Where(sc => !string.IsNullOrEmpty(sc.CodigoServicio))
            .GroupBy(sc => (sc.ContratoId, sc.CodigoServicio!))
            .ToDictionary(g => g.Key, g => g.First());
        var servicioPorId = serviciosContrato.ToDictionary(sc => sc.Id, sc => sc);

        // Paquetes (para reemplazar CUPS por representativo cuando aplica).
        var paqueteCodigos = asignaciones.Values.Where(a => !string.IsNullOrEmpty(a.PaqueteCodigo))
            .Select(a => a.PaqueteCodigo!).Distinct().ToList();
        var paquetes = paqueteCodigos.Count == 0
            ? new List<Paquete>()
            : await db.Paquetes.AsNoTracking()
                .Include(p => p.Servicios)
                .Where(p => paqueteCodigos.Contains(p.Codigo))
                .ToListAsync(ct);
        var paquetePorCodigo = paquetes.ToDictionary(p => p.Codigo, p => p);

        // Catalogo de referencia (CUPS + descripcion). Traemos solo los codigos que aparecen.
        var codigosCups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sc in serviciosContrato) { if (!string.IsNullOrEmpty(sc.CodigoServicio)) { codigosCups.Add(sc.CodigoServicio); } }
        foreach (var p in paquetes) { foreach (var s in p.Servicios) { codigosCups.Add(s.Codigo); } }
        var codigosList = codigosCups.ToList();
        var catalogo = await db.CatalogosServicioReferencia.AsNoTracking()
            .Where(c => codigosList.Contains(c.Codigo))
            .ToListAsync(ct);
        var catalogoPorCodigo = catalogo.ToDictionary(c => c.Codigo, c => c, StringComparer.OrdinalIgnoreCase);

        // Geografia (Departamento, Municipio, Pais) — catalogos globales.
        var depIds = pacientes.Values.Where(p => p.DepartamentoId is not null).Select(p => p.DepartamentoId!.Value).Distinct().ToList();
        var munIds = pacientes.Values.Where(p => p.MunicipioId is not null).Select(p => p.MunicipioId!.Value).Distinct().ToList();
        var paisIds = pacientes.Values.Where(p => p.PaisResidenciaId is not null).Select(p => p.PaisResidenciaId!.Value).Distinct().ToList();
        var depts = await db.Departamentos.AsNoTracking().Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Nombre, ct);
        var muns = await db.Municipios.AsNoTracking().Where(m => munIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);
        var paisesNombre = await db.Paises.AsNoTracking().Where(p => paisIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        // Dedup paquete: agrupamos por (Asignacion, PaqueteInstanciaId) y solo dejamos
        // los turnos cuya Asignacion trae PaqueteValorPactado (regla del spec §5.1).
        var lotesPaqueteYaEmitidos = new HashSet<Guid>();

        foreach (var nota in notas)
        {
            ct.ThrowIfCancellationRequested();

            if (!pacientes.TryGetValue(nota.PacienteId, out var paciente)) { continue; }
            if (nota.AsignacionTurnoId is null || !turnos.TryGetValue(nota.AsignacionTurnoId.Value, out var turno)) { continue; }
            if (!asignaciones.TryGetValue(turno.AsignacionId, out var asignacion)) { continue; }
            if (!contratoPorCodigo.TryGetValue(asignacion.ContratoCodigo, out var contrato)) { continue; }

            // Filtro por aseguradora (obligatorio).
            if (contrato.AseguradoraId != filtros.AseguradoraId) { continue; }

            // Filtro por sucursal (opcional). Asignacion.Sucursal es codigo string.
            Sucursal? sucursal = null;
            if (!string.IsNullOrEmpty(asignacion.Sucursal))
            {
                sucursalPorCodigo.TryGetValue(asignacion.Sucursal, out sucursal);
            }
            if (filtros.SucursalIds is { Count: > 0 })
            {
                if (sucursal is null || !filtros.SucursalIds.Contains(sucursal.Id)) { continue; }
            }

            // Regla paquete: 1 fila por lote. La fila es la de la asignacion cuyo
            // PaqueteValorPactado != null. Las demas se descartan.
            if (asignacion.PaqueteInstanciaId is Guid loteId)
            {
                if (asignacion.PaqueteValorPactado is null) { continue; } // asignacion no-ancla
                if (!lotesPaqueteYaEmitidos.Add(loteId)) { continue; } // ancla ya emitida
            }

            profesionales.TryGetValue(turno.ProfesionalId, out var profesional);

            // Resolver ServicioContrato: Asignacion.ServicioId puede ser Guid string o codigo.
            ServicioContrato? servicio = null;
            if (Guid.TryParse(asignacion.ServicioId, out var scGuid) && servicioPorId.TryGetValue(scGuid, out var scPorGuid))
            {
                servicio = scPorGuid;
            }
            else if (servicioPorCodigo.TryGetValue((contrato.Id, asignacion.ServicioId), out var scPorCod))
            {
                servicio = scPorCod;
            }

            // Determinar CUPS + descripcion + servicio origen (para RIPS).
            // Si es paquete y hay CUPS representativo → usar ese. Si no, usar el
            // ServicioContrato de la asignacion como origen.
            string? cupsCodigo = servicio?.CodigoServicio;
            string? cupsDescripcion = null;
            ServicioContrato? servicioParaRips = servicio;

            if (asignacion.PaqueteInstanciaId is not null
                && !string.IsNullOrEmpty(asignacion.PaqueteCodigo)
                && paquetePorCodigo.TryGetValue(asignacion.PaqueteCodigo, out var paquete))
            {
                var pServ = ResolverPaqueteServicioRepresentativo(paquete);
                if (pServ is not null)
                {
                    cupsCodigo = pServ.Codigo;
                }
            }

            if (!string.IsNullOrEmpty(cupsCodigo) && catalogoPorCodigo.TryGetValue(cupsCodigo, out var cat))
            {
                cupsDescripcion = cat.Nombre;
            }

            // Cuota/Copago mutuamente excluyentes (spec §7.3).
            decimal? vCuota = null, vCopago = null;
            if (string.Equals(asignacion.TipoPago, "CUOTA", StringComparison.OrdinalIgnoreCase))
            {
                vCuota = asignacion.ValorPagoReal ?? asignacion.ValorPagoSugerido;
            }
            else if (string.Equals(asignacion.TipoPago, "COPAGO", StringComparison.OrdinalIgnoreCase))
            {
                vCopago = asignacion.ValorPagoReal ?? asignacion.ValorPagoSugerido;
            }

            // Valor unitario: paquete → PaqueteValorPactado; suelto → ServicioContrato.Tarifa.
            decimal? valorUnitario = asignacion.PaqueteValorPactado ?? servicio?.Tarifa;
            decimal? valorTotal = servicio?.ValorTotal ?? valorUnitario;

            // Nacionalidad: Pais.Nombre via Paciente.PaisResidenciaId (fallback COLOMBIA).
            string nacionalidad = "COLOMBIA";
            if (paciente.PaisResidenciaId is Guid pid && paisesNombre.TryGetValue(pid, out var pn))
            {
                nacionalidad = pn;
            }

            var fila = new Dictionary<string, object?>
            {
                ["Consecutivo Factura"] = null,                                // 1  — proceso posterior
                ["Orden"] = null,                                              // 2  — vacio por ahora
                ["Contrato"] = asignacion.ContratoCodigo,                      // 3
                ["codigo habilitacion "] = sucursal?.CodigoHabilitacion,       // 4
                ["Regimen"] = paciente.Regimen,                                // 5
                ["Archivo json"] = null,                                       // 6  — vacio
                ["Autorizacion"] = asignacion.CodigoAutorizacion,              // 7
                ["Tipo_Id"] = paciente.TipoDocumento,                          // 8
                ["Identificación"] = paciente.NumeroDocumento,                 // 9
                ["Primer Apellido"] = paciente.PrimerApellido,                 // 10
                ["Segundo Apellido"] = paciente.SegundoApellido,               // 11
                ["Primer Nombre"] = paciente.PrimerNombre,                     // 12
                ["Segundo Nombre"] = paciente.SegundoNombre,                   // 13
                ["Fecha de Nacimiento"] = paciente.FechaNacimiento?.ToString("yyyy-MM-dd"), // 14
                ["Sexo"] = paciente.Sexo,                                      // 15
                ["Fecha suministro de tecnologia"] = turno.FechaInicio?.ToString("yyyy-MM-dd"), // 16
                ["Hora"] = nota.HoraNota?.ToString("HH:mm:ss") ?? nota.CreatedAt.ToString("HH:mm:ss"), // 17
                ["CUPS"] = cupsCodigo,                                         // 18
                ["Codigo Externo (Factura)"] = servicio?.CodigoInterno,        // 19 — CodigoInterno hace las veces
                ["Cantidad"] = asignacion.PaqueteInstanciaId is not null ? 1 : turno.Cantidad, // 20
                ["Descripción del procedimiento (Factura)"] = cupsDescripcion, // 21
                ["Valor Unitario"] = valorUnitario,                            // 22
                ["Vr Cuota Moderadora "] = vCuota,                             // 23
                ["Copago o Pago Compartido"] = vCopago,                        // 24
                ["Valor Total"] = valorTotal,                                  // 25
                ["Diagnóstico"] = paciente.Cie10Codigo ?? paciente.DiagnosticoPrincipal, // 26
                ["TipoDocProfesional"] = profesional?.TipoDocumento,           // 27
                ["DocumentoProf"] = profesional?.NumeroDocumento,              // 28
                ["NomProf"] = profesional?.NombreCompleto,                     // 29
                ["Finalidad"] = servicioParaRips?.Finalidad,                   // 30
                ["Causa Externa"] = servicioParaRips?.CausaExterna,            // 31
                ["Modalidad Atención"] = servicioParaRips?.ModalidadAtencion,  // 32
                ["Vía de Ingreso"] = servicioParaRips?.ViaIngreso,             // 33
                ["Grupo Servicios"] = servicioParaRips?.GrupoServicios,        // 34
                ["Servicios"] = servicioParaRips?.Servicios,                   // 35
                ["Nacionalidad"] = nacionalidad,                               // 36
                ["Departamento"] = paciente.DepartamentoId is Guid dId && depts.TryGetValue(dId, out var dn) ? dn : null, // 37
                ["Municipio"] = paciente.MunicipioId is Guid mId && muns.TryGetValue(mId, out var mn) ? mn : null,        // 38
                ["Dirección"] = paciente.Direccion,                            // 39
                ["Telefono"] = paciente.Telefono,                              // 40
                ["Correo electrónico"] = aseguradora?.CorreoFacturacion,       // 41
            };

            yield return fila;
        }
    }

    private static PaqueteServicio? ResolverPaqueteServicioRepresentativo(Paquete paquete)
    {
        if (paquete.CupsRepresentativoServicioId is Guid rid)
        {
            var explicito = paquete.Servicios.FirstOrDefault(s => s.Id == rid);
            if (explicito is not null) { return explicito; }
        }
        // Fallback determinista: el primero por codigo.
        return paquete.Servicios.OrderBy(s => s.Codigo, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static async Task<Dictionary<Guid, T>> CargarDictAsync<T>(
        Microsoft.EntityFrameworkCore.DbSet<T> set,
        List<Guid> ids,
        CancellationToken ct) where T : Visal.Domain.Common.BaseEntity
    {
        if (ids.Count == 0) { return new Dictionary<Guid, T>(); }
        var items = await set.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        return items.ToDictionary(x => x.Id, x => x);
    }

    private sealed record FiltrosRelacionFacturas(
        Guid AseguradoraId,
        IReadOnlyList<Guid>? SucursalIds,
        DateOnly FechaInicio,
        DateOnly FechaFin);

    private static FiltrosRelacionFacturas ParsearFiltros(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;

        Guid aseg = default;
        if (root.TryGetProperty("aseguradoraId", out var ae) && ae.ValueKind == JsonValueKind.String)
        {
            Guid.TryParse(ae.GetString(), out aseg);
        }
        List<Guid>? sedes = null;
        if (root.TryGetProperty("sucursalIds", out var se) && se.ValueKind == JsonValueKind.Array)
        {
            sedes = new List<Guid>();
            foreach (var el in se.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var g)) { sedes.Add(g); }
            }
        }
        var fi = LeerFecha(root, "fechaInicio") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var ff = LeerFecha(root, "fechaFin") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return new FiltrosRelacionFacturas(aseg, sedes, fi, ff);
    }

    private static DateOnly? LeerFecha(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String) { return null; }
        return DateOnly.TryParse(el.GetString(), out var d) ? d : null;
    }
}
