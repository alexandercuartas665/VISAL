namespace Visal.Application.Tenancy;

public sealed record HcMenuConfigDto(string TipoServicio, string PestanaKey, bool Visible);

/// <summary>
/// Gestiona la visibilidad de pestanas del modal HC por TipoServicio.
/// El default (cuando no hay fila) es Visible=true — para revelar solo se
/// necesita borrar el override, para ocultar se persiste Visible=false.
/// </summary>
public interface IHcMenuConfigService
{
    /// <summary>Lista todos los overrides configurados en el tenant.</summary>
    Task<IReadOnlyList<HcMenuConfigDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Devuelve las pestanas cuya visibilidad esta explicitamente en false para el TipoServicio dado.
    /// Se pasan como "pestana oculta" al menu para filtrarlas. Si tipoServicio es null/vacio, retorna set vacio (todo visible).</summary>
    Task<HashSet<string>> ObtenerPestanasOcultasAsync(string? tipoServicio, CancellationToken ct = default);

    /// <summary>Upsert de un override. Si visible=true elimina la fila (vuelve al default). Si false persiste la fila.</summary>
    Task SaveAsync(string tipoServicio, string pestanaKey, bool visible, CancellationToken ct = default);
}
