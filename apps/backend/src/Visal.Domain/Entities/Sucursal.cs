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

    /// <summary>
    /// Codigo de habilitacion REPS de la sede ante MinSalud (12 digitos numericos).
    /// Se factura por sede prestadora — el snapshot Relacion de Facturas lo pide
    /// como columna 4. NO confundir con InteroperabilidadCredencialSede.CodigoHabilitacion
    /// (aquel es por sede+ambiente para OAuth IHCE).
    /// </summary>
    public string? CodigoHabilitacion { get; set; }

    /// <summary>
    /// Cuando es true, el snapshot Relacion de Facturas excluye silenciosamente
    /// las sesiones de pacientes de esta sede cuyo estado de revision clinica
    /// NO sea "Aprobada" en el rango. Regla operativa por sede — cada sucursal
    /// decide si quiere el gate anti-facturacion-sin-revision. Default false.
    /// </summary>
    public bool ExigirHcRevisadaParaFacturar { get; set; }
}
