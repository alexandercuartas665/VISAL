using System.Globalization;
using System.Text;

namespace Visal.Application.Tenancy.CamposDinamicos;

/// <summary>Slug ASCII estable para claves de tab/campo (sin acentos, [a-z0-9_]).</summary>
internal static class CamposDinamicosSlug
{
    public static string Make(string? texto, string fallback)
    {
        var s = (texto ?? "").Trim();
        if (s.Length == 0) { return fallback; }

        // Quita acentos (NFD -> descarta diacriticos).
        var nfd = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nfd.Length);
        foreach (var ch in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) { continue; }
            var c = char.ToLowerInvariant(ch);
            sb.Append((c is >= 'a' and <= 'z' or >= '0' and <= '9') ? c : '_');
        }

        // Colapsa guiones bajos repetidos y recorta extremos.
        var raw = sb.ToString();
        var partes = raw.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var slug = string.Join('_', partes);
        return slug.Length == 0 ? fallback : slug;
    }
}
