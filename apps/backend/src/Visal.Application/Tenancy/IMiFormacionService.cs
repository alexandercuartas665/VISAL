namespace Visal.Application.Tenancy;

/// <summary>Curso visible para el alumno (usuario logueado) con su avance.</summary>
public sealed record MiCursoDto(
    Guid Id, string Titulo, string? Descripcion,
    int Modulos, int Lecciones, bool TieneEvaluacion,
    bool Iniciado, bool Aprobado, int MejorNota);

/// <summary>Pregunta para presentar al alumno: nunca incluye el indice correcto.</summary>
public sealed record PreguntaAlumnoDto(Guid Id, int Orden, string Enunciado, IReadOnlyList<string> Opciones);

/// <summary>Evaluacion final del curso tal como la ve el alumno.</summary>
public sealed record EvaluacionAlumnoDto(
    Guid Id, string Titulo, string? Descripcion, int PuntajeMinimo, IReadOnlyList<PreguntaAlumnoDto> Preguntas);

/// <summary>Resultado de un intento: nota, aprobacion y retroalimentacion de lo fallado.</summary>
public sealed record ResultadoIntentoDto(
    int Puntaje, bool Aprobado, int Correctas, int Total, IReadOnlyList<string> Retroalimentacion);

/// <summary>Estado del alumno frente a la evaluacion de un curso.</summary>
public sealed record EstadoEvaluacionDto(bool Aprobado, int Intentos, int MejorPuntaje, bool HayEvaluacion);

/// <summary>
/// Lado alumno del modulo de capacitaciones (LMS interno). Todo se resuelve para
/// el usuario logueado del tenant (platform_user_id); el progreso y los intentos
/// quedan atados a ese usuario.
/// </summary>
public interface IMiFormacionService
{
    Task<IReadOnlyList<MiCursoDto>> ListarMisCursosAsync(CancellationToken ct = default);
    /// <summary>Detalle (modulos + lecciones) de un curso ACTIVO para el reproductor. Null si no existe o esta inactivo.</summary>
    Task<CursoDetalleDto?> CursoAsync(Guid cursoId, CancellationToken ct = default);
    /// <summary>Marca el inicio del curso para el usuario (crea el progreso si no existe).</summary>
    Task RegistrarInicioAsync(Guid cursoId, CancellationToken ct = default);
    /// <summary>Evaluacion final del curso (sin la respuesta correcta). Null si el curso no tiene evaluacion activa.</summary>
    Task<EvaluacionAlumnoDto?> EvaluacionAsync(Guid cursoId, CancellationToken ct = default);
    Task<EstadoEvaluacionDto> EstadoAsync(Guid cursoId, CancellationToken ct = default);
    /// <summary>Califica un intento en el servidor, lo guarda y actualiza el progreso. Null si el curso no tiene evaluacion.</summary>
    Task<ResultadoIntentoDto?> ResponderAsync(Guid cursoId, IReadOnlyList<int> respuestas, CancellationToken ct = default);
}
