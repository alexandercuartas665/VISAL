using Visal.Application.Facturacion;

namespace Visal.SuperAdmin.Facturacion;

/// <summary>
/// Genera el ZIP de una tipologia/archivo de la cuenta medica para TODOS los
/// pacientes de un snapshot: un PDF por paciente (fusionando el contenido real
/// de los origenes soportados) nombrado con el patron configurado.
///
/// Ola 1: origenes basados en archivo ya recuperables — AutorizacionAsignacion
/// (Asignacion.PdfAutorizacionUrl) y FirmaPaciente (NotaMedica.FirmaPacienteDataUrl).
/// Los origenes de formularios de HC (HistoriaClinicaPdf, Evolucion, Escala,
/// Consentimiento) requieren el pipeline de impresion y llegan en una ola posterior;
/// por ahora dejan una pagina-nota en el PDF del paciente.
/// </summary>
public interface ITipologiaZipService
{
    /// <summary>
    /// Devuelve el ZIP (bytes + nombre) del archivo <paramref name="archivoItemId"/>
    /// para el snapshot indicado, o null si el snapshot/archivo no existen o el
    /// snapshot no tiene aseguradora.
    /// </summary>
    Task<ArchivoExportado?> GenerarZipArchivoAsync(
        Guid snapshotId, Guid archivoItemId, CancellationToken ct = default);
}
