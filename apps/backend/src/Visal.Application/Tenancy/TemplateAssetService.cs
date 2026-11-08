using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed record TemplateAssetDto(Guid Id, string FileName, string Url, string? MimeType, long SizeBytes, DateTimeOffset CreatedAt);

public interface ITemplateAssetService
{
    Task<IReadOnlyList<TemplateAssetDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TemplateAssetDto?> AddAsync(string fileName, string url, string? mimeType, long sizeBytes, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Galeria de recursos (imagenes) de las plantillas de cotizacion por agencia. Entidad TENANT-SCOPED.
/// Los archivos se guardan en disco por la UI; aqui solo se registra la referencia (URL) reutilizable
/// por cualquier plantilla.
/// </summary>
public sealed class TemplateAssetService : ITemplateAssetService
{
    // Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenantContext;

    public TemplateAssetService(IApplicationDbContextFactory dbFactory, ITenantContext tenantContext)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<TemplateAssetDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        return await db.TemplateAssets
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TemplateAssetDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TemplateAssetDto?> AddAsync(string fileName, string url, string? mimeType, long sizeBytes, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var entity = new TemplateAsset
        {
            TenantId = tenantId,
            FileName = (fileName ?? "imagen").Trim(),
            Url = url,
            MimeType = mimeType,
            SizeBytes = sizeBytes
        };
        db.TemplateAssets.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new TemplateAssetDto(entity.Id, entity.FileName, entity.Url, entity.MimeType, entity.SizeBytes, entity.CreatedAt);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var entity = await db.TemplateAssets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null) { return false; }
        db.TemplateAssets.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
