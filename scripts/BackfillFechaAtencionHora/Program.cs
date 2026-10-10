// Recompute OFFLINE de fecha_atencion para HCs con hora "sucia" recuperable
// (Refuerzo 2 / HoraNormalizer). No toca la BD: lee dos archivos JSONL exportados
// desde la BD y escribe un TSV de updates (id -> nueva fecha UTC) que se aplica
// aparte por psql. Reusa la MISMA logica del backend (FechaAtencionHelper, que ya
// normaliza la hora con HoraNormalizer), para que el resultado sea identico al que
// produciria el sistema al guardar la HC.
//
// Regla de seguridad: solo se emite un cambio cuando la fecha recomputada cae en la
// MISMA fecha (Bogota) que la actual y difiere en la HORA. Un cambio de DIA no se
// aplica automatico: se reporta aparte para revision manual.
//
// Uso:
//   dotnet run --project scripts/BackfillFechaAtencionHora -- <schemas.jsonl> <hcs.jsonl> <updates.tsv>
//     schemas.jsonl : {"fd":"<guid>","s64":"<schema_json en base64 UTF8>"}
//     hcs.jsonl     : {"id":"<guid>","fd":"<guid>","fa":<epoch|null>,"v64":"<valores_json base64>"}
//     updates.tsv   : salida -> "<id>\t<nueva_fecha_utc_iso>"
// (valores/schema viajan en base64 para evitar doble-escape de COPY/JSON.)

using System.Globalization;
using System.Text;
using System.Text.Json;
using Visal.Application.Tenancy.Forms;

if (args.Length < 3)
{
    Console.Error.WriteLine("Args: <schemas.jsonl> <hcs.jsonl> <updates.tsv>");
    return 1;
}
var schemasPath = args[0];
var hcsPath = args[1];
var outPath = args[2];
var bog = TimeSpan.FromHours(-5);

// 1) Cargar schemas por FormDefinitionId.
var schemas = new Dictionary<string, FormSchema>(StringComparer.OrdinalIgnoreCase);
foreach (var line in File.ReadLines(schemasPath))
{
    if (string.IsNullOrWhiteSpace(line)) { continue; }
    using var doc = JsonDocument.Parse(line);
    var fd = doc.RootElement.GetProperty("fd").GetString();
    var s64 = doc.RootElement.GetProperty("s64").GetString();
    if (fd is null || string.IsNullOrWhiteSpace(s64)) { continue; }
    var s = Encoding.UTF8.GetString(Convert.FromBase64String(s64));
    try { schemas[fd] = FormSchema.FromJson(s); } catch { /* schema roto -> se ignora */ }
}

int total = 0, cambios = 0, cambioDia = 0, sinCambio = 0, noParse = 0;
var sb = new StringBuilder();
var muestraHora = new List<string>();
var muestraDia = new List<string>();

foreach (var line in File.ReadLines(hcsPath))
{
    if (string.IsNullOrWhiteSpace(line)) { continue; }
    total++;
    using var doc = JsonDocument.Parse(line);
    var root = doc.RootElement;
    var id = root.GetProperty("id").GetString();
    var fd = root.GetProperty("fd").GetString();
    if (id is null || fd is null || !schemas.TryGetValue(fd, out var schema)) { continue; }

    long? faEpoch = root.TryGetProperty("fa", out var faEl) && faEl.ValueKind == JsonValueKind.Number
        ? faEl.GetInt64() : null;
    var v64 = root.TryGetProperty("v64", out var vEl) ? vEl.GetString() : null;
    if (string.IsNullOrWhiteSpace(v64)) { continue; }
    var v = Encoding.UTF8.GetString(Convert.FromBase64String(v64));

    Dictionary<string, string?>? valores;
    try { valores = JsonSerializer.Deserialize<Dictionary<string, string?>>(v); }
    catch { noParse++; continue; }
    if (valores is null) { noParse++; continue; }

    var nueva = FechaAtencionHelper.Calcular(schema, valores);
    if (nueva is null) { continue; }

    var cur = faEpoch is long e ? DateTimeOffset.FromUnixTimeSeconds(e) : (DateTimeOffset?)null;
    var bn = nueva.Value.ToOffset(bog);

    if (cur is null)
    {
        // Sin fecha previa pero ahora computa una: la fijamos.
        sb.Append(id).Append('\t').Append(nueva.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        cambios++;
        if (muestraHora.Count < 20) { muestraHora.Add($"{id}  (null) -> {bn:yyyy-MM-dd HH:mm}"); }
        continue;
    }
    if (nueva.Value == cur.Value) { sinCambio++; continue; }

    var bc = cur.Value.ToOffset(bog);
    if (bc.Date == bn.Date)
    {
        // Mismo dia, distinta hora -> reparacion segura.
        sb.Append(id).Append('\t').Append(nueva.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        cambios++;
        if (muestraHora.Count < 20) { muestraHora.Add($"{id}  {bc:yyyy-MM-dd HH:mm} -> {bn:HH:mm}"); }
    }
    else
    {
        // Cambiaria el DIA -> no se aplica automatico.
        cambioDia++;
        if (muestraDia.Count < 20) { muestraDia.Add($"{id}  {bc:yyyy-MM-dd HH:mm} -> {bn:yyyy-MM-dd HH:mm}"); }
    }
}

File.WriteAllText(outPath, sb.ToString());

Console.WriteLine($"Candidatos leidos      : {total}");
Console.WriteLine($"Reparaciones (hora)    : {cambios}   -> {outPath}");
Console.WriteLine($"Cambios de DIA (revisar): {cambioDia}");
Console.WriteLine($"Sin cambio             : {sinCambio}");
Console.WriteLine($"No parseables          : {noParse}");
if (muestraHora.Count > 0)
{
    Console.WriteLine("\n-- Muestra reparaciones de hora (actual -> nueva, Bogota) --");
    foreach (var m in muestraHora) { Console.WriteLine("  " + m); }
}
if (muestraDia.Count > 0)
{
    Console.WriteLine("\n-- Muestra cambios de DIA (NO aplicados, revisar) --");
    foreach (var m in muestraDia) { Console.WriteLine("  " + m); }
}
return 0;
