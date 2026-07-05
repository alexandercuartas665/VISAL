using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.WhatsApp;

internal sealed class WhatsAppTemplateBindingService : IWhatsAppTemplateBindingService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public WhatsAppTemplateBindingService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<WhatsAppTemplateBindingDto?> GetAsync(WhatsAppTemplateRole role, CancellationToken ct = default)
    {
        var row = await _db.TenantWhatsAppTemplateBindings
            .Include(b => b.Line)
            .FirstOrDefaultAsync(b => b.Role == role, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<IReadOnlyList<WhatsAppTemplateBindingDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.TenantWhatsAppTemplateBindings
            .Include(b => b.Line)
            .OrderBy(b => b.Role)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<WhatsAppTemplateBindingDto> UpsertAsync(WhatsAppTemplateBindingSetRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        // Validaciones antes de tocar BD: nombres y TemplateId no vacios.
        if (string.IsNullOrWhiteSpace(req.TemplateId)) { throw new ArgumentException("TemplateId requerido", nameof(req)); }
        if (string.IsNullOrWhiteSpace(req.TemplateName)) { throw new ArgumentException("TemplateName requerido", nameof(req)); }

        // La linea debe existir y pertenecer al tenant (query filter lo garantiza).
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == req.LineId, ct)
            ?? throw new InvalidOperationException("La linea no existe en este tenant.");

        var existing = await _db.TenantWhatsAppTemplateBindings.FirstOrDefaultAsync(b => b.Role == req.Role, ct);
        object? prev = existing is null ? null : new
        {
            existing.LineId, existing.TemplateId, existing.TemplateName, existing.LanguageCode, existing.ParameterCount,
        };
        if (existing is null)
        {
            existing = new TenantWhatsAppTemplateBinding
            {
                TenantId = line.TenantId,
                Role = req.Role,
                LineId = req.LineId,
                TemplateId = req.TemplateId.Trim(),
                TemplateName = req.TemplateName.Trim(),
                LanguageCode = string.IsNullOrWhiteSpace(req.LanguageCode) ? "es" : req.LanguageCode.Trim(),
                ParameterCount = req.ParameterCount,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = actorUserId,
            };
            _db.TenantWhatsAppTemplateBindings.Add(existing);
        }
        else
        {
            existing.LineId = req.LineId;
            existing.TemplateId = req.TemplateId.Trim();
            existing.TemplateName = req.TemplateName.Trim();
            existing.LanguageCode = string.IsNullOrWhiteSpace(req.LanguageCode) ? "es" : req.LanguageCode.Trim();
            existing.ParameterCount = req.ParameterCount;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actorUserId;
        }
        _audit.Write(actorUserId, "wa-template-binding.upsert", nameof(TenantWhatsAppTemplateBinding), existing.Id,
            previousValue: prev,
            newValue: new { existing.Role, existing.LineId, existing.TemplateId, existing.TemplateName, existing.ParameterCount },
            tenantId: line.TenantId);
        await _db.SaveChangesAsync(ct);
        // Reload con Include para tener el nombre de la linea en el DTO. Barato:
        // solo la fila que acabamos de guardar.
        var full = await _db.TenantWhatsAppTemplateBindings
            .Include(b => b.Line)
            .FirstAsync(b => b.Id == existing.Id, ct);
        return ToDto(full);
    }

    public async Task<bool> DeleteAsync(WhatsAppTemplateRole role, Guid actorUserId, CancellationToken ct = default)
    {
        var row = await _db.TenantWhatsAppTemplateBindings.FirstOrDefaultAsync(b => b.Role == role, ct);
        if (row is null) { return false; }
        _audit.Write(actorUserId, "wa-template-binding.delete", nameof(TenantWhatsAppTemplateBinding), row.Id,
            previousValue: new { row.Role, row.TemplateName },
            newValue: null,
            tenantId: row.TenantId);
        _db.TenantWhatsAppTemplateBindings.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static WhatsAppTemplateBindingDto ToDto(TenantWhatsAppTemplateBinding b) => new(
        b.Id, b.Role, b.LineId,
        b.Line?.InstanceName ?? "",
        b.TemplateId, b.TemplateName, b.LanguageCode, b.ParameterCount);
}
