using Visal.Application.Tenancy;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Helpers de presentacion de los documentos anexos de pacientes (tab Documentos de
/// Admision y modulo global /documentos): tipo de archivo, icono, tamano, fechas en
/// hora de Bogota y el tooltip de la historia de origen.
/// </summary>
public static class DocumentoArchivoUi
{
    /// <summary>Bogota (UTC-5): el server corre en UTC.</summary>
    public static readonly TimeSpan Bog = TimeSpan.FromHours(-5);

    public static bool EsImagen(string? mime, string? nombre)
    {
        if (!string.IsNullOrEmpty(mime) && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) { return true; }
        var ext = Path.GetExtension(nombre ?? "").ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";
    }

    public static bool EsPdf(string? mime, string? nombre)
    {
        if (!string.IsNullOrEmpty(mime) && mime.Contains("pdf", StringComparison.OrdinalIgnoreCase)) { return true; }
        return Path.GetExtension(nombre ?? "").Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static string Icono(string? nombre)
    {
        var ext = Path.GetExtension(nombre ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "\U0001F4D5",
            ".doc" or ".docx" => "\U0001F4D8",
            ".xls" or ".xlsx" => "\U0001F4D7",
            ".ppt" or ".pptx" => "\U0001F4D9",
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => "\U0001F5BC",
            ".zip" or ".rar" => "\U0001F5DC",
            _ => "\U0001F4C4",
        };
    }

    public static string Tamano(long bytes)
    {
        if (bytes < 1024) { return bytes + " B"; }
        if (bytes < 1024 * 1024) { return (bytes / 1024.0).ToString("0.#") + " KB"; }
        return (bytes / (1024.0 * 1024)).ToString("0.##") + " MB";
    }

    public static string Dia(DateTimeOffset f) => f.ToOffset(Bog).ToString("dd/MM/yyyy");

    public static string DiaHora(DateTimeOffset f) => f.ToOffset(Bog).ToString("dd/MM/yyyy HH:mm");

    /// <summary>Tooltip de la historia de origen: codigo, formato, fecha de atencion.</summary>
    public static string TituloHc(DocumentoPacienteDto d)
    {
        var partes = new List<string> { "Historia clinica " + d.CodigoHc };
        if (!string.IsNullOrWhiteSpace(d.FormatoHc)) { partes.Add("Formato: " + d.FormatoHc); }
        if (d.FechaAtencionHc is DateTimeOffset f) { partes.Add("Atencion: " + Dia(f)); }
        if (d.NotaMedicaId is not null) { partes.Add("(anexo de una nota medica de esa historia)"); }
        return string.Join("\n", partes);
    }

    /// <summary>Dia (Bogota) a mediodia: evita saltos de dia por zona al guardar la fecha del documento.</summary>
    public static DateTimeOffset? MediodiaBog(DateOnly? d) =>
        d is DateOnly v ? new DateTimeOffset(v.Year, v.Month, v.Day, 12, 0, 0, Bog) : null;
}
