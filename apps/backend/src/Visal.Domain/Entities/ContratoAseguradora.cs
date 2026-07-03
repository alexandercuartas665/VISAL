using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Contrato de una aseguradora (1 aseguradora -> N contratos). Tenant-scoped.</summary>
public class ContratoAseguradora : TenantEntity
{
    public Guid AseguradoraId { get; set; }
    public Aseguradora? Aseguradora { get; set; }

    public string CodigoContrato { get; set; } = null!;
    public DateOnly? FechaInicial { get; set; }
    public DateOnly? FechaFinal { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public bool Prorroga { get; set; }
    /// <summary>Si true, la asignacion contra este contrato exige adjuntar el PDF de
    /// autorizacion antes de guardar. Si false, el PDF es opcional. Default false
    /// para no romper contratos existentes.</summary>
    public bool RequierePdfAutorizacion { get; set; }
}
