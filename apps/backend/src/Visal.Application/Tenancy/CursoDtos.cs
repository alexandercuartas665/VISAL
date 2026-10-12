namespace Visal.Application.Tenancy;

// ---------- Lectura ----------

public sealed record CursoDto(
    Guid Id, string Titulo, string? Descripcion, bool Activo,
    Guid? CuestionarioId, string? CuestionarioTitulo,
    int Modulos, int Lecciones);

public sealed record CursoModuloDto(Guid Id, Guid CursoId, string Titulo, string? Descripcion, int Orden, int Lecciones);

public sealed record CursoLeccionDto(
    Guid Id, Guid CursoModuloId, string Titulo, string? Descripcion, int Orden,
    string Tipo, string? RutaArchivo, string? Mime, long? TamanoBytes, string? Contenido,
    string? DescripcionHtml = null);

/// <summary>Curso completo para el reproductor (modulos con sus lecciones anidadas).</summary>
public sealed record CursoDetalleDto(
    Guid Id, string Titulo, string? Descripcion, bool Activo,
    Guid? CuestionarioId,
    IReadOnlyList<CursoModuloConLeccionesDto> Modulos);

public sealed record CursoModuloConLeccionesDto(
    Guid Id, string Titulo, string? Descripcion, int Orden,
    IReadOnlyList<CursoLeccionDto> Lecciones);

// ---------- Escritura ----------

public sealed class GuardarCursoRequest
{
    public Guid? Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;
    public Guid? CuestionarioId { get; set; }
}

public sealed class GuardarModuloRequest
{
    public Guid? Id { get; set; }
    public Guid CursoId { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
}

public sealed class GuardarLeccionRequest
{
    public Guid? Id { get; set; }
    public Guid CursoModuloId { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    public string Tipo { get; set; } = "VIDEO";
    public string? Contenido { get; set; }
}
