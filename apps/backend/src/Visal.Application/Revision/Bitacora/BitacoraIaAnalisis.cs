using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Visal.Application.Revision.Ia;

namespace Visal.Application.Revision.Bitacora;

/// <summary>Una corrida del agente lista para analizar (ya deserializada).</summary>
public sealed record BitacoraIntentoInput(
    Guid Id,
    DateTimeOffset En,
    bool Ok,
    string? Resultado,
    IReadOnlyList<string> Hallazgos,
    IReadOnlyList<SugerenciaTexto> Sugerencias,
    IReadOnlyDictionary<string, string?> Campos);

/// <summary>Campo de la HC que cambio entre el intento anterior y este.</summary>
public sealed record BitacoraCampoCambio(string Campo, string? Antes, string? Despues);

/// <summary>Hallazgo de un intento con su comparacion contra el intento anterior.</summary>
/// <param name="Estado">"Primero" (primer intento), "Nuevo" (no estaba antes) o "Repetido".</param>
/// <param name="Alerta">Codigo de incoherencia o null: RepiteTrasCorregir, NuevoSinCambio,
/// ContradiceSugerencia.</param>
public sealed record BitacoraHallazgo(
    string Texto,
    string Estado,
    double Similitud,
    string? HallazgoAnterior,
    IReadOnlyList<string> CamposMencionados,
    bool CampoMencionadoCambio,
    string? Alerta);

/// <summary>Resultado del analisis de UN intento contra el anterior intento valido.</summary>
public sealed record BitacoraIntentoAnalisis(
    Guid Id,
    int Numero,
    Guid? AnteriorId,
    IReadOnlyList<BitacoraCampoCambio> CamposCambiados,
    IReadOnlyList<BitacoraHallazgo> Hallazgos,
    IReadOnlyList<string> HallazgosResueltos,
    IReadOnlyList<string> SugerenciasAdoptadas,
    int Alertas);

/// <summary>Senales de incoherencia acumuladas de todos los intentos de una HC.</summary>
/// <remarks><see cref="Total"/> cuenta solo las INCOHERENCIAS del agente (nuevo sin cambio +
/// contradice su sugerencia). RepiteTrasCorregir va aparte: puede ser legitimo (la correccion no
/// alcanzo) y sirve para revisar, no prueba un error del agente.</remarks>
public sealed record BitacoraSenales(
    int RepiteTrasCorregir,
    int NuevoSinCambio,
    int ContradiceSugerencia)
{
    public int Total => NuevoSinCambio + ContradiceSugerencia;
}

/// <summary>
/// IMP-72: compara los intentos sucesivos del auditor IA sobre una misma HC para detectar
/// revisiones incoherentes ("corregi lo que pidio y ahora pide otra cosa"). Heuristico y puro
/// (sin BD): similitud de palabras (Dice) entre hallazgos, campos mencionados por su rotulo y
/// diferencias de la HC legible guardada en cada corrida.
/// </summary>
public static class BitacoraIaAnalisis
{
    public const string AlertaRepiteTrasCorregir = "RepiteTrasCorregir";
    public const string AlertaNuevoSinCambio = "NuevoSinCambio";
    public const string AlertaContradiceSugerencia = "ContradiceSugerencia";

    /// <summary>Similitud minima para considerar que dos hallazgos dicen lo mismo.</summary>
    public const double UmbralRepetido = 0.45;
    /// <summary>Similitud minima cuando ademas nombran el mismo campo o la misma HC de referencia.</summary>
    public const double UmbralMismoCampo = 0.2;
    /// <summary>Fraccion minima de las palabras de la sugerencia que deben estar en el campo.</summary>
    public const double UmbralSugerenciaAdoptada = 0.7;

    public static string TextoAlerta(string? codigo) => codigo switch
    {
        AlertaRepiteTrasCorregir => "Insiste en el hallazgo aunque el campo se corrigio (revisar si la correccion alcanzo)",
        AlertaNuevoSinCambio => "Hallazgo nuevo sobre algo que no cambio (antes no lo marco)",
        AlertaContradiceSugerencia => "Vuelve a marcar un campo donde se copio su propia sugerencia",
        _ => "",
    };

    /// <summary>Analiza los intentos en orden cronologico. Los intentos fallidos (Ok=false) no se
    /// comparan: el intento N se compara contra el ultimo intento VALIDO anterior.</summary>
    public static IReadOnlyList<BitacoraIntentoAnalisis> Analizar(IReadOnlyList<BitacoraIntentoInput> intentos)
    {
        var res = new List<BitacoraIntentoAnalisis>();
        BitacoraIntentoInput? prev = null;
        var historial = new List<string>(); // hallazgos de TODOS los intentos validos anteriores
        var vigentes = new Dictionary<string, string?>(StringComparer.Ordinal); // campo -> texto adoptado
        var numero = 0;
        foreach (var cur in intentos.OrderBy(i => i.En).ThenBy(i => i.Id))
        {
            numero++;
            if (!cur.Ok)
            {
                res.Add(new BitacoraIntentoAnalisis(cur.Id, numero, prev?.Id, [], [], [], [], 0));
                continue;
            }
            res.Add(prev is null ? Primero(cur, numero) : Comparar(prev, cur, numero, historial, vigentes));
            historial.AddRange(cur.Hallazgos);
            prev = cur;
        }
        return res;
    }

    public static BitacoraSenales Senales(IEnumerable<BitacoraIntentoAnalisis> analisis)
    {
        int a = 0, b = 0, c = 0;
        foreach (var h in analisis.SelectMany(x => x.Hallazgos))
        {
            switch (h.Alerta)
            {
                case AlertaRepiteTrasCorregir: a++; break;
                case AlertaNuevoSinCambio: b++; break;
                case AlertaContradiceSugerencia: c++; break;
            }
        }
        return new BitacoraSenales(a, b, c);
    }

    private static BitacoraIntentoAnalisis Primero(BitacoraIntentoInput cur, int numero)
    {
        var labels = Labels(cur.Campos);
        var hs = cur.Hallazgos
            .Select(h => new BitacoraHallazgo(h, "Primero", 0, null, Mencionados(h, labels), false, null))
            .ToList();
        return new BitacoraIntentoAnalisis(cur.Id, numero, null, [], hs, [], [], 0);
    }

    private static BitacoraIntentoAnalisis Comparar(BitacoraIntentoInput prev, BitacoraIntentoInput cur, int numero,
        IReadOnlyList<string> historial, Dictionary<string, string?> vigentes)
    {
        var cambios = DiffCampos(prev.Campos, cur.Campos);
        var cambiados = new HashSet<string>(cambios.Select(c => c.Campo), StringComparer.Ordinal);
        var labels = Labels(cur.Campos);
        var labelsPrev = Labels(prev.Campos);

        // Sugerencias del intento anterior que el profesional COPIO: el campo cambio y el texto nuevo
        // contiene casi todas las palabras sugeridas (y mas que el texto de antes, porque el agente
        // redacta con las mismas palabras del campo). La adopcion sigue vigente mientras el campo no
        // vuelva a cambiar. Si el agente marca un campo con su propia sugerencia, se contradice.
        foreach (var k in vigentes.Keys.ToList())
        {
            if (!cur.Campos.TryGetValue(k, out var v) || Compacto(v) != Compacto(vigentes[k])) { vigentes.Remove(k); }
        }
        var adoptadas = new List<string>();
        foreach (var s in prev.Sugerencias)
        {
            var campo = ResolverCampo(s.Campo, cur.Campos);
            if (campo is null || !cambiados.Contains(campo)) { continue; }
            if (!cur.Campos.TryGetValue(campo, out var valor) || string.IsNullOrWhiteSpace(valor)) { continue; }
            var sug = SinMarcadores(s.TextoSugerido);
            prev.Campos.TryGetValue(campo, out var antes);
            var cAhora = Contencion(sug, valor);
            if (cAhora >= UmbralSugerenciaAdoptada && cAhora >= Contencion(sug, antes) + 0.1 && !adoptadas.Contains(campo))
            {
                adoptadas.Add(campo);
                vigentes[campo] = valor;
            }
        }
        var adoptadasSet = new HashSet<string>(vigentes.Keys, StringComparer.Ordinal);

        var hallazgos = new List<BitacoraHallazgo>();
        foreach (var h in cur.Hallazgos)
        {
            var menc = Mencionados(h, labels);
            // "Repetido" = mismo tema que algun hallazgo de CUALQUIER intento anterior (el agente
            // reformula: se mira similitud de palabras y, con menos exigencia, si nombran los mismos
            // campos o la misma HC de referencia).
            string? anterior = null;
            var mejor = 0.0;
            foreach (var p in historial)
            {
                var sim = Similitud(h, p);
                var umbral = UmbralRepetido;
                if (menc.Count > 0 && Mencionados(p, labelsPrev).Concat(Mencionados(p, labels)).Any(menc.Contains)) { umbral = UmbralMismoCampo; }
                else if (ComparteHc(h, p) || ComparteCitado(h, p)) { umbral = UmbralMismoCampo; }
                if (sim >= umbral && sim > mejor) { mejor = sim; anterior = p; }
            }
            var repetido = anterior is not null;
            var campoCambio = menc.Any(cambiados.Contains);

            string? alerta = null;
            if (menc.Any(adoptadasSet.Contains)) { alerta = AlertaContradiceSugerencia; }
            else if (repetido && campoCambio) { alerta = AlertaRepiteTrasCorregir; }
            else if (!repetido && (cambios.Count == 0 || (menc.Count > 0 && !campoCambio))) { alerta = AlertaNuevoSinCambio; }

            if (!repetido) { mejor = historial.Count == 0 ? 0 : historial.Max(p => Similitud(h, p)); }
            hallazgos.Add(new BitacoraHallazgo(h, repetido ? "Repetido" : "Nuevo", Math.Round(mejor, 2),
                anterior, menc, campoCambio, alerta));
        }

        // Resueltos: hallazgos del intento inmediatamente anterior que ya no aparecen.
        var resueltos = prev.Hallazgos
            .Where(p => !cur.Hallazgos.Any(h => Similitud(h, p) >= UmbralRepetido
                || (ComparteCampo(h, p, labels, labelsPrev) && Similitud(h, p) >= UmbralMismoCampo)
                || ((ComparteHc(h, p) || ComparteCitado(h, p)) && Similitud(h, p) >= UmbralMismoCampo)))
            .ToList();

        return new BitacoraIntentoAnalisis(cur.Id, numero, prev.Id, cambios, hallazgos, resueltos, adoptadas,
            hallazgos.Count(x => x.Alerta is not null));
    }

    /// <summary>Campos que cambiaron (agregados, quitados o editados). Ignora claves internas
    /// ("__tmpl:", "__hm:") y diferencias solo de espacios.</summary>
    public static IReadOnlyList<BitacoraCampoCambio> DiffCampos(
        IReadOnlyDictionary<string, string?> antes, IReadOnlyDictionary<string, string?> despues)
    {
        var res = new List<BitacoraCampoCambio>();
        foreach (var k in antes.Keys.Union(despues.Keys))
        {
            if (k.StartsWith("__", StringComparison.Ordinal)) { continue; }
            antes.TryGetValue(k, out var a);
            despues.TryGetValue(k, out var d);
            if (Compacto(a) == Compacto(d)) { continue; }
            res.Add(new BitacoraCampoCambio(k, a, d));
        }
        return res;
    }

    /// <summary>Deserializa la HC legible guardada en el log ({ rotulo: contenido }).</summary>
    public static IReadOnlyDictionary<string, string?> ParseCampos(string? json)
    {
        var res = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) { return res; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return res; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                res[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => p.Value.GetRawText(),
                };
            }
        }
        catch (JsonException) { }
        return res;
    }

    /// <summary>Hallazgos de la corrida (sin las observaciones, IMP-73).</summary>
    public static IReadOnlyList<string> ParseHallazgos(string? json) => Ia.HallazgosIa.Separar(json).Hallazgos;

    /// <summary>Observaciones de la corrida (IMP-73: no impiden aprobar; no entran al analisis).</summary>
    public static IReadOnlyList<string> ParseObservaciones(string? json) => Ia.HallazgosIa.Separar(json).Observaciones;

    public static IReadOnlyList<SugerenciaTexto> ParseSugerencias(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return []; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return []; }
            var res = new List<SugerenciaTexto>();
            foreach (var it in doc.RootElement.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) { continue; }
                var campo = it.TryGetProperty("campo", out var c) ? c.GetString() : null;
                var texto = it.TryGetProperty("texto_sugerido", out var t) ? t.GetString() : null;
                if (!string.IsNullOrWhiteSpace(campo) && !string.IsNullOrWhiteSpace(texto))
                {
                    res.Add(new SugerenciaTexto(campo!, texto!));
                }
            }
            return res;
        }
        catch (JsonException) { return []; }
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>Similitud de Dice sobre el conjunto de palabras significativas (0..1).</summary>
    public static double Similitud(string? a, string? b)
    {
        var x = Palabras(a);
        var y = Palabras(b);
        if (x.Count == 0 || y.Count == 0) { return 0; }
        var inter = x.Count(y.Contains);
        return 2.0 * inter / (x.Count + y.Count);
    }

    private sealed record Label(string Campo, string Norm, string Base, bool UnaPalabra);

    /// <summary>Rotulos de la HC (sin el sufijo " (2)" de duplicados). Solo los de al menos 5 letras
    /// para evitar falsos positivos con rotulos cortos ("TA", "FC").</summary>
    private static IReadOnlyList<Label> Labels(IReadOnlyDictionary<string, string?> campos)
        => campos.Keys
            .Where(k => !k.StartsWith("__", StringComparison.Ordinal) && !k.StartsWith("tbl:", StringComparison.Ordinal))
            .Select(k =>
            {
                var b = QuitarSufijoDuplicado(k).Trim();
                var n = Normalizar(b);
                return new Label(k, n, b, !n.Contains(' '));
            })
            .Where(x => x.Norm.Length >= 5)
            .ToList();

    /// <summary>Campos que nombra un hallazgo. Rotulos de varias palabras: coincidencia normalizada.
    /// Rotulos de UNA palabra (p. ej. "Registro", "RECIBE"): solo si van entre comillas o, si el
    /// rotulo esta en mayusculas, escritos igual en mayusculas (evita confundir palabras comunes).</summary>
    private static IReadOnlyList<string> Mencionados(string hallazgo, IReadOnlyList<Label> labels)
    {
        var h = " " + Normalizar(hallazgo) + " ";
        var res = new List<string>();
        foreach (var l in labels)
        {
            if (!h.Contains(" " + l.Norm + " ", StringComparison.Ordinal)) { continue; }
            if (l.UnaPalabra && !Entrecomillado(hallazgo, l.Base)
                && !(l.Base == l.Base.ToUpperInvariant() && ContienePalabra(hallazgo, l.Base)))
            {
                continue;
            }
            if (!res.Contains(l.Campo)) { res.Add(l.Campo); }
        }
        return res;
    }

    private static readonly string[] Comillas = ["'", "\"", "\u2018", "\u201C"];

    private static bool Entrecomillado(string texto, string palabra)
        => Comillas.Any(c => texto.Contains(c + palabra, StringComparison.OrdinalIgnoreCase));

    private static bool ContienePalabra(string texto, string palabra)
    {
        var i = 0;
        while ((i = texto.IndexOf(palabra, i, StringComparison.Ordinal)) >= 0)
        {
            var antes = i == 0 || !char.IsLetterOrDigit(texto[i - 1]);
            var fin = i + palabra.Length;
            var despues = fin >= texto.Length || !char.IsLetterOrDigit(texto[fin]);
            if (antes && despues) { return true; }
            i = fin;
        }
        return false;
    }

    private static bool ComparteCampo(string a, string b, IReadOnlyList<Label> la, IReadOnlyList<Label> lb)
    {
        var ma = Mencionados(a, la);
        return ma.Count > 0 && Mencionados(b, lb).Any(ma.Contains);
    }

    private static readonly Regex RxHc = new(@"HC-?\s*0*(\d{3,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True si ambos hallazgos citan la misma HC de referencia (p. ej. "HC-031601").</summary>
    private static bool ComparteHc(string a, string b)
    {
        var ha = RxHc.Matches(a).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return ha.Count > 0 && RxHc.Matches(b).Any(m => ha.Contains(m.Groups[1].Value));
    }

    private static readonly Regex RxCitado = new("['\u2018\u201C\"]([^'\u2019\u201D\"]{2,40})['\u2019\u201D\"]", RegexOptions.Compiled);

    /// <summary>True si ambos hallazgos citan entre comillas el mismo termino (p. ej. 'CC', 'Registro').</summary>
    private static bool ComparteCitado(string a, string b)
    {
        var ca = RxCitado.Matches(a).Select(m => Normalizar(m.Groups[1].Value)).Where(x => x.Length >= 2).ToHashSet(StringComparer.Ordinal);
        return ca.Count > 0 && RxCitado.Matches(b).Any(m => ca.Contains(Normalizar(m.Groups[1].Value)));
    }

    private static readonly Regex RxMarcador = new(@"\[COMPLETAR[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string SinMarcadores(string s) => RxMarcador.Replace(s, " ");

    /// <summary>Fraccion de las palabras de <paramref name="parte"/> que estan en <paramref name="todo"/>.</summary>
    public static double Contencion(string? parte, string? todo)
    {
        var x = Palabras(parte);
        if (x.Count == 0) { return 0; }
        var y = Palabras(todo);
        return (double)x.Count(y.Contains) / x.Count;
    }

    /// <summary>El agente nombra el campo de la sugerencia por su rotulo; se busca exacto y luego
    /// normalizado.</summary>
    private static string? ResolverCampo(string campo, IReadOnlyDictionary<string, string?> campos)
    {
        if (campos.ContainsKey(campo)) { return campo; }
        var n = Normalizar(campo);
        return campos.Keys.FirstOrDefault(k => Normalizar(QuitarSufijoDuplicado(k)) == n);
    }

    private static string QuitarSufijoDuplicado(string k)
    {
        var i = k.LastIndexOf(" (", StringComparison.Ordinal);
        return i > 0 && k.EndsWith(')') && int.TryParse(k[(i + 2)..^1], out _) ? k[..i] : k;
    }

    private static string Compacto(string? s)
        => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Minusculas, sin tildes, solo letras/digitos separados por un espacio.</summary>
    public static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return ""; }
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var espacio = false;
        foreach (var ch in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); espacio = false; }
            else if (!espacio && sb.Length > 0) { sb.Append(' '); espacio = true; }
        }
        return sb.ToString().Trim();
    }

    private static readonly HashSet<string> Vacias = new(StringComparer.Ordinal)
    {
        "los", "las", "del", "con", "por", "para", "que", "una", "uno", "unos", "unas", "sin", "como",
        "mas", "pero", "sus", "este", "esta", "esto", "estos", "estas", "ese", "esa", "son", "fue",
        "ser", "hay", "the", "and", "entre", "sobre", "desde", "hasta", "cuando", "donde", "tiene",
        "debe", "deberia", "campo", "registro", "historia", "paciente",
    };

    private static HashSet<string> Palabras(string? s)
        => Normalizar(s).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !Vacias.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
}
