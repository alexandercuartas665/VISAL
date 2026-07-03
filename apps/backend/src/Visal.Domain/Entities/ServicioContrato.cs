using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Servicio asociado a un contrato (1 contrato -> N servicios). Tenant-scoped.
/// Estructura alineada al archivo de carga (Excel) de contratos.
/// </summary>
public class ServicioContrato : TenantEntity
{
    public Guid ContratoId { get; set; }
    public ContratoAseguradora? Contrato { get; set; }

    public string? Sede { get; set; }
    /// <summary>Codigo de la historia/formato que maneja el servicio (ej. 00018).</summary>
    public string? Historia { get; set; }
    /// <summary>Paquete comercial opcional al que pertenece el servicio (FK a Paquete).</summary>
    public Guid? PaqueteId { get; set; }
    public Paquete? Paquete { get; set; }
    public string? CodigoServicio { get; set; }
    public string? CodigoInterno { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Tarifa { get; set; }
    public string? Modulo { get; set; }
    public string? Especialidad { get; set; }
    public string? Modalidad { get; set; }
    public string? Clasificacion { get; set; }
    public string? Observaciones { get; set; }
}
