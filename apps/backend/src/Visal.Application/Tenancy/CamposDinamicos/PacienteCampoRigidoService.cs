using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.CamposDinamicos;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class PacienteCampoRigidoService(IApplicationDbContextFactory dbFactory, ITenantContext tenant)
    : IPacienteCampoRigidoService
{
    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return; }
        using var db = dbFactory.Create();

        // Siembra TOLERANTE A ADICIONES: en vez de "sembrar solo si la tabla esta vacia", sembramos
        // las claves del catalogo que aun no tienen fila para este tenant. Asi, cuando el catalogo
        // crece (p.ej. se agrega "ingreso"/"motivo_no_ingreso"), los tenants ya sembrados reciben la
        // colocacion de los campos nuevos sin perder su personalizacion de los existentes.
        var existentes = await db.PacienteCampoRigidoDefs
            .Select(r => r.CampoKey)
            .ToListAsync(ct);
        var existentesSet = new HashSet<string>(existentes, StringComparer.Ordinal);

        var faltantes = CamposRigidosCatalogo.Todos.Where(i => !existentesSet.Contains(i.Key)).ToList();
        if (faltantes.Count == 0) { return; }

        // Colocacion de los campos nuevos: si su ANTECESOR en el catalogo (el campo inmediatamente
        // anterior del mismo tab por defecto) ya existe para el tenant, el nuevo se inserta JUSTO
        // DESPUES de el (mismo tab y Orden+1, corriendo una posicion lo que venia detras, fijo o
        // dinamico). Asi "Detalles de la direccion" cae debajo de "Direccion" aunque el tenant ya
        // haya personalizado su ficha. Sin antecesor -> al final de su tab (comportamiento previo).
        var rigTracked = await db.PacienteCampoRigidoDefs.ToListAsync(ct);
        var porKey = rigTracked.ToDictionary(r => r.CampoKey, StringComparer.Ordinal);
        var maxPorTab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var info in faltantes)
        {
            PacienteCampoRigidoDef? antecesor = null;
            var idx = -1;
            for (var k = 0; k < CamposRigidosCatalogo.Todos.Count; k++)
            {
                if (CamposRigidosCatalogo.Todos[k].Key == info.Key) { idx = k; break; }
            }
            if (idx > 0)
            {
                var prev = CamposRigidosCatalogo.Todos[idx - 1];
                if (prev.TabDefault == info.TabDefault) { porKey.TryGetValue(prev.Key, out antecesor); }
            }

            string tab;
            int orden;
            if (antecesor is not null)
            {
                tab = antecesor.TabKey;
                orden = antecesor.Orden + 1;
                foreach (var r in rigTracked.Where(r => r.TabKey == tab && r.Orden >= orden)) { r.Orden += 1; }
                var dinTab = await db.PacienteCampoDefs.Where(c => c.TabKey == tab && c.Orden >= orden).ToListAsync(ct);
                foreach (var c in dinTab) { c.Orden += 1; }
                maxPorTab.Remove(tab);
            }
            else
            {
                tab = info.TabDefault;
                if (!maxPorTab.TryGetValue(tab, out var n))
                {
                    var maxRig = rigTracked.Where(r => r.TabKey == tab).Select(r => (int?)r.Orden).Max() ?? -1;
                    var maxDin = await db.PacienteCampoDefs
                        .Where(c => c.TabKey == tab).MaxAsync(c => (int?)c.Orden, ct) ?? -1;
                    n = Math.Max(maxRig, maxDin);
                }
                orden = n + 1;
                maxPorTab[tab] = orden;
            }

            var nuevo = new PacienteCampoRigidoDef
            {
                TenantId = tid,
                CampoKey = info.Key,
                TabKey = tab,
                Orden = orden,
                Ancho = info.AnchoDefault,
                Oculto = false,
            };
            db.PacienteCampoRigidoDefs.Add(nuevo);
            rigTracked.Add(nuevo);
            porKey[info.Key] = nuevo;
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
        using var db = dbFactory.Create();
        var rows = await db.PacienteCampoRigidoDefs.AsNoTracking()
            .OrderBy(r => r.TabKey).ThenBy(r => r.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).Where(d => d is not null).Select(d => d!).ToList();
    }

    public async Task<IReadOnlyList<PacienteCampoRigidoDto>> ListByTabAsync(string tabKey, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var rows = await db.PacienteCampoRigidoDefs.AsNoTracking()
            .Where(r => r.TabKey == tabKey)
            .OrderBy(r => r.Orden)
            .ToListAsync(ct);
        return rows.Select(Map).Where(d => d is not null).Select(d => d!).ToList();
    }

    public async Task MoverATabAsync(string campoKey, string tabKey, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var e = await db.PacienteCampoRigidoDefs.FirstOrDefaultAsync(r => r.CampoKey == campoKey, ct);
        if (e is null) { return; }
        var destino = (tabKey ?? "").Trim();
        if (!await db.PacienteFichaDefs.AnyAsync(f => f.TabKey == destino, ct))
        {
            throw new InvalidOperationException("El tab destino no existe.");
        }
        e.TabKey = destino;
        e.Orden = await NextOrdenEnTabAsync(db, destino, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CicloAnchoAsync(string campoKey, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var info = CamposRigidosCatalogo.Find(campoKey);
        if (info is null || info.Compuesto) { return; }
        var e = await db.PacienteCampoRigidoDefs.FirstOrDefaultAsync(r => r.CampoKey == campoKey, ct);
        if (e is null) { return; }
        e.Ancho = e.Ancho >= 3 ? 1 : e.Ancho + 1;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetOcultoAsync(string campoKey, bool oculto, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
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
        using var db = dbFactory.Create();
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
    private async Task<int> NextOrdenEnTabAsync(IApplicationDbContext db, string tabKey, CancellationToken ct)
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
