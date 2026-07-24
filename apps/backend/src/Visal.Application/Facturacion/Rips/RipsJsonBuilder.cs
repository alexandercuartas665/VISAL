namespace Visal.Application.Facturacion.Rips;

/// <summary>
/// Implementacion R1 (esqueleto). Emite <see cref="RipsPayload"/> con:
/// - <c>transaccion</c> con numFactura tomado de la primera fila del snapshot,
/// - <c>usuarios</c> unicos por (tipoDocumento, numDocumento) con demograficos "como vienen"
///   del snapshot (sin catalogos DIVIPOLA/ISO todavia, eso llega en R4),
/// - <c>servicios</c> con arrays vacios (R3 hidrata dispatch por TipoArchivoRips).
/// Sin catalogos oficiales todavia; los valores del snapshot pasan tal cual — R2/R4 mapearan
/// contra catalogos MinSalud.
/// </summary>
public sealed class RipsJsonBuilder : IRipsJsonBuilder
{
    private const string ColFactura        = "Consecutivo Factura";
    private const string ColTipoDoc        = "Tipo_Id";
    private const string ColNumDoc         = "Identificación";
    private const string ColRegimen        = "Regimen";
    private const string ColFechaNacim     = "Fecha de Nacimiento";
    private const string ColSexo           = "Sexo";
    private const string ColNacionalidad   = "Nacionalidad";
    private const string ColMunicipio      = "Municipio";

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

        var usuariosMap = new Dictionary<(string tipo, string num), RipsUsuario>();
        var seq = 1;
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
                FechaNacimiento: FormatFecha(fila, ColFechaNacim),
                CodSexo: ReadString(fila, ColSexo).ToUpperInvariant(),
                CodPaisResidencia: NonEmptyOr(ReadString(fila, ColNacionalidad), "170"),
                CodMunicipioResidencia: NullIfEmpty(ReadString(fila, ColMunicipio)),
                CodZonaTerritorialResidencia: "01", // default urbana; R4 leera Paciente.Zona
                Incapacidad: "NO",                   // default; R4 leera HC
                Consecutivo: seq++);
        }

        return new RipsPayload(
            Transaccion: new RipsTransaccion(
                // NIT del obligado: solo digitos, sin DV ni guiones (manual seccion 3.1)
                NumDocumentoIdObligado: NormalizarNit(numDocumentoIdObligado),
                NumFactura: numFactura,
                NumNota: null,
                TipoNota: null),
            Usuarios: usuariosMap.Values.ToList(),
            Servicios: new RipsServicios(
                Consultas: Array.Empty<RipsConsulta>(),
                Procedimientos: Array.Empty<RipsProcedimiento>(),
                Urgencias: Array.Empty<RipsUrgencia>(),
                Hospitalizacion: Array.Empty<RipsHospitalizacion>(),
                RecienNacidos: Array.Empty<RipsRecienNacido>(),
                Medicamentos: Array.Empty<RipsMedicamento>(),
                OtrosServicios: Array.Empty<RipsOtroServicio>()));
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

    /// <summary>Normaliza a YYYY-MM-DD segun el manual (Resolucion 2275).</summary>
    private static string FormatFecha(IReadOnlyDictionary<string, object?> fila, string col)
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
}
