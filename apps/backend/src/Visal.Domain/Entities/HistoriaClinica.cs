using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Instancia de una Historia Clinica diligenciada para un paciente, usando un
/// FormDefinition como plantilla. Los valores de los campos diligenciados se
/// guardan como JSON en <see cref="ValoresJson"/> (jsonb).
///
/// Ciclo de vida:
/// - Estado nace en <c>Abierta</c> al "Iniciar historia medica".
/// - Pasa a <c>Cerrada</c> cuando el profesional finaliza ("Cerrar").
/// - Pasa a <c>Inactiva</c> si se descarta (con motivo opcional).
///
/// Tenant-scoped.
/// </summary>
public class HistoriaClinica : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    public Guid FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    /// <summary>Profesional que abrio la historia (opcional). Vinculo al catalogo.</summary>
    public Guid? ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }

    /// <summary>
    /// Diccionario clave→valor con los datos del formulario diligenciados.
    /// Se guarda como jsonb. Formato: { "campo_target": "valor", ... }
    /// </summary>
    public string ValoresJson { get; set; } = "{}";

    public HistoriaClinicaEstado Estado { get; set; } = HistoriaClinicaEstado.Abierta;

    public DateTimeOffset FechaApertura { get; set; }
    public DateTimeOffset? FechaCierre { get; set; }

    /// <summary>
    /// Fecha real de la atencion clinica, calculada a partir de los campos del
    /// formulario marcados con <c>IsFechaAtencion=true</c> (mayor entre los que
    /// tengan valor). Se recalcula al Guardar valores y al Cerrar. Es
    /// independiente de FechaApertura (que registra cuando se abrio el modal)
    /// y de FechaCierre (cuando el profesional finalizo). Puede ser null si
    /// el schema no marca ningun campo o ninguno tiene valor.
    /// </summary>
    public DateTimeOffset? FechaAtencion { get; set; }

    /// <summary>Motivo de inactivacion cuando Estado = Inactiva.</summary>
    public string? MotivoInactivacion { get; set; }

    /// <summary>Cache del nombre del profesional para mostrar sin join (opcional).</summary>
    public string? EspecialistaNombre { get; set; }

    // ── Datos RIPS obligatorios al iniciar la HC ────────────────────────
    /// <summary>Codigo del catalogo RipsViaIngreso (ej. "14" = CONTRARREFERIDO DE OTRA INSTITUCION).</summary>
    public string? RipsViaIngresoCodigo { get; set; }
    public string? RipsViaIngresoNombre { get; set; }

    /// <summary>Codigo del catalogo RipsFinalidadConsulta (ej. "14" = PROTECCION ESPECIFICA).</summary>
    public string? RipsFinalidadCodigo { get; set; }
    public string? RipsFinalidadNombre { get; set; }

    /// <summary>Codigo del catalogo RipsCausaExterna (ej. "13" = ENFERMEDAD GENERAL).</summary>
    public string? RipsCausaExternaCodigo { get; set; }
    public string? RipsCausaExternaNombre { get; set; }
}

public enum HistoriaClinicaEstado
{
    Abierta = 0,
    Cerrada = 1,
    Inactiva = 2
}
