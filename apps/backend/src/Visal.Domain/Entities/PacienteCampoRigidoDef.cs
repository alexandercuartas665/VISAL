using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// COLOCACION por tenant de un campo FIJO de la ficha de admision. No guarda el tipo ni el
/// valor del campo (el valor vive en las columnas reales de <see cref="Paciente"/>); solo
/// decide en que tab aparece, en que orden, con que ancho, y si esta oculto.
/// La definicion (label, grupo, si es compuesto, si es ocultable) vive en el catalogo en
/// codigo (CamposRigidosCatalogo); esta fila solo lleva la personalizacion del tenant.
/// Se mantiene separada de <see cref="PacienteCampoDef"/> a proposito: los campos fijos no se
/// almacenan en jsonb ni se enrutan por la ruta jsonb del CENSO.
/// </summary>
public class PacienteCampoRigidoDef : TenantEntity
{
    /// <summary>Clave estable del campo fijo (empata con el catalogo en codigo).</summary>
    public string CampoKey { get; set; } = null!;

    /// <summary>Tab (ficha) donde se renderiza (<see cref="PacienteFichaDef.TabKey"/>).</summary>
    public string TabKey { get; set; } = null!;

    /// <summary>Orden dentro del tab (espacio de orden compartido con los campos dinamicos).</summary>
    public int Orden { get; set; }

    /// <summary>Ancho en la grilla de 3: 1 = 1/3, 2 = 2/3, 3 = completo. Ignorado si es compuesto.</summary>
    public int Ancho { get; set; } = 1;

    /// <summary>Oculta el campo en la ficha sin perder su valor. No permitido en campos obligatorios.</summary>
    public bool Oculto { get; set; }
}
