namespace Visal.Domain.Enums;

/// <summary>
/// Tipos del catalogo de servicios "de referencia" — CUPS y CUM oficiales
/// del sector salud, agrupados por hoja del Excel origen. Sirven para
/// autocompletar en HC, remisiones y ordenes. Independientes entre si:
/// cada uno tiene su propia pagina, importacion y vaciado.
/// </summary>
public enum TipoCatalogoServicio
{
    /// <summary>Radiografias, ecografias, tomografias, resonancias. CUPS 87xxxx.</summary>
    RxImagenologia,
    /// <summary>Examenes de laboratorio clinico. CUPS 90xxxx.</summary>
    Laboratorio,
    /// <summary>Servicios asistenciales y quirurgicos. CUPS 89xxxx.</summary>
    ServicioGeneral,
    /// <summary>Insumos y dispositivos medicos. Codigos INDIxx propios.</summary>
    Insumo
}
