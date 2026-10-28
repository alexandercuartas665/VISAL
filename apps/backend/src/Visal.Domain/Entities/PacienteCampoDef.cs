using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Definicion de un campo dinamico de la ficha de paciente (gestor de campos del tenant).
/// El valor por paciente se guarda en <c>Paciente.CamposDinamicosJson</c> bajo
/// <see cref="CampoKey"/>. Tenant-scoped. Calcado de ECOREX TerceroFieldDefinition.
/// </summary>
public class PacienteCampoDef : TenantEntity
{
    /// <summary>Tab (ficha) al que pertenece el campo (<see cref="PacienteFichaDef.TabKey"/>).</summary>
    public string TabKey { get; set; } = null!;

    /// <summary>Clave estable (slug) del campo; UNICA por tenant. Es la clave en el jsonb.</summary>
    public string CampoKey { get; set; } = null!;

    /// <summary>Etiqueta visible.</summary>
    public string Label { get; set; } = null!;

    /// <summary>Tipo de dato / input.</summary>
    public PacienteCampoTipo Tipo { get; set; } = PacienteCampoTipo.Texto;

    /// <summary>Opciones para <see cref="PacienteCampoTipo.Seleccion"/> (una por linea).</summary>
    public string? Opciones { get; set; }

    /// <summary>Ancho en la grilla de 3: 1 = 1/3, 2 = 2/3, 3 = ancho completo.</summary>
    public int Ancho { get; set; } = 1;

    /// <summary>Orden dentro del tab ("que tan arriba").</summary>
    public int Orden { get; set; }

    /// <summary>Texto de ayuda opcional.</summary>
    public string? Descripcion { get; set; }

    /// <summary>Si el campo es obligatorio al guardar el paciente.</summary>
    public bool Requerido { get; set; }

    /// <summary>Campo activo (visible/usable). Inactivo = oculto sin borrar.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Campo sembrado por la plataforma.</summary>
    public bool EsSistema { get; set; }
}
