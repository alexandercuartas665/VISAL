// Normaliza strings TODO_EN_MAYUSCULAS a "Sentence case" (primera letra
// mayus, resto minus) dentro de los schema_json de form_definitions.
//
// Solo toca valores que estan dentro de keys de DATO (no etiquetas/labels):
//   defaultValue, options, seedRows, seedRowCellOptions, enabledByValue
//
// El trigger Postgres trg_form_definition_snapshot guarda un snapshot
// automatico de cada UPDATE; si algo sale mal, restaurar desde /formularios
// > Historial.
//
// Uso:
//   dotnet run --project scripts/NormalizeUppercase
//     [-c "Host=localhost;Port=5434;Database=visal_dev;Username=visal;Password=visal"]
//     [--dry-run]

using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace NormalizeUppercase;

public static class Program
{
    // DataKeys: cuando entramos a una de estas, marcamos inData=true para todo
    // el subarbol. Eso captura strings dentro de arrays (seedRows[][], options[])
    // sin necesidad de saber donde estamos en la lista.
    private static readonly HashSet<string> DataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "defaultValue", "options", "seedRows", "seedRowCellOptions", "enabledByValue"
    };

    private static readonly List<string> Changes = new();
    private static bool ShowChanges;

    public static async Task<int> Main(string[] args)
    {
        var connStr = "Host=localhost;Port=5434;Database=visal_dev;Username=visal;Password=visal";
        bool dryRun = false;
        ShowChanges = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-c" && i + 1 < args.Length) { connStr = args[++i]; }
            else if (args[i] == "--dry-run") { dryRun = true; }
            else if (args[i] == "--show") { ShowChanges = true; }
        }

        Console.WriteLine($"Conexion: {connStr.Split(';').First()}...");
        Console.WriteLine($"Dry-run: {dryRun}");

        await using var db = new NpgsqlConnection(connStr);
        await db.OpenAsync();

        var rows = new List<(Guid id, string codigo, string nombre, string json)>();
        await using (var cmd = new NpgsqlCommand("SELECT id, codigo, nombre, schema_json::text FROM form_definitions WHERE schema_json IS NOT NULL", db))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                rows.Add((rd.GetGuid(0), rd.GetString(1), rd.GetString(2), rd.GetString(3)));
            }
        }
        Console.WriteLine($"Form_definitions encontrados: {rows.Count}");

        int totalForms = 0, totalChanges = 0;
        foreach (var (id, codigo, nombre, json) in rows)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (Exception ex) { Console.WriteLine($"  SKIP {codigo}: parse fallo ({ex.Message})"); continue; }
            if (root is null) { continue; }

            Changes.Clear();
            int n = Walk(root, inData: false);
            if (n == 0) { continue; }

            totalForms++;
            totalChanges += n;
            Console.WriteLine($"  {codigo} ({nombre}): {n} valores normalizados.");
            if (ShowChanges)
            {
                foreach (var ch in Changes) { Console.WriteLine($"      {ch}"); }
            }

            if (!dryRun)
            {
                var newJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                await using var upd = new NpgsqlCommand("UPDATE form_definitions SET schema_json = @j::jsonb WHERE id = @i", db);
                upd.Parameters.AddWithValue("j", newJson);
                upd.Parameters.AddWithValue("i", id);
                await upd.ExecuteNonQueryAsync();
            }
        }

        Console.WriteLine();
        Console.WriteLine($"RESUMEN: {totalForms} formularios tocados, {totalChanges} valores transformados.");
        if (dryRun) { Console.WriteLine("[dry-run] No se escribio nada a la BD."); }
        return 0;
    }

    private static int Walk(JsonNode node, bool inData)
    {
        int n = 0;
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kv => kv.Key).ToList();
            foreach (var k in keys)
            {
                var v = obj[k];
                if (v is null) { continue; }
                bool childInData = inData || DataKeys.Contains(k);
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s))
                {
                    if (childInData && IsAllCaps(s))
                    {
                        var ns = ToSentence(s);
                        obj[k] = ns;
                        Changes.Add($"\"{s}\" -> \"{ns}\"");
                        n++;
                    }
                }
                else
                {
                    n += Walk(v, childInData);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var v = arr[i];
                if (v is null) { continue; }
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s))
                {
                    if (inData && IsAllCaps(s))
                    {
                        var ns = ToSentence(s);
                        arr[i] = ns;
                        Changes.Add($"\"{s}\" -> \"{ns}\"");
                        n++;
                    }
                }
                else
                {
                    n += Walk(v, inData);
                }
            }
        }
        return n;
    }

    private static bool IsAllCaps(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        bool hasAlpha = false;
        foreach (var c in s)
        {
            if (char.IsLetter(c))
            {
                hasAlpha = true;
                if (char.IsLower(c)) { return false; }
            }
        }
        return hasAlpha;
    }

    private static string ToSentence(string s)
    {
        var t = s.ToLowerInvariant();
        var chars = t.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                break;
            }
        }
        return new string(chars);
    }
}
