using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion de visibilidad de pestanas del modal de Historia Clinica
/// segun el TipoServicio (Modulo) del servicio contratado en la asignacion.
///
/// Ejemplo: para TipoServicio="ENFERMERIA" se pueden ocultar las pestanas
/// "Ordenes medicamento" e "Insumos" ya que el profesional de enfermeria
/// no las diligencia. La clave compuesta (TenantId, TipoServicio, PestanaKey)
/// es unica: hay al menos una fila por par (tipo,pestana) — Visible=true
/// (default) si no existe la fila.
/// </summary>
public class HcMenuConfig : TenantEntity
{
    /// <summary>Valor de Asignacion.TipoServicio (CONSULTA/TERAPIA/ENFERMERIA/EQUIPOS/INSUMOS).
    /// Guardado en MAYUSCULAS, sin tildes.</summary>
    public string TipoServicio { get; set; } = null!;

    /// <summary>Identificador logico de la pestana en el menu lateral del HC.
    /// Coincide con las cadenas del array _tabs en HistoriasClinicasModulo.razor
    /// (ej. "Historial", "Escalas", "Atenciones", "Documentos externos", etc.).</summary>
    public string PestanaKey { get; set; } = null!;

    /// <summary>True = mostrar la pestana para ese tipo de servicio.
    /// Default true (opt-out). Solo se persisten los toggles que el admin cambia.</summary>
    public bool Visible { get; set; } = true;
}
