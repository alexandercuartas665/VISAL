using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Concepto/categoria seleccionable al generar un token de autorizacion (ej.
/// "Registro tardio por conectividad"). Cada tenant administra su propia lista
/// desde el modulo /autorizaciones. Es la razon tipificada; la observacion del
/// token sigue siendo el detalle en texto libre. Tenant-scoped.
/// </summary>
public class AutorizacionConcepto : TenantEntity
{
    /// <summary>Etiqueta del concepto. Unico por tenant (case-insensitive).</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Orden de visualizacion en el dropdown. Menor primero.</summary>
    public int Orden { get; set; }

    /// <summary>Si esta apagado no aparece en el dropdown nuevo; los tokens historicos lo conservan.</summary>
    public bool Activo { get; set; } = true;
}
