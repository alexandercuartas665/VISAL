using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Servicio asignado a un paciente dentro de un lote. Reemplaza la tabla legacy
/// VISAL_ASIGNACIONES del modulo Visal (000822). Tenant-scoped.
///
/// Reglas:
/// - Cantidad > 0 (validacion en dominio).
/// - mes_vigencia / mes_final son enteros 1..12 (no string).
/// - Estado nace Pendiente y lo evoluciona Coordinacion.
/// - Texto se decodifica en presentacion (no HTML en BD).
/// </summary>
public class Asignacion : TenantEntity
{
    public Guid LoteId { get; set; }
    public AsignacionLote? Lote { get; set; }

    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    public string Sucursal { get; set; } = null!;

    /// <summary>Id del servicio del catalogo (puede ser el GUID del ServicioContrato o el codigo string).</summary>
    public string ServicioId { get; set; } = null!;
    public string NombreServicio { get; set; } = null!;

    /// <summary>Tipo de servicio derivado de servicios_contrato.Modulo (CONSULTA/TERAPIA/ENFERMERIA/EQUIPOS/INSUMOS).</summary>
    public string TipoServicio { get; set; } = null!;

    /// <summary>Modulo (puede coincidir con TipoServicio en la mayoria de casos; se conserva para casos data-driven).</summary>
    public string? Modulo { get; set; }

    public int Cantidad { get; set; }

    public string ContratoCodigo { get; set; } = null!;

    /// <summary>Numero de orden / autorizacion de la aseguradora.</summary>
    public string? CodigoAutorizacion { get; set; }

    public short? AnioServicio { get; set; }
    public short MesVigencia { get; set; }
    public short? MesFinal { get; set; }

    public DateOnly FechaInicio { get; set; }
    public DateOnly? FechaFinal { get; set; }

    public string? Observaciones { get; set; }

    /// <summary>Formato de historia ligado al servicio (FormDefinition.Codigo, p.ej.).</summary>
    public string? FormatoHistoria { get; set; }

    public AsignacionEstado Estado { get; set; } = AsignacionEstado.Pendiente;
}
