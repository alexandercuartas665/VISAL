namespace Visal.Domain.Enums;

/// <summary>
/// Tipo de origen de un item dentro de la "Cuenta medica" configurada por
/// aseguradora. El generador (fase 2) recorre los items de la configuracion
/// y para cada uno resuelve el documento fisico desde la fuente indicada.
///
/// - HistoriaClinicaPdf: HC completa impresa (reusa la maquinaria de
///   "Imprimir paquete" del modulo HC).
/// - DocumentoHc: adjunto externo atado a una HC via HistoriaClinicaId,
///   filtrable por TipologiaArchivoId.
/// - DocumentoPacienteLibre: subido en Admision -> tab Documentos, sin HC
///   ni nota. Filtrable por TipologiaArchivoId.
/// - DocumentoNota: adjunto a una nota medica, filtrable por TipologiaArchivoId.
/// - FirmaPaciente: ultima firma capturada del paciente (multi-signatario).
/// - AutorizacionAsignacion: PDF de contrato/autorizacion guardado en la
///   asignacion (Asignacion.ContratoPdf).
/// - Consentimiento / Evolucion / Escala: formularios firmados de la HC
///   (tabs del modal HC atados a RelacionesFormulario).
/// </summary>
public enum OrigenInformeItem
{
    HistoriaClinicaPdf = 0,
    DocumentoHc = 1,
    DocumentoPacienteLibre = 2,
    DocumentoNota = 3,
    FirmaPaciente = 4,
    AutorizacionAsignacion = 5,
    Consentimiento = 6,
    Evolucion = 7,
    Escala = 8,
}
