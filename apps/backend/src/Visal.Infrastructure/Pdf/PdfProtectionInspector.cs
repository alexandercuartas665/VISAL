using PdfSharp.Pdf.IO;

namespace Visal.Infrastructure.Pdf;

/// <summary>Resultado de inspeccionar un PDF subido por el usuario.</summary>
public enum PdfEstado
{
    /// <summary>Abre sin pedir clave (sin cifrar, o cifrado de solo-permisos con clave de usuario vacia).</summary>
    Ok,

    /// <summary>Esta cifrado y exige una contrasena de usuario para abrirse (PDF "con clave").</summary>
    ProtegidoPorPassword,

    /// <summary>No es un PDF legible (archivo danado o no es PDF).</summary>
    NoEsPdfValido,
}

/// <summary>
/// Detecta PDFs protegidos con contrasena antes de aceptarlos. Un PDF "con clave"
/// (contrasena de usuario para abrir) rompe el procesamiento posterior: la extraccion
/// de texto, el render y la fusion en Cuenta Medica / ZIP lo reemplazan por una pagina
/// "no legible". Mejor rechazarlo en la carga con un mensaje claro.
///
/// Estrategia: se intenta abrir con PdfSharp SIN proveedor de contrasena, exactamente
/// como lo hace la fusion de PDFs (TipologiaZipService.FusionarPdfs). Si abre, el resto
/// del pipeline tambien podra -> Ok (cubre PDFs sin cifrar y los de solo-permisos, que
/// tienen clave de usuario vacia). Si lanza, se clasifica mirando el contenido crudo:
/// un PDF con firma %PDF y una entrada /Encrypt en su estructura esta cifrado y exige
/// clave -> ProtegidoPorPassword; cualquier otro fallo -> NoEsPdfValido. No se depende
/// del texto del error (viene en ingles y puede cambiar entre versiones de PdfSharp).
/// </summary>
public static class PdfProtectionInspector
{
    public static PdfEstado Inspeccionar(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 8) { return PdfEstado.NoEsPdfValido; }

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            // Sin proveedor de contrasena: PdfSharp prueba la clave vacia por defecto.
            // Si el PDF exige contrasena de usuario, lanza (igual que la fusion real).
            using var _ = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            return PdfEstado.Ok;
        }
        catch (Exception) when (EstaCifrado(bytes))
        {
            return PdfEstado.ProtegidoPorPassword;
        }
        catch
        {
            return PdfEstado.NoEsPdfValido;
        }
    }

    /// <summary>
    /// Un PDF cifrado declara la entrada <c>/Encrypt</c> en su trailer. Se exige ademas
    /// la firma <c>%PDF</c> cerca del inicio para no confundir un archivo arbitrario que
    /// contenga ese texto por casualidad.
    /// </summary>
    private static bool EstaCifrado(byte[] bytes)
    {
        var head = Math.Min(bytes.Length, 1024);
        var span = bytes.AsSpan();
        var tieneFirma = span[..head].IndexOf("%PDF"u8) >= 0;
        if (!tieneFirma) { return false; }
        return span.IndexOf("/Encrypt"u8) >= 0;
    }
}
