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

    /// <summary>Codigo unico contable/administrativo (CUCON) que identifica el
    /// contrato en sistemas externos. Texto libre; puede contener uno o varios
    /// numeros separados por coma. Nullable para no romper contratos preexistentes.</summary>
    public string? Cucon { get; set; }

    /// <summary>Tipo de contrato segun el regimen SGSSS colombiano. La UI lo exige
    /// al crear/editar; en BD queda nullable para contratos migrados hasta que se
    /// re-editen.</summary>
    public TipoContrato? TipoContrato { get; set; }
}

/// <summary>Regimen del contrato (Ley 100 SGSSS Colombia).</summary>
public enum TipoContrato
{
    Subsidiado = 1,
    Contributivo = 2,
}
