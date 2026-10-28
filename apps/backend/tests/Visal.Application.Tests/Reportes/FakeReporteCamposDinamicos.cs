using Visal.Application.Tenancy.Reportes;

namespace Visal.Application.Tests.Reportes;

/// <summary>
/// Fake de <see cref="IReporteCamposDinamicos"/> para pruebas: devuelve una lista fija de
/// campos dinamicos (vacia por defecto) sin tocar la BD.
/// </summary>
public sealed class FakeReporteCamposDinamicos(IReadOnlyList<ReportableFieldDef>? campos = null)
    : IReporteCamposDinamicos
{
    private readonly IReadOnlyList<ReportableFieldDef> _campos = campos ?? [];

    public IReadOnlyList<ReportableFieldDef> CamposPaciente => _campos;

    public Task<IReadOnlyList<ReportableFieldDef>> CargarAsync(CancellationToken ct = default)
        => Task.FromResult(_campos);
}
