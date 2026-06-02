using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Codigo Unico de Procedimientos en Salud (CUPS) - clasificador oficial colombiano
/// de procedimientos medico-quirurgicos. Cada fila es un codigo CUPS con su nombre,
/// capitulo, clasificacion y atributos publicados por el Ministerio de Salud (MSPS).
/// Tenant-scoped: cada agencia mantiene su propia copia (igual que Medicamentos).
/// </summary>
public class Cup : TenantEntity
{
    // -------- Identificador --------
    /// <summary>Tipo de tabla del archivo MSPS (siempre "CUPS" en el muestra oficial).</summary>
    public string? Tabla { get; set; }
    /// <summary>Codigo del procedimiento (ej. 010101). Es el identificador principal.</summary>
    public string? Codigo { get; set; }

    // -------- Descripcion --------
    public string? Nombre { get; set; }
    /// <summary>Capitulo / area del CUPS (ej. "CapItulo 01 SISTEMA NERVIOSO").</summary>
    public string? Descripcion { get; set; }

    // -------- Estado y aplicacion --------
    public string? Habilitado { get; set; }
    public string? Aplicacion { get; set; }
    public string? IsStandardGEL { get; set; }
    public string? IsStandardMSPS { get; set; }

    // -------- Columnas "Extra" del muestra oficial --------
    // Se preservan literales para no perder datos del cargue; las que se usan en
    // la UI son ExtraIV (clasificacion: CATEGORIA / SUBCATEGORIA / ...) y ExtraV
    // (codigo jerarquico).
    public string? ExtraI { get; set; }
    public string? ExtraII { get; set; }
    public string? ExtraIII { get; set; }
    public string? ExtraIV { get; set; }
    public string? ExtraV { get; set; }
    public string? ExtraVI { get; set; }
    public string? ExtraVII { get; set; }
    public string? ExtraVIII { get; set; }
    public string? ExtraIX { get; set; }
    public string? ExtraX { get; set; }

    // -------- Auditoria del archivo origen --------
    public string? ValorRegistro { get; set; }
    public string? UsuarioResponsable { get; set; }
    public DateTimeOffset? FechaActualizacion { get; set; }
    public string? IsPublicPrivate { get; set; }
}
