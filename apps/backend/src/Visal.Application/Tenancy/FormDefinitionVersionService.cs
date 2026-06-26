using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class FormDefinitionVersionService : IFormDefinitionVersionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public FormDefinitionVersionService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FormDefinitionSnapshotDto>> ListarAsync(
        Guid formDefinitionId,
        CancellationToken cancellationToken = default)
    {
        // El query filter global por TenantId garantiza que solo veo snapshots
        // de mi tenant — no necesito filtrar manualmente por TenantId aqui.
        return await _db.FormDefinitionSnapshots
            .AsNoTracking()
            .Where(s => s.FormDefinitionId == formDefinitionId)
            .OrderByDescending(s => s.SnapshotAt)
            .Select(s => new FormDefinitionSnapshotDto(
                s.Id, s.FormDefinitionId,
                s.Codigo, s.Nombre, s.Version, s.Tipo, s.Activo,
                s.SnapshotAt, s.Motivo, s.SnapshotBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<FormDefinitionSnapshotDetailDto?> ObtenerAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        return await _db.FormDefinitionSnapshots
            .AsNoTracking()
            .Where(s => s.Id == snapshotId)
            .Select(s => new FormDefinitionSnapshotDetailDto(
                s.Id, s.FormDefinitionId,
                s.Codigo, s.Nombre, s.Version, s.Tipo, s.Activo,
                s.SchemaJson, s.PrefillRoutesJson,
                s.SnapshotAt, s.Motivo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> RestaurarAsync(
        Guid snapshotId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        // Cargamos el snapshot completo (incluye el schema_json) Y la fila viva
        // del formulario al que pertenece. Ambas operaciones pasan por el query
        // filter global, asi que no veo nada de otros tenants.
        var snapshot = await _db.FormDefinitionSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);
        if (snapshot is null) { return false; }

        var live = await _db.FormDefinitions
            .FirstOrDefaultAsync(f => f.Id == snapshot.FormDefinitionId, cancellationToken);
        if (live is null) { return false; }

        // Copiamos los 7 campos versionados. El trigger BEFORE UPDATE detectara
        // los cambios y generara un snapshot adicional del estado ACTUAL (el que
        // estamos reemplazando) — asi la restauracion tambien es reversible. El
        // motivo del snapshot generado por el trigger sera "auto-trigger" (el
        // trigger no sabe que viene de un restore); ese matiz lo registramos
        // en el audit log, suficiente para auditoria.
        live.Codigo = snapshot.Codigo;
        live.Nombre = snapshot.Nombre;
        live.Version = snapshot.Version;
        live.Tipo = snapshot.Tipo;
        live.SchemaJson = snapshot.SchemaJson;
        live.PrefillRoutesJson = snapshot.PrefillRoutesJson;
        live.Activo = snapshot.Activo;

        _audit.Write(actorUserId,
            "form-definition.restore",
            nameof(FormDefinition),
            live.Id,
            previousValue: new { snapshotId = snapshot.Id, snapshotAt = snapshot.SnapshotAt },
            newValue: new { codigo = snapshot.Codigo, nombre = snapshot.Nombre, version = snapshot.Version });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
