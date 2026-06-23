namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Aplica las rutas de prefill cuyo sourceModule = "historiaMedica". A diferencia
/// de PacientePrefillHelper (datos estaticos del paciente), aqui las fuentes son
/// DERIVADAS de la instancia actual de la HC: medicamentos, remisiones,
/// incapacidades, certificaciones, ordenes de servicios solicitados.
///
/// Destino TEXTO (textarea/text): se llena con una "lista numerada" generada a
/// partir de los items. Destino TABLA: se expande en filas, mapeando cada
/// campo del item a la columna correspondiente por heuristica de nombre/label.
/// Los campos rellenados se marcan como readonly en el FormViewer (badge auto).
/// </summary>
public static class HistoriaMedicaPrefillHelper
{
    /// <summary>Catalogo de campos disponibles bajo sourceModule = "historiaMedica".</summary>
    public static readonly string[] CamposDisponibles = new[]
    {
        "medicamentos.lista_numerada",
        "remisiones.lista_numerada",
        "incapacidades.lista_numerada",
        "certificaciones.lista_numerada",
        "ordenes_servicio.lista_numerada",
        "insumos.lista_numerada"
    };

    /// <summary>Contenedor con todas las listas derivadas de la HC actual.</summary>
    public sealed record HmFuentes(
        IReadOnlyList<OrdenMedicamentoItemDto> Medicamentos,
        IReadOnlyList<RemisionItemDto> Remisiones,
        IReadOnlyList<IncapacidadItemDto> Incapacidades,
        IReadOnlyList<CertificacionItemDto> Certificaciones,
        IReadOnlyList<OrdenServicioItemDto> OrdenesServicio,
        IReadOnlyList<InsumoItemDto> Insumos)
    {
        public static HmFuentes Empty => new(
            Array.Empty<OrdenMedicamentoItemDto>(),
            Array.Empty<RemisionItemDto>(),
            Array.Empty<IncapacidadItemDto>(),
            Array.Empty<CertificacionItemDto>(),
            Array.Empty<OrdenServicioItemDto>(),
            Array.Empty<InsumoItemDto>());
    }

    /// <summary>
    /// Construye el diccionario de valores derivados (lista_numerada como string)
    /// a partir de los datos actuales de la HC. Las claves coinciden con
    /// CamposDisponibles. Solo se usa cuando el destino es un campo de texto.
    /// </summary>
    public static Dictionary<string, string?> BuildValores(HmFuentes fuentes)
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["medicamentos.lista_numerada"] = ListaNumeradaMedicamentos(fuentes.Medicamentos),
            ["remisiones.lista_numerada"] = ListaNumeradaRemisiones(fuentes.Remisiones),
            ["incapacidades.lista_numerada"] = ListaNumeradaIncapacidades(fuentes.Incapacidades),
            ["certificaciones.lista_numerada"] = ListaNumeradaCertificaciones(fuentes.Certificaciones),
            ["ordenes_servicio.lista_numerada"] = ListaNumeradaOrdenesServicio(fuentes.OrdenesServicio),
            ["insumos.lista_numerada"] = ListaNumeradaInsumos(fuentes.Insumos)
        };
    }

    public static string ListaNumeradaMedicamentos(IReadOnlyList<OrdenMedicamentoItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var m in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ").Append(m.NombreMedicamento);
            var posologia = !string.IsNullOrWhiteSpace(m.Posologia)
                ? m.Posologia!
                : string.Join(" - ", new[] { m.Cantidad, m.Frecuencia, m.Dias }.Where(x => !string.IsNullOrWhiteSpace(x))!);
            if (!string.IsNullOrWhiteSpace(posologia)) { sb.Append(" - ").Append(posologia); }
            if (!string.IsNullOrWhiteSpace(m.Observacion)) { sb.Append(" - ").Append(m.Observacion); }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string ListaNumeradaRemisiones(IReadOnlyList<RemisionItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var r in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ").Append(r.EspecialidadNombre);
            if (!string.IsNullOrWhiteSpace(r.EspecialidadCodigo)) { sb.Append(" (").Append(r.EspecialidadCodigo).Append(')'); }
            if (!string.IsNullOrWhiteSpace(r.Capitulo)) { sb.Append(" - ").Append(r.Capitulo); }
            if (!string.IsNullOrWhiteSpace(r.Motivo)) { sb.Append(" - ").Append(r.Motivo); }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string ListaNumeradaIncapacidades(IReadOnlyList<IncapacidadItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var inc in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ").Append(inc.Motivo);
            if (inc.FechaDesde is DateOnly d && inc.FechaHasta is DateOnly h)
            {
                sb.Append(" - del ").Append(d.ToString("dd/MM/yyyy")).Append(" al ").Append(h.ToString("dd/MM/yyyy"));
            }
            if (inc.Dias is int dias) { sb.Append(" (").Append(dias).Append(" dias)"); }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string ListaNumeradaCertificaciones(IReadOnlyList<CertificacionItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var c in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ").Append(c.Titulo);
            if (!string.IsNullOrWhiteSpace(c.Contenido))
            {
                var resumen = c.Contenido.Length > 120 ? c.Contenido[..120] + "..." : c.Contenido;
                sb.Append(" - ").Append(resumen);
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string ListaNumeradaOrdenesServicio(IReadOnlyList<OrdenServicioItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var s in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ");
            if (!string.IsNullOrWhiteSpace(s.CodigoServicio)) { sb.Append(s.CodigoServicio).Append(" - "); }
            sb.Append(s.Descripcion);
            if (!string.IsNullOrWhiteSpace(s.Cantidad)) { sb.Append(" - ").Append(s.Cantidad); }
            if (!string.IsNullOrWhiteSpace(s.Observaciones)) { sb.Append(" - ").Append(s.Observaciones); }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string ListaNumeradaInsumos(IReadOnlyList<InsumoItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var s in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ");
            if (!string.IsNullOrWhiteSpace(s.Codigo)) { sb.Append(s.Codigo).Append(" - "); }
            sb.Append(s.Descripcion);
            if (!string.IsNullOrWhiteSpace(s.Cantidad)) { sb.Append(" - ").Append(s.Cantidad); }
            if (!string.IsNullOrWhiteSpace(s.Observaciones)) { sb.Append(" - ").Append(s.Observaciones); }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Aplica los mapeos de la ruta sourceModule = "historiaMedica" al diccionario
    /// de valores del formulario. Si el destino es un campo de texto, escribe la
    /// lista_numerada como string. Si el destino es una tabla, expande los items
    /// en filas mapeando por nombre/label de columna.
    /// Devuelve el conjunto de keys (Name del campo, o tbl:{id}:{i}:{col} para
    /// celdas) que fueron poblados, para marcarlos readonly en el FormViewer.
    /// </summary>
    public static HashSet<string> Aplicar(
        Dictionary<string, string?> valores,
        HmFuentes fuentes,
        PrefillRouteSet rutas,
        FormSchema? schema = null)
    {
        var readOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ruta = rutas.Routes.FirstOrDefault(r =>
            string.Equals(r.SourceModule, "historiaMedica", StringComparison.OrdinalIgnoreCase));
        if (ruta is null || ruta.Mappings.Count == 0) { return readOnly; }

        var stringFuente = BuildValores(fuentes);

        var nodosPorName = new Dictionary<string, FormNode>(StringComparer.OrdinalIgnoreCase);
        if (schema is not null)
        {
            void Walk(IEnumerable<FormNode> ns)
            {
                foreach (var n in ns)
                {
                    if (n.IsSection && n.Children is not null) { Walk(n.Children); continue; }
                    if (!string.IsNullOrWhiteSpace(n.Name)) { nodosPorName[n.Name!] = n; }
                }
            }
            Walk(schema.Children);
        }

        foreach (var m in ruta.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Source) || string.IsNullOrWhiteSpace(m.Target)) { continue; }

            FormNode? targetNode = null;
            nodosPorName.TryGetValue(m.Target, out targetNode);

            if (targetNode is { IsTable: true, Columns: { Count: > 0 } cols })
            {
                AplicarTabla(valores, readOnly, targetNode, cols, m.Source, fuentes, m.ColumnMappings);
            }
            else
            {
                if (stringFuente.TryGetValue(m.Source, out var v))
                {
                    valores[m.Target] = v;
                    readOnly.Add(m.Target);
                }
            }
        }
        return readOnly;
    }

    private static void AplicarTabla(
        Dictionary<string, string?> valores,
        HashSet<string> readOnly,
        FormNode tableNode,
        List<FormColumn> cols,
        string source,
        HmFuentes fuentes,
        Dictionary<string, string>? columnMappings = null)
    {
        IList<Dictionary<string, string?>>? rows = source.Trim().ToLowerInvariant() switch
        {
            "medicamentos.lista_numerada" => fuentes.Medicamentos.OrderBy(m => m.Orden).Select(MedicamentoToFields).ToList<Dictionary<string, string?>>(),
            "remisiones.lista_numerada" => fuentes.Remisiones.OrderBy(m => m.Orden).Select(RemisionToFields).ToList<Dictionary<string, string?>>(),
            "incapacidades.lista_numerada" => fuentes.Incapacidades.OrderBy(m => m.Orden).Select(IncapacidadToFields).ToList<Dictionary<string, string?>>(),
            "certificaciones.lista_numerada" => fuentes.Certificaciones.OrderBy(m => m.Orden).Select(CertificacionToFields).ToList<Dictionary<string, string?>>(),
            "ordenes_servicio.lista_numerada" => fuentes.OrdenesServicio.OrderBy(m => m.Orden).Select(OrdenServicioToFields).ToList<Dictionary<string, string?>>(),
            "insumos.lista_numerada" => fuentes.Insumos.OrderBy(m => m.Orden).Select(InsumoToFields).ToList<Dictionary<string, string?>>(),
            _ => null
        };

        if (rows is null) { return; }

        var seedCount = tableNode.SeedRows?.Count ?? 0;
        var key = $"tbl:{tableNode.Id}";

        if (!tableNode.LockRows)
        {
            var rowsKey = $"{key}:_rows";
            valores[rowsKey] = rows.Count.ToString();
            readOnly.Add(rowsKey);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var fila = rows[i];
            var idx = seedCount + i;
            foreach (var col in cols)
            {
                // Mapeo explicito columna -> source field si el usuario lo
                // configuro en el modal. Cae a la heuristica de nombre si no
                // hay entrada para esta columna.
                string? v = null;
                if (columnMappings is not null
                    && columnMappings.TryGetValue(col.Id, out var explicitSource)
                    && !string.IsNullOrWhiteSpace(explicitSource))
                {
                    var k = NormalizeKey(explicitSource);
                    fila.TryGetValue(k, out v);
                }
                if (v is null) { v = ResolveColumnValue(col, fila); }
                if (v is null) { continue; }
                var cellKey = $"{key}:{idx}:{col.Id}";
                valores[cellKey] = v;
                readOnly.Add(cellKey);
            }
        }

        // Limpia celdas residuales de filas que el doctor habia agregado y ya no caben.
        for (int i = rows.Count; i < rows.Count + 20; i++)
        {
            var idx = seedCount + i;
            foreach (var col in cols)
            {
                var cellKey = $"{key}:{idx}:{col.Id}";
                if (valores.ContainsKey(cellKey)) { valores[cellKey] = ""; }
            }
        }
    }

    private static string? ResolveColumnValue(FormColumn col, Dictionary<string, string?> fila)
    {
        if (!string.IsNullOrWhiteSpace(col.Name))
        {
            var k = NormalizeKey(col.Name!);
            if (fila.TryGetValue(k, out var v) && v is not null) { return v; }
        }
        if (!string.IsNullOrWhiteSpace(col.Label))
        {
            var k = NormalizeKey(col.Label);
            if (fila.TryGetValue(k, out var v) && v is not null) { return v; }
        }
        return null;
    }

    private static string NormalizeKey(string s)
    {
        var arr = new char[s.Length];
        var n = 0;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) { arr[n++] = char.ToLowerInvariant(c); }
        }
        return new string(arr, 0, n);
    }

    private static Dictionary<string, string?> MedicamentoToFields(OrdenMedicamentoItemDto m)
    {
        var posologia = !string.IsNullOrWhiteSpace(m.Posologia)
            ? m.Posologia!
            : string.Join(" - ", new[] { m.Cantidad, m.Frecuencia, m.Dias }.Where(x => !string.IsNullOrWhiteSpace(x))!);
        // Cantidad total = cantidad por toma * frecuencia/dia * dias, cuando los
        // tres son numericos. Es lo que el doctor pone en "Cantidad Total" de
        // la orden impresa para autorizar despacho en farmacia.
        var total = CalcularTotalUnidades(m.Cantidad, m.Frecuencia, m.Dias);
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["descripcion"] = m.NombreMedicamento,
            ["nombre"] = m.NombreMedicamento,
            ["medicamento"] = m.NombreMedicamento,
            ["nombremedicamento"] = m.NombreMedicamento,
            ["codigo"] = m.CodigoMedicamento,
            ["codigomedicamento"] = m.CodigoMedicamento,
            ["registrosanitario"] = m.CodigoMedicamento,
            // Cantidad por toma (texto literal del doctor).
            ["cantidad"] = m.Cantidad,
            // Cantidad total despachable = c * f * d cuando se puede calcular,
            // si no, cae al texto literal de cantidad por toma.
            ["cantidadtotal"] = total ?? m.Cantidad,
            ["frecuencia"] = m.Frecuencia,
            ["dias"] = m.Dias,
            ["posologia"] = posologia,
            ["dosis"] = posologia,
            // Posologia tambien va a "via": el HC-FO-08 tiene una columna
            // "Via Administracion" donde el doctor pone "VIA ORAL c/8h x 5 dias"
            // o similar; mejor el dato derivado que celda vacia.
            ["via"] = posologia,
            ["viaadministracion"] = posologia,
            ["observacion"] = m.Observacion,
            ["obs"] = m.Observacion,
            ["observaciones"] = m.Observacion
        };
    }

    /// <summary>Calcula cantidad total = cantidad por toma * frecuencia/dia * dias.
    /// Devuelve null si alguno de los tres no es un numero positivo (caso libre,
    /// ej. "cada 8h" o "indefinido").</summary>
    public static string? CalcularTotalUnidades(string? cantidad, string? frecuencia, string? dias)
    {
        if (decimal.TryParse(cantidad?.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var c)
            && decimal.TryParse(frecuencia?.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var f)
            && int.TryParse(dias?.Trim(), out var d)
            && c > 0 && f > 0 && d > 0)
        {
            var total = c * f * d;
            return total == Math.Truncate(total)
                ? ((int)total).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static Dictionary<string, string?> RemisionToFields(RemisionItemDto r)
    {
        var label = string.IsNullOrWhiteSpace(r.EspecialidadCodigo)
            ? r.EspecialidadNombre
            : $"{r.EspecialidadCodigo} - {r.EspecialidadNombre}";
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["descripcion"] = label,
            ["especialidad"] = r.EspecialidadNombre,
            ["especialidadnombre"] = r.EspecialidadNombre,
            ["nombreespecialidad"] = r.EspecialidadNombre,
            ["nombre"] = r.EspecialidadNombre,
            ["codigo"] = r.EspecialidadCodigo,
            ["codigoespecialidad"] = r.EspecialidadCodigo,
            ["capitulo"] = r.Capitulo,
            ["motivo"] = r.Motivo,
            ["observacion"] = r.Motivo,
            ["obs"] = r.Motivo,
            ["observaciones"] = r.Motivo
        };
    }

    private static Dictionary<string, string?> IncapacidadToFields(IncapacidadItemDto i)
    {
        // Las fechas las emitimos en ISO (yyyy-MM-dd) porque las columnas
        // fieldType=date del FormViewer usan <input type="date">, que solo
        // acepta ese formato. La vista de impresion reformatea a dd/MM/yyyy
        // cuando detecta una celda con valor ISO en columna date.
        var desdeIso = i.FechaDesde?.ToString("yyyy-MM-dd");
        var hastaIso = i.FechaHasta?.ToString("yyyy-MM-dd");
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["motivo"] = i.Motivo,
            ["descripcion"] = i.Motivo,
            ["tipo"] = i.Tipo,
            ["dias"] = i.Dias?.ToString(),
            ["fechadesde"] = desdeIso,
            ["desde"] = desdeIso,
            ["fechahasta"] = hastaIso,
            ["hasta"] = hastaIso
        };
    }

    private static Dictionary<string, string?> CertificacionToFields(CertificacionItemDto c)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["titulo"] = c.Titulo,
            ["descripcion"] = c.Titulo,
            ["nombre"] = c.Titulo,
            ["contenido"] = c.Contenido,
            ["texto"] = c.Contenido,
            ["observacion"] = c.Contenido,
            ["obs"] = c.Contenido,
            ["observaciones"] = c.Contenido
        };
    }

    private static Dictionary<string, string?> OrdenServicioToFields(OrdenServicioItemDto s)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["descripcion"] = s.Descripcion,
            ["nombre"] = s.Descripcion,
            ["servicio"] = s.Descripcion,
            ["codigo"] = s.CodigoServicio,
            ["codigoservicio"] = s.CodigoServicio,
            ["codservicio"] = s.CodigoServicio,
            ["cantidad"] = s.Cantidad,
            ["observacion"] = s.Observaciones,
            ["obs"] = s.Observaciones,
            ["observaciones"] = s.Observaciones
        };
    }

    private static Dictionary<string, string?> InsumoToFields(InsumoItemDto s)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["descripcion"] = s.Descripcion,
            ["nombre"] = s.Descripcion,
            ["insumo"] = s.Descripcion,
            ["codigo"] = s.Codigo,
            ["cantidad"] = s.Cantidad,
            ["observacion"] = s.Observaciones,
            ["obs"] = s.Observaciones,
            ["observaciones"] = s.Observaciones
        };
    }
}
