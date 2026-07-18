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

    // ---- RIPS Res 2275 (Fase 4 Facturacion) ----
    // Guardados como codigo MinSalud directo (string). Doc: 07. Facturacion §5.10.

    /// <summary>Codigo RIPS Finalidad (ej. 17 = terapeutica).</summary>
    public string? Finalidad { get; set; }
    /// <summary>Codigo RIPS Causa externa (ej. 38 = enfermedad general).</summary>
    public string? CausaExterna { get; set; }
    /// <summary>Codigo RIPS Modalidad atencion (ej. 02 = extramural domiciliaria).</summary>
    public string? ModalidadAtencion { get; set; }
    /// <summary>Codigo RIPS Via de ingreso al servicio de salud (ej. 03).</summary>
    public string? ViaIngreso { get; set; }
    /// <summary>Codigo MinSalud Grupo de servicios (ej. 07 = terapias).</summary>
    public string? GrupoServicios { get; set; }
    /// <summary>Codigo REPS del servicio (ej. 312 = atencion domiciliaria).</summary>
    public string? Servicios { get; set; }
    /// <summary>Valor Total del servicio segun contrato (Tarifa * cantidad tope). Nullable — si no se define, el builder cae a Tarifa.</summary>
    public decimal? ValorTotal { get; set; }
}
