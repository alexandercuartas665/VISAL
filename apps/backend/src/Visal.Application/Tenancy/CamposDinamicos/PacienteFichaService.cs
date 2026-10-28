using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.CamposDinamicos;

public sealed class PacienteFichaService(IApplicationDbContext db, ITenantContext tenant) : IPacienteFichaService
{
    // Tabs de sistema: los 4 tabs fijos de la ficha de admision, ahora unificados en el mismo
    // espacio de tabs que los dinamicos para que cualquier campo (fijo o dinamico) pueda vivir
    // en cualquiera. "documentos" es especial (solo UI de archivos; no recibe campos).
    private static readonly (string Key, string Titulo)[] TabsSistema =
    {
        ("generales", "Datos Generales"),
        ("adicionales", "Datos Adicionales"),
        ("detalle", "Detalle"),
        ("documentos", "Documentos"),
    };

    public async Task<IReadOnlyList<PacienteFichaDto>> ListAsync(CancellationToken ct = default)
    {
        return await db.PacienteFichaDefs.AsNoTracking()
            .OrderBy(f => f.Orden).ThenBy(f => f.Titulo)
            .Select(f => new PacienteFichaDto(f.Id, f.TabKey, f.Titulo, f.Descripcion, f.Color, f.Orden, f.Oculto, f.EsSistema))
            .ToListAsync(ct);
    }

    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return; }
        // Idempotente: solo siembra si falta el primer tab de sistema.
        if (await db.PacienteFichaDefs.AnyAsync(f => f.TabKey == TabsSistema[0].Key, ct)) { return; }

        // Deja sitio al frente: los tabs ya existentes (p.ej. datos_dinamicos) quedan detras.
        var existentes = await db.PacienteFichaDefs.ToListAsync(ct);
        foreach (var e in existentes) { e.Orden += TabsSistema.Length; }

        for (var i = 0; i < TabsSistema.Length; i++)
        {
            var (key, titulo) = TabsSistema[i];
            db.PacienteFichaDefs.Add(new PacienteFichaDef
            {
                TenantId = tid,
                TabKey = key,
                Titulo = titulo,
                Orden = i,
                EsSistema = true,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<PacienteFichaDto> CreateAsync(CrearFichaRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { throw new InvalidOperationException("El tab necesita un titulo."); }

        var tabKey = await UniqueTabKeyAsync(CamposDinamicosSlug.Make(titulo, "tab"), ct);
        var maxOrden = await db.PacienteFichaDefs.MaxAsync(f => (int?)f.Orden, ct) ?? -1;
        var e = new PacienteFichaDef
        {
            TenantId = tid,
            TabKey = tabKey,
            Titulo = titulo,
            Color = Limpio(req.Color),
            Descripcion = Limpio(req.Descripcion),
            Orden = maxOrden + 1,
        };
        db.PacienteFichaDefs.Add(e);
        await db.SaveChangesAsync(ct);
        return new PacienteFichaDto(e.Id, e.TabKey, e.Titulo, e.Descripcion, e.Color, e.Orden, e.Oculto, e.EsSistema);
    }

    public async Task<PacienteFichaDto?> UpdateAsync(Guid id, ActualizarFichaRequest req, CancellationToken ct = default)
    {
        var e = await db.PacienteFichaDefs.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (e is null) { return null; }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { throw new InvalidOperationException("El tab necesita un titulo."); }
        e.Titulo = titulo;
        e.Color = Limpio(req.Color);
        e.Descripcion = Limpio(req.Descripcion);
        e.Oculto = req.Oculto;
        await db.SaveChangesAsync(ct);
        return new PacienteFichaDto(e.Id, e.TabKey, e.Titulo, e.Descripcion, e.Color, e.Orden, e.Oculto, e.EsSistema);
    }

    public async Task ReorderAsync(Guid id, bool up, CancellationToken ct = default)
    {
        var fichas = await db.PacienteFichaDefs.OrderBy(f => f.Orden).ToListAsync(ct);
        var idx = fichas.FindIndex(f => f.Id == id);
        if (idx < 0) { return; }
        var destino = up ? idx - 1 : idx + 1;
        if (destino < 0 || destino >= fichas.Count) { return; }
        (fichas[idx].Orden, fichas[destino].Orden) = (fichas[destino].Orden, fichas[idx].Orden);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.PacienteFichaDefs.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (e is null) { return; }
        if (e.EsSistema) { throw new InvalidOperationException("No se puede eliminar un tab de sistema."); }
        if (await db.PacienteCampoDefs.AnyAsync(c => c.TabKey == e.TabKey, ct)
            || await db.PacienteCampoRigidoDefs.AnyAsync(r => r.TabKey == e.TabKey, ct))
        {
            throw new InvalidOperationException("El tab tiene campos; mueve o elimina los campos primero.");
        }
        db.PacienteFichaDefs.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> UniqueTabKeyAsync(string baseKey, CancellationToken ct)
    {
        var key = baseKey;
        var n = 2;
        while (await db.PacienteFichaDefs.AnyAsync(f => f.TabKey == key, ct))
        {
            key = $"{baseKey}_{n++}";
        }
        return key;
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
