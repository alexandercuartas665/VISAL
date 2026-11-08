using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Admin;

/// <summary>Vista de la marca de la plataforma para pintar el login y otras superficies publicas.</summary>
public sealed record PlatformBrandingDto(
    string PlatformName,
    string? Tagline,
    string? LoginLogoUrl,
    string? LoginHeadline,
    string? LoginSubtext)
{
    /// <summary>Valores por defecto cuando aun no se ha configurado la marca.</summary>
    public static PlatformBrandingDto Default => new(
        "Visal",
        "Salud Domiciliaria",
        null,
        "Atencion humana, agil y oportuna",
        "Coordina la atencion domiciliaria: pacientes, historias clinicas, profesionales y agendas en una sola plataforma.");
}

public sealed record SaveBrandingRequest(
    string PlatformName,
    string? Tagline,
    string? LoginLogoUrl,
    string? LoginHeadline,
    string? LoginSubtext);

public interface IPlatformBrandingService
{
    Task<PlatformBrandingDto> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SaveBrandingRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marca de la plataforma: tabla de una sola fila. El Super Admin la edita; el login la lee.
/// Si no existe fila, se devuelven valores por defecto sin tocar la BD.
/// </summary>
public sealed class PlatformBrandingService : IPlatformBrandingService
{
    // Ola 4c estabilizacion (P0): contexto CORTO por operacion (using var db = _dbFactory.Create();)
    // en vez del IApplicationDbContext scoped compartido por el circuito. Este servicio se lee en
    // el render de /login, donde se solapaba con otras lecturas de config -> "second operation".
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly IAuditWriter _audit;

    public PlatformBrandingService(IApplicationDbContextFactory dbFactory, IAuditWriter audit)
    {
        _dbFactory = dbFactory;
        _audit = audit;
    }

    public async Task<PlatformBrandingDto> GetAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        // OrderBy determinista: PlatformBranding es singleton global, pero sin OrderBy EF emite
        // FirstWithoutOrderByAndFilterWarning; fija el orden para que el resultado sea estable.
        var row = await db.PlatformBrandings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (row is null) { return PlatformBrandingDto.Default; }
        return new PlatformBrandingDto(row.PlatformName, row.Tagline, row.LoginLogoUrl, row.LoginHeadline, row.LoginSubtext);
    }

    public async Task SaveAsync(SaveBrandingRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var name = string.IsNullOrWhiteSpace(request.PlatformName) ? "Visal" : request.PlatformName.Trim();

        var row = await db.PlatformBrandings.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new PlatformBranding();
            db.PlatformBrandings.Add(row);
        }

        row.PlatformName = name;
        row.Tagline = request.Tagline?.Trim();
        row.LoginLogoUrl = string.IsNullOrWhiteSpace(request.LoginLogoUrl) ? null : request.LoginLogoUrl.Trim();
        row.LoginHeadline = request.LoginHeadline?.Trim();
        row.LoginSubtext = request.LoginSubtext?.Trim();

        _audit.Write(db, actorUserId, "platform.branding.save", nameof(PlatformBranding), row.Id,
            previousValue: null,
            newValue: new { row.PlatformName, HasLogo = row.LoginLogoUrl is not null });

        await db.SaveChangesAsync(cancellationToken);
    }
}
