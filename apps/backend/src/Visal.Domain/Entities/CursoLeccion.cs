using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Leccion de un modulo: un recurso que el usuario ve o lee. El archivo
/// (video/imagen/pdf) se guarda en wwwroot/uploads/cursos via IUploadStorage y
/// aqui se conserva su ruta web servible; el texto va inline en Contenido.
/// </summary>
public class CursoLeccion : TenantEntity
{
    public Guid CursoModuloId { get; set; }
    public CursoModulo Modulo { get; set; } = null!;

    public string Titulo { get; set; } = null!;
    public string? Descripcion { get; set; }
    public int Orden { get; set; }

    /// <summary>VIDEO | IMAGEN | PDF | TEXTO.</summary>
    public string Tipo { get; set; } = "VIDEO";

    /// <summary>Ruta web servible del recurso (ej. "/uploads/cursos/ab..mp4").
    /// Null en TEXTO o mientras no se haya subido el archivo.</summary>
    public string? RutaArchivo { get; set; }
    public string? Mime { get; set; }
    public long? TamanoBytes { get; set; }

    /// <summary>Cuerpo de la leccion TEXTO (o pie de un recurso). Texto plano (legacy).</summary>
    public string? Contenido { get; set; }

    /// <summary>Descripcion/contenido enriquecido (HTML sanitizado) de la leccion:
    /// negrita, subrayado, listas, enlaces, tablas e imagenes embebidas. Se muestra
    /// en el reproductor del alumno. Null = sin contenido enriquecido.</summary>
    public string? DescripcionHtml { get; set; }
}
