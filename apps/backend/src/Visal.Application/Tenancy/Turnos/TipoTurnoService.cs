using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy.Turnos;

public sealed class TipoTurnoService(IApplicationDbContext db) : ITipoTurnoService
{
    public async Task<IReadOnlyList<TipoTurnoDto>> ListarAsync(bool incluirInactivos = false, CancellationToken ct = default)
    {
        var q = db.TiposTurno.AsNoTracking();
        if (!incluirInactivos) { q = q.Where(t => t.Activo); }
        var rows = await q.OrderBy(t => t.Orden).ThenBy(t => t.Codigo).ToListAsync(ct);
        return rows
            .Select(t => new TipoTurnoDto(
                t.Id, t.Codigo, t.Etiqueta, t.HorasDefault,
                t.ColorFondo, t.ColorTexto, t.ColorBorde, t.Orden, t.Activo))
            .ToList();
    }
}
