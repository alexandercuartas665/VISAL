using Visal.Application.Facturacion;

namespace Visal.SuperAdmin.Facturacion;

/// <summary>Progreso de la generacion de un ZIP de tipologia, para mostrar en la UI
/// mientras corre en background. <see cref="Fase"/> es un texto corto ("Renderizando
/// formularios", "Armando PDFs"); <see cref="Hechos"/>/<see cref="Total"/> es el avance
/// de la fase larga (render de formularios).</summary>
public readonly record struct ZipProgreso(string Fase, int Hechos, int Total);

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
        Guid snapshotId, Guid archivoItemId, IProgress<ZipProgreso>? progreso = null, CancellationToken ct = default);

    /// <summary>
    /// Para cada archivo (tipologia) configurado en la aseguradora del snapshot,
    /// cuenta cuantos pacientes del snapshot tienen REALMENTE contenido para ese
    /// archivo (p. ej. cuantos tienen firma para un archivo de firmas). Es el numero
    /// de PDFs que produciria el ZIP. Devuelve un mapa archivoItemId -&gt; cantidad.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> ContarConContenidoAsync(
        Guid snapshotId, CancellationToken ct = default);
}
