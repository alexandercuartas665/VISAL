using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.CamposDinamicos;

public sealed class PacienteCampoService(IApplicationDbContext db, ITenantContext tenant) : IPacienteCampoService
{
    public async Task<IReadOnlyList<PacienteCampoDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.PacienteCampoDefs.AsNoTracking()
            .OrderBy(c => c.TabKey).ThenBy(c => c.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<PacienteCampoDto>> ListByTabAsync(string tabKey, CancellationToken ct = default)
    {
        var rows = await db.PacienteCampoDefs.AsNoTracking()
            .Where(c => c.TabKey == tabKey)
            .OrderBy(c => c.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<PacienteCampoDto>> ListActivosAsync(CancellationToken ct = default)
    {
        var rows = await db.PacienteCampoDefs.AsNoTracking()
            .Where(c => c.Activo)
            .OrderBy(c => c.TabKey).ThenBy(c => c.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<PacienteCampoDto> CreateAsync(CrearCampoRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var label = (req.Label ?? "").Trim();
        if (label.Length == 0) { throw new InvalidOperationException("El campo necesita una etiqueta."); }
        var tabKey = (req.TabKey ?? "").Trim();
        if (!await db.PacienteFichaDefs.AnyAsync(f => f.TabKey == tabKey, ct))
        {
            throw new InvalidOperationException("El tab indicado no existe.");
        }

        var campoKey = await UniqueCampoKeyAsync(CamposDinamicosSlug.Make(label, "campo"), ct);
        var maxOrden = await db.PacienteCampoDefs.Where(c => c.TabKey == tabKey).MaxAsync(c => (int?)c.Orden, ct) ?? -1;
        var e = new PacienteCampoDef
        {
            TenantId = tid,
            TabKey = tabKey,
            CampoKey = campoKey,
            Label = label,
            Tipo = req.Tipo,
            Opciones = Limpio(req.Opciones),
            Ancho = Math.Clamp(req.Ancho, 1, 3),
            Orden = maxOrden + 1,
            Descripcion = Limpio(req.Descripcion),
            Requerido = req.Requerido,
            Activo = true,
        };
        db.PacienteCampoDefs.Add(e);
        await db.SaveChangesAsync(ct);
        return Map(e);
    }

    public async Task<PacienteCampoDto?> UpdateAsync(Guid id, ActualizarCampoRequest req, CancellationToken ct = default)
    {
        var e = await db.PacienteCampoDefs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) { return null; }
        var label = (req.Label ?? "").Trim();
        if (label.Length == 0) { throw new InvalidOperationException("El campo necesita una etiqueta."); }
        // CampoKey NO se cambia (es la clave estable del jsonb y del CENSO).
        e.Label = label;
        e.Tipo = req.Tipo;
        e.Opciones = Limpio(req.Opciones);
        e.Ancho = Math.Clamp(req.Ancho, 1, 3);
        e.Descripcion = Limpio(req.Descripcion);
        e.Requerido = req.Requerido;
        e.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return Map(e);
    }

    public async Task ReorderAsync(string tabKey, ReordenarCamposRequest req, CancellationToken ct = default)
    {
        var campos = await db.PacienteCampoDefs.Where(c => c.TabKey == tabKey).ToListAsync(ct);
        var orden = 0;
        foreach (var cid in req.OrdenIds)
        {
            var c = campos.FirstOrDefault(x => x.Id == cid);
            if (c is not null) { c.Orden = orden++; }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task CicloAnchoAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.PacienteCampoDefs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) { return; }
        e.Ancho = e.Ancho >= 3 ? 1 : e.Ancho + 1;
        await db.SaveChangesAsync(ct);
    }

    public async Task MoverATabAsync(Guid id, string tabKey, CancellationToken ct = default)
    {
        var e = await db.PacienteCampoDefs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) { return; }
        var destino = (tabKey ?? "").Trim();
        if (!await db.PacienteFichaDefs.AnyAsync(f => f.TabKey == destino, ct))
        {
            throw new InvalidOperationException("El tab destino no existe.");
        }
        // Orden unificado: considera fijos Y dinamicos del destino para quedar al final de la mezcla.
        var maxDin = await db.PacienteCampoDefs.Where(c => c.TabKey == destino).MaxAsync(c => (int?)c.Orden, ct) ?? -1;
        var maxRig = await db.PacienteCampoRigidoDefs.Where(r => r.TabKey == destino).MaxAsync(r => (int?)r.Orden, ct) ?? -1;
        e.TabKey = destino;
        e.Orden = Math.Max(maxDin, maxRig) + 1;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.PacienteCampoDefs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) { return; }
        db.PacienteCampoDefs.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> UniqueCampoKeyAsync(string baseKey, CancellationToken ct)
    {
        var key = baseKey;
        var n = 2;
        while (await db.PacienteCampoDefs.AnyAsync(c => c.CampoKey == key, ct))
        {
            key = $"{baseKey}_{n++}";
        }
        return key;
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static PacienteCampoDto Map(PacienteCampoDef c) => new(
        c.Id, c.TabKey, c.CampoKey, c.Label, c.Tipo, c.Opciones, c.Ancho, c.Orden,
        c.Descripcion, c.Requerido, c.Activo, c.EsSistema);
}
