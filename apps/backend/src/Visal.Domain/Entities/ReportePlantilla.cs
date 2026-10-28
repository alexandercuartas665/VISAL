using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Reporte multi-hoja armado por el admin del tenant con el constructor visual (Capa 13).
/// Es la "plantilla" del pack: metadatos + N hojas. Tenant-scoped: cada agencia arma los
/// suyos. NO guarda SQL: cada hoja guarda su definicion visual, y el motor genera el SQL
/// al ejecutar (con el filtro por tenant obligatorio).
/// </summary>
public class ReportePlantilla : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public string? Categoria { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;

    // ---- Cabecera institucional (Ola 3): control documental tipo ISO que se imprime
    // arriba de cada hoja del Excel (Codigo / Version / Subtitulo-periodo). El nombre de
    // la agencia y la fecha de generacion no se guardan: salen del tenant y del reloj. ----

    /// <summary>Si es true, el Excel pinta la cabecera institucional arriba de cada hoja.</summary>
    public bool MostrarEncabezado { get; set; } = true;

    /// <summary>Codigo del formato (control documental), p.ej. "PP-FO-200".</summary>
    public string? Codigo { get; set; }

    /// <summary>Version del formato, p.ej. "1".</summary>
    public string? Version { get; set; }

    /// <summary>Subtitulo / periodo del reporte, p.ej. "Censo 2026 - I Trimestre".</summary>
    public string? Subtitulo { get; set; }

    /// <summary>
    /// Parametros de ejecucion (globales) del reporte, serializados como JSON
    /// (lista de ReporteParametroDef). Se piden al descargar y se aplican a las hojas
    /// cuyos filtros los referencian. Null / vacio = el reporte no pide parametros.
    /// </summary>
    public string? ParametrosJson { get; set; }

    public List<ReporteHoja> Hojas { get; set; } = new();
}

/// <summary>
/// Una hoja del pack. Guarda la definicion VISUAL (columnas, filtros, group-by, agregados,
/// orden) serializada en <see cref="DefinicionJson"/>; el generador la traduce a SQL. Los
/// campos Nombre/EntidadKey/Modo/Orden se replican como columnas para listar/ordenar sin
/// parsear el JSON.
/// </summary>
public class ReporteHoja : TenantEntity
{
    public Guid ReportePlantillaId { get; set; }
    public ReportePlantilla? ReportePlantilla { get; set; }

    public int Orden { get; set; }
    public string Nombre { get; set; } = null!;

    /// <summary>Key de la entidad reportable base (del modelo semantico).</summary>
    public string EntidadKey { get; set; } = null!;

    /// <summary>"Detalle" o "Agregado".</summary>
    public string Modo { get; set; } = "Detalle";

    /// <summary>
    /// Definicion visual de la hoja serializada (columnas, filtros, group-by, agregados,
    /// orden, limite). Es la fuente para armar la consulta; nunca contiene SQL.
    /// </summary>
    public string DefinicionJson { get; set; } = "{}";
}
