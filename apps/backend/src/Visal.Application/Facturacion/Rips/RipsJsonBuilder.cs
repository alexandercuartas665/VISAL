using System.Globalization;

namespace Visal.Application.Facturacion.Rips;

/// <summary>
/// Implementacion R1-R3 del builder RIPS JSON:
/// - R1: usuarios unicos + estructura raiz con arrays vacios.
/// - R2: NIT del tenant normalizado + validador pre-serializacion (numFactura, NIT).
/// - R3: dispatch por columna "Archivo json" (AC/AP/AM/AT) al sub-array correspondiente,
///   con bloque financiero minimo (vrServicio / vrPagoModerador / conceptoRecaudo) y
///   consecutivo autoincremental por sub-array.
/// </summary>
public sealed class RipsJsonBuilder : IRipsJsonBuilder
{
    // ==== Columnas del snapshot RelacionFacturas (nombres EXACTOS del template EPS) ====
    private const string ColFactura        = "Consecutivo Factura";
    private const string ColArchivoJson    = "Archivo json";
    private const string ColTipoDoc        = "Tipo_Id";
    private const string ColNumDoc         = "Identificación";
    private const string ColRegimen        = "Regimen";
    private const string ColFechaNacim     = "Fecha de Nacimiento";
    private const string ColSexo           = "Sexo";
    private const string ColNacionalidad   = "Nacionalidad";
    private const string ColMunicipio      = "Municipio";
    private const string ColCodHab         = "codigo habilitacion ";
    private const string ColFechaSuministro= "Fecha suministro de tecnologia";
    private const string ColHora           = "Hora";
    private const string ColCups           = "CUPS";
    private const string ColCodExterno     = "Codigo Externo (Factura)";
    private const string ColCantidad       = "Cantidad";
    private const string ColDescripcion    = "Descripción del procedimiento (Factura)";
    private const string ColValorTotal     = "Valor Total";
    private const string ColCuota          = "Vr Cuota Moderadora "; // ojo espacio final
    private const string ColCopago         = "Copago o Pago Compartido";
    private const string ColDiagnostico    = "Diagnóstico";
    private const string ColAutorizacion   = "Autorizacion";
    private const string ColFinalidad      = "Finalidad";
    private const string ColCausaExterna   = "Causa Externa";
    private const string ColModalidad      = "Modalidad Atención";
    private const string ColViaIngreso     = "Vía de Ingreso";
    private const string ColGrupoServicios = "Grupo Servicios";
    private const string ColServicios      = "Servicios";

    public RipsPayload Build(
        FacturacionSnapshotDetalleDto detalle,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> filas,
        string numDocumentoIdObligado)
    {
        var numFactura = string.Empty;
        if (filas.Count > 0 && filas[0].TryGetValue(ColFactura, out var f) && f is not null)
        {
            numFactura = f.ToString() ?? string.Empty;
        }

        // Usuarios: dedup por (tipoDoc, numDoc) con consecutivo autoincremental.
        var usuariosMap = new Dictionary<(string tipo, string num), RipsUsuario>();
        var uSeq = 1;
        foreach (var fila in filas)
        {
            var tipoDoc = ReadString(fila, ColTipoDoc);
            var numDoc = ReadString(fila, ColNumDoc);
            if (string.IsNullOrWhiteSpace(numDoc)) { continue; }
            var key = (tipoDoc, numDoc);
            if (usuariosMap.ContainsKey(key)) { continue; }

            usuariosMap[key] = new RipsUsuario(
                TipoDocumentoIdentificacion: tipoDoc,
                NumDocumentoIdentificacion: numDoc,
                TipoUsuario: ReadString(fila, ColRegimen),
                FechaNacimiento: FormatFechaCorta(fila, ColFechaNacim),
                CodSexo: ReadString(fila, ColSexo).ToUpperInvariant(),
                CodPaisResidencia: NonEmptyOr(ReadString(fila, ColNacionalidad), "170"),
                CodMunicipioResidencia: NullIfEmpty(ReadString(fila, ColMunicipio)),
                CodZonaTerritorialResidencia: "01",
                Incapacidad: "NO",
                Consecutivo: uSeq++);
        }

        // Servicios: dispatch por "Archivo json" (AC/AP/AM/AT). Consecutivo por sub-array.
        var consultas = new List<RipsConsulta>();
        var procedimientos = new List<RipsProcedimiento>();
        var medicamentos = new List<RipsMedicamento>();
        var otrosServicios = new List<RipsOtroServicio>();

        foreach (var fila in filas)
        {
            var tipoDoc = ReadString(fila, ColTipoDoc);
            var numDoc = ReadString(fila, ColNumDoc);
            if (string.IsNullOrWhiteSpace(numDoc)) { continue; }

            var archivo = ReadString(fila, ColArchivoJson).ToUpperInvariant().Trim();
            switch (archivo)
            {
                case "AC":
                    consultas.Add(BuildConsulta(fila, tipoDoc, numDoc, consultas.Count + 1));
                    break;
                case "AP":
                    procedimientos.Add(BuildProcedimiento(fila, tipoDoc, numDoc, procedimientos.Count + 1));
                    break;
                case "AM":
                    medicamentos.Add(BuildMedicamento(fila, tipoDoc, numDoc, medicamentos.Count + 1));
                    break;
                case "AT":
                    otrosServicios.Add(BuildOtroServicio(fila, tipoDoc, numDoc, otrosServicios.Count + 1));
                    break;
                default:
                    // Sin tipo o desconocido: por defecto la EPS espera "otros servicios".
                    // Manual §3.3.6 acepta insumos/traslados/estancia sin catalogo formal.
                    otrosServicios.Add(BuildOtroServicio(fila, tipoDoc, numDoc, otrosServicios.Count + 1));
                    break;
            }
        }

        return new RipsPayload(
            Transaccion: new RipsTransaccion(
                NumDocumentoIdObligado: NormalizarNit(numDocumentoIdObligado),
                NumFactura: numFactura,
                NumNota: null,
                TipoNota: null),
            Usuarios: usuariosMap.Values.ToList(),
            Servicios: new RipsServicios(
                Consultas: consultas,
                Procedimientos: procedimientos,
                Urgencias: Array.Empty<RipsUrgencia>(),
                Hospitalizacion: Array.Empty<RipsHospitalizacion>(),
                RecienNacidos: Array.Empty<RipsRecienNacido>(),
                Medicamentos: medicamentos,
                OtrosServicios: otrosServicios));
    }

    private static RipsConsulta BuildConsulta(IReadOnlyDictionary<string, object?> f, string tipoDoc, string numDoc, int consecutivo)
    {
        var (vrServicio, vrModerador, concepto) = ExtraerFinancieros(f);
        return new RipsConsulta(
            CodPrestador: ReadString(f, ColCodHab),
            FechaInicioAtencion: FormatFechaHora(f, ColFechaSuministro, ColHora),
            NumAutorizacion: NullIfEmpty(ReadString(f, ColAutorizacion)),
            CodConsulta: ReadString(f, ColCups),
            ModalidadGrupoServicioTecSal: ReadString(f, ColModalidad),
            GrupoServicios: ReadString(f, ColGrupoServicios),
            CodServicio: ReadString(f, ColServicios),
            FinalidadTecnologiaSalud: NullIfEmpty(ReadString(f, ColFinalidad)),
            CausaMotivoAtencion: NullIfEmpty(ReadString(f, ColCausaExterna)),
            CodDiagnosticoPrincipal: ReadString(f, ColDiagnostico),
            TipoDiagnosticoPrincipal: "02", // "Confirmado nuevo" default; R4 leera HC.tipoDiagnostico
            TipoDocumentoIdentificacion: tipoDoc,
            NumDocumentoIdentificacion: numDoc,
            VrServicio: vrServicio,
            ConceptoRecaudo: concepto,
            VrPagoModerador: vrModerador,
            Consecutivo: consecutivo);
    }

    private static RipsProcedimiento BuildProcedimiento(IReadOnlyDictionary<string, object?> f, string tipoDoc, string numDoc, int consecutivo)
    {
        var (vrServicio, vrModerador, concepto) = ExtraerFinancieros(f);
        return new RipsProcedimiento(
            CodPrestador: ReadString(f, ColCodHab),
            FechaInicioAtencion: FormatFechaHora(f, ColFechaSuministro, ColHora),
            NumAutorizacion: NullIfEmpty(ReadString(f, ColAutorizacion)),
            CodProcedimiento: ReadString(f, ColCups),
            ViaIngresoServicioSalud: NonEmptyOr(ReadString(f, ColViaIngreso), "02"), // 02 = Consulta externa default
            ModalidadGrupoServicioTecSal: ReadString(f, ColModalidad),
            GrupoServicios: ReadString(f, ColGrupoServicios),
            CodServicio: ReadString(f, ColServicios),
            FinalidadTecnologiaSalud: NullIfEmpty(ReadString(f, ColFinalidad)),
            CodDiagnosticoPrincipal: ReadString(f, ColDiagnostico),
            TipoDocumentoIdentificacion: tipoDoc,
            NumDocumentoIdentificacion: numDoc,
            VrServicio: vrServicio,
            ConceptoRecaudo: concepto,
            VrPagoModerador: vrModerador,
            Consecutivo: consecutivo);
    }

    private static RipsMedicamento BuildMedicamento(IReadOnlyDictionary<string, object?> f, string tipoDoc, string numDoc, int consecutivo)
    {
        var (vrServicio, vrModerador, concepto) = ExtraerFinancieros(f);
        return new RipsMedicamento(
            CodPrestador: ReadString(f, ColCodHab),
            NumAutorizacion: NullIfEmpty(ReadString(f, ColAutorizacion)),
            FechaDispensacionAdmon: FormatFechaHora(f, ColFechaSuministro, ColHora),
            CodDiagnosticoPrincipal: ReadString(f, ColDiagnostico),
            TipoMedicamento: "01", // 01 = POS/PBS default; R4 leera catalogo
            CodTecnologiaSalud: ReadString(f, ColCups), // R5: usar CUM en vez de CUPS
            NomTecnologiaSalud: LimpiarStringDescriptivo(ReadString(f, ColDescripcion)),
            CantidadMedicamento: ReadInt(f, ColCantidad, defaultVal: 1),
            TipoDocumentoIdentificacion: tipoDoc,
            NumDocumentoIdentificacion: numDoc,
            VrServicio: vrServicio,
            ConceptoRecaudo: concepto,
            VrPagoModerador: vrModerador,
            Consecutivo: consecutivo);
    }

    private static RipsOtroServicio BuildOtroServicio(IReadOnlyDictionary<string, object?> f, string tipoDoc, string numDoc, int consecutivo)
    {
        var (vrServicio, vrModerador, concepto) = ExtraerFinancieros(f);
        return new RipsOtroServicio(
            CodPrestador: ReadString(f, ColCodHab),
            NumAutorizacion: NullIfEmpty(ReadString(f, ColAutorizacion)),
            FechaSuministroTecnologia: FormatFechaHora(f, ColFechaSuministro, ColHora),
            TipoOS: "01", // 01 = Materiales/insumos default
            CodTecnologiaSalud: ReadString(f, ColCups),
            NomTecnologiaSalud: LimpiarStringDescriptivo(ReadString(f, ColDescripcion)),
            CantidadOS: ReadInt(f, ColCantidad, defaultVal: 1),
            TipoDocumentoIdentificacion: tipoDoc,
            NumDocumentoIdentificacion: numDoc,
            VrServicio: vrServicio,
            ConceptoRecaudo: concepto,
            VrPagoModerador: vrModerador,
            Consecutivo: consecutivo);
    }

    /// <summary>
    /// Deriva el bloque financiero del snapshot. Regla ciclica del manual §4.3:
    /// <c>conceptoRecaudo=04</c> obliga <c>vrPagoModerador=0</c>. Los otros codigos:
    /// 01=Copago, 02=Cuota moderadora, 03=Pago compartido (cuando trae ambos).
    /// </summary>
    private static (decimal vrServicio, decimal vrModerador, string concepto) ExtraerFinancieros(IReadOnlyDictionary<string, object?> f)
    {
        var vrTotal = ReadDecimal(f, ColValorTotal);
        var cuota = ReadDecimal(f, ColCuota);
        var copago = ReadDecimal(f, ColCopago);

        string concepto;
        decimal moderador;
        if (cuota > 0 && copago > 0) { concepto = "03"; moderador = cuota + copago; }
        else if (cuota > 0) { concepto = "02"; moderador = cuota; }
        else if (copago > 0) { concepto = "01"; moderador = copago; }
        else { concepto = "04"; moderador = 0m; }

        return (vrTotal, moderador, concepto);
    }

    public IReadOnlyList<string> Validate(RipsPayload payload)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(payload.Transaccion.NumDocumentoIdObligado))
        {
            errores.Add("El NIT del obligado (Tenant.TaxId) esta vacio. Configuralo en Mi cuenta > Perfil de la agencia.");
        }
        if (string.IsNullOrWhiteSpace(payload.Transaccion.NumFactura))
        {
            errores.Add("No se pudo determinar el numero de factura (columna 'Consecutivo Factura' vacia en la 1ra fila del snapshot).");
        }
        return errores;
    }

    // ==== Helpers ====

    /// <summary>Devuelve solo digitos. La regla del manual excluye DV y guiones.</summary>
    private static string NormalizarNit(string? nit)
    {
        if (string.IsNullOrWhiteSpace(nit)) { return string.Empty; }
        var sb = new System.Text.StringBuilder(nit.Length);
        foreach (var c in nit) { if (char.IsDigit(c)) { sb.Append(c); } }
        return sb.ToString();
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> fila, string col)
        => fila.TryGetValue(col, out var v) && v is not null ? v.ToString() ?? string.Empty : string.Empty;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string NonEmptyOr(string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s;

    private static decimal ReadDecimal(IReadOnlyDictionary<string, object?> fila, string col)
    {
        if (!fila.TryGetValue(col, out var v) || v is null) { return 0m; }
        return v switch
        {
            decimal d => d,
            double db => (decimal)db,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> fila, string col, int defaultVal = 0)
    {
        if (!fila.TryGetValue(col, out var v) || v is null) { return defaultVal; }
        return v switch
        {
            int i => i,
            long l => (int)l,
            decimal d => (int)d,
            double db => (int)db,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultVal
        };
    }

    /// <summary>Fecha YYYY-MM-DD (nacimiento/egresos admin).</summary>
    private static string FormatFechaCorta(IReadOnlyDictionary<string, object?> fila, string col)
    {
        if (!fila.TryGetValue(col, out var val) || val is null) { return string.Empty; }
        return val switch
        {
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd"),
            _ => val.ToString() ?? string.Empty
        };
    }

    /// <summary>Fecha+hora YYYY-MM-DD HH:MM 24h (atenciones y registros clinicos). Combina 2 columnas del snapshot.</summary>
    private static string FormatFechaHora(IReadOnlyDictionary<string, object?> fila, string colFecha, string colHora)
    {
        var fecha = FormatFechaCorta(fila, colFecha);
        if (string.IsNullOrEmpty(fecha)) { return string.Empty; }
        var hora = ReadString(fila, colHora).Trim();
        if (string.IsNullOrEmpty(hora)) { return $"{fecha} 00:00"; }

        // Normaliza "8:0" -> "08:00", "8:30 AM" no aplica (snapshot ya es 24h).
        var partes = hora.Split(':');
        if (partes.Length >= 2 &&
            int.TryParse(partes[0], out var h) &&
            int.TryParse(partes[1], out var m))
        {
            return $"{fecha} {h:D2}:{m:D2}";
        }
        return $"{fecha} {hora}";
    }

    /// <summary>Manual §5: eliminar saltos de linea, tabs y comillas dobles en campos descriptivos.</summary>
    private static string LimpiarStringDescriptivo(string s)
    {
        if (string.IsNullOrEmpty(s)) { return s; }
        return s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Replace("\"", "'").Trim();
    }
}
