namespace Visal.Application.Tenancy;

/// <summary>
/// Modulo de capacitaciones (lado administrador): arma cursos con modulos y
/// lecciones (video/imagen/pdf/texto servidos desde wwwroot/uploads), elige la
/// evaluacion final (reusa un cuestionario) y edita las evaluaciones.
/// (LMS interno de personal; el lado alumno llega en una fase posterior.)
/// </summary>
public interface ICursoService
{
    // Cursos
    Task<IReadOnlyList<CursoDto>> ListarCursosAsync(CancellationToken ct = default);
    Task<CursoDetalleDto?> DetalleAsync(Guid cursoId, CancellationToken ct = default);
    Task<Guid?> GuardarCursoAsync(GuardarCursoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarCursoAsync(Guid cursoId, Guid actor, CancellationToken ct = default);

    // Modulos (episodios)
    Task<Guid?> GuardarModuloAsync(GuardarModuloRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarModuloAsync(Guid moduloId, Guid actor, CancellationToken ct = default);

    // Lecciones (recursos). El archivo se sube aparte con SubirRecursoAsync.
    Task<Guid?> GuardarLeccionAsync(GuardarLeccionRequest req, Guid actor, CancellationToken ct = default);
    /// <summary>Guarda el archivo de una leccion (video/imagen/pdf) en wwwroot/uploads/cursos
    /// y registra su ruta web. Devuelve false si no hay tenant activo.</summary>
    Task<bool> SubirRecursoAsync(Guid leccionId, byte[] contenido, string nombreArchivo, string mime, Guid actor, CancellationToken ct = default);
    /// <summary>Guarda la descripcion/contenido enriquecido (HTML) de una leccion. El HTML se
    /// sanitiza en el servidor antes de persistir (anti-XSS). No toca los demas campos.</summary>
    Task<bool> GuardarDescripcionHtmlAsync(Guid leccionId, string? html, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarLeccionAsync(Guid leccionId, Guid actor, CancellationToken ct = default);

    // Cuestionarios disponibles para elegir como evaluacion final
    Task<IReadOnlyList<(Guid Id, string Titulo)>> CuestionariosAsync(CancellationToken ct = default);

    // --- Editor de la evaluacion (cuestionario + preguntas + respuesta correcta) ---
    Task<IReadOnlyList<CuestionarioAdminDto>> CuestionariosAdminAsync(CancellationToken ct = default);
    Task<CuestionarioDetalleAdminDto?> CuestionarioAsync(Guid id, CancellationToken ct = default);
    /// <summary>Crea (Id nulo) o edita la cabecera de una evaluacion. Devuelve su Id.</summary>
    Task<Guid?> GuardarCuestionarioAsync(GuardarCuestionarioRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarCuestionarioAsync(Guid id, Guid actor, CancellationToken ct = default);
    /// <summary>Crea (Id nulo, se agrega al final) o edita una pregunta. Valida opciones y respuesta correcta. Devuelve su Id.</summary>
    Task<Guid?> GuardarPreguntaAsync(GuardarPreguntaRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarPreguntaAsync(Guid preguntaId, Guid actor, CancellationToken ct = default);
    /// <summary>Sube o baja una pregunta intercambiando su orden con la vecina.</summary>
    Task<bool> MoverPreguntaAsync(Guid preguntaId, bool arriba, Guid actor, CancellationToken ct = default);
}
