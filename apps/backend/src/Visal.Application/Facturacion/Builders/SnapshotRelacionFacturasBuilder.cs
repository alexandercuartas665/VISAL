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

    // Orden EXACTO del template EPS "RELACION FACTURAS VISAL RT final v0.xlsx".
    // NO reformatear los espacios ni las tildes — cualquier cambio rompe la
    // validacion de la EPS al radicar.
    public IReadOnlyList<string> Columnas { get; } = new[]
    {
        "Consecutivo Factura",                        //  1
        "Orden",                                      //  2
        "Contrato",                                   //  3
        "codigo habilitacion ",                       //  4  (con espacio final del template)
        "Sede",                                       //  5  (agregada por spec EPS julio 2026)
        "Regimen",                                    //  6
        "Archivo json",                               //  7
        "Autorizacion",                               //  8
        "Tipo_Id",                                    //  9
        "Identificación",                             // 10
        "Primer Apellido",                            // 11
        "Segundo Apellido",                           // 12
        "Primer Nombre",                              // 13
        "Segundo Nombre",                             // 14
        "Fecha de Nacimiento",                        // 15
        "Sexo",                                       // 16
        "Fecha suministro de tecnologia",             // 17
        "Hora",                                       // 18
        "CUPS",                                       // 19
        "Codigo Externo (Factura)",                   // 20
        "Cantidad",                                   // 21
        "Descripción del procedimiento (Factura)",    // 22
        "Valor Unitario",                             // 23
        "Vr Cuota Moderadora ",                       // 24  (con espacio final)
        "Copago o Pago Compartido",                   // 25
        "Valor Total",                                // 26
        "Diagnóstico",                                // 27
        "TipoDocProfesional",                         // 28
        "DocumentoProf",                              // 29
        "NomProf",                                    // 30
        "Finalidad",                                  // 31
        "Causa Externa",                              // 32
        "Modalidad Atención",                         // 33
        "Vía de Ingreso",                             // 34
        "Grupo Servicios",                            // 35
        "Servicios",                                  // 36
        "Nacionalidad",                               // 37
        "Departamento",                               // 38
        "Municipio",                                  // 39
        "Dirección",                                  // 40
        "Telefono",                                   // 41
        "Correo electrónico"                          // 42
    };

    /// <summary>
    /// Descripciones de cada columna copiadas EXACTAS de la fila 2 del template
    /// EPS (columnas sin descripcion en el Excel quedan sin entrada aqui — la
    /// UI usa null como default). Sirven de valor inicial al configurar
    /// columnas del snapshot; el tenant puede sobrescribirlas.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Descripciones { get; } = new Dictionary<string, string?>
    {
        ["Consecutivo Factura"]                     = "Numero de factura",
        ["Contrato"]                                = "sale del modulo de admision casilla contrato",
        ["codigo habilitacion "]                    = "Config Interoperabilidad -> Credenciales por sede (ambiente activo). Fallback: campo directo de la sucursal.",
        ["Sede"]                                    = "Nombre de la sede que atendio (Sucursal del paciente)",
        ["Regimen"]                                 = "sale del modulo de admision casilla tipo usuario",
        ["Autorizacion"]                            = "Asignacion.CodigoAutorizacion — se captura al asignar el servicio al paciente en /asignacion",
        ["Tipo_Id"]                                 = "sale del modulo de admision Datos del paciente",
        ["Archivo json"]                            = "Tipo de archivo RIPS del modulo del servicio prestado (Catalogo /config/tipos-servicio → TipoArchivoRips: AC/AP/AT/AM)",
        ["Fecha suministro de tecnologia"]          = "sale del modulo de coordinacion momento que asigna el servicio",
        ["CUPS"]                                    = "sale del modulo de asignacion, cuando se selecciona el servicio y la cantidad al momento de asignar",
        ["Diagnóstico"]                             = "Modulo de atencion, historia clinica",
        ["TipoDocProfesional"]                      = "Modulo de atencion datos del profesional que realiza la historia clinica",
        ["Finalidad"]                               = "Modulo atencion, cuando el profesional llena la historia clinica",
        ["Modalidad Atención"]                      = "Modulo de asignacion al momento de agregar el servicio o consulta",
        ["Vía de Ingreso"]                          = "Modulo de admision",
        ["Grupo Servicios"]                         = "Modulo de admision, casilla contratos",
        ["Nacionalidad"]                            = "sale del modulo de admision Datos del paciente",
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
        // v3: la HC es la unidad base. Los campos de asignacion/turno/sesion/
        // servicio ya no aplican (prod no genera esos registros). Los dejamos
        // en null explicito para que la EPS reciba filas consistentes aunque
        // incompletas — el flujo de facturacion posterior debera completarlos.
        var fechaCierre = h.Hc.FechaCierre ?? h.Hc.UpdatedAt ?? h.Hc.CreatedAt;
        var fechaLocal = fechaCierre.ToLocalTime();

        return new Dictionary<string, object?>
        {
            ["Consecutivo Factura"] = null,                                    //  1  — proceso posterior
            ["Orden"] = null,                                                  //  2  — vacio por ahora
            ["Contrato"] = h.Contrato.CodigoContrato,                          //  3  — codigo del contrato de la EPS
            ["codigo habilitacion "] = h.CodigoHabilitacionResuelto,           //  4  — credencial interop x sede (fallback Sucursal.CodigoHabilitacion)
            ["Sede"] = h.Sucursal?.Nombre,                                     //  5  — nombre de la sede que atendio
            ["Regimen"] = h.Paciente.Regimen,                                  //  6
            ["Archivo json"] = h.TipoArchivoRips,                              //  7  — TipoArchivoRips del catalogo tipo servicio (AC/AP/AT/AM)
            ["Autorizacion"] = h.CodigoAutorizacion,                           //  8  — Asignacion.CodigoAutorizacion
            ["Tipo_Id"] = h.Paciente.TipoDocumento,                            //  9
            ["Identificación"] = h.Paciente.NumeroDocumento,                   // 10
            ["Primer Apellido"] = h.Paciente.PrimerApellido,                   // 11
            ["Segundo Apellido"] = h.Paciente.SegundoApellido,                 // 12
            ["Primer Nombre"] = h.Paciente.PrimerNombre,                       // 13
            ["Segundo Nombre"] = h.Paciente.SegundoNombre,                     // 14
            ["Fecha de Nacimiento"] = h.Paciente.FechaNacimiento?.ToString("yyyy-MM-dd"), // 15
            ["Sexo"] = h.Paciente.Sexo,                                        // 16
            ["Fecha suministro de tecnologia"] = fechaLocal.ToString("yyyy-MM-dd"), // 17 — fecha de cierre de la HC
            ["Hora"] = fechaLocal.ToString("HH:mm:ss"),                        // 18
            ["CUPS"] = h.CupsCodigo,                                           // 19
            ["Codigo Externo (Factura)"] = null,                               // 20
            ["Cantidad"] = 1,                                                  // 21 — 1 HC = 1 fila
            ["Descripción del procedimiento (Factura)"] = h.CupsDescripcion,   // 22
            ["Valor Unitario"] = null,                                         // 23 — sin servicio/tarifa asociada
            ["Vr Cuota Moderadora "] = null,                                   // 24
            ["Copago o Pago Compartido"] = null,                               // 25
            ["Valor Total"] = null,                                            // 26
            ["Diagnóstico"] = h.Paciente.Cie10Codigo ?? h.Paciente.DiagnosticoPrincipal, // 27
            ["TipoDocProfesional"] = h.Profesional?.TipoDocumento,             // 28
            ["DocumentoProf"] = h.Profesional?.NumeroDocumento,                // 29
            ["NomProf"] = h.Profesional?.NombreCompleto,                       // 30
            ["Finalidad"] = h.Hc.RipsFinalidadCodigo,                          // 31
            ["Causa Externa"] = h.Hc.RipsCausaExternaCodigo,                   // 32
            ["Modalidad Atención"] = null,                                     // 33
            ["Vía de Ingreso"] = h.Hc.RipsViaIngresoCodigo,                    // 34
            ["Grupo Servicios"] = null,                                        // 35
            ["Servicios"] = null,                                              // 36
            ["Nacionalidad"] = h.NacionalidadNombre ?? "COLOMBIA",             // 37
            ["Departamento"] = h.DepartamentoNombre,                           // 38
            ["Municipio"] = h.MunicipioNombre,                                 // 39
            ["Dirección"] = h.Paciente.Direccion,                              // 40
            ["Telefono"] = h.Paciente.Telefono,                                // 41
            ["Correo electrónico"] = h.Aseguradora.CorreoFacturacion,          // 42
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
