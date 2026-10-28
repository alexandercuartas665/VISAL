namespace Visal.Application.Tenancy.CamposDinamicos;

/// <summary>
/// Gestiona la COLOCACION por tenant de los campos fijos de la ficha de admision
/// (tab / orden / ancho / oculto). La definicion vive en <see cref="CamposRigidosCatalogo"/>;
/// el valor por paciente sigue en las columnas reales de Paciente. Tenant-scoped.
/// Tambien expone el reorden UNIFICADO de un tab (mezcla fijos + dinamicos en un solo orden).
/// </summary>
public interface IPacienteCampoRigidoService
{
    /// <summary>Siembra una colocacion por cada campo del catalogo si el tenant no tiene (idempotente).</summary>
    Task EnsureDefaultsAsync(CancellationToken ct = default);

    /// <summary>Todas las colocaciones del tenant fusionadas con el catalogo.</summary>
    Task<IReadOnlyList<PacienteCampoRigidoDto>> ListAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PacienteCampoRigidoDto>> ListByTabAsync(string tabKey, CancellationToken ct = default);

    /// <summary>Mueve un campo fijo a otro tab (queda al final del orden unificado del destino).</summary>
    Task MoverATabAsync(string campoKey, string tabKey, CancellationToken ct = default);

    /// <summary>Cicla el ancho 1 -&gt; 2 -&gt; 3 -&gt; 1. Ignorado si el campo es compuesto.</summary>
    Task CicloAnchoAsync(string campoKey, CancellationToken ct = default);

    /// <summary>Oculta/muestra el campo. Falla si el campo no es ocultable (obligatorio).</summary>
    Task SetOcultoAsync(string campoKey, bool oculto, CancellationToken ct = default);

    /// <summary>
    /// Reescribe el Orden 0..n de un tab segun la lista recibida, que mezcla campos fijos y
    /// dinamicos. Escribe a la tabla correspondiente de cada item.
    /// </summary>
    Task ReordenarTabAsync(string tabKey, IReadOnlyList<ItemColocacion> items, CancellationToken ct = default);
}
