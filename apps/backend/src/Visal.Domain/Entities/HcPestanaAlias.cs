using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Nombre personalizable que un tenant le pone a una pestana del menu lateral
/// del modal de Historia Clinica. Por defecto la UI muestra la clave tecnica
/// (ej. "Ordenes medicamento"), pero el admin puede renombrar cualquier pestana
/// a "Recetas" (o lo que quiera) sin afectar la logica interna — el codigo
/// sigue referenciando la PestanaKey.
///
/// Una fila por (tenant, pestana). Si no existe fila para una pestana dada,
/// la UI cae al nombre por defecto.
/// </summary>
public class HcPestanaAlias : TenantEntity
{
    /// <summary>Clave logica de la pestana. Coincide con los identificadores usados
    /// en HistoriasClinicasModulo.razor y HcMenuConfig.PestanaKey.</summary>
    public string PestanaKey { get; set; } = null!;

    /// <summary>Etiqueta que la UI muestra al usuario. Si esta vacia o null
    /// se cae al nombre por defecto de la pestana.</summary>
    public string? Alias { get; set; }

    /// <summary>Posicion de la pestana en el menu lateral (menor primero).
    /// Cuando es null la UI usa el orden hardcodeado del array _tabs de
    /// HistoriasClinicasModulo.razor. Un valor override gana sobre ese default.</summary>
    public int? Orden { get; set; }
}
