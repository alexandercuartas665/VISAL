using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Representa un Resumen Digital de Atencion (RDA) bajo la Resolucion 1888 de 2025.
/// Cada fila corresponde a un Bundle FHIR R4 generado a partir de una Historia Clinica
/// que se envia (o se intentara enviar) a la plataforma IHCE del Ministerio de Salud.
///
/// El <see cref="BundleJson"/> es la FUENTE DE VERDAD: una vez generado, no se modifica
/// aunque la HC fuente cambie despues. El <see cref="BundleHash"/> garantiza idempotencia
/// (no se envia dos veces el mismo contenido aunque alguien haga doble click).
/// </summary>
public class RdaEvento : TenantEntity
{
    // ---- Atribucion clinica
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }
    public Guid? ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }

    // ---- Atribucion de envio (que credencial IHCE se uso)
    public Guid SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }
    public ModalidadRdaIhce Modalidad { get; set; } = ModalidadRdaIhce.ConsultaExterna;
    public AmbienteIhce Ambiente { get; set; }

    // ---- Contenido del Bundle FHIR
    public string BundleJson { get; set; } = null!;
    public string BundleHash { get; set; } = null!;

    // ---- Estado y trazabilidad de envio
    public EstadoRdaEvento Estado { get; set; } = EstadoRdaEvento.Borrador;
    public int Intentos { get; set; }
    public DateTimeOffset? UltimoIntento { get; set; }
    public DateTimeOffset FechaGeneracion { get; set; }
    public DateTimeOffset? FechaEnvio { get; set; }

    /// <summary>JSON con errores de validacion local o respuesta de rechazo de MinSalud.</summary>
    public string? ErroresJson { get; set; }

    /// <summary>ID de recibo que devuelve la plataforma IHCE cuando acepta el Bundle.</summary>
    public string? ReferenciaMinsalud { get; set; }
}
