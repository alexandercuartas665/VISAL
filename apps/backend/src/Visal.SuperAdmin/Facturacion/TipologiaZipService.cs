using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Visal.Application.Common;
using Visal.Application.Facturacion;
using Visal.Application.Tenancy;
using Visal.Domain.Enums;

namespace Visal.SuperAdmin.Facturacion;

/// <inheritdoc />
public sealed class TipologiaZipService : ITipologiaZipService
{
    private readonly IApplicationDbContext _db;
    private readonly IFacturacionSnapshotService _snaps;
    private readonly ICuentaMedicaConfigService _cuenta;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TipologiaZipService> _log;

    static TipologiaZipService()
    {
        // QuestPDF Community (gratis para este uso). Idempotente.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public TipologiaZipService(
        IApplicationDbContext db,
        IFacturacionSnapshotService snaps,
        ICuentaMedicaConfigService cuenta,
        IWebHostEnvironment env,
        ILogger<TipologiaZipService> log)
    {
        _db = db;
        _snaps = snaps;
        _cuenta = cuenta;
        _env = env;
        _log = log;
    }

    public async Task<ArchivoExportado?> GenerarZipArchivoAsync(
        Guid snapshotId, Guid archivoItemId, CancellationToken ct = default)
    {
        var detalle = await _snaps.ObtenerAsync(snapshotId, ct);
        if (detalle is null || detalle.Metadata.AseguradoraId is not Guid aseguradoraId)
        {
            return null;
        }

        var archivos = await _cuenta.ListarItemsAsync(aseguradoraId, ct);
        var archivo = archivos.FirstOrDefault(a => a.Id == archivoItemId);
        if (archivo is null) { return null; }
        var cfg = await _cuenta.GetOrCreateAsync(aseguradoraId, ct);
        var patron = !string.IsNullOrWhiteSpace(archivo.PatronNombre) ? archivo.PatronNombre!
            : !string.IsNullOrWhiteSpace(cfg.PatronNombreDefault) ? cfg.PatronNombreDefault!
            : "{sigla}_{cedula}";

        var pacientes = await _snaps.ListarPacientesSnapshotAsync(snapshotId, ct);

        using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pac in pacientes)
            {
                ct.ThrowIfCancellationRequested();
                var pdf = await ArmarPdfPacienteAsync(archivo, pac, ct);
                var baseName = SanitizarNombre(ResolverNombre(patron, archivo, pac));
                var name = baseName + ".pdf";
                // Evita colisiones de nombre (dos pacientes con mismo patron).
                var n = 2;
                while (!usados.Add(name)) { name = $"{baseName}_{n++}.pdf"; }

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(pdf, ct);
            }
        }

        var nombreZip = SanitizarNombre($"{archivo.Alias}_{detalle.Metadata.Nombre}") + ".zip";
        return new ArchivoExportado(zipMs.ToArray(), "application/zip", nombreZip);
    }

    /// <summary>
    /// Arma el PDF de un archivo para un paciente: recorre los contenidos, recupera
    /// el contenido real de los origenes soportados y fusiona todo en un solo PDF.
    /// </summary>
    private async Task<byte[]> ArmarPdfPacienteAsync(
        InformeItemDto archivo, PacienteSnapshotDto pac, CancellationToken ct)
    {
        var pid = await _db.Pacientes.AsNoTracking()
            .Where(p => p.NumeroDocumento == pac.Documento)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        // Lista de PDFs (bytes) a fusionar, en el orden de los contenidos.
        var partes = new List<byte[]>();
        var notas = new List<string>();

        if (pid is Guid pacienteId)
        {
            foreach (var c in archivo.Contenidos)
            {
                ct.ThrowIfCancellationRequested();
                switch (c.Origen)
                {
                    case OrigenInformeItem.AutorizacionAsignacion:
                        partes.AddRange(await CargarAutorizacionesAsync(pacienteId, c.SoloUltimo, ct));
                        break;
                    case OrigenInformeItem.FirmaPaciente:
                        partes.AddRange(await CargarFirmasAsync(pacienteId, c.SoloUltimo, ct));
                        break;
                    default:
                        // Origenes de formularios de HC: ola posterior.
                        notas.Add($"- {c.Origen} (pendiente de generacion)");
                        break;
                }
            }
        }
        else
        {
            notas.Add("No se encontro el paciente en el sistema para recuperar sus documentos.");
        }

        if (partes.Count == 0)
        {
            // Sin contenido real: pagina-nota para que el ZIP no quede con archivos vacios.
            var msg = new StringBuilder();
            msg.AppendLine($"Archivo: {(string.IsNullOrWhiteSpace(archivo.Descripcion) ? archivo.Alias : archivo.Descripcion)}");
            msg.AppendLine($"Paciente: {pac.Nombre} ({pac.TipoDocumento} {pac.Documento})");
            msg.AppendLine();
            msg.AppendLine("Sin documentos disponibles para este archivo todavia.");
            if (notas.Count > 0) { msg.AppendLine(); msg.AppendLine(string.Join(Environment.NewLine, notas)); }
            return PaginaNota(msg.ToString());
        }

        var fusion = FusionarPdfs(partes);
        return fusion;
    }

    private async Task<List<byte[]>> CargarAutorizacionesAsync(Guid pacienteId, bool soloUltimo, CancellationToken ct)
    {
        var q = _db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == pacienteId && a.PdfAutorizacionUrl != null);
        var urls = soloUltimo
            ? await q.OrderByDescending(a => a.CreatedAt).Select(a => a.PdfAutorizacionUrl!).Take(1).ToListAsync(ct)
            : await q.OrderBy(a => a.CreatedAt).Select(a => a.PdfAutorizacionUrl!).ToListAsync(ct);

        var res = new List<byte[]>();
        foreach (var url in urls.Distinct())
        {
            var pdf = LeerArchivoComoPdf(url);
            if (pdf is not null) { res.Add(pdf); }
        }
        return res;
    }

    private async Task<List<byte[]>> CargarFirmasAsync(Guid pacienteId, bool soloUltimo, CancellationToken ct)
    {
        var q = _db.NotasMedicas.AsNoTracking()
            .Where(n => n.PacienteId == pacienteId && n.FirmaPacienteDataUrl != null);
        var firmas = soloUltimo
            ? await q.OrderByDescending(n => n.CreatedAt).Select(n => n.FirmaPacienteDataUrl!).Take(1).ToListAsync(ct)
            : await q.OrderBy(n => n.CreatedAt).Select(n => n.FirmaPacienteDataUrl!).ToListAsync(ct);

        var res = new List<byte[]>();
        foreach (var dataUrl in firmas)
        {
            var img = DecodeDataUrl(dataUrl);
            if (img is not null) { res.Add(PaginaImagen(img, "Firma del paciente")); }
        }
        return res;
    }

    // ---- Recuperacion de archivos fisicos (wwwroot) ----

    /// <summary>Lee un archivo servido desde wwwroot (ruta relativa como
    /// "/uploads/...") y lo devuelve como PDF: si ya es PDF lo pasa tal cual; si es
    /// imagen la envuelve en un PDF. Devuelve null si no existe o no se pudo leer.</summary>
    private byte[]? LeerArchivoComoPdf(string urlRelativa)
    {
        try
        {
            var rel = urlRelativa.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.Combine(_env.WebRootPath ?? "", rel);
            if (!File.Exists(full)) { _log.LogWarning("Tipologia ZIP: archivo no existe {Path}", full); return null; }
            var bytes = File.ReadAllBytes(full);
            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == ".pdf") { return bytes; }
            // Imagen u otro: intenta envolver como imagen.
            return PaginaImagen(bytes, Path.GetFileName(full));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tipologia ZIP: no se pudo leer {Url}", urlRelativa);
            return null;
        }
    }

    // ---- Composicion PDF (QuestPDF + PdfSharp) ----

    private static byte[] PaginaImagen(byte[] imagen, string? titulo)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.Content().Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(titulo))
                    {
                        col.Item().Text(titulo).FontSize(12).SemiBold();
                        col.Item().PaddingBottom(8);
                    }
                    try { col.Item().Image(imagen).FitArea(); }
                    catch { col.Item().Text("(no se pudo renderizar la imagen)"); }
                });
            });
        }).GeneratePdf();
    }

    private static byte[] PaginaNota(string texto)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.Content().Text(texto).FontSize(11);
            });
        }).GeneratePdf();
    }

    /// <summary>Fusiona varios PDFs (bytes) en uno solo con PdfSharp. Los que no se
    /// puedan abrir se reemplazan por una pagina-nota para no perder el resto.</summary>
    private byte[] FusionarPdfs(IReadOnlyList<byte[]> partes)
    {
        if (partes.Count == 1)
        {
            // Un solo PDF valido: passthrough (sin recomprimir).
            if (EsPdfValido(partes[0])) { return partes[0]; }
        }

        using var output = new PdfDocument();
        foreach (var parte in partes)
        {
            try
            {
                using var ms = new MemoryStream(parte);
                using var input = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
                for (var i = 0; i < input.PageCount; i++) { output.AddPage(input.Pages[i]); }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Tipologia ZIP: PDF no fusionable, se reemplaza por nota.");
                try
                {
                    using var ms = new MemoryStream(PaginaNota("(documento no legible / no fusionable)"));
                    using var input = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
                    for (var i = 0; i < input.PageCount; i++) { output.AddPage(input.Pages[i]); }
                }
                catch { /* nada mas que hacer */ }
            }
        }
        using var outMs = new MemoryStream();
        output.Save(outMs);
        return outMs.ToArray();
    }

    private static bool EsPdfValido(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
            return doc.PageCount > 0;
        }
        catch { return false; }
    }

    private static byte[]? DecodeDataUrl(string dataUrl)
    {
        try
        {
            var idx = dataUrl.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            var b64 = idx >= 0 ? dataUrl[(idx + 7)..] : dataUrl;
            return Convert.FromBase64String(b64);
        }
        catch { return null; }
    }

    // ---- Nombre de archivo (mismos tokens que el resumen del tab) ----

    private static string ResolverNombre(string patron, InformeItemDto a, PacienteSnapshotDto p)
    {
        var hoy = DateTimeOffset.Now.ToLocalTime();
        var aut = p.Autorizaciones.Count > 0 ? p.Autorizaciones[0] : "";
        var nombre = (p.Nombre ?? "").Replace(' ', '_');
        var res = patron
            .Replace("{sigla}", a.Alias ?? "")
            .Replace("{cedula}", p.Documento ?? "")
            .Replace("{autorizacion}", aut)
            .Replace("{nombre}", nombre)
            .Replace("{consecutivo}", "")
            .Replace("{codigo_hc}", "")
            .Replace("{tipo_servicio}", "")
            .Replace("{mes}", hoy.ToString("MM"));
        res = Regex.Replace(res, @"\{fecha:([^}]+)\}", m =>
        {
            try { return hoy.ToString(m.Groups[1].Value); } catch { return ""; }
        });
        res = res.Replace("{fecha}", hoy.ToString("yyyyMMdd"));
        return res;
    }

    private static string SanitizarNombre(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) { sb.Append(invalid.Contains(ch) ? '_' : ch); }
        var r = sb.ToString().Trim();
        return string.IsNullOrEmpty(r) ? "archivo" : r;
    }
}
