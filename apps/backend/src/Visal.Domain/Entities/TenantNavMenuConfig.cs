using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion (por tenant) del menu lateral de navegacion. Guarda como JSON la
/// organizacion que el administrador definio: grupos (nombre, icono, orden), a que
/// grupo va cada opcion, orden dentro del grupo, alias/icono override, opciones
/// ocultas y enlaces externos propios. Una fila por tenant (singleton).
///
/// Sin fila -> el menu usa el catalogo por defecto (<see cref="Visal.Application.Tenancy.Menu.NavMenuCatalog"/>),
/// identico al comportamiento previo (cero riesgo de romper el menu). El servicio
/// hace merge: las opciones nuevas que se agreguen en el codigo aparecen solas en
/// su grupo por defecto aunque el tenant tenga una config vieja guardada.
/// </summary>
public class TenantNavMenuConfig : TenantEntity
{
    /// <summary>Arbol serializado (grupos + items). "{}" o vacio = usar default.</summary>
    public string ConfigJson { get; set; } = "{}";
}
