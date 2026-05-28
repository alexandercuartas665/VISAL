using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy;

public sealed class GeografiaService(IApplicationDbContext db) : IGeografiaService
{
    public async Task<IReadOnlyList<PaisDto>> ListPaisesAsync(CancellationToken ct = default)
        => await db.Paises.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.Nombre)
            .Select(p => new PaisDto(p.Id, p.Codigo, p.Nombre)).ToListAsync(ct);

    public async Task<IReadOnlyList<DepartamentoDto>> ListDepartamentosAsync(Guid paisId, CancellationToken ct = default)
        => await db.Departamentos.AsNoTracking().Where(d => d.PaisId == paisId && d.Activo).OrderBy(d => d.Nombre)
            .Select(d => new DepartamentoDto(d.Id, d.PaisId, d.Nombre)).ToListAsync(ct);

    public async Task<IReadOnlyList<MunicipioDto>> ListMunicipiosAsync(Guid departamentoId, CancellationToken ct = default)
        => await db.Municipios.AsNoTracking().Where(m => m.DepartamentoId == departamentoId && m.Activo).OrderBy(m => m.Nombre)
            .Select(m => new MunicipioDto(m.Id, m.DepartamentoId, m.Nombre)).ToListAsync(ct);
}
