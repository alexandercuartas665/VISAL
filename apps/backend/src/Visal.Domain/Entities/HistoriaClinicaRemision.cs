using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la pestana "Remisiones" de una Historia Clinica. Cada remision
/// referencia un servicio del catalogo de referencia (Tipo=ServicioGeneral).
/// Guarda un snapshot del codigo+descripcion al momento de crear la remision
/// para no depender del catalogo si este cambia despues.
/// </summary>
public class HistoriaClinicaRemision : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    // Columna heredada del flujo CUPS. Se conserva por compatibilidad con
    // registros historicos; en el flujo nuevo queda vacia. No la borramos
    // para no perder el snapshot de las remisiones ya guardadas.
    public string Capitulo { get; set; } = "";

    /// <summary>Snapshot del codigo del catalogo (columna "codigo").</summary>
    public string? EspecialidadCodigo { get; set; }

    /// <summary>Snapshot del nombre/descripcion del catalogo.</summary>
    public string EspecialidadNombre { get; set; } = null!;

    /// <summary>Cantidad solicitada (texto libre — puede ser "1", "5 unidades", etc).</summary>
    public string? Cantidad { get; set; }

    /// <summary>Motivo libre de la remision.</summary>
    public string? Motivo { get; set; }

    public int Orden { get; set; }
}
