using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.CamposDinamicos;

public sealed class PacienteCampoRigidoService(IApplicationDbContext db, ITenantContext tenant)
    : IPacienteCampoRigidoService
{
    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return; }
        if (await db.PacienteCampoRigidoDefs.AnyAsync(ct)) { return; }

        // Orden por defecto = posicion dentro del tab en el catalogo (replica la ficha actual).
        var ordenPorTab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var info in CamposRigidosCatalogo.Todos)
        {
            var orden = ordenPorTab.TryGetValue(info.TabDefault, out var n) ? n : 0;
            ordenPorTab[info.TabDefault] = orden + 1;
            db.PacienteCampoRigidoDefs.Add(new PacienteCampoRigidoDef
            {
                TenantId = tid,
                CampoKey = info.Key,
                TabKey = info.TabDefault,
                Orden = orden,
                Ancho = info.AnchoDefault,
                Oculto = false,
            });
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Carrera de siembra (prerender + interactivo de Blazor con contextos distintos): si
            // otro pase concurrente ya sembro, el INSERT choca con ix_..._tenant_id_campo_key
            // (23505). Lo damos por bueno y descartamos los Added para no dejarlos colgando en el
            // DbContext compartido del circuito (reintentarian el INSERT en el proximo SaveChanges).
            foreach (var entry in db.ChangeTracker.Entries<PacienteCampoRigidoDef>()
                         .Where(e => e.State is EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    public async Task<IReadOnlyList<PacienteCampoRigidoDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.PacienteCampoRigidoDefs.AsNoTracking()
            .OrderBy(r => r.TabKey).ThenBy(r => r.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).Where(d => d is not null).Select(d => d!).ToList();
    }

    public async Task<IReadOnlyList<PacienteCampoRigidoDto>> ListByTabAsync(string tabKey, CancellationToken ct = default)
    {
        var rows = await db.PacienteCampoRigidoDefs.AsNoTracking()
            .Where(r => r.TabKey == tabKey)
            .OrderBy(r => r.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).Where(d => d is not null).Select(d => d!).ToList();
    }

    public async Task MoverATabAsync(string campoKey, string tabKey, CancellationToken ct = default)
    {
        var e = await db.PacienteCampoRigidoDefs.FirstOrDefaultAsync(r => r.CampoKey == campoKey, ct);
        if (e is null) { return; }
        var destino = (tabKey ?? "").Trim();
        if (!await db.PacienteFichaDefs.AnyAsync(f => f.TabKey == destino, ct))
        {
            throw new InvalidOperationException("El tab destino no existe.");
        }
        e.TabKey = destino;
        e.Orden = await NextOrdenEnTabAsync(destino, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CicloAnchoAsync(string campoKey, CancellationToken ct = default)
    {
        var info = CamposRigidosCatalogo.Find(campoKey);
        if (info is null || info.Compuesto) { return; }
        var e = await db.PacienteCampoRigidoDefs.FirstOrDefaultAsync(r => r.CampoKey == campoKey, ct);
        if (e is null) { return; }
        e.Ancho = e.Ancho >= 3 ? 1 : e.Ancho + 1;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetOcultoAsync(string campoKey, bool oculto, CancellationToken ct = default)
    {
        var info = CamposRigidosCatalogo.Find(campoKey);
        if (info is null) { return; }
        if (oculto && !info.Ocultable)
        {
            throw new InvalidOperationException($"El campo \"{info.Label}\" es obligatorio y no se puede ocultar.");
        }
        var e = await db.PacienteCampoRigidoDefs.FirstOrDefaultAsync(r => r.CampoKey == campoKey, ct);
        if (e is null) { return; }
        e.Oculto = oculto;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReordenarTabAsync(string tabKey, IReadOnlyList<ItemColocacion> items, CancellationToken ct = default)
    {
        var rigidos = await db.PacienteCampoRigidoDefs.Where(r => r.TabKey == tabKey).ToListAsync(ct);
        var dinamicos = await db.PacienteCampoDefs.Where(c => c.TabKey == tabKey).ToListAsync(ct);
        var orden = 0;
        foreach (var it in items)
        {
            if (it.EsRigido)
            {
                var r = rigidos.FirstOrDefault(x => x.CampoKey == it.CampoKey);
                if (r is not null) { r.Orden = orden++; }
            }
            else if (it.CampoDefId is Guid id)
            {
                var c = dinamicos.FirstOrDefault(x => x.Id == id);
                if (c is not null) { c.Orden = orden++; }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Siguiente orden libre en un tab, considerando fijos Y dinamicos (orden unificado).</summary>
    private async Task<int> NextOrdenEnTabAsync(string tabKey, CancellationToken ct)
    {
        var maxRig = await db.PacienteCampoRigidoDefs.Where(r => r.TabKey == tabKey).MaxAsync(r => (int?)r.Orden, ct) ?? -1;
        var maxDin = await db.PacienteCampoDefs.Where(c => c.TabKey == tabKey).MaxAsync(c => (int?)c.Orden, ct) ?? -1;
        return Math.Max(maxRig, maxDin) + 1;
    }

    private static PacienteCampoRigidoDto? Map(PacienteCampoRigidoDef r)
    {
        var info = CamposRigidosCatalogo.Find(r.CampoKey);
        if (info is null) { return null; } // fila huerfana (catalogo cambio): se ignora.
        return new PacienteCampoRigidoDto(
            r.CampoKey, info.Label, info.Grupo, r.TabKey, r.Orden,
            info.Compuesto ? 3 : r.Ancho, r.Oculto, info.Compuesto, info.Ocultable);
    }
}
