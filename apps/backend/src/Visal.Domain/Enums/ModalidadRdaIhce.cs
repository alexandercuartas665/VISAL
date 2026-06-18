namespace Visal.Domain.Enums;

/// <summary>
/// Modalidades de Resumen Digital de Atencion (RDA) reconocidas por la Resolucion 1888 de 2025.
/// Visal RT, como prestador domiciliario, usa <see cref="ConsultaExterna"/> por defecto
/// hasta que MinSalud publique una modalidad domiciliaria explicita.
/// </summary>
public enum ModalidadRdaIhce
{
    /// <summary>Resumen consolidado del paciente (no atado a una atencion especifica).</summary>
    Paciente = 0,

    /// <summary>Episodio de hospitalizacion intrahospitalaria.</summary>
    Hospitalizacion = 1,

    /// <summary>Atencion ambulatoria — modalidad principal para Visal RT.</summary>
    ConsultaExterna = 2,

    /// <summary>Atencion en servicio de urgencias.</summary>
    Urgencias = 3
}
