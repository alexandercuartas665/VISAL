using System.Text.Json;
using System.Text.Json.Nodes;

namespace Visal.Application.Tenancy.Turnos;

/// <summary>
/// Parser + validador del JSON <c>grid_data</c> que se persiste en
/// <see cref="Visal.Domain.Entities.TurnoProgramacion.GridDataJson"/>. Formato:
///
/// <code>
/// {
///   "turnos": ["Turno 1", "Turno 2"],
///   "dias": {
///     "Turno 1": { "1": {"tipo":"M","horas":8}, "2": {"tipo":"M","horas":8} },
///     "Turno 2": { "5": {"tipo":"T","horas":8} }
///   }
/// }
/// </code>
///
/// Formato legado (del prototipo VB): la celda podia ser string suelto en vez de objeto
/// (<c>dias["Turno 1"]["1"] = "M"</c>). El parser normaliza a la forma canonica usando
/// las horas por defecto del tipo. Esto permite importar plantillas de vis_admturnos
/// si algun dia aparece BD real, sin romper nada.
///
/// Reglas duras validadas por el servicio (min 1, max 7 turnos, overload 24h/dia
/// opcional) viven en el metodo <see cref="Validate"/>.
/// </summary>
public sealed class GridDataModel
{
    public const int MinTurnos = 1;
    public const int MaxTurnos = 7;

    /// <summary>Lista ordenada de nombres de turno del dia. Determina el orden visual
    /// en la grilla del editor.</summary>
    public List<string> Turnos { get; init; } = new();

    /// <summary>Celdas indexadas por nombre de turno -> dia (string "1".."31") -> celda.
    /// El diccionario externo usa case-sensitive porque los turnos son nombres libres
    /// del usuario ("Manana Fija" != "manana fija" no aplica aca — el editor los
    /// deduplica al agregar).</summary>
    public Dictionary<string, Dictionary<string, GridCell>> Dias { get; init; } = new();

    /// <summary>Serializa a JSON canonico (celdas siempre como objeto {tipo, horas}).</summary>
    public string ToJson()
    {
        var obj = new JsonObject
        {
            ["turnos"] = new JsonArray(Turnos.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
            ["dias"] = new JsonObject()
        };
        var dias = (JsonObject)obj["dias"]!;
        foreach (var turno in Turnos)
        {
            var celdas = new JsonObject();
            if (Dias.TryGetValue(turno, out var celdasTurno))
            {
                foreach (var (dia, celda) in celdasTurno)
                {
                    celdas[dia] = new JsonObject
                    {
                        ["tipo"] = celda.Tipo,
                        ["horas"] = celda.Horas
                    };
                }
            }
            dias[turno] = celdas;
        }
        return obj.ToJsonString();
    }

    /// <summary>Parsea un JSON tolerando tanto formato canonico como el legado.
    /// Nunca lanza — un JSON invalido devuelve un modelo con un solo turno vacio
    /// para que el editor siempre tenga algo que mostrar.</summary>
    public static GridDataModel FromJson(string? json)
    {
        var model = new GridDataModel();
        if (string.IsNullOrWhiteSpace(json))
        {
            model.Turnos.Add("Turno 1");
            model.Dias["Turno 1"] = new Dictionary<string, GridCell>();
            return model;
        }
        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null)
            {
                model.Turnos.Add("Turno 1");
                model.Dias["Turno 1"] = new Dictionary<string, GridCell>();
                return model;
            }

            var turnos = root["turnos"]?.AsArray();
            if (turnos is not null)
            {
                foreach (var t in turnos)
                {
                    var nombre = t?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(nombre)) { model.Turnos.Add(nombre); }
                }
            }
            if (model.Turnos.Count == 0) { model.Turnos.Add("Turno 1"); }

            var dias = root["dias"]?.AsObject();
            foreach (var turno in model.Turnos)
            {
                model.Dias[turno] = new Dictionary<string, GridCell>();
                var celdasNodo = dias?[turno]?.AsObject();
                if (celdasNodo is null) { continue; }
                foreach (var (dia, celdaNodo) in celdasNodo)
                {
                    if (celdaNodo is null) { continue; }
                    // Formato canonico: objeto {tipo, horas}
                    if (celdaNodo is JsonObject obj)
                    {
                        var tipo = obj["tipo"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(tipo)) { continue; }
                        var horas = TryReadDecimal(obj["horas"]) ?? 0m;
                        model.Dias[turno][dia] = new GridCell(tipo, horas);
                    }
                    // Formato legado: string suelto con solo el tipo (horas quedan en 0
                    // porque no sabemos el default aca — el servicio o el editor pueden
                    // rellenar consultando el catalogo TipoTurno).
                    else if (celdaNodo is JsonValue)
                    {
                        var tipo = celdaNodo.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(tipo))
                        {
                            model.Dias[turno][dia] = new GridCell(tipo, 0m);
                        }
                    }
                }
            }
        }
        catch
        {
            // JSON basura: modelo vacio como fallback.
            model.Turnos.Clear();
            model.Turnos.Add("Turno 1");
            model.Dias.Clear();
            model.Dias["Turno 1"] = new Dictionary<string, GridCell>();
        }
        return model;
    }

    /// <summary>Suma total de horas del dia N sumando todos los turnos del dia.
    /// Usado por la validacion de overload (>24h) y por el resumen del editor.</summary>
    public decimal HorasDelDia(int dia)
    {
        var key = dia.ToString();
        var total = 0m;
        foreach (var celdasTurno in Dias.Values)
        {
            if (celdasTurno.TryGetValue(key, out var celda)) { total += celda.Horas; }
        }
        return total;
    }

    /// <summary>Ejecuta las reglas duras. Devuelve el primer error o null si todo OK.
    /// <paramref name="bloquearOverload"/> = false: no chequea >24h/dia (solo warning en UI).
    /// <paramref name="diasEnMes"/>: 28..31 segun (Anio, Mes) — solo se valida overload
    /// para dias reales del mes, no siempre hasta 31.</summary>
    public string? Validate(bool bloquearOverload, int diasEnMes)
    {
        if (Turnos.Count < MinTurnos) { return $"Debe haber al menos {MinTurnos} turno."; }
        if (Turnos.Count > MaxTurnos) { return $"Maximo {MaxTurnos} turnos por programacion."; }
        // Nombres unicos (case sensitive: legacy hacia lo mismo).
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Turnos)
        {
            if (string.IsNullOrWhiteSpace(t)) { return "Un turno tiene nombre vacio."; }
            if (!vistos.Add(t)) { return $"Nombre de turno duplicado: '{t}'."; }
        }
        // Overload por dia (opcional).
        if (bloquearOverload)
        {
            for (var d = 1; d <= diasEnMes; d++)
            {
                var h = HorasDelDia(d);
                if (h > 24m) { return $"El dia {d} supera 24h ({h}h) sumando todos los turnos."; }
            }
        }
        return null;
    }

    private static decimal? TryReadDecimal(JsonNode? node)
    {
        if (node is null) { return null; }
        try { return node.GetValue<decimal>(); }
        catch
        {
            // Puede venir como number JS que no cabe en decimal o como string.
            try { return decimal.TryParse(node.ToString(), out var d) ? d : null; }
            catch { return null; }
        }
    }
}

/// <summary>Una celda de la grilla: tipo (codigo del <see cref="Visal.Domain.Entities.TipoTurno"/>)
/// + horas efectivas. Horas puede diferir del default del tipo si el usuario las override
/// en el editor antes de pintar la celda.</summary>
public sealed record GridCell(string Tipo, decimal Horas);
