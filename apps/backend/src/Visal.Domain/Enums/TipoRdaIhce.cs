namespace Visal.Domain.Enums;

/// <summary>
/// Tipo de RDA emitido al IHCE de MinSalud (Res. 1888/2025). Visal soporta los dos
/// flujos principales:
/// </summary>
public enum TipoRdaIhce
{
    /// <summary>
    /// Resumen Digital de Atencion del Paciente — antecedentes manifestados
    /// (medicamentos cronicos, alergias conocidas, dx previos, antecedentes
    /// familiares). Se emite al admitir un paciente nuevo para subir su historia.
    /// Endpoint: <c>$enviar-rda-paciente</c>. Perfil <c>CompositionPatientStatementRDA</c>.
    /// </summary>
    Paciente = 0,

    /// <summary>
    /// RDA Consulta — reporte de una atencion clinica especifica (encuentro).
    /// Se emite cada vez que un profesional cierra una HC. Incluye Encounter,
    /// CUPS del servicio, prescripciones, ordenes y epicrisis en PDF.
    /// Endpoint: <c>$enviar-rda-consulta</c>. Perfil <c>CompositionAmbulatoryRDA</c>.
    /// </summary>
    Consulta = 1
}
