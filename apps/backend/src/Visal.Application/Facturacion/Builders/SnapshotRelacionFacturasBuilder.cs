using System.Runtime.CompilerServices;
using System.Text.Json;
using Visal.Application.Facturacion.Selectors;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion.Builders;

/// <summary>
/// Builder del snapshot Relacion de Facturas (Tipo 1 del motor). Produce las 41
/// columnas EXACTAS del template EPS/MinSalud — nombres, orden, tildes rotas
/// incluidas (spec: "07. Facturacion / 2. Snapshot Relacion de Facturas").
///
/// Este builder es intencionalmente delgado: NO conoce reglas de negocio sobre
/// que se factura. Solo (a) parsea los filtros del JSON, (b) pide los "hechos
/// facturables" al <see cref="IRelacionFacturasSelector"/> y (c) mapea cada
/// hecho a un diccionario columna->valor. Iteraciones sobre "que cuenta como
/// facturado" viven en el selector, no aqui.
/// </summary>
public sealed class SnapshotRelacionFacturasBuilder(IRelacionFacturasSelector selector) : ISnapshotBuilder
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
        var hechos = await selector.SelectAsync(filtros, ct);

        foreach (var h in hechos)
        {
            ct.ThrowIfCancellationRequested();
            yield return Mapear(h);
        }
    }

    private static Dictionary<string, object?> Mapear(RelacionFacturasHecho h)
    {
        // Cuota/Copago mutuamente excluyentes (spec §7.3).
        decimal? vCuota = null, vCopago = null;
        if (string.Equals(h.Asignacion.TipoPago, "CUOTA", StringComparison.OrdinalIgnoreCase))
        {
            vCuota = h.Asignacion.ValorPagoReal ?? h.Asignacion.ValorPagoSugerido;
        }
        else if (string.Equals(h.Asignacion.TipoPago, "COPAGO", StringComparison.OrdinalIgnoreCase))
        {
            vCopago = h.Asignacion.ValorPagoReal ?? h.Asignacion.ValorPagoSugerido;
        }

        decimal? valorUnitario = h.Asignacion.PaqueteValorPactado ?? h.Servicio?.Tarifa;
        decimal? valorTotal = h.Servicio?.ValorTotal ?? valorUnitario;

        return new Dictionary<string, object?>
        {
            ["Consecutivo Factura"] = null,                                    //  1  — proceso posterior
            ["Orden"] = null,                                                  //  2  — vacio por ahora
            ["Contrato"] = h.Asignacion.ContratoCodigo,                        //  3
            ["codigo habilitacion "] = h.Sucursal?.CodigoHabilitacion,         //  4
            ["Regimen"] = h.Paciente.Regimen,                                  //  5
            ["Archivo json"] = null,                                           //  6  — vacio
            ["Autorizacion"] = h.Asignacion.CodigoAutorizacion,                //  7
            ["Tipo_Id"] = h.Paciente.TipoDocumento,                            //  8
            ["Identificación"] = h.Paciente.NumeroDocumento,                   //  9
            ["Primer Apellido"] = h.Paciente.PrimerApellido,                   // 10
            ["Segundo Apellido"] = h.Paciente.SegundoApellido,                 // 11
            ["Primer Nombre"] = h.Paciente.PrimerNombre,                       // 12
            ["Segundo Nombre"] = h.Paciente.SegundoNombre,                     // 13
            ["Fecha de Nacimiento"] = h.Paciente.FechaNacimiento?.ToString("yyyy-MM-dd"), // 14
            ["Sexo"] = h.Paciente.Sexo,                                        // 15
            ["Fecha suministro de tecnologia"] = h.Sesion.FechaAtencion.ToString("yyyy-MM-dd"), // 16 — fecha real de atencion
            ["Hora"] = h.Sesion.CreatedAt.ToString("HH:mm:ss"),                // 17 — no hay hora explicita, usamos CreatedAt de la sesion
            ["CUPS"] = h.CupsCodigo,                                           // 18
            ["Codigo Externo (Factura)"] = h.Servicio?.CodigoInterno,          // 19
            ["Cantidad"] = h.Asignacion.PaqueteInstanciaId is not null ? 1 : h.Turno.Cantidad, // 20
            ["Descripción del procedimiento (Factura)"] = h.CupsDescripcion,   // 21
            ["Valor Unitario"] = valorUnitario,                                // 22
            ["Vr Cuota Moderadora "] = vCuota,                                 // 23
            ["Copago o Pago Compartido"] = vCopago,                            // 24
            ["Valor Total"] = valorTotal,                                      // 25
            ["Diagnóstico"] = h.Paciente.Cie10Codigo ?? h.Paciente.DiagnosticoPrincipal, // 26
            ["TipoDocProfesional"] = h.Profesional?.TipoDocumento,             // 27
            ["DocumentoProf"] = h.Profesional?.NumeroDocumento,                // 28
            ["NomProf"] = h.Profesional?.NombreCompleto,                       // 29
            ["Finalidad"] = h.Servicio?.Finalidad,                             // 30
            ["Causa Externa"] = h.Servicio?.CausaExterna,                      // 31
            ["Modalidad Atención"] = h.Servicio?.ModalidadAtencion,            // 32
            ["Vía de Ingreso"] = h.Servicio?.ViaIngreso,                       // 33
            ["Grupo Servicios"] = h.Servicio?.GrupoServicios,                  // 34
            ["Servicios"] = h.Servicio?.Servicios,                             // 35
            ["Nacionalidad"] = h.NacionalidadNombre ?? "COLOMBIA",             // 36
            ["Departamento"] = h.DepartamentoNombre,                           // 37
            ["Municipio"] = h.MunicipioNombre,                                 // 38
            ["Dirección"] = h.Paciente.Direccion,                              // 39
            ["Telefono"] = h.Paciente.Telefono,                                // 40
            ["Correo electrónico"] = h.Aseguradora.CorreoFacturacion,          // 41
        };
    }

    private static RelacionFacturasFiltros ParsearFiltros(string json)
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
            foreach (var e in se.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var g))
                {
                    sedes.Add(g);
                }
            }
        }

        var hoy = DateTime.Today;
        var fechaIni = new DateOnly(hoy.Year, hoy.Month, 1);
        var fechaFin = DateOnly.FromDateTime(hoy);
        if (root.TryGetProperty("fechaInicio", out var fi) && fi.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(fi.GetString(), out var fiParsed))
        {
            fechaIni = fiParsed;
        }
        if (root.TryGetProperty("fechaFin", out var ff) && ff.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(ff.GetString(), out var ffParsed))
        {
            fechaFin = ffParsed;
        }

        return new RelacionFacturasFiltros(aseg, sedes, fechaIni, fechaFin);
    }
}
