using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class FormDefinitionService : IFormDefinitionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public FormDefinitionService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FormDefinitionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.FormDefinitions
            .AsNoTracking()
            .OrderBy(f => f.Nombre)
            .Select(f => new FormDefinitionDto(f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo, f.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<FormDefinitionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.FormDefinitions
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new FormDefinitionDetailDto(f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo, f.SchemaJson, f.PrefillRoutesJson))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FormDefinitionDetailDto?> SaveAsync(SaveFormDefinitionRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var codigo = request.Codigo.Trim();
        var nombre = request.Nombre.Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre))
        {
            throw new InvalidOperationException("El codigo y el nombre del formulario son obligatorios.");
        }

        FormDefinition entity;

        if (request.Id is Guid id)
        {
            // El filtro global garantiza que solo se edita un formulario del tenant activo.
            var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (existing is null)
            {
                return null;
            }

            var clash = await _db.FormDefinitions.AnyAsync(f => f.Codigo == codigo && f.Id != id, cancellationToken);
            if (clash)
            {
                throw new InvalidOperationException($"Ya existe otro formulario con el codigo '{codigo}'.");
            }

            existing.Codigo = codigo;
            existing.Nombre = nombre;
            existing.Version = request.Version?.Trim();
            existing.Tipo = request.Tipo?.Trim();
            existing.SchemaJson = string.IsNullOrWhiteSpace(request.SchemaJson) ? "{\"children\":[]}" : request.SchemaJson;
            existing.Activo = request.Activo;
            if (request.PrefillRoutesJson is not null) { existing.PrefillRoutesJson = request.PrefillRoutesJson; }
            entity = existing;

            _audit.Write(actorUserId, "form-definition.update", nameof(FormDefinition), entity.Id,
                previousValue: null, newValue: new { entity.Codigo, entity.Nombre }, tenantId: entity.TenantId);
        }
        else
        {
            if (_tenantContext.TenantId is not Guid tenantId)
            {
                return null;
            }

            var clash = await _db.FormDefinitions.AnyAsync(f => f.Codigo == codigo, cancellationToken);
            if (clash)
            {
                throw new InvalidOperationException($"Ya existe un formulario con el codigo '{codigo}'.");
            }

            entity = new FormDefinition
            {
                TenantId = tenantId,
                Codigo = codigo,
                Nombre = nombre,
                Version = request.Version?.Trim(),
                Tipo = request.Tipo?.Trim(),
                SchemaJson = string.IsNullOrWhiteSpace(request.SchemaJson) ? "{\"children\":[]}" : request.SchemaJson,
                Activo = request.Activo,
                PrefillRoutesJson = request.PrefillRoutesJson
            };
            _db.FormDefinitions.Add(entity);

            _audit.Write(actorUserId, "form-definition.create", nameof(FormDefinition), entity.Id,
                previousValue: null, newValue: new { entity.Codigo, entity.Nombre }, tenantId: tenantId);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new FormDefinitionDetailDto(entity.Id, entity.Codigo, entity.Nombre, entity.Version, entity.Tipo, entity.Activo, entity.SchemaJson, entity.PrefillRoutesJson);
    }

    public async Task<bool> UpdatePrefillRoutesAsync(Guid id, string? prefillRoutesJson, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (existing is null) { return false; }
        existing.PrefillRoutesJson = string.IsNullOrWhiteSpace(prefillRoutesJson) ? null : prefillRoutesJson;
        _audit.Write(actorUserId, "form-definition.update-prefill-routes", nameof(FormDefinition), existing.Id,
            previousValue: null, newValue: new { existing.Codigo }, tenantId: existing.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        _db.FormDefinitions.Remove(existing);
        _audit.Write(actorUserId, "form-definition.delete", nameof(FormDefinition), existing.Id,
            previousValue: new { existing.Codigo, existing.Nombre }, newValue: null, tenantId: existing.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
