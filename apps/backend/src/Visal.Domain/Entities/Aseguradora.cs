using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Entidad aseguradora / pagador (EPS, IPS, ARL, etc.) con la que la IPS tiene contratos.
/// Tenant-scoped. Cabecera; los contratos y sus servicios cuelgan de aqui.
/// </summary>
public class Aseguradora : TenantEntity
{
    public string Codigo { get; set; } = null!;
    public string Tipo { get; set; } = "EPS";
    public string Nombre { get; set; } = null!;
    public string? CodigoMovilidad { get; set; }
    public string? Nit { get; set; }
    public string? Regimen { get; set; }
    public string? CodInt { get; set; }
    public string? Descripcion { get; set; }
    /// <summary>Correo destino para radicar facturas ante la EPS (Fase 4 Facturacion).</summary>
    public string? CorreoFacturacion { get; set; }
}
