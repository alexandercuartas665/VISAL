using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Campos dinamicos de un tablero (portado del pipeline de CUBOT.travels) + valores por
/// tarjeta. Reusa el gate de acceso de <see cref="ITaskBoardService"/> (owner o miembro).
/// </summary>
public sealed class TaskFieldService(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITaskBoardService boards) : ITaskFieldService
{
    // ---- Definiciones de campo ----

    public async Task<IReadOnlyList<TaskFieldDto>> ListFieldsAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        if (!await boards.HasAccessAsync(boardId, actor, ct)) { return []; }
        return await db.TaskFieldDefinitions.AsNoTracking()
            .Where(f => f.BoardId == boardId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => Map(f))
            .ToListAsync(ct);
    }

    public async Task<TaskFieldDto?> CreateFieldAsync(CreateTaskFieldRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return null; }
        if (!await boards.HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        if (string.IsNullOrWhiteSpace(req.Label)) { return null; }

        var existentes = await db.TaskFieldDefinitions.AsNoTracking()
            .Where(f => f.BoardId == req.BoardId)
            .Select(f => new { f.FieldKey, f.SortOrder })
            .ToListAsync(ct);

        var key = EnsureUniqueKey(Slugify(req.Label), existentes.Select(x => x.FieldKey));
        var maxOrder = existentes.Count == 0 ? -1 : existentes.Max(x => x.SortOrder);

        var entity = new TaskFieldDefinition
        {
            TenantId = tid,
            BoardId = req.BoardId,
            FieldKey = key,
            Label = req.Label.Trim(),
            FieldType = req.FieldType,
            Column = Clamp(req.Column, 1, 3),
            SortOrder = maxOrder + 1,
            Options = Trim(req.Options),
            Description = Trim(req.Description),
            ShowInFilter = req.ShowInFilter,
            AllowMultiple = req.AllowMultiple,
            MultiWithDetail = req.AllowMultiple && req.MultiWithDetail,
            TotalSourceKeys = Trim(req.TotalSourceKeys),
            RepeatWithFieldKey = Trim(req.RepeatWithFieldKey)
        };
        db.TaskFieldDefinitions.Add(entity);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<TaskFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTaskFieldRequest req, Guid actor, CancellationToken ct = default)
    {
        var f = await db.TaskFieldDefinitions.FirstOrDefaultAsync(x => x.Id == fieldId, ct);
        if (f is null) { return null; }
        if (!await boards.HasAccessAsync(f.BoardId, actor, ct)) { return null; }
        if (string.IsNullOrWhiteSpace(req.Label)) { return null; }

        f.Label = req.Label.Trim();
        f.FieldType = req.FieldType;
        f.Column = Clamp(req.Column, 1, 3);
        f.Options = Trim(req.Options);
        f.Description = Trim(req.Description);
        f.ShowInFilter = req.ShowInFilter;
        f.AllowMultiple = req.AllowMultiple;
        f.MultiWithDetail = req.AllowMultiple && req.MultiWithDetail;
        f.TotalSourceKeys = Trim(req.TotalSourceKeys);
        f.RepeatWithFieldKey = Trim(req.RepeatWithFieldKey);
        await db.SaveChangesAsync(ct);
        return Map(f);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, Guid actor, CancellationToken ct = default)
    {
        var f = await db.TaskFieldDefinitions.FirstOrDefaultAsync(x => x.Id == fieldId, ct);
        if (f is null) { return false; }
        if (!await boards.HasAccessAsync(f.BoardId, actor, ct)) { return false; }
        db.TaskFieldDefinitions.Remove(f);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReorderFieldsAsync(Guid boardId, IReadOnlyList<Guid> orderedFieldIds, Guid actor, CancellationToken ct = default)
    {
        if (!await boards.HasAccessAsync(boardId, actor, ct)) { return false; }
        var fields = await db.TaskFieldDefinitions.Where(f => f.BoardId == boardId).ToListAsync(ct);
        var pos = 0;
        foreach (var id in orderedFieldIds)
        {
            var f = fields.FirstOrDefault(x => x.Id == id);
            if (f is not null) { f.SortOrder = pos++; }
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Valores por tarjeta ----

    public async Task<Dictionary<string, string?>> GetCardValuesAsync(Guid cardId, Guid actor, CancellationToken ct = default)
    {
        var card = await db.TaskCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => new { c.BoardId, c.FieldValuesJson })
            .FirstOrDefaultAsync(ct);
        if (card is null) { return new(); }
        if (!await boards.HasAccessAsync(card.BoardId, actor, ct)) { return new(); }
        return Deserialize(card.FieldValuesJson);
    }

    public async Task<bool> SaveCardValuesAsync(Guid cardId, IReadOnlyDictionary<string, string?> values, Guid actor, CancellationToken ct = default)
    {
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null) { return false; }
        if (!await boards.HasAccessAsync(card.BoardId, actor, ct)) { return false; }

        // No guardamos claves vacias para no inflar el jsonb.
        var clean = new Dictionary<string, string?>();
        foreach (var kv in values)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value)) { clean[kv.Key] = kv.Value; }
        }
        card.FieldValuesJson = clean.Count == 0 ? null : JsonSerializer.Serialize(clean);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Helpers ----

    private static TaskFieldDto Map(TaskFieldDefinition f) => new(
        f.Id, f.BoardId, f.FieldKey, f.Label, f.FieldType, f.ShowInFilter, f.Column, f.SortOrder,
        f.Options, f.Description, f.AllowMultiple, f.MultiWithDetail, f.TotalSourceKeys, f.RepeatWithFieldKey);

    private static Dictionary<string, string?> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
        }
        catch { return new(); }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>Genera una clave estable a partir de la etiqueta: minusculas, sin tildes, [a-z0-9_].</summary>
    private static string Slugify(string label)
    {
        var norm = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_' or '.' or '/') { sb.Append('_'); }
        }
        var key = sb.ToString().Trim('_');
        while (key.Contains("__")) { key = key.Replace("__", "_"); }
        if (key.Length == 0) { key = "campo"; }
        if (key.Length > 70) { key = key[..70]; }
        return key;
    }

    private static string EnsureUniqueKey(string baseKey, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseKey)) { return baseKey; }
        var n = 2;
        while (set.Contains($"{baseKey}_{n}")) { n++; }
        return $"{baseKey}_{n}";
    }
}
