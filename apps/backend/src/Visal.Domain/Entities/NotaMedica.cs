using Visal.Domain.Common;

namespace Visal.Domain.Entities;

public enum NotaMedicaEstado
{
    /// <summary>Guardado parcial: se puede seguir editando.</summary>
    Parcial = 0,
    /// <summary>Guardado definitivo: ya no se edita, solo se ve.</summary>
    Definitivo = 1
}

public enum NotaMedicaCriticidad
{
    /// <summary>Por defecto. Sin alertas, paciente estable.</summary>
    Estable = 0,
    /// <summary>Requiere monitoreo extra.</summary>
    Vigilancia = 1,
    /// <summary>Cambios en evolucion clinica, ajustar plan.</summary>
    Alerta = 2,
    /// <summary>Critico: requiere accion inmediata.</summary>
    Critico = 3
}

/// <summary>
/// Nota medica de una sesion del paciente. Atada a la Historia Clinica
/// (FK), pero independiente del schema del FormDefinition: la nota es
/// texto libre del profesional sobre LA ATENCION del dia. Sirve para
/// seguimientos diarios sin tener que abrir/cerrar historias completas.
/// </summary>
public class NotaMedica : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    /// <summary>Turno/sesion en el que se hace la nota (cuando aplica).</summary>
    public Guid? AsignacionTurnoId { get; set; }
    public int? SessionNo { get; set; }

    public DateOnly FechaNota { get; set; }
    public TimeOnly? HoraNota { get; set; }

    /// <summary>Codigo corto autogenerado (los primeros 8 chars del Id).</summary>
    public string CodigoUnico { get; set; } = "";

    public string Contenido { get; set; } = "";

    /// <summary>Nombre del profesional que firma.</summary>
    public string? EspecialistaNombre { get; set; }

    public NotaMedicaEstado Estado { get; set; } = NotaMedicaEstado.Parcial;

    public NotaMedicaCriticidad Criticidad { get; set; } = NotaMedicaCriticidad.Estable;

    /// <summary>Data URL de la firma (base64 png). Null si no se firmo.</summary>
    public string? FirmaDataUrl { get; set; }
}
