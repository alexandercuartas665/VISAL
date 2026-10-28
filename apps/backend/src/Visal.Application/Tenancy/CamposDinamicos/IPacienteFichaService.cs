namespace Visal.Application.Tenancy.CamposDinamicos;

/// <summary>
/// Gestiona los TABS configurables de la ficha de paciente (gestor de campos del tenant).
/// Tenant-scoped (filtro global + stamp de TenantId al insertar).
/// </summary>
public interface IPacienteFichaService
{
    Task<IReadOnlyList<PacienteFichaDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Siembra el/los tabs de sistema del tenant si no existen (idempotente).</summary>
    Task EnsureDefaultsAsync(CancellationToken ct = default);

    Task<PacienteFichaDto> CreateAsync(CrearFichaRequest req, CancellationToken ct = default);
    Task<PacienteFichaDto?> UpdateAsync(Guid id, ActualizarFichaRequest req, CancellationToken ct = default);

    /// <summary>Reordena un tab un paso arriba/abajo (swap de Orden).</summary>
    Task ReorderAsync(Guid id, bool up, CancellationToken ct = default);

    /// <summary>Elimina un tab. Falla si es de sistema o si aun tiene campos.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
