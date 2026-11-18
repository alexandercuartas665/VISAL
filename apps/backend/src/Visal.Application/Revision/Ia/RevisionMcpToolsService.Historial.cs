using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Revision.Ia;

/// <summary>
/// Historial del paciente para el agente REVISOR CLINICO IA: las historias ANTERIORES (por fecha de
/// atencion) a la historia revisada, dentro de una ventana en meses, en orden cronologico. Es SOLO
/// contexto (continuidad, evolucion, texto copiado, contradicciones); el veredicto es sobre la HC
/// revisada. Acotado por volumen: las mas recientes (priorizando el mismo tipo de servicio) van
/// COMPLETAS hasta <see cref="OpcionesHistorial.MaxCompletas"/> y un presupuesto de tokens; el resto
/// de la ventana va como INDICE (fecha, formato, profesional).
/// </summary>
public sealed partial class RevisionMcpToolsService
{
    private const string EstadoAbiertaTexto = "Abierta - en curso, puede cambiar";
    private const int MinCharsTextoCopiado = 40;
    /// <summary>Similitud (Dice de bigramas de palabras) desde la cual un campo se marca casi-identico.
    /// Calibrado con notas de enfermeria reales: copia/pega 0.97; notas propias del mismo turno-tipo <= 0.89.</summary>
    private const double UmbralCasiIdentico = 0.90;
    /// <summary>Caracteres por token del proveedor para texto clinico en espanol (mayusculas, tildes).
    /// Medido en una corrida real de DeepSeek (~2.6). Con chars/4 el tope quedaba ~1.5x por debajo de lo real.</summary>
    internal const double CharsPorToken = 2.6;
    /// <summary>Tope de lineas de la lista de medicamentos/insumos ordenados en la ventana.</summary>
    private const int MaxLineasOrdenesVentana = 300;
    private static readonly Regex RxEspacios = new(@"\s+", RegexOptions.Compiled);

    private sealed record CandidataHc(
        Guid Id, long Consecutivo, Guid FormDefinitionId, HistoriaClinicaEstado Estado,
        DateTimeOffset Fecha, string? Profesional);

    private static async Task<string> ArmarHistorialPacienteAsync(
        IApplicationDbContext db, Guid hcId, OpcionesHistorial op, CancellationToken ct)
    {
        var obj = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new { h.PacienteId, h.Consecutivo, h.FormDefinitionId, h.FechaAtencion, h.FechaApertura, h.ValoresJson })
            .FirstOrDefaultAsync(ct);
        if (obj is null) { return "{}"; }

        var fechaObj = obj.FechaAtencion ?? obj.FechaApertura;
        var meses = Math.Max(1, op.Meses);
        var desde = fechaObj.AddMonths(-meses);

        // 1) Candidatas: mismo paciente, no inactivas, distinta a la revisada, dentro de la ventana y
        //    ESTRICTAMENTE anteriores (empate de fecha -> consecutivo menor). Nunca posteriores.
        var crudas = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == obj.PacienteId && h.Id != hcId && h.Estado != HistoriaClinicaEstado.Inactiva)
            .Select(h => new
            {
                h.Id, h.Consecutivo, h.FormDefinitionId, h.Estado,
                Fecha = h.FechaAtencion ?? h.FechaApertura,
                h.EspecialistaNombre,
            })
            .Where(h => h.Fecha >= desde && h.Fecha <= fechaObj)
            .ToListAsync(ct);
        var cands = crudas
            .Where(c => c.Fecha < fechaObj || (c.Fecha == fechaObj && c.Consecutivo < obj.Consecutivo))
            .Select(c => new CandidataHc(c.Id, c.Consecutivo, c.FormDefinitionId, c.Estado, c.Fecha, c.EspecialistaNombre))
            .ToList();

        var resumenBase = new
        {
            meses,
            desde,
            hasta = fechaObj,
        };
        if (cands.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                resumen = new
                {
                    ventana = resumenBase,
                    total_en_ventana = 0,
                    incluidas_completas = 0,
                    incluidas_indice = 0,
                    tokens_estimados = 0,
                    nota = "El paciente no tiene historias anteriores a la revisada dentro de la ventana.",
                },
                documentos = Array.Empty<object>(),
            }, JsonOpts);
        }

        // 2) Tipo de servicio de cada HC via pivote sesion -> turno -> asignacion.
        var idsTodas = cands.Select(c => c.Id).Append(hcId).ToList();
        var tiposRaw = await db.AsignacionTurnoSesionHcs.AsNoTracking()
            .Where(p => idsTodas.Contains(p.HistoriaClinicaId))
            .Select(p => new { p.HistoriaClinicaId, Tipo = p.Sesion!.AsignacionTurno!.Asignacion!.TipoServicio })
            .ToListAsync(ct);
        var tipoPorHc = tiposRaw
            .Where(t => !string.IsNullOrWhiteSpace(t.Tipo))
            .GroupBy(t => t.HistoriaClinicaId)
            .ToDictionary(g => g.Key, g => g.First().Tipo);
        tipoPorHc.TryGetValue(hcId, out var tipoObj);

        bool MismoServicio(CandidataHc c)
        {
            if (!string.IsNullOrWhiteSpace(tipoObj))
            {
                return tipoPorHc.TryGetValue(c.Id, out var t)
                    && string.Equals(t?.Trim(), tipoObj.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            return c.FormDefinitionId == obj.FormDefinitionId;
        }

        // 3) Seleccion de COMPLETAS: mismo servicio mas recientes primero, luego las demas mas recientes.
        var recientes = cands.OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Consecutivo).ToList();
        var maxCompletas = Math.Max(0, op.MaxCompletas);
        var completas = recientes.Where(MismoServicio).Take(maxCompletas).ToList();
        if (completas.Count < maxCompletas)
        {
            completas.AddRange(recientes.Where(c => !MismoServicio(c)).Take(maxCompletas - completas.Count));
        }
        var completasIds = completas.Select(c => c.Id).ToList();

        // 4) Carga en lote del detalle de las completas (+ la revisada para el chequeo de copia).
        var idsDetalle = completasIds.Append(hcId).ToList();
        var valoresPorHc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => idsDetalle.Contains(h.Id))
            .Select(h => new { h.Id, h.ValoresJson })
            .ToDictionaryAsync(h => h.Id, h => h.ValoresJson, ct);
        var evos = completasIds.Count == 0 ? new() : await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(d => completasIds.Contains(d.HistoriaClinicaId) && d.Tipo == "EVOLUCION")
            .Select(d => new { d.HistoriaClinicaId, d.FormDefinitionId, d.Consecutivo, d.FechaApertura, d.EspecialistaNombre, d.ValoresJson })
            .ToListAsync(ct);
        var ordenesPorHc = await CargarOrdenesPorHcAsync(db, completasIds, ct);

        var fdIds = cands.Select(c => c.FormDefinitionId)
            .Concat(evos.Select(e => e.FormDefinitionId))
            .Append(obj.FormDefinitionId)
            .Distinct().ToList();
        var formatos = await db.FormDefinitions.AsNoTracking()
            .Where(f => fdIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Codigo, f.Nombre, f.SchemaJson })
            .ToDictionaryAsync(f => f.Id, ct);
        string? SchemaDe(Guid fdId) => formatos.TryGetValue(fdId, out var f) ? f.SchemaJson : null;
        object? FormatoDe(Guid fdId) => formatos.TryGetValue(fdId, out var f) ? new { codigo = f.Codigo, nombre = f.Nombre } : null;

        // Textos largos de la HC revisada para detectar copia/pega: por valor exacto (cualquier campo)
        // y por campo (mismo Name) para la similitud casi-identica.
        var textosObj = TextosLargos(SchemaDe(obj.FormDefinitionId), obj.ValoresJson);
        var objPorValor = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in textosObj) { objPorValor.TryAdd(t.Norm, t.Label); }
        var objPorCampo = textosObj.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Medicamentos e insumos ORDENADOS en TODA la ventana (tambien en historias del indice): una nota
        // de enfermeria administra lo que ordeno un medico en otra historia.
        var ordenesVentana = await ListarOrdenesVentanaAsync(db, cands, formatos.ToDictionary(f => f.Key, f => f.Value.Codigo), ct);

        string? TipoDe(Guid id) => tipoPorHc.TryGetValue(id, out var t) ? t : null;
        string EstadoTexto(HistoriaClinicaEstado e) => e == HistoriaClinicaEstado.Abierta ? EstadoAbiertaTexto : "Cerrada";

        object Completa(CandidataHc c)
        {
            var valores = valoresPorHc.TryGetValue(c.Id, out var v) ? v : "{}";
            var schema = SchemaDe(c.FormDefinitionId);
            var campos = CamposLlenos(schema, valores);

            var identicos = new List<string>();
            var casi = new List<string>();
            foreach (var t in TextosLargos(schema, valores))
            {
                if (objPorValor.TryGetValue(t.Norm, out var labelObj))
                {
                    identicos.Add(string.Equals(t.Label, labelObj, StringComparison.OrdinalIgnoreCase)
                        ? t.Label
                        : $"{t.Label} (igual a '{labelObj}' de la revisada)");
                }
                else if (objPorCampo.TryGetValue(t.Name, out var tObj))
                {
                    var sim = SimilitudBigramas(t.Bigramas, tObj.Bigramas);
                    if (sim >= UmbralCasiIdentico)
                    {
                        casi.Add($"{t.Label} ({Math.Round(sim * 100)}% igual)");
                    }
                }
            }

            var evolucionesHc = evos.Where(e => e.HistoriaClinicaId == c.Id)
                .OrderBy(e => e.FechaApertura)
                .Select(e => new
                {
                    consecutivo = e.Consecutivo,
                    fecha = e.FechaApertura,
                    profesional = e.EspecialistaNombre,
                    formato = formatos.TryGetValue(e.FormDefinitionId, out var ef) ? ef.Codigo : null,
                    campos = CamposLlenos(SchemaDe(e.FormDefinitionId), e.ValoresJson),
                })
                .ToList();

            ordenesPorHc.TryGetValue(c.Id, out var ordenes);
            var ordenesNoVacias = ordenes?.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);

            return new
            {
                detalle = "completa",
                consecutivo = CodigoPublico.Hc(c.Consecutivo),
                fecha_atencion = c.Fecha,
                estado = EstadoTexto(c.Estado),
                formato = FormatoDe(c.FormDefinitionId),
                tipo_servicio = TipoDe(c.Id),
                mismo_servicio_que_revisada = MismoServicio(c),
                profesional = c.Profesional,
                campos,
                campos_identicos_a_la_revisada = identicos.Count > 0 ? identicos : null,
                campos_casi_identicos_a_la_revisada = casi.Count > 0 ? casi : null,
                evoluciones = evolucionesHc.Count > 0 ? evolucionesHc : null,
                ordenes = ordenesNoVacias is { Count: > 0 } ? ordenesNoVacias : null,
            };
        }

        // INDICE compacto: una linea por historia (fecha en hora Colombia, consecutivo, formato,
        // servicio, profesional; "ABIERTA" si esta en curso). Solo prueba que la atencion existio.
        string Indice(CandidataHc c, int n)
        {
            var partes = new List<string>
            {
                "n=" + n,
                CodigoPublico.Hc(c.Consecutivo),
                c.Fecha.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd HH:mm") + " (COL)",
                formatos.TryGetValue(c.FormDefinitionId, out var f) ? f.Codigo : "?",
            };
            if (TipoDe(c.Id) is { } t) { partes.Add(t); }
            if (!string.IsNullOrWhiteSpace(c.Profesional)) { partes.Add(c.Profesional!); }
            if (c.Estado == HistoriaClinicaEstado.Abierta) { partes.Add("ABIERTA"); }
            return string.Join(" | ", partes);
        }

        // Numeracion global cronologica (n=1 la mas antigua) compartida por completas e indice.
        var cronologico = cands.OrderBy(c => c.Fecha).ThenBy(c => c.Consecutivo).ToList();
        var nPorId = cronologico.Select((c, i) => (c.Id, n: i + 1)).ToDictionary(x => x.Id, x => x.n);

        // 5) Presupuesto de tokens: si se pasa, las completas mas antiguas (primero las de otro
        //    servicio) se degradan a indice hasta caber.
        var completaJson = completas.ToDictionary(c => c.Id, c => JsonSerializer.Serialize(Completa(c), JsonOpts));
        var indiceTxt = cands.ToDictionary(c => c.Id, c => Indice(c, nPorId[c.Id]));
        var setCompletas = completasIds.ToHashSet();
        var charsOrdenes = ordenesVentana.Sum(l => l.Length + 3);
        int Tokens() => (int)Math.Ceiling((cands.Sum(c => setCompletas.Contains(c.Id) ? completaJson[c.Id].Length : indiceTxt[c.Id].Length + 3)
                                           + charsOrdenes) / CharsPorToken);
        var maxTokens = Math.Max(1000, op.MaxTokens);
        var ordenDegradar = completas
            .OrderBy(c => MismoServicio(c) ? 1 : 0)
            .ThenBy(c => c.Fecha).ThenBy(c => c.Consecutivo)
            .ToList();
        foreach (var c in ordenDegradar)
        {
            if (Tokens() <= maxTokens) { break; }
            setCompletas.Remove(c.Id);
        }

        // 6) Salida cronologica ascendente: 'documentos' (completas) e 'indice' (el resto).
        var documentos = new List<JsonElement>();
        var indice = new List<string>();
        foreach (var c in cronologico)
        {
            if (setCompletas.Contains(c.Id))
            {
                // Antepone el ordinal n sin re-serializar el objeto.
                documentos.Add(JsonDocument.Parse("{\"n\":" + nPorId[c.Id] + "," + completaJson[c.Id][1..]).RootElement.Clone());
            }
            else
            {
                indice.Add(indiceTxt[c.Id]);
            }
        }

        var payload = new
        {
            resumen = new
            {
                ventana = resumenBase,
                tipo_servicio_revisada = tipoObj,
                total_en_ventana = cands.Count,
                incluidas_completas = setCompletas.Count,
                incluidas_indice = cands.Count - setCompletas.Count,
                tokens_estimados = Tokens(),
                criterio = "Orden cronologico ascendente por fecha de atencion; 'n' numera TODAS las historias de "
                         + "la ventana (n=1 la mas antigua). 'documentos' trae COMPLETAS las mas recientes, "
                         + "priorizando el mismo tipo de servicio de la revisada. 'indice' lista el resto en una "
                         + "linea cada una: solo prueba que la atencion existio, no muestra su contenido. "
                         + "'medicamentos_insumos_ordenados_en_ventana' lista lo ORDENADO al paciente en TODA la "
                         + "ventana (incluidas las historias del indice).",
            },
            medicamentos_insumos_ordenados_en_ventana = ordenesVentana.Count > 0 ? ordenesVentana : null,
            documentos,
            indice = indice.Count > 0 ? indice : null,
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    /// <summary>Campos LLENOS de una historia del historial como {label: valor} (reusa
    /// <see cref="BuildCatalogo"/>). Sin campos de ENCABEZADO (no. historia, documento, nombre, edad...:
    /// se repiten en cada historia y la fecha ya viene en fecha_atencion) ni firmas. Textos hasta
    /// 800 caracteres para poder juzgar evolucion. Labels repetidos se desambiguan con sufijo.</summary>
    private static Dictionary<string, string> CamposLlenos(string? schemaJson, string? valoresJson)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in BuildCatalogo(schemaJson, valoresJson, maxChars: 800).Campos)
        {
            if (c.Estado != "lleno" || c.Valor is null) { continue; }
            if (c.Name is not null && c.Name.StartsWith("hdr:", StringComparison.Ordinal)) { continue; }
            if (c.Valor == "(firma registrada)") { continue; }
            var key = c.Campo;
            for (var i = 2; dict.ContainsKey(key); i++) { key = $"{c.Campo} ({i})"; }
            dict[key] = c.Valor;
        }
        return dict;
    }

    private sealed record TextoCampo(string Name, string Label, string Norm, HashSet<string> Bigramas);

    /// <summary>Textos largos (>= 40 chars) de los campos del CUERPO de una historia: valor COMPLETO
    /// normalizado (espacios colapsados, mayusculas) + bigramas de palabras. Se usa para detectar
    /// copia/pega (exacta o casi-identica) entre la revisada y las anteriores sin depender del truncado.</summary>
    private static List<TextoCampo> TextosLargos(string? schemaJson, string? valoresJson)
    {
        var res = new List<TextoCampo>();
        if (string.IsNullOrWhiteSpace(valoresJson)) { return res; }

        // Solo campos del CUERPO: los de encabezado (direccion, nombre...) se repiten por naturaleza y
        // no son copia/pega clinica.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var schema = FormSchema.FromJson(schemaJson);
        void Recurse(IEnumerable<FormNode>? nodes)
        {
            if (nodes is null) { return; }
            foreach (var nd in nodes)
            {
                if (nd.IsSection) { Recurse(nd.Children); continue; }
                if (nd.IsText || string.IsNullOrWhiteSpace(nd.Name)) { continue; }
                labels[nd.Name!] = string.IsNullOrWhiteSpace(nd.Label) ? nd.Name! : nd.Label!;
            }
        }
        Recurse(schema.Children);

        try
        {
            using var doc = JsonDocument.Parse(valoresJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return res; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) { continue; }
                if (p.Name.Contains("firma", StringComparison.OrdinalIgnoreCase)) { continue; }
                // Solo campos del formato (excluye claves internas/tecnicas).
                if (!labels.TryGetValue(p.Name, out var label)) { continue; }
                var raw = p.Value.GetString();
                if (string.IsNullOrWhiteSpace(raw)) { continue; }
                var norm = RxEspacios.Replace(raw.Trim(), " ").ToUpperInvariant();
                if (norm.Length < MinCharsTextoCopiado) { continue; }
                if (norm.StartsWith("DATA:", StringComparison.Ordinal)) { continue; } // imagenes base64
                res.Add(new TextoCampo(p.Name, label, norm, Bigramas(norm)));
            }
        }
        catch (JsonException) { /* valores corruptos: sin chequeo de copia */ }
        return res;
    }

    private static readonly Regex RxPalabra = new(@"\w+", RegexOptions.Compiled);

    /// <summary>Bigramas de palabras de un texto ya normalizado.</summary>
    private static HashSet<string> Bigramas(string norm)
    {
        var palabras = RxPalabra.Matches(norm).Select(m => m.Value).ToList();
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < palabras.Count; i++) { set.Add(palabras[i] + " " + palabras[i + 1]); }
        return set;
    }

    /// <summary>Coeficiente de Dice entre dos conjuntos de bigramas (0..1).</summary>
    private static double SimilitudBigramas(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) { return 0; }
        var comunes = a.Count <= b.Count ? a.Count(b.Contains) : b.Count(a.Contains);
        return 2.0 * comunes / (a.Count + b.Count);
    }

    /// <summary>Medicamentos e insumos ORDENADOS al paciente en TODA la ventana del historial (de
    /// cualquier historia, tambien las del indice), una linea cada uno en orden cronologico. Permite
    /// validar lo que una nota de enfermeria administra contra lo que un medico ordeno en otra historia.</summary>
    private static async Task<List<string>> ListarOrdenesVentanaAsync(
        IApplicationDbContext db, List<CandidataHc> cands, Dictionary<Guid, string> codigoFormato, CancellationToken ct)
    {
        var ids = cands.Select(c => c.Id).ToList();
        var porId = cands.ToDictionary(c => c.Id);
        var meds = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => ids.Contains(x.HistoriaClinicaId))
            .Select(x => new { x.HistoriaClinicaId, x.NombreMedicamento, x.Cantidad, x.Posologia, x.Frecuencia, x.Dias })
            .ToListAsync(ct);
        var insumos = await db.HistoriaClinicaInsumos.AsNoTracking()
            .Where(x => ids.Contains(x.HistoriaClinicaId))
            .Select(x => new { x.HistoriaClinicaId, x.Descripcion, x.Cantidad })
            .ToListAsync(ct);

        string Cabeza(Guid hcId)
        {
            var c = porId[hcId];
            var fmt = codigoFormato.TryGetValue(c.FormDefinitionId, out var f) ? f : "?";
            return $"{CodigoPublico.Hc(c.Consecutivo)} | {c.Fecha.ToOffset(TimeSpan.FromHours(-5)):yyyy-MM-dd} | {fmt}";
        }
        static string Txt(object? v) => v?.ToString()?.Trim() ?? "";
        static string Opc(string etiqueta, object? v, string sufijo = "")
        {
            var t = Txt(v);
            return t.Length > 0 ? $" | {etiqueta}{t}{sufijo}" : "";
        }

        var lineas = meds.Select(m => (Hc: porId[m.HistoriaClinicaId], Linea:
                $"{Cabeza(m.HistoriaClinicaId)} | MEDICAMENTO: {Txt(m.NombreMedicamento)}"
                + Opc("cant ", m.Cantidad) + Opc("", m.Posologia) + Opc("", m.Frecuencia) + Opc("", m.Dias, " dias")))
            .Concat(insumos.Select(i => (Hc: porId[i.HistoriaClinicaId], Linea:
                $"{Cabeza(i.HistoriaClinicaId)} | INSUMO: {Txt(i.Descripcion)}" + Opc("cant ", i.Cantidad))))
            .ToList();

        // Si hay demasiadas, conserva las MAS RECIENTES; salida cronologica ascendente.
        return lineas
            .OrderByDescending(l => l.Hc.Fecha).ThenByDescending(l => l.Hc.Consecutivo)
            .Take(MaxLineasOrdenesVentana)
            .OrderBy(l => l.Hc.Fecha).ThenBy(l => l.Hc.Consecutivo)
            .Select(l => l.Linea)
            .ToList();
    }

    /// <summary>Ordenes de varias historias en LOTE (una query por tabla), por HC y por categoria.
    /// Mismas proyecciones que la tool <c>list_ordenes_hc</c>.</summary>
    private static async Task<Dictionary<Guid, Dictionary<string, List<object>>>> CargarOrdenesPorHcAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> hcIds, CancellationToken ct)
    {
        var res = hcIds.Distinct().ToDictionary(id => id, _ => new Dictionary<string, List<object>>
        {
            ["medicamentos"] = new(), ["servicios"] = new(), ["incapacidades"] = new(),
            ["certificaciones"] = new(), ["remisiones"] = new(), ["insumos"] = new(),
            ["rxExternos"] = new(), ["laboratoriosExternos"] = new(), ["serviciosExternos"] = new(),
            ["insumosExternos"] = new(),
        });
        if (res.Count == 0) { return res; }
        var ids = res.Keys.ToList();

        void Add(Guid hc, string cat, object item)
        {
            if (res.TryGetValue(hc, out var d)) { d[cat].Add(item); }
        }

        foreach (var x in await db.HistoriaClinicaMedicamentos.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new
                     {
                         x.HistoriaClinicaId,
                         Item = new
                         {
                             Codigo = x.CodigoMedicamento, Nombre = x.NombreMedicamento,
                             x.Cantidad, x.Posologia, x.Frecuencia, x.Dias, x.Observacion,
                         },
                     }).ToListAsync(ct))
        { Add(x.HistoriaClinicaId, "medicamentos", x.Item); }

        foreach (var x in await db.HistoriaClinicaOrdenesServicio.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new { x.HistoriaClinicaId, Item = new { x.CodigoServicio, x.Descripcion, x.Cantidad, x.Observaciones } })
                     .ToListAsync(ct))
        { Add(x.HistoriaClinicaId, "servicios", x.Item); }

        foreach (var x in await db.HistoriaClinicaIncapacidades.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new { x.HistoriaClinicaId, Item = new { x.Motivo, x.FechaDesde, x.FechaHasta, x.Dias, x.Tipo } })
                     .ToListAsync(ct))
        { Add(x.HistoriaClinicaId, "incapacidades", x.Item); }

        foreach (var x in await db.HistoriaClinicaCertificaciones.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new { x.HistoriaClinicaId, Item = new { x.Titulo, x.Contenido } })
                     .ToListAsync(ct))
        { Add(x.HistoriaClinicaId, "certificaciones", x.Item); }

        foreach (var x in await db.HistoriaClinicaRemisiones.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new { x.HistoriaClinicaId, Item = new { x.Capitulo, x.EspecialidadCodigo, x.EspecialidadNombre, x.Cantidad, x.Motivo } })
                     .ToListAsync(ct))
        { Add(x.HistoriaClinicaId, "remisiones", x.Item); }

        foreach (var x in await db.HistoriaClinicaInsumos.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new { x.HistoriaClinicaId, Item = new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones } })
                     .ToListAsync(ct))
        { Add(x.HistoriaClinicaId, "insumos", x.Item); }

        // REV-9 — Ordenes EXTERNAS (a otra IPS): una sola tabla discriminada por Tipo.
        foreach (var x in await db.HistoriaClinicaOrdenesExternas.AsNoTracking()
                     .Where(x => ids.Contains(x.HistoriaClinicaId))
                     .Select(x => new { x.HistoriaClinicaId, x.Tipo, Item = new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones } })
                     .ToListAsync(ct))
        {
            var cat = x.Tipo switch
            {
                TipoCatalogoServicio.RxImagenologia => "rxExternos",
                TipoCatalogoServicio.Laboratorio => "laboratoriosExternos",
                TipoCatalogoServicio.ServicioGeneral => "serviciosExternos",
                TipoCatalogoServicio.Insumo => "insumosExternos",
                _ => null,
            };
            if (cat is not null) { Add(x.HistoriaClinicaId, cat, x.Item); }
        }
        return res;
    }
}
