using System.Globalization;

namespace Visal.Application.Facturacion.Rips;

/// <summary>
/// Implementacion R1-R7 del builder RIPS JSON:
/// - R1: usuarios unicos + estructura raiz con arrays vacios.
/// - R2: NIT del tenant normalizado + validador pre-serializacion (numFactura, NIT).
/// - R3: dispatch por columna "Archivo json" (AC/AP/AM/AT) + bloque financiero.
/// - R4: normalizadores (sexo, tipoDoc, regimen) a codigos oficiales del manual,
///   defaults sensatos y validaciones de estructura.
/// - R5: cuadre financiero cruzado (manual §4): copagos por paciente <= servicios,
///   regla ciclica 04 reversa, vrServicio/vrModerador >= 0. TotalNeto() expone el
///   sumatorio para comparar contra &lt;PayableAmount&gt; de la FEV manualmente.
/// - R6: campos ricos del §3.3.5-6: concentracion y unidadMedida derivadas del
///   nomTecnologiaSalud via regex (heuristica "500 mg" / "10 ml" / "500 mcg").
/// - R7: si el snapshot referencia un medicamento del catalogo (CUM/expediente),
///   sobrescribe con datos reales: CumInvima, concentracion, formaFarmaceutica,
///   EsPos -> tipoMedicamento 01/02. La heuristica de R6 sigue como fallback.
/// - R8: Validate() advierte si un codDiagnosticoPrincipal no existe en el catalogo
///   Diagnosticos del tenant (para atrapar tipeos y CIE-10 obsoletos).
/// - R9: tipoDiagnosticoPrincipal usa hook TiposDiagnosticoPorPacienteFactura del
///   catalogo cuando esta disponible; sino cae al default "02" (Confirmado nuevo).
/// - R10: Validate() falla si el snapshot mezcla varias facturas (manual §3.1
///   exige numFactura unico por JSON RIPS).
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
        string numDocumentoIdObligado,
        RipsCatalogos catalogos)
    {
        // R10: recolectar TODAS las facturas distintas del snapshot para que Validate
        // pueda advertir si viene mezcla. numFactura oficial usa la primera fila.
        var facturasDetectadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fila in filas)
        {
            var v = ReadString(fila, ColFactura);
            if (!string.IsNullOrWhiteSpace(v)) { facturasDetectadas.Add(v); }
        }
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
                TipoDocumentoIdentificacion: NormalizarTipoDoc(tipoDoc),
                NumDocumentoIdentificacion: numDoc,
                TipoUsuario: NormalizarRegimen(ReadString(fila, ColRegimen)),
                FechaNacimiento: FormatFechaCorta(fila, ColFechaNacim),
                CodSexo: NormalizarSexo(ReadString(fila, ColSexo)),
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

            // Normaliza el tipo doc UNA VEZ para que la FK de servicios haga match
            // con la clave del array usuarios (que tambien se normalizo arriba).
            var tipoDocN = NormalizarTipoDoc(tipoDoc);
            var archivo = ReadString(fila, ColArchivoJson).ToUpperInvariant().Trim();
            switch (archivo)
            {
                case "AC":
                    consultas.Add(BuildConsulta(fila, tipoDocN, numDoc, consultas.Count + 1, numFactura, catalogos));
                    break;
                case "AP":
                    procedimientos.Add(BuildProcedimiento(fila, tipoDocN, numDoc, procedimientos.Count + 1));
                    break;
                case "AM":
                    medicamentos.Add(BuildMedicamento(fila, tipoDocN, numDoc, medicamentos.Count + 1, catalogos));
                    break;
                case "AT":
                    otrosServicios.Add(BuildOtroServicio(fila, tipoDocN, numDoc, otrosServicios.Count + 1));
                    break;
                default:
                    // Sin tipo o desconocido: por defecto la EPS espera "otros servicios".
                    // Manual §3.3.6 acepta insumos/traslados/estancia sin catalogo formal.
                    otrosServicios.Add(BuildOtroServicio(fila, tipoDocN, numDoc, otrosServicios.Count + 1));
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
                OtrosServicios: otrosServicios),
            FacturasDetectadas: facturasDetectadas.ToList());
    }

    private static RipsConsulta BuildConsulta(IReadOnlyDictionary<string, object?> f, string tipoDoc, string numDoc, int consecutivo, string numFactura, RipsCatalogos catalogos)
    {
        var (vrServicio, vrModerador, concepto) = ExtraerFinancieros(f);
        // R9: si el catalogo trae tipoDiagnostico real desde HC, usarlo. Sino "02".
        var tipoDx = catalogos.TiposDiagnosticoPorPacienteFactura?.TryGetValue((numDoc, numFactura), out var t) == true
            ? t
            : "02";
        return new RipsConsulta(
            CodPrestador: ReadString(f, ColCodHab),
            FechaInicioAtencion: FormatFechaHora(f, ColFechaSuministro, ColHora),
            NumAutorizacion: NullIfEmpty(ReadString(f, ColAutorizacion)),
            CodConsulta: ReadString(f, ColCups),
            ModalidadGrupoServicioTecSal: ReadString(f, ColModalidad),
            GrupoServicios: ReadString(f, ColGrupoServicios),
            CodServicio: ReadString(f, ColServicios),
            // Defaults del manual: finalidad 10 = Enfermedad general, causa 15 = Enf. general.
            FinalidadTecnologiaSalud: NonEmptyOr(ReadString(f, ColFinalidad), "10"),
            CausaMotivoAtencion: NonEmptyOr(ReadString(f, ColCausaExterna), "15"),
            CodDiagnosticoPrincipal: ReadString(f, ColDiagnostico),
            TipoDiagnosticoPrincipal: tipoDx,
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
            FinalidadTecnologiaSalud: NonEmptyOr(ReadString(f, ColFinalidad), "10"),
            CodDiagnosticoPrincipal: ReadString(f, ColDiagnostico),
            TipoDocumentoIdentificacion: tipoDoc,
            NumDocumentoIdentificacion: numDoc,
            VrServicio: vrServicio,
            ConceptoRecaudo: concepto,
            VrPagoModerador: vrModerador,
            Consecutivo: consecutivo);
    }

    private static RipsMedicamento BuildMedicamento(IReadOnlyDictionary<string, object?> f, string tipoDoc, string numDoc, int consecutivo, RipsCatalogos catalogos)
    {
        var (vrServicio, vrModerador, concepto) = ExtraerFinancieros(f);
        var nombreRaw = LimpiarStringDescriptivo(ReadString(f, ColDescripcion));
        var codExt = ReadString(f, ColCodExterno);
        var cups = ReadString(f, ColCups);

        // R7: intentar match contra catalogo por Codigo Externo o CUPS. Si el
        // medicamento existe en la BD del tenant, sus datos ganan sobre la
        // heuristica de R6 y sobre lo que venga en el snapshot.
        MedicamentoCatalogoInfo? info = null;
        if (!string.IsNullOrWhiteSpace(codExt) && catalogos.MedicamentosPorCodigo.TryGetValue(codExt, out var i1)) { info = i1; }
        else if (!string.IsNullOrWhiteSpace(cups) && catalogos.MedicamentosPorCodigo.TryGetValue(cups, out var i2)) { info = i2; }

        string codTec;
        string? concentracion, unidad, formaFarm;
        string tipoMed;
        string nombre;

        if (info is not null)
        {
            // Match del catalogo: datos oficiales ganan.
            codTec = NullIfEmpty(info.CumInvima) ?? (string.IsNullOrWhiteSpace(codExt) ? cups : codExt);
            nombre = NullIfEmpty(info.Nombre) ?? nombreRaw;
            concentracion = NullIfEmpty(info.Concentracion);
            unidad = MapearUnidadMedida(info.UnidadMedida) ?? MapearUnidadMedida(ExtraerConcentracionYUnidad(nombreRaw).unidadMedida);
            formaFarm = NullIfEmpty(info.FormaFarmaceutica);
            tipoMed = info.EsPos ? "01" : "02";
        }
        else
        {
            // Sin match: R6 fallback (regex sobre el nombre).
            codTec = string.IsNullOrWhiteSpace(codExt) ? cups : codExt;
            nombre = nombreRaw;
            var (c, u) = ExtraerConcentracionYUnidad(nombreRaw);
            concentracion = c;
            unidad = u;
            formaFarm = null;
            tipoMed = "01";
        }

        return new RipsMedicamento(
            CodPrestador: ReadString(f, ColCodHab),
            NumAutorizacion: NullIfEmpty(ReadString(f, ColAutorizacion)),
            FechaDispensacionAdmon: FormatFechaHora(f, ColFechaSuministro, ColHora),
            CodDiagnosticoPrincipal: ReadString(f, ColDiagnostico),
            TipoMedicamento: tipoMed,
            CodTecnologiaSalud: codTec,
            NomTecnologiaSalud: nombre,
            CantidadMedicamento: ReadInt(f, ColCantidad, defaultVal: 1),
            TipoDocumentoIdentificacion: tipoDoc,
            NumDocumentoIdentificacion: numDoc,
            VrServicio: vrServicio,
            ConceptoRecaudo: concepto,
            VrPagoModerador: vrModerador,
            Consecutivo: consecutivo,
            ConcentracionMedicamento: concentracion,
            UnidadMedida: unidad,
            FormaFarmaceutica: formaFarm);
    }

    /// <summary>
    /// Traduce texto libre del catalogo Medicamentos.UnidadMedida al codigo MinSalud
    /// (01 mg, 02 ml, 03 UI, 04 g, 05 mcg, 06 %). Pass-through si ya viene en 2 digitos.
    /// </summary>
    private static string? MapearUnidadMedida(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var v = raw.Trim().ToLowerInvariant();
        if (v.Length == 2 && v.All(char.IsDigit)) { return v; }
        return v switch
        {
            "mg" or "miligramo" or "miligramos" => "01",
            "ml" or "mililitro" or "mililitros" => "02",
            "ui" or "iu" or "unidad internacional" => "03",
            "g" or "gramo" or "gramos" => "04",
            "mcg" or "ug" or "microgramo" or "microgramos" => "05",
            "%" or "porcentaje" => "06",
            _ => null
        };
    }

    // Nota: no usamos \b al final porque % no es un char \w y romperia el match.
    // El orden dentro del grupo importa (mcg antes de mg, mcg antes de g).
    private static readonly System.Text.RegularExpressions.Regex ConcentracionRx =
        new(@"(?<num>\d+(?:[.,]\d+)?)\s*(?<u>mcg|ug|mg|ml|ui|iu|g|%)(?![a-zA-Z])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Extrae la concentracion y la unidad codificada del nombre del medicamento.
    /// Ejemplos: "ACETAMINOFEN 500 mg tabletas" -> ("500 mg", "01").
    /// Codigos MinSalud: 01 mg, 02 ml, 03 UI/IU, 04 g, 05 mcg/ug, 06 %.
    /// Retorna (null, null) si no se detecta patron.
    /// </summary>
    public static (string? concentracion, string? unidadMedida) ExtraerConcentracionYUnidad(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) { return (null, null); }
        var m = ConcentracionRx.Match(nombre);
        if (!m.Success) { return (null, null); }
        var num = m.Groups["num"].Value.Replace(",", ".");
        var u = m.Groups["u"].Value.ToLowerInvariant();
        var codigo = u switch
        {
            "mg"   => "01",
            "ml"   => "02",
            "ui" or "iu" => "03",
            "g"    => "04",
            "mcg" or "ug" => "05",
            "%"    => "06",
            _ => null
        };
        return ($"{num} {u}", codigo);
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

    public IReadOnlyList<string> Validate(RipsPayload payload) => ValidateWith(payload, RipsCatalogos.Empty);

    /// <summary>
    /// Overload de Validate que ademas usa el catalogo de Diagnosticos (R8) para
    /// advertir sobre CIE-10 no registrados en el tenant. El endpoint del service
    /// llama a este overload; los tests puros pueden seguir usando <see cref="Validate"/>.
    /// </summary>
    public IReadOnlyList<string> ValidateWith(RipsPayload payload, RipsCatalogos catalogos)
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

        // R10: manual §3.1 exige numFactura unico en el JSON RIPS. Si el snapshot
        // mezcla varias facturas, la EPS rechazara: hay que generar un snapshot por
        // factura o filtrar antes de generar.
        if (payload.FacturasDetectadas is { Count: > 1 } multi)
        {
            errores.Add($"El snapshot mezcla {multi.Count} facturas distintas ({string.Join(", ", multi.Take(5))}{(multi.Count > 5 ? "..." : "")}). Manual §3.1 exige numFactura unico; genera un snapshot por factura.");
        }

        // Debe haber al menos un servicio; JSON sin servicios lo rechaza MinSalud.
        var totalServicios =
            payload.Servicios.Consultas.Count +
            payload.Servicios.Procedimientos.Count +
            payload.Servicios.Medicamentos.Count +
            payload.Servicios.OtrosServicios.Count +
            payload.Servicios.Urgencias.Count +
            payload.Servicios.Hospitalizacion.Count +
            payload.Servicios.RecienNacidos.Count;
        if (totalServicios == 0)
        {
            errores.Add("El snapshot no contiene ningun servicio facturable (usuarios y servicios estan vacios).");
        }

        // Cada usuario debe tener numDoc + sexo + fecha nacimiento.
        foreach (var u in payload.Usuarios)
        {
            if (string.IsNullOrWhiteSpace(u.NumDocumentoIdentificacion))
            {
                errores.Add($"Usuario consecutivo {u.Consecutivo}: numDocumentoIdentificacion vacio.");
            }
            if (u.CodSexo is not ("M" or "F" or "I"))
            {
                errores.Add($"Usuario {u.NumDocumentoIdentificacion}: codSexo '{u.CodSexo}' invalido (esperado M/F/I).");
            }
            if (string.IsNullOrWhiteSpace(u.FechaNacimiento))
            {
                errores.Add($"Usuario {u.NumDocumentoIdentificacion}: fechaNacimiento vacia.");
            }
        }

        // Cada consulta obliga codPrestador + diagnostico CIE-10 no vacio.
        foreach (var c in payload.Servicios.Consultas)
        {
            if (string.IsNullOrWhiteSpace(c.CodPrestador))
            {
                errores.Add($"Consulta {c.Consecutivo} ({c.NumDocumentoIdentificacion}): codPrestador vacio (col 'codigo habilitacion' del snapshot).");
            }
            if (string.IsNullOrWhiteSpace(c.CodDiagnosticoPrincipal))
            {
                errores.Add($"Consulta {c.Consecutivo} ({c.NumDocumentoIdentificacion}): codDiagnosticoPrincipal vacio (col 'Diagnostico' del snapshot).");
            }
            // R8: si hay catalogo Diagnosticos, verificar que el CIE-10 este registrado.
            else if (catalogos.CodigosCie10Validos is { } cieSet && cieSet.Count > 0 && !cieSet.Contains(c.CodDiagnosticoPrincipal))
            {
                errores.Add($"Consulta {c.Consecutivo} ({c.NumDocumentoIdentificacion}): codDiagnosticoPrincipal '{c.CodDiagnosticoPrincipal}' no existe en el catalogo Diagnosticos del tenant.");
            }
        }
        foreach (var p in payload.Servicios.Procedimientos)
        {
            if (string.IsNullOrWhiteSpace(p.CodPrestador))
            {
                errores.Add($"Procedimiento {p.Consecutivo} ({p.NumDocumentoIdentificacion}): codPrestador vacio.");
            }
            if (string.IsNullOrWhiteSpace(p.CodProcedimiento))
            {
                errores.Add($"Procedimiento {p.Consecutivo} ({p.NumDocumentoIdentificacion}): codProcedimiento vacio (col 'CUPS' del snapshot).");
            }
        }

        // ==== R5: cuadre financiero (manual seccion 4) ====

        // Recorrido unico por todos los items para acumular por paciente + validar
        // reglas por item. La proyeccion (numDoc, vrServicio, vrModerador, concepto)
        // se calcula una vez y sirve para las 2 verificaciones.
        var items = EnumerarItems(payload).ToList();

        // Regla ciclica (§4.3): vrServicio nunca puede ser negativo (manual §1.1: monetario >= 0).
        // Regla ciclica reversa (§4.3): si conceptoRecaudo == 04, moderador debe ser 0.
        // Si conceptoRecaudo != 04, moderador debe ser > 0 (o el 04 aplica). Si aparece
        // moderador = 0 con concepto != 04, se corrige a 04 en R3 al momento de armar;
        // aqui detectamos inconsistencias que hayan quedado tras edicion inline.
        foreach (var it in items)
        {
            if (it.VrServicio < 0m)
            {
                errores.Add($"{it.Tipo} {it.Consecutivo} ({it.NumDoc}): vrServicio negativo ({it.VrServicio:0.00}). Manual §1.1 exige valores >= 0.");
            }
            if (it.VrModerador < 0m)
            {
                errores.Add($"{it.Tipo} {it.Consecutivo} ({it.NumDoc}): vrPagoModerador negativo ({it.VrModerador:0.00}).");
            }
            if (it.Concepto == "04" && it.VrModerador > 0m)
            {
                errores.Add($"{it.Tipo} {it.Consecutivo} ({it.NumDoc}): conceptoRecaudo=04 (No aplica) pero vrPagoModerador={it.VrModerador:0.00}. Manual §4.3 exige 0 en este caso.");
            }
            if (it.Concepto != "04" && it.VrModerador <= 0m)
            {
                errores.Add($"{it.Tipo} {it.Consecutivo} ({it.NumDoc}): conceptoRecaudo={it.Concepto} pero vrPagoModerador={it.VrModerador:0.00}. Manual §4.3 exige > 0 cuando el concepto no es 04.");
            }
        }

        // Regla §4.1: la suma de copagos por paciente no puede superar la suma de
        // servicios de ese paciente. Se agrupa por numDoc para el cruce.
        foreach (var g in items.GroupBy(x => x.NumDoc))
        {
            var sumServ = g.Sum(x => x.VrServicio);
            var sumMod = g.Sum(x => x.VrModerador);
            if (sumMod > sumServ)
            {
                errores.Add($"Paciente {g.Key}: suma copagos ({sumMod:0.00}) supera suma servicios ({sumServ:0.00}). Manual §4.1.");
            }
        }

        return errores;
    }

    /// <summary>
    /// Total neto del payload (Σ vrServicio - Σ vrPagoModerador). Manual §4.2:
    /// debe cuadrar al centavo con &lt;PayableAmount&gt; del XML de la FEV enviado a la DIAN.
    /// No forzamos el match aqui (no tenemos el FEV en el snapshot) — el operador lo
    /// verifica manualmente contra la factura. La UI puede mostrar este total al lado
    /// del boton "Generar JSON RIPS" para el cruce visual.
    /// </summary>
    public static (decimal TotalServicios, decimal TotalModerador, decimal Neto) TotalNeto(RipsPayload payload)
    {
        decimal totServ = 0m, totMod = 0m;
        foreach (var it in EnumerarItems(payload))
        {
            totServ += it.VrServicio;
            totMod += it.VrModerador;
        }
        return (totServ, totMod, totServ - totMod);
    }

    /// <summary>Aplana los 4 sub-arrays a una proyeccion comun para las validaciones §4.</summary>
    private static IEnumerable<ItemFinanciero> EnumerarItems(RipsPayload p)
    {
        foreach (var c in p.Servicios.Consultas)
            yield return new ItemFinanciero("Consulta", c.Consecutivo, c.NumDocumentoIdentificacion, c.VrServicio, c.VrPagoModerador, c.ConceptoRecaudo);
        foreach (var pr in p.Servicios.Procedimientos)
            yield return new ItemFinanciero("Procedimiento", pr.Consecutivo, pr.NumDocumentoIdentificacion, pr.VrServicio, pr.VrPagoModerador, pr.ConceptoRecaudo);
        foreach (var m in p.Servicios.Medicamentos)
            yield return new ItemFinanciero("Medicamento", m.Consecutivo, m.NumDocumentoIdentificacion, m.VrServicio, m.VrPagoModerador, m.ConceptoRecaudo);
        foreach (var o in p.Servicios.OtrosServicios)
            yield return new ItemFinanciero("OtroServicio", o.Consecutivo, o.NumDocumentoIdentificacion, o.VrServicio, o.VrPagoModerador, o.ConceptoRecaudo);
    }

    private sealed record ItemFinanciero(string Tipo, int Consecutivo, string NumDoc, decimal VrServicio, decimal VrModerador, string Concepto);

    // ==== Helpers ====

    /// <summary>
    /// Sexo -> M/F/I. Acepta variantes en espanol: "MASCULINO"/"HOMBRE"/"M",
    /// "FEMENINO"/"MUJER"/"F". Cualquier otro texto no reconocido cae en "I".
    /// </summary>
    public static string NormalizarSexo(string s)
    {
        var v = (s ?? "").Trim().ToUpperInvariant();
        if (v.Length == 0) { return "I"; }
        if (v == "M" || v.StartsWith("MASC") || v == "HOMBRE") { return "M"; }
        if (v == "F" || v.StartsWith("FEM")  || v == "MUJER")  { return "F"; }
        return "I";
    }

    /// <summary>
    /// Tipo documento -> codigos oficiales del catalogo MinSalud (2 letras):
    /// CC/TI/CE/PA/RC/AS/MS/SC/PE/PT. Acepta el texto completo o el codigo. Si
    /// no reconoce, deja el input original en mayusculas (mejor pasar algo raro
    /// que perder la fila).
    /// </summary>
    public static string NormalizarTipoDoc(string s)
    {
        var v = (s ?? "").Trim().ToUpperInvariant();
        if (v.Length == 0) { return string.Empty; }
        return v switch
        {
            "CC" or "TI" or "CE" or "PA" or "RC" or "AS" or "MS" or "SC" or "PE" or "PT" => v,
            "CEDULA" or "CEDULA DE CIUDADANIA" or "C.C." or "C.C" => "CC",
            "TARJETA DE IDENTIDAD" or "T.I." or "T.I" => "TI",
            "CEDULA DE EXTRANJERIA" or "C.E." or "C.E" => "CE",
            "PASAPORTE" => "PA",
            "REGISTRO CIVIL" or "R.C." or "R.C" => "RC",
            "ADULTO SIN IDENTIFICAR" => "AS",
            "MENOR SIN IDENTIFICAR" => "MS",
            "SALVOCONDUCTO" => "SC",
            "PERMISO ESPECIAL" or "PERMISO ESPECIAL DE PERMANENCIA" => "PE",
            "PERMISO POR PROTECCION TEMPORAL" or "PPT" => "PT",
            _ => v
        };
    }

    /// <summary>
    /// Regimen -> tipoUsuario 2 digitos MinSalud (§3.2). "CONTRIBUTIVO"->01,
    /// "SUBSIDIADO"->02, "VINCULADO"->03, "PARTICULAR"->04. Si ya viene en
    /// 2 digitos, passthrough.
    /// </summary>
    public static string NormalizarRegimen(string s)
    {
        var v = (s ?? "").Trim().ToUpperInvariant();
        if (v.Length == 0) { return string.Empty; }
        if (v.Length == 2 && v.All(char.IsDigit)) { return v; }
        if (v.StartsWith("CONTRIB")) { return "01"; }
        if (v.StartsWith("SUBSID"))  { return "02"; }
        if (v.StartsWith("VINCUL"))  { return "03"; }
        if (v.StartsWith("PARTIC"))  { return "04"; }
        if (v.Contains("ESPECIAL"))  { return "05"; }
        return v;
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
