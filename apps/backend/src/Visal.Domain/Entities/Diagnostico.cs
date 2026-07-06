using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Codigo de diagnostico usado en el modal "Buscar CIE-10 / CIE-11" del paciente
/// y de la historia clinica. Reemplaza la consulta a la WHO ICD-11 API (lenta e
/// incompleta) por una BD local que la agencia carga desde su Excel de referencia.
/// Tenant-scoped: cada agencia mantiene su propia copia.
/// </summary>
public class Diagnostico : TenantEntity
{
    /// <summary>Codigo unico (ej. CIE-10 A00.0, o codigo CUPS 010100). Case-insensitive.</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Nombre / descripcion corta que se muestra en la lista del modal.</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Descripcion larga o clasificacion (capitulo/seccion). Opcional.</summary>
    public string? Descripcion { get; set; }

    /// <summary>Si esta habilitado se lista en el modal de busqueda; si no, se oculta
    /// pero se conserva el registro por si hay historicos que lo referencian.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Origen del codigo (CUPSRips, CIE10, CIE11...). Permite filtrar por tipo
    /// desde el modal si mas adelante convive con otros catalogos.</summary>
    public string? Fuente { get; set; }
}
