using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Reportes;

/// <summary>
/// Provee los campos dinamicos del tenant (gestor de campos de la ficha de paciente) como
/// <see cref="ReportableFieldDef"/> con ruta jsonb, para que el constructor de reportes los
/// ofrezca como columnas/filtros y el generador sepa resolverlos. Scoped (lee la BD del tenant);
/// cachea el resultado por request para que el generador (sincrono) lo lea sin await.
/// </summary>
public interface IReporteCamposDinamicos
{
    /// <summary>Carga (una vez) y cachea los campos dinamicos activos del paciente.</summary>
    Task<IReadOnlyList<ReportableFieldDef>> CargarAsync(CancellationToken ct = default);

    /// <summary>Snapshot ya cargado (vacio si aun no se llamo <see cref="CargarAsync"/>).</summary>
    IReadOnlyList<ReportableFieldDef> CamposPaciente { get; }
}

public sealed class ReporteCamposDinamicos(IApplicationDbContext db) : IReporteCamposDinamicos
{
    private IReadOnlyList<ReportableFieldDef>? _cache;

    public IReadOnlyList<ReportableFieldDef> CamposPaciente => _cache ?? [];

    public async Task<IReadOnlyList<ReportableFieldDef>> CargarAsync(CancellationToken ct = default)
    {
        if (_cache is not null) { return _cache; }
        // El query filter de tenant limita a los campos del tenant actual.
        var rows = await db.PacienteCampoDefs.AsNoTracking()
            .Where(c => c.Activo && c.Tipo != Domain.Enums.PacienteCampoTipo.Separador)
            .OrderBy(c => c.TabKey).ThenBy(c => c.Orden)
            .Select(c => new { c.CampoKey, c.Label })
            .ToListAsync(ct);
        _cache = rows
            .Select(c => new ReportableFieldDef(
                c.CampoKey,
                c.Label,
                nameof(Paciente.CamposDinamicosJson),
                ReportFieldKind.Text,
                JsonKey: c.CampoKey))
            .ToList();
        return _cache;
    }
}
