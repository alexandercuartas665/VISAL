using Visal.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class FirmaResolverService : IFirmaResolverService
{
    /// <summary>Categoria de los documentos externos que cuentan como "firma del paciente".
    /// Debe coincidir con la categoria que usa FirmaRemotaService al crear el NotaMedicaDocumento
    /// cuando el paciente firma desde WhatsApp, y con la tipologia configurable en
    /// /cfg-tipologia-archivos.</summary>
    private const string CategoriaFirmaPaciente = "Firma del Paciente";

    private readonly IApplicationDbContext _db;

    public FirmaResolverService(IApplicationDbContext db) { _db = db; }

    public async Task<string?> ResolverFirmaPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        if (pacienteId == Guid.Empty) { return null; }
        return await _db.NotaMedicaDocumentos.AsNoTracking()
            .Where(d => d.PacienteId == pacienteId && d.Categoria == CategoriaFirmaPaciente)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => d.RutaArchivo)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> ResolverFirmaProfesionalAsync(Guid tenantUserId, CancellationToken ct = default)
    {
        if (tenantUserId == Guid.Empty) { return null; }
        // TenantUser -> ProfesionalId -> Profesional.FirmaUrl. Si cualquier eslabon
        // es null, devolvemos null silenciosamente.
        var profesionalId = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == tenantUserId)
            .Select(u => u.ProfesionalId)
            .FirstOrDefaultAsync(ct);
        if (profesionalId is not Guid pid || pid == Guid.Empty) { return null; }
        return await _db.Profesionales.AsNoTracking()
            .Where(p => p.Id == pid)
            .Select(p => p.FirmaUrl)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> ResolverFirmaPorProfesionalAsync(Guid profesionalId, CancellationToken ct = default)
    {
        if (profesionalId == Guid.Empty) { return null; }
        return await _db.Profesionales.AsNoTracking()
            .Where(p => p.Id == profesionalId)
            .Select(p => p.FirmaUrl)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> ResolverFirmaProfesionalPorPlatformUserAsync(Guid platformUserId, Guid tenantId, CancellationToken ct = default)
    {
        if (platformUserId == Guid.Empty || tenantId == Guid.Empty) { return null; }
        // Buscar TenantUser por (platform_user_id, tenant_id) y devolver
        // su Profesional.FirmaUrl. Util para usuarios admin que no llevan
        // claim profesional_id pero igual tienen profesional vinculado.
        var profesionalId = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == platformUserId && u.TenantId == tenantId)
            .Select(u => u.ProfesionalId)
            .FirstOrDefaultAsync(ct);
        if (profesionalId is not Guid pid || pid == Guid.Empty) { return null; }
        return await _db.Profesionales.AsNoTracking()
            .Where(p => p.Id == pid)
            .Select(p => p.FirmaUrl)
            .FirstOrDefaultAsync(ct);
    }
}
