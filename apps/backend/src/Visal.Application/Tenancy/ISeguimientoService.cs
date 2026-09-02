namespace Visal.Application.Tenancy;

/// <summary>
/// Encuesta SIAU de satisfaccion para un paciente en un mes dado.
/// Un registro por (tenant, paciente, mes). Estado "Pendiente" al aparecer,
/// pasa a "Realizada" cuando se guarda con datos, "NoContactado" cuando la
/// llamada se intento sin exito.
/// </summary>
public sealed record SeguimientoEncuestaDto(
    Guid Id,
    Guid PacienteId,
    string PacienteNombre,
    string? PacienteTipoDoc,
    string? PacienteDocumento,
    string? PacienteTelefono,
    string? PacienteCodigoPais,
    int Mes,
    string Estado,
    DateTime? FechaLlamada,
    Guid? ResponsableLlamadaId,
    string? ResponsableLlamadaNombre,
    int? Pregunta1,
    int? Pregunta2,
    int? Pregunta3,
    int? Pregunta4,
    int? Pregunta5,
    string? PersonaAtiende,
    string? Observaciones);

public sealed record GuardarEncuestaRequest(
    DateTime? FechaLlamada,
    int? Pregunta1,
    int? Pregunta2,
    int? Pregunta3,
    int? Pregunta4,
    int? Pregunta5,
    string? PersonaAtiende,
    string? Observaciones);

public interface ISeguimientoService
{
    /// <summary>
    /// Lista todos los registros del mes (YYYYMM). Auto-materializa los
    /// pacientes que tuvieron actividad clinica en el mes y aun no tienen fila.
    /// </summary>
    Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarPorMesAsync(int mes, CancellationToken ct = default);

    /// <summary>
    /// Lista los registros cuyo Mes cae en el rango [desdeMes, hastaMes] (YYYYMM).
    /// NO auto-materializa: solo muestra las tarjetas ya traidas a la bandeja.
    /// </summary>
    Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarPorRangoAsync(int desdeMes, int hastaMes, CancellationToken ct = default);

    /// <summary>
    /// "Trae" a la bandeja (etapa Pendiente) a todo paciente con una historia
    /// clinica CERRADA cuyo FechaCierre cae en [desde, hasta]. Crea una tarjeta
    /// por (paciente, mes de cierre) si aun no existe. Devuelve cuantas creo y
    /// cuantas ya existian.
    /// </summary>
    Task<(int Creados, int Existentes)> TraerPacientesAsync(DateOnly desde, DateOnly hasta, Guid actor, CancellationToken ct = default);

    /// <summary>Guarda la encuesta -> estado Realizada.</summary>
    Task<bool> GuardarEncuestaAsync(Guid id, GuardarEncuestaRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Cambia el estado (Pendiente / Realizada / NoContactado).</summary>
    Task<bool> CambiarEstadoAsync(Guid id, string estado, Guid actor, CancellationToken ct = default);

    /// <summary>Historial completo: todas las encuestas en estado Realizada del
    /// tenant, ordenadas por FechaLlamada descendente. Usado por el tab "Historial"
    /// del modulo Seguimiento para consulta y exportacion.</summary>
    Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarHistorialRealizadasAsync(CancellationToken ct = default);
}
