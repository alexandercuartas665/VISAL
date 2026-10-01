using Visal.Domain.Common;

namespace Visal.Domain.Entities;

public enum HcMarcaErrorEstado
{
    /// <summary>Marcada con error, pendiente de reparar.</summary>
    Pendiente = 0,
    /// <summary>Ya revisada/corregida.</summary>
    Reparado = 1
}

/// <summary>
/// Marca de control de calidad sobre una ASIGNACION (lote) detectada mientras se
/// valida/recorre Ordenes Clinicas (p. ej. numeracion de sesion sospechosa). Es
/// un flujo de triage independiente de la revision clinica: el usuario marca con
/// una observacion, lo ve en el tab "Marcadas" y lo cierra como Reparado.
/// Los campos de auditoria (CreatedAt/CreatedBy = marcado) los pone el interceptor;
/// los *Nombre se guardan como snapshot para mostrar sin joins extra.
/// </summary>
public class HcMarcaError : TenantEntity
{
    /// <summary>Lote de asignacion (AsignacionLoteId) que se marco.</summary>
    public Guid AsignacionId { get; set; }

    /// <summary>Codigo corto (8 chars) para mostrar/filtrar.</summary>
    public string CodigoAsignacion { get; set; } = "";

    /// <summary>Snapshot del paciente al marcar (para la lista del tab).</summary>
    public string? PacienteNombre { get; set; }
    public string? PacienteDoc { get; set; }

    /// <summary>Observacion obligatoria al marcar con error.</summary>
    public string Observacion { get; set; } = "";

    public HcMarcaErrorEstado Estado { get; set; } = HcMarcaErrorEstado.Pendiente;

    /// <summary>Nombre de quien marco (snapshot; CreatedAt = cuando).</summary>
    public string? MarcadoPorNombre { get; set; }

    /// <summary>Datos de la reparacion (cuando Estado = Reparado).</summary>
    public string? ReparadoPorNombre { get; set; }
    public DateTimeOffset? ReparadoEn { get; set; }
    public string? ObservacionReparacion { get; set; }
}
