namespace Visal.Application.Tenancy.CamposDinamicos;

/// <summary>
/// Gestiona las DEFINICIONES de campos dinamicos de la ficha de paciente. Tenant-scoped.
/// El valor por paciente vive en Paciente.CamposDinamicosJson (clave = CampoKey).
/// </summary>
public interface IPacienteCampoService
{
    Task<IReadOnlyList<PacienteCampoDto>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PacienteCampoDto>> ListByTabAsync(string tabKey, CancellationToken ct = default);

    /// <summary>Campos activos del tenant, para el renderer de la ficha y el CENSO.</summary>
    Task<IReadOnlyList<PacienteCampoDto>> ListActivosAsync(CancellationToken ct = default);

    Task<PacienteCampoDto> CreateAsync(CrearCampoRequest req, CancellationToken ct = default);
    Task<PacienteCampoDto?> UpdateAsync(Guid id, ActualizarCampoRequest req, CancellationToken ct = default);

    /// <summary>Reescribe el Orden 0..n de los campos de un tab segun la lista recibida.</summary>
    Task ReorderAsync(string tabKey, ReordenarCamposRequest req, CancellationToken ct = default);

    /// <summary>Cicla el ancho del campo 1 -&gt; 2 -&gt; 3 -&gt; 1.</summary>
    Task CicloAnchoAsync(Guid id, CancellationToken ct = default);

    /// <summary>Mueve el campo a otro tab (queda al final del orden del destino).</summary>
    Task MoverATabAsync(Guid id, string tabKey, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
