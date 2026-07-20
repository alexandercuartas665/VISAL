using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class DocumentoNotaService(
    IApplicationDbContext db,
    ITenantContext tenant,
    TimeProvider clock) : IDocumentoNotaService
{
    public async Task<IReadOnlyList<DocumentoNotaDto>> ListarPorDocumentoAsync(
        Guid documentoId, bool incluirArchivadas, CancellationToken ct = default)
    {
        var q = db.DocumentoNotas.AsNoTracking()
            .Where(n => n.NotaMedicaDocumentoId == documentoId);
        if (!incluirArchivadas)
        {
            q = q.Where(n => !n.Archivada);
        }
        // JOIN a PlatformUser por email — se puede prescindir en el futuro si
        // aparece un directorio interno de usuarios, pero aca la lista es corta
        // (~decenas de notas por doc como maximo) y evita N+1 en el UI.
        var rows = await q
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.Id,
                n.NotaMedicaDocumentoId,
                n.Texto,
                n.AutorUserId,
                n.CreatedAt,
                n.Archivada,
                n.ArchivadaPorUserId,
                n.ArchivadaEn,
                AutorEmail = db.PlatformUsers.AsNoTracking()
                    .Where(u => u.Id == n.AutorUserId).Select(u => u.Email).FirstOrDefault(),
                ArchivadaPorEmail = n.ArchivadaPorUserId == null ? null : db.PlatformUsers.AsNoTracking()
                    .Where(u => u.Id == n.ArchivadaPorUserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new DocumentoNotaDto(
            r.Id, r.NotaMedicaDocumentoId, r.Texto, r.AutorUserId, r.AutorEmail,
            r.CreatedAt, r.Archivada, r.ArchivadaPorUserId, r.ArchivadaPorEmail, r.ArchivadaEn))
            .ToList();
    }

    public async Task<DocumentoNotaDto> AgregarAsync(
        Guid documentoId, string texto, Guid autorUserId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var t = (texto ?? "").Trim();
        if (t.Length == 0) { throw new ArgumentException("El texto de la nota no puede estar vacio.", nameof(texto)); }

        // Verificamos que el documento exista en el tenant (global filter aplica).
        var docExiste = await db.NotaMedicaDocumentos.AsNoTracking()
            .AnyAsync(d => d.Id == documentoId, ct);
        if (!docExiste) { throw new InvalidOperationException("Documento no encontrado."); }

        var nota = new DocumentoNota
        {
            TenantId = tid,
            NotaMedicaDocumentoId = documentoId,
            Texto = t,
            AutorUserId = autorUserId,
            Archivada = false,
        };
        db.DocumentoNotas.Add(nota);
        await db.SaveChangesAsync(ct);

        var autorEmail = await db.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == autorUserId).Select(u => u.Email).FirstOrDefaultAsync(ct);

        return new DocumentoNotaDto(
            nota.Id, nota.NotaMedicaDocumentoId, nota.Texto, nota.AutorUserId, autorEmail,
            nota.CreatedAt, nota.Archivada, null, null, null);
    }

    public async Task<bool> ArchivarAsync(Guid notaId, Guid actorUserId, CancellationToken ct = default)
    {
        var n = await db.DocumentoNotas.FirstOrDefaultAsync(x => x.Id == notaId, ct);
        if (n is null) { return false; }
        if (n.Archivada) { return true; }
        n.Archivada = true;
        n.ArchivadaPorUserId = actorUserId;
        n.ArchivadaEn = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DesarchivarAsync(Guid notaId, Guid actorUserId, CancellationToken ct = default)
    {
        var n = await db.DocumentoNotas.FirstOrDefaultAsync(x => x.Id == notaId, ct);
        if (n is null) { return false; }
        if (!n.Archivada) { return true; }
        n.Archivada = false;
        n.ArchivadaPorUserId = null;
        n.ArchivadaEn = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
