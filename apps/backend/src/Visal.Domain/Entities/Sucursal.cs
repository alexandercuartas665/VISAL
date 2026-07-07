using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Sucursal / sede que maneja la entidad. Tenant-scoped.</summary>
public class Sucursal : TenantEntity
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Cuando es true, la sede exige que toda entrega de medicamentos e insumos
    /// registre el codigo/URL de MIPRES. La HC (tabs Medicamentos e Insumos)
    /// bloquea el boton Agregar si falta ese dato. Regla operativa por sede
    /// (ej. IBAGUE lo exige, otras sedes no). Default false.
    /// </summary>
    public bool MipresObligatorio { get; set; }
}
