using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Catalogo de valores para Cuota Moderadora (regimen contributivo) y Copago
/// (regimen subsidiado). Se configura por categoria/rango salarial y guarda el
/// valor sugerido para la vigencia. En la asignacion, el modal ofrece el valor
/// segun (Tipo x CategoriaSalarial); el usuario puede sobreescribirlo. Tenant-scoped.
/// </summary>
public class CuotaCopago : TenantEntity
{
    /// <summary>"CUOTA" (cuota moderadora) o "COPAGO".</summary>
    public string Tipo { get; set; } = "CUOTA";

    /// <summary>Rango o categoria: p.ej. "SMLDV_MENOR_2", "SMLDV_2_A_5", "SMLDV_MAYOR_5" o
    /// descripcion libre. Se muestra en el dropdown del modal.</summary>
    public string Categoria { get; set; } = null!;

    /// <summary>Valor sugerido en pesos (COP). El usuario puede editarlo en el modal.</summary>
    public decimal ValorSugerido { get; set; }

    /// <summary>Descripcion opcional (Ejemplo del rango salarial en SMMLV).</summary>
    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;
}
