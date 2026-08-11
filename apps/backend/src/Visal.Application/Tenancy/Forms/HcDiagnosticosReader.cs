using System.Text.Json;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Un diagnostico capturado dentro del formulario de la HC (tabla "Diagnosticos").
/// A diferencia de medicamentos/remisiones/etc, los diagnosticos NO viven en una
/// entidad/servicio dedicado: se guardan como filas de una tabla del schema_json
/// de la propia HC. De ahi que se lean con <see cref="HcDiagnosticosReader"/> y
/// viajen a otros formatos (formula medica, ordenes) por el sistema de prefill.
/// </summary>
/// <param name="Orden">1-based, en el orden en que estan en la tabla.</param>
/// <param name="Diagnostico">Texto crudo de la celda (ej. "Z251 - NECESIDAD DE...").</param>
/// <param name="Codigo">Codigo CIE parseado del prefijo (ej. "Z251"); null si no se detecta.</param>
/// <param name="Nombre">Descripcion sin el codigo (ej. "NECESIDAD DE INMUNIZACION...").</param>
/// <param name="Origen">Columna Origen (ej. "ENFERMEDAD GENERAL").</param>
/// <param name="Tipo">Columna Tipo (principal/relacionado/...).</param>
/// <param name="Relacion">Columna Relacion.</param>
public sealed record DiagnosticoItemDto(
    int Orden,
    string? Diagnostico,
    string? Codigo,
    string? Nombre,
    string? Origen,
    string? Tipo,
    string? Relacion);

/// <summary>
/// Lee los diagnosticos de una HC directamente de su schema_json + valores_json,
/// ubicando la tabla de diagnosticos por nombre/label (su id y el de sus columnas
/// cambian por formato de HC) y extrayendo cada fila. El codigo CIE se parsea del
/// prefijo de la celda "diagnostico" (formato "CODIGO - Nombre").
/// </summary>
public static class HcDiagnosticosReader
{
    public static List<DiagnosticoItemDto> Leer(string? schemaJson, string? valoresJson)
    {
        var result = new List<DiagnosticoItemDto>();
        if (string.IsNullOrWhiteSpace(schemaJson) || string.IsNullOrWhiteSpace(valoresJson))
        {
            return result;
        }

        FormSchema? schema;
        Dictionary<string, string?>? valores;
        try { schema = JsonSerializer.Deserialize<FormSchema>(schemaJson!); }
        catch { return result; }
        try { valores = JsonSerializer.Deserialize<Dictionary<string, string?>>(valoresJson!); }
        catch { return result; }
        if (schema is null || valores is null) { return result; }

        var tbl = BuscarTablaDiagnosticos(schema.Children);
        if (tbl is null || tbl.Columns is null || tbl.Columns.Count == 0) { return result; }

        var colDx = BuscarColumna(tbl.Columns, "diagn");
        if (colDx is null) { return result; }
        var colOrigen = BuscarColumna(tbl.Columns, "origen");
        var colTipo = BuscarColumna(tbl.Columns, "tipo");
        var colRel = BuscarColumna(tbl.Columns, "relac");

        var key = $"tbl:{tbl.Id}";
        var seed = tbl.SeedRows?.Count ?? 0;
        var extra = 0;
        if (valores.TryGetValue($"{key}:_rows", out var rs) && int.TryParse(rs, out var p) && p > 0)
        {
            extra = p;
        }
        // Defensivo: si _rows no esta seteado escaneamos una ventana amplia para
        // no perder filas capturadas por el doctor.
        var scanMax = Math.Max(seed + extra, 60);

        var orden = 0;
        for (var i = 0; i < scanMax; i++)
        {
            string? Cell(string? colId)
                => colId is null ? null
                   : (valores.TryGetValue($"{key}:{i}:{colId}", out var v) ? v?.Trim() : null);

            var dx = Cell(colDx);
            if (string.IsNullOrWhiteSpace(dx)) { continue; }

            var (cod, nom) = SepararCodigoNombre(dx!);
            result.Add(new DiagnosticoItemDto(
                ++orden, dx, cod, nom, Cell(colOrigen), Cell(colTipo), Cell(colRel)));
        }
        return result;
    }

    /// <summary>Separa "Z251 - NECESIDAD DE..." en (codigo, nombre). Si el prefijo
    /// antes del primer " - " parece un codigo (sin espacios, corto) lo toma como
    /// codigo; si no, deja el codigo en null y todo el texto como nombre.</summary>
    public static (string? Codigo, string? Nombre) SepararCodigoNombre(string raw)
    {
        var s = raw.Trim();
        var idx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
        {
            var izq = s[..idx].Trim();
            var der = s[(idx + 3)..].Trim();
            if (izq.Length > 0 && izq.Length <= 8 && !izq.Contains(' '))
            {
                return (izq, der.Length > 0 ? der : null);
            }
        }
        return (null, s);
    }

    private static FormNode? BuscarTablaDiagnosticos(IEnumerable<FormNode>? nodos)
    {
        if (nodos is null) { return null; }
        foreach (var n in nodos)
        {
            if (n.IsTable && EsDiagnostico(n.Name) || (n.IsTable && EsDiagnostico(n.Label)))
            {
                return n;
            }
            if (n.IsSection)
            {
                var hit = BuscarTablaDiagnosticos(n.Children);
                if (hit is not null) { return hit; }
            }
        }
        return null;
    }

    private static string? BuscarColumna(List<FormColumn> cols, string keyword)
    {
        foreach (var c in cols)
        {
            var txt = ((c.Name ?? "") + " " + (c.Label ?? "")).ToLowerInvariant();
            if (txt.Contains(keyword)) { return c.Id; }
        }
        return null;
    }

    private static bool EsDiagnostico(string? s)
        => !string.IsNullOrWhiteSpace(s) && s!.ToLowerInvariant().Contains("diagn");
}
