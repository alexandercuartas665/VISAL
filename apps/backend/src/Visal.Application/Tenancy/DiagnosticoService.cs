using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class DiagnosticoService(IApplicationDbContext db, ITenantContext tenant) : IDiagnosticoService
{
    public async Task<(IReadOnlyList<DiagnosticoDto> rows, int total)> SearchAsync(
        string? termino, int skip, int take, bool soloHabilitados, CancellationToken ct = default)
    {
        var q = db.Diagnosticos.AsNoTracking().AsQueryable();
        if (soloHabilitados) { q = q.Where(d => d.Habilitado); }
        if (!string.IsNullOrWhiteSpace(termino))
        {
            var t = termino.Trim().ToLower();
            q = q.Where(d => d.Codigo.ToLower().Contains(t) || d.Nombre.ToLower().Contains(t));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(d => d.Codigo)
            .Skip(skip).Take(take)
            .Select(d => new DiagnosticoDto(d.Id, d.Codigo, d.Nombre, d.Descripcion, d.Habilitado, d.Fuente))
            .ToListAsync(ct);
        return (rows, total);
    }

    public async Task<DiagnosticoDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Diagnosticos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? null : new DiagnosticoDto(d.Id, d.Codigo, d.Nombre, d.Descripcion, d.Habilitado, d.Fuente);
    }

    public async Task<DiagnosticoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var c = (codigo ?? "").Trim();
        if (c.Length == 0) { return null; }
        var cLower = c.ToLower();
        var d = await db.Diagnosticos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codigo.ToLower() == cLower, ct);
        return d is null ? null : new DiagnosticoDto(d.Id, d.Codigo, d.Nombre, d.Descripcion, d.Habilitado, d.Fuente);
    }

    public async Task<DiagnosticoDto?> SaveAsync(SaveDiagnosticoRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0) { throw new InvalidOperationException("El codigo es obligatorio."); }
        if (nombre.Length == 0) { throw new InvalidOperationException("El nombre es obligatorio."); }

        Diagnostico e;
        if (req.Id is Guid gid)
        {
            e = await db.Diagnosticos.FirstOrDefaultAsync(x => x.Id == gid, ct)
                ?? throw new InvalidOperationException("Diagnostico no encontrado.");
            if (await db.Diagnosticos.AnyAsync(x => x.Codigo == codigo && x.Id != gid, ct))
            {
                throw new InvalidOperationException($"Ya existe un diagnostico con codigo '{codigo}'.");
            }
        }
        else
        {
            if (tenant.TenantId is not Guid tid) { return null; }
            if (await db.Diagnosticos.AnyAsync(x => x.Codigo == codigo, ct))
            {
                throw new InvalidOperationException($"Ya existe un diagnostico con codigo '{codigo}'.");
            }
            e = new Diagnostico { TenantId = tid };
            db.Diagnosticos.Add(e);
        }
        e.Codigo = codigo;
        e.Nombre = nombre;
        e.Descripcion = req.Descripcion?.Trim();
        e.Habilitado = req.Habilitado;
        e.Fuente = req.Fuente?.Trim();
        await db.SaveChangesAsync(ct);
        return new DiagnosticoDto(e.Id, e.Codigo, e.Nombre, e.Descripcion, e.Habilitado, e.Fuente);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.Diagnosticos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.Diagnosticos.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(int inserted, int updated)> ImportAsync(
        IReadOnlyList<DiagnosticoImportRow> rows,
        Guid actor,
        IProgress<DiagnosticoImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return (0, 0); }

        // Sanitizar y de-duplicar por codigo dentro del batch mismo (el Excel puede
        // traer duplicados; nos quedamos con la ultima ocurrencia).
        var limpias = new Dictionary<string, DiagnosticoImportRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var cod = (r.Codigo ?? string.Empty).Trim();
            var nom = (r.Nombre ?? string.Empty).Trim();
            if (cod.Length == 0 || nom.Length == 0) { continue; }
            limpias[cod] = r with { Codigo = cod, Nombre = nom };
        }

        var total = limpias.Count;
        progress?.Report(new DiagnosticoImportProgress("Validando", 0, total));

        // Cargar los codigos ya existentes del tenant para hacer upsert eficiente.
        var codigos = limpias.Keys.ToList();
        var existentes = await db.Diagnosticos
            .Where(d => codigos.Contains(d.Codigo))
            .ToDictionaryAsync(d => d.Codigo, StringComparer.OrdinalIgnoreCase, ct);

        var inserted = 0;
        var updated = 0;
        var procesados = 0;

        foreach (var kv in limpias)
        {
            ct.ThrowIfCancellationRequested();
            var r = kv.Value;
            var habilitado = ParseSiNo(r.Habilitado, true);

            if (existentes.TryGetValue(r.Codigo!, out var e))
            {
                e.Nombre = r.Nombre!;
                e.Descripcion = r.Descripcion?.Trim();
                e.Habilitado = habilitado;
                e.Fuente = r.Fuente?.Trim();
                updated++;
            }
            else
            {
                db.Diagnosticos.Add(new Diagnostico
                {
                    TenantId = tid,
                    Codigo = r.Codigo!,
                    Nombre = r.Nombre!,
                    Descripcion = r.Descripcion?.Trim(),
                    Habilitado = habilitado,
                    Fuente = r.Fuente?.Trim()
                });
                inserted++;
            }

            procesados++;
            if (procesados % 500 == 0)
            {
                await db.SaveChangesAsync(ct);
                progress?.Report(new DiagnosticoImportProgress("Insertando", procesados, total));
            }
        }
        if (procesados % 500 != 0) { await db.SaveChangesAsync(ct); }
        progress?.Report(new DiagnosticoImportProgress("Listo", total, total));
        return (inserted, updated);
    }

    public async Task<int> ClearAllAsync(Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return 0; }
        return await db.Diagnosticos.Where(d => d.TenantId == tid).ExecuteDeleteAsync(ct);
    }

    /// <summary>Parsea SI/NO/TRUE/FALSE/1/0 (case-insensitive). Vacio → default.</summary>
    private static bool ParseSiNo(string? raw, bool def)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return def; }
        var s = raw.Trim().ToUpperInvariant();
        if (s == "SI" || s == "SÍ" || s == "TRUE" || s == "1" || s == "S" || s == "YES") { return true; }
        if (s == "NO" || s == "FALSE" || s == "0" || s == "N") { return false; }
        return def;
    }
}
