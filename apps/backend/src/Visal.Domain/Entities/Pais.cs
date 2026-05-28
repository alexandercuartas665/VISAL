using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Catalogo global de paises (no tenant-scoped). Se siembra desde api-colombia.com
/// al arrancar la app en Development (`EnsureGeografiaColombiaAsync`). Cubre la
/// referencia para pais_residencia y pais_origen del paciente.
/// </summary>
public class Pais : BaseEntity
{
    public string Codigo { get; set; } = null!;       // ISO Alpha-2 o codigo interno (CO, US, etc.)
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; } = true;
}
