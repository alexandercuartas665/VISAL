using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Catalogo dinamico de "modulos" o tipos de servicio contratado (CONSULTA,
/// TERAPIA, ENFERMERIA, EQUIPOS, INSUMOS, ...). Antes vivia como cadena libre
/// en <c>servicios_contrato.modulo</c> y en columnas boolean hardcodeadas
/// (CoordinaTerapias, CoordinaConsultas, ...), lo que impedia agregar tipos
/// nuevos sin tocar codigo. Este catalogo es la unica fuente de verdad:
///
/// - Import Excel de servicios de contrato valida contra Codigo.
/// - Coordinacion filtra por Codigo (via tabla N:N tenant_user_tipos_coordinados).
/// - Menu HC por tipo (/config/menu-hc) renderiza una columna por fila activa.
/// - Wizard de asignacion (/asignacion) pobla el dropdown desde este catalogo.
///
/// Cada tenant administra su propia lista. Valores canonicos por convencion:
/// CODIGO en MAYUSCULAS, sin tildes, singular (CONSULTA, no CONSULTAS).
/// </summary>
public class CatalogoTipoServicio : TenantEntity
{
    /// <summary>Codigo canonico usado como llave logica en servicios_contrato.modulo
    /// y asignaciones.modulo. En MAYUSCULAS, sin tildes, unico por tenant.</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Etiqueta que se muestra en la UI. Puede tener acentos y minusculas.</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Orden de visualizacion en columnas/dropdowns. Menor primero.</summary>
    public int Orden { get; set; }

    /// <summary>Si esta apagado no aparece en dropdowns nuevos; los datos historicos
    /// que lo referencien siguen validos.</summary>
    public bool Activo { get; set; } = true;
}
