namespace Visal.Application.Tenancy;

/// <summary>Cabecera de una evaluacion (cuestionario) como la ve el editor del administrador.</summary>
public sealed record CuestionarioAdminDto(Guid Id, string Modulo, string Titulo, string? Descripcion, int PuntajeMinimo, bool Activo, int Preguntas);

/// <summary>Una pregunta editable: enunciado, opciones y el indice (base 0) de la correcta.</summary>
public sealed record PreguntaAdminDto(Guid Id, int Orden, string Enunciado, IReadOnlyList<string> Opciones, int IndiceCorrecto, string? Retroalimentacion);

/// <summary>Evaluacion completa para el editor: cabecera + preguntas en orden.</summary>
public sealed record CuestionarioDetalleAdminDto(CuestionarioAdminDto Cabecera, IReadOnlyList<PreguntaAdminDto> Preguntas);

/// <summary>Alta o edicion de una evaluacion (Id nulo = crear).</summary>
public sealed class GuardarCuestionarioRequest
{
    public Guid? Id { get; set; }
    public string Modulo { get; set; } = "CAPACITACION";
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    public int PuntajeMinimo { get; set; } = 60;
    public bool Activo { get; set; } = true;
}

/// <summary>Alta o edicion de una pregunta (Id nulo = crear; se agrega al final).</summary>
public sealed class GuardarPreguntaRequest
{
    public Guid? Id { get; set; }
    public Guid CuestionarioId { get; set; }
    public string Enunciado { get; set; } = "";
    public List<string> Opciones { get; set; } = new();
    public int IndiceCorrecto { get; set; }
    public string? Retroalimentacion { get; set; }
}
