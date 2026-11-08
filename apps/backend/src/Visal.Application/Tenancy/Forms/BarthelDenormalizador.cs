using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Implementacion de <see cref="IBarthelDenormalizador"/>. Identifica la escala Barthel
/// por su codigo/nombre (contiene "barthel", insensible a mayusculas) y lee el puntaje del
/// campo <c>barthel_total</c> y la clasificacion de <c>barthel_clasificacion</c> dentro del
/// diccionario plano de valores de la escala. Tenant-scoped via query filters de EF Core.
/// </summary>
// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class BarthelDenormalizador(IApplicationDbContextFactory dbFactory) : IBarthelDenormalizador
{
    public const string CampoTotal = "barthel_total";
    public const string CampoClasificacion = "barthel_clasificacion";

    public async Task<bool> AplicarDesdeEscalaAsync(Guid escalaId, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var row = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(e => e.Id == escalaId)
            .Join(db.FormDefinitions.AsNoTracking(), e => e.FormDefinitionId, f => f.Id, (e, f) => new
            {
                e.HistoriaClinicaId, e.ValoresJson, e.Estado, e.FechaCierre,
                f.Codigo, f.Nombre
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) { return false; }
        if (row.Estado != HistoriaClinicaEstado.Cerrada) { return false; }
        if (!EsBarthel(row.Codigo, row.Nombre)) { return false; }

        var pacienteId = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == row.HistoriaClinicaId)
            .Select(h => (Guid?)h.PacienteId)
            .FirstOrDefaultAsync(ct);
        if (pacienteId is not Guid pid) { return false; }

        var paciente = await db.Pacientes.FirstOrDefaultAsync(p => p.Id == pid, ct);
        if (paciente is null) { return false; }

        var fecha = row.FechaCierre ?? DateTimeOffset.UtcNow;
        // Latest-wins: no pisar con una escala igual o mas antigua que la ya guardada.
        if (paciente.BarthelActualizadoEn is DateTimeOffset prev && prev >= fecha) { return false; }

        Aplicar(paciente, row.ValoresJson, fecha);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RecalcularTodosAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var barthelFormIds = await db.FormDefinitions.AsNoTracking()
            .Where(f => (f.Codigo != null && f.Codigo.ToLower().Contains("barthel"))
                        || (f.Nombre != null && f.Nombre.ToLower().Contains("barthel")))
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (barthelFormIds.Count == 0) { return 0; }

        var escalas = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(e => barthelFormIds.Contains(e.FormDefinitionId)
                        && e.Estado == HistoriaClinicaEstado.Cerrada
                        && e.FechaCierre != null)
            .Join(db.HistoriasClinicas.AsNoTracking(), e => e.HistoriaClinicaId, h => h.Id,
                (e, h) => new { h.PacienteId, e.ValoresJson, e.FechaCierre })
            .ToListAsync(ct);
        if (escalas.Count == 0) { return 0; }

        // Por paciente, la escala cerrada mas reciente.
        var ultimas = escalas
            .GroupBy(x => x.PacienteId)
            .Select(g => g.OrderByDescending(x => x.FechaCierre).First())
            .ToList();

        var ids = ultimas.Select(u => u.PacienteId).ToList();
        var pacientes = await db.Pacientes.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var n = 0;
        foreach (var u in ultimas)
        {
            if (!pacientes.TryGetValue(u.PacienteId, out var paciente)) { continue; }
            Aplicar(paciente, u.ValoresJson, u.FechaCierre!.Value);
            n++;
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    private static bool EsBarthel(string? codigo, string? nombre)
    {
        return (codigo?.Contains("barthel", StringComparison.OrdinalIgnoreCase) ?? false)
               || (nombre?.Contains("barthel", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>Escribe en el paciente el puntaje, la clasificacion, el snapshot completo y la fecha.</summary>
    private static void Aplicar(Paciente paciente, string? valoresJson, DateTimeOffset fecha)
    {
        var valores = ParseValores(valoresJson);
        paciente.BarthelTotal = ParsearEntero(Buscar(valores, CampoTotal, "total"));
        paciente.BarthelClasificacion = Limpiar(Buscar(valores, CampoClasificacion, "clasific", "grado"));
        // Snapshot: el json plano tal cual (re-serializado para normalizar), o null si vacio.
        paciente.BarthelDatosJson = valores.Count == 0 ? null : JsonSerializer.Serialize(valores);
        paciente.BarthelActualizadoEn = fecha;
    }

    private static Dictionary<string, string?> ParseValores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>Valor por clave exacta; si falta, primera clave que contenga alguno de los fragmentos.</summary>
    private static string? Buscar(Dictionary<string, string?> valores, string claveExacta, params string[] fragmentos)
    {
        if (valores.TryGetValue(claveExacta, out var v) && !string.IsNullOrWhiteSpace(v)) { return v; }
        foreach (var kv in valores)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) { continue; }
            foreach (var frag in fragmentos)
            {
                if (kv.Key.Contains(frag, StringComparison.OrdinalIgnoreCase)) { return kv.Value; }
            }
        }
        return null;
    }

    private static int? ParsearEntero(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { return n; }
        var m = Regex.Match(s, "-?\\d+");
        return m.Success && int.TryParse(m.Value, out var n2) ? n2 : null;
    }

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
