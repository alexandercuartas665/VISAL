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
using Visal.Domain.Entities;
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
    private readonly IQuotePdfRenderer _pdfRenderer;
    private readonly IHcPrintTokenStore _printTokens;
    private readonly ITenantContext _tenant;
    private readonly Microsoft.AspNetCore.Hosting.Server.IServer _server;

    // Cuantas paginas rinde Puppeteer en paralelo (un solo navegador para todo el ZIP).
    // 4 equilibra velocidad y memoria del headless Chrome en el server.
    private const int MaxRenderConcurrency = 4;

    /// <summary>Una parte del PDF de un paciente: o bytes ya listos (autorizacion/firma,
    /// leidos de disco/BD) via <see cref="Inline"/>, o una referencia a la URL global
    /// <see cref="RenderIdx"/> que se renderiza en lote (formulario por tipo).</summary>
    private readonly record struct PartePlan(byte[]? Inline, int RenderIdx);

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
        ILogger<TipologiaZipService> log,
        IQuotePdfRenderer pdfRenderer,
        IHcPrintTokenStore printTokens,
        ITenantContext tenant,
        Microsoft.AspNetCore.Hosting.Server.IServer server)
    {
        _db = db;
        _snaps = snaps;
        _cuenta = cuenta;
        _env = env;
        _log = log;
        _pdfRenderer = pdfRenderer;
        _printTokens = printTokens;
        _tenant = tenant;
        _server = server;
    }

    public async Task<ArchivoExportado?> GenerarZipArchivoAsync(
        Guid snapshotId, Guid archivoItemId, IProgress<ZipProgreso>? progreso = null, CancellationToken ct = default)
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

        // --- Pase 1: plan por paciente + junta TODAS las URLs de formularios-por-tipo.
        // En vez de lanzar un Chrome por paciente y rendir en serie, juntamos todas las
        // URLs de todos los pacientes para rendirlas de una sola vez (un navegador, en
        // paralelo). Las partes de disco/BD (autorizacion, firma) se resuelven aqui. ---
        var allUrls = new List<string>();
        var planes = new List<(PacienteSnapshotDto pac, List<PartePlan> partes)>();
        foreach (var pac in pacientes)
        {
            ct.ThrowIfCancellationRequested();
            var partes = await ConstruirPlanPacienteAsync(archivo, pac, allUrls, ct);
            if (partes is null) { continue; } // paciente no existe en el sistema
            planes.Add((pac, partes));
        }

        // --- Render en lote: UN solo Chrome, hasta MaxRenderConcurrency paginas a la vez. ---
        IReadOnlyList<byte[]> rendered;
        if (allUrls.Count > 0)
        {
            progreso?.Report(new ZipProgreso("Renderizando formularios", 0, allUrls.Count));
            var onEach = progreso is null ? null
                : new Progress<int>(n => progreso.Report(new ZipProgreso("Renderizando formularios", n, allUrls.Count)));
            rendered = await _pdfRenderer.RenderUrlsToPdfAsync(allUrls, ".pack-doc", MaxRenderConcurrency, onEach, ct);
        }
        else
        {
            rendered = Array.Empty<byte[]>();
        }
        progreso?.Report(new ZipProgreso("Armando PDFs", allUrls.Count, allUrls.Count));

        using var zipMs = new MemoryStream();
        var incluidos = 0;
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // --- Pase 2: ensambla cada PDF con las partes ya resueltas/renderizadas. ---
            foreach (var (pac, partes) in planes)
            {
                ct.ThrowIfCancellationRequested();
                var bytesPartes = new List<byte[]>(partes.Count);
                foreach (var p in partes)
                {
                    if (p.Inline is { Length: > 0 }) { bytesPartes.Add(p.Inline); }
                    else if (p.RenderIdx >= 0 && p.RenderIdx < rendered.Count
                             && rendered[p.RenderIdx] is { Length: > 0 } b) { bytesPartes.Add(b); }
                }
                // Solo se incluye el paciente si REALMENTE quedo contenido (firma,
                // autorizacion o formularios renderizados). Los vacios se omiten.
                if (bytesPartes.Count == 0) { continue; }

                var pdf = FusionarPdfs(bytesPartes);
                var baseName = SanitizarNombre(ResolverNombre(patron, archivo, pac));
                var name = baseName + ".pdf";
                // Evita colisiones de nombre (dos pacientes con mismo patron).
                var n = 2;
                while (!usados.Add(name)) { name = $"{baseName}_{n++}.pdf"; }

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(pdf, ct);
                incluidos++;
            }

            // Si ningun paciente tenia contenido, dejamos una unica nota resumen para
            // que el ZIP no baje totalmente vacio y quede claro el porque.
            if (incluidos == 0)
            {
                var nota = PaginaNota(
                    $"Archivo: {(string.IsNullOrWhiteSpace(archivo.Descripcion) ? archivo.Alias : archivo.Descripcion)}\n\n" +
                    $"Ninguno de los {pacientes.Count} paciente(s) de este snapshot tiene contenido " +
                    "disponible para este archivo, por lo que no se genero ningun PDF.");
                var entry = zip.CreateEntry("_SIN_CONTENIDO.pdf", CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(nota, ct);
            }
        }

        var nombreZip = SanitizarNombre($"{archivo.Alias}_{detalle.Metadata.Nombre}") + ".zip";
        return new ArchivoExportado(zipMs.ToArray(), "application/zip", nombreZip);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> ContarConContenidoAsync(
        Guid snapshotId, CancellationToken ct = default)
    {
        var vacio = new Dictionary<Guid, int>();
        var detalle = await _snaps.ObtenerAsync(snapshotId, ct);
        if (detalle is null || detalle.Metadata.AseguradoraId is not Guid aseguradoraId) { return vacio; }

        var archivos = await _cuenta.ListarItemsAsync(aseguradoraId, ct);
        if (archivos.Count == 0) { return vacio; }

        var pacientes = await _snaps.ListarPacientesSnapshotAsync(snapshotId, ct);
        var docs = pacientes.Select(p => p.Documento).Distinct().ToList();

        // doc -> pacienteId (los del snapshot que existen en el sistema).
        var pacs = await _db.Pacientes.AsNoTracking()
            .Where(p => docs.Contains(p.NumeroDocumento))
            .Select(p => new { p.Id, p.NumeroDocumento })
            .ToListAsync(ct);
        var docToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pacs) { docToId[p.NumeroDocumento] = p.Id; }
        var idSet = docToId.Values.ToHashSet();

        // Precarga: pacientes (del snapshot) con autorizacion y con firma.
        // Autorizacion: el ZIP lee el PDF fisico de wwwroot, asi que aqui contamos solo
        // los que TIENEN el archivo en disco — de lo contrario el conteo diria "18" pero
        // el ZIP saldria vacio (p. ej. en local, donde no estan los archivos de prod).
        var conAut = new HashSet<Guid>();
        if (idSet.Count > 0)
        {
            var autRows = await _db.Asignaciones.AsNoTracking()
                .Where(a => idSet.Contains(a.PacienteId) && a.PdfAutorizacionUrl != null)
                .Select(a => new { a.PacienteId, a.PdfAutorizacionUrl })
                .ToListAsync(ct);
            foreach (var r in autRows)
            {
                if (!conAut.Contains(r.PacienteId) && ArchivoExisteEnDisco(r.PdfAutorizacionUrl!))
                {
                    conAut.Add(r.PacienteId);
                }
            }
        }
        // Firma: data URL en BD (dos fuentes: notas y solicitudes de firma remota).
        var conFirma = new HashSet<Guid>();
        if (idSet.Count > 0)
        {
            foreach (var pid in await _db.NotasMedicas.AsNoTracking()
                .Where(n => idSet.Contains(n.PacienteId) && n.FirmaPacienteDataUrl != null)
                .Select(n => n.PacienteId).Distinct().ToListAsync(ct)) { conFirma.Add(pid); }
            foreach (var pid in await _db.FirmaPacienteRequests.AsNoTracking()
                .Where(r => idSet.Contains(r.PacienteId) && r.ImageDataUrl != null)
                .Select(r => r.PacienteId).Distinct().ToListAsync(ct)) { conFirma.Add(pid); }
        }

        // Formularios por tipo: para los tipos referenciados por algun archivo, que
        // pacientes del snapshot tienen al menos una HC Cerrada de ese tipo. Lookup
        // tipo -> set de pacienteIds. (No toca las ramas de autorizacion/firma.)
        var tiposReferenciados = archivos
            .SelectMany(a => a.Contenidos)
            .Where(c => c.Origen == OrigenInformeItem.FormularioPorTipo && !string.IsNullOrWhiteSpace(c.FormularioTipo))
            .Select(c => c.FormularioTipo!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var conFormTipo = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        if (idSet.Count > 0 && tiposReferenciados.Count > 0)
        {
            var filas = await _db.HistoriasClinicas.AsNoTracking()
                .Where(h => idSet.Contains(h.PacienteId) && h.Estado == HistoriaClinicaEstado.Cerrada)
                .Join(_db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id,
                      (h, f) => new { h.PacienteId, f.Tipo })
                .Where(x => x.Tipo != null && tiposReferenciados.Contains(x.Tipo))
                .Distinct()
                .ToListAsync(ct);
            foreach (var x in filas)
            {
                if (!conFormTipo.TryGetValue(x.Tipo!, out var set)) { conFormTipo[x.Tipo!] = set = new(); }
                set.Add(x.PacienteId);
            }
        }

        var res = new Dictionary<Guid, int>();
        foreach (var a in archivos)
        {
            var n = 0;
            foreach (var pac in pacientes)
            {
                if (!docToId.TryGetValue(pac.Documento, out var pid)) { continue; }
                // El paciente cuenta si ALGUN contenido del archivo tiene material para el.
                var tiene = a.Contenidos.Any(c =>
                    (c.Origen == OrigenInformeItem.AutorizacionAsignacion && conAut.Contains(pid)) ||
                    (c.Origen == OrigenInformeItem.FirmaPaciente && conFirma.Contains(pid)) ||
                    (c.Origen == OrigenInformeItem.FormularioPorTipo
                        && c.FormularioTipo is string ft
                        && conFormTipo.TryGetValue(ft, out var set) && set.Contains(pid)));
                if (tiene) { n++; }
            }
            res[a.Id] = n;
        }
        return res;
    }

    /// <summary>
    /// Arma el PDF de un archivo para un paciente: recorre los contenidos, recupera
    /// el contenido real de los origenes soportados y fusiona todo en un solo PDF.
    /// </summary>
    /// <summary>Pase 1: arma el "plan" ordenado de partes de un paciente. Las partes de
    /// disco/BD (autorizacion, firma) se resuelven aqui como bytes; las de formulario-por-tipo
    /// se agregan como URLs al lote global <paramref name="allUrls"/> y se referencian por
    /// indice, para rendirse todas juntas luego (un solo navegador, en paralelo).
    /// Devuelve null si el paciente no existe en el sistema.</summary>
    private async Task<List<PartePlan>?> ConstruirPlanPacienteAsync(
        InformeItemDto archivo, PacienteSnapshotDto pac, List<string> allUrls, CancellationToken ct)
    {
        var pid = await _db.Pacientes.AsNoTracking()
            .Where(p => p.NumeroDocumento == pac.Documento)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (pid is not Guid pacienteId) { return null; }

        var partes = new List<PartePlan>();
        foreach (var c in archivo.Contenidos)
        {
            ct.ThrowIfCancellationRequested();
            switch (c.Origen)
            {
                case OrigenInformeItem.AutorizacionAsignacion:
                    foreach (var b in await CargarAutorizacionesAsync(pacienteId, c.SoloUltimo, ct))
                    {
                        partes.Add(new PartePlan(b, -1));
                    }
                    break;
                case OrigenInformeItem.FirmaPaciente:
                    foreach (var b in await CargarFirmasAsync(pacienteId, c.SoloUltimo, ct))
                    {
                        partes.Add(new PartePlan(b, -1));
                    }
                    break;
                case OrigenInformeItem.FormularioPorTipo:
                    foreach (var url in await ResolverUrlsFormularioPorTipoAsync(pacienteId, c.FormularioTipo, c.SoloUltimo, ct))
                    {
                        partes.Add(new PartePlan(null, allUrls.Count));
                        allUrls.Add(url);
                    }
                    break;
                default:
                    // Otros origenes de formularios de HC (DocumentoHc, Evolucion, Escala,
                    // etc.): pendientes de una ola posterior. No producen contenido.
                    break;
            }
        }
        return partes;
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
        // La firma del paciente puede estar en dos lugares:
        //  1) NotaMedica.FirmaPacienteDataUrl (cuando la firma se pidio ligada a una nota).
        //  2) FirmaPacienteRequest.ImageDataUrl (firmas "libres" pedidas desde HC/paciente/
        //     WhatsApp; NO actualizan la nota). La mayoria de firmas del paciente viven aqui.
        // Unimos ambas fuentes, ordenadas por fecha, y aplicamos SoloUltimo sobre el total.
        var deNotas = await _db.NotasMedicas.AsNoTracking()
            .Where(n => n.PacienteId == pacienteId && n.FirmaPacienteDataUrl != null)
            .Select(n => new { Fecha = n.CreatedAt, Url = n.FirmaPacienteDataUrl! })
            .ToListAsync(ct);
        var deRequests = await _db.FirmaPacienteRequests.AsNoTracking()
            .Where(r => r.PacienteId == pacienteId && r.ImageDataUrl != null)
            .Select(r => new { Fecha = r.CompletedAt ?? r.CreatedAt, Url = r.ImageDataUrl! })
            .ToListAsync(ct);

        var todas = deNotas.Concat(deRequests).OrderByDescending(x => x.Fecha).ToList();
        if (todas.Count == 0) { return new List<byte[]>(); }
        var seleccion = soloUltimo ? todas.Take(1) : todas.AsEnumerable().Reverse();

        var res = new List<byte[]>();
        foreach (var f in seleccion)
        {
            var img = DecodeDataUrl(f.Url);
            if (img is not null) { res.Add(PaginaImagen(img, "Firma del paciente")); }
        }
        return res;
    }

    /// <summary>Origen "Formulario por tipo": devuelve las URLs publicas (con token de un
    /// solo uso) de cada HC Cerrada del paciente cuyo FormDefinition.Tipo =
    /// <paramref name="tipo"/>. No renderiza aqui: las URLs se juntan con las de los demas
    /// pacientes y se rinden en lote (un solo navegador, en paralelo). SoloUltimo = solo la
    /// HC mas reciente (por fecha de atencion).</summary>
    private async Task<List<string>> ResolverUrlsFormularioPorTipoAsync(
        Guid pacienteId, string? tipo, bool soloUltimo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tipo) || _tenant.TenantId is not Guid tid) { return new(); }

        var q = _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId && h.Estado == HistoriaClinicaEstado.Cerrada)
            .Join(_db.FormDefinitions.AsNoTracking(),
                  h => h.FormDefinitionId, f => f.Id,
                  (h, f) => new { h.Id, h.FechaCierre, h.FechaAtencion, h.CreatedAt, f.Tipo })
            .Where(x => x.Tipo == tipo);

        var hcIds = soloUltimo
            ? await q.OrderByDescending(x => x.FechaAtencion ?? x.FechaCierre ?? x.CreatedAt)
                     .Take(1).Select(x => x.Id).ToListAsync(ct)
            : await q.OrderBy(x => x.FechaAtencion ?? x.FechaCierre ?? x.CreatedAt)
                     .Select(x => x.Id).ToListAsync(ct);

        if (hcIds.Count == 0) { return new(); }
        var baseUrl = BaseUrlInterna();
        return hcIds.Select(hcId => $"{baseUrl}/p/hc-form/{hcId}?t={_printTokens.Mint(hcId, tid)}").ToList();
    }

    /// <summary>URL base interna del propio servidor para que Puppeteer navegue a las
    /// rutas publicas de render. Sale de las direcciones en que escucha el servidor
    /// (normalizando 0.0.0.0/+/[::] a localhost); fallback al puerto interno 8080.</summary>
    private string BaseUrlInterna()
    {
        var feat = _server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var addr = feat?.Addresses?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(addr))
        {
            return addr.Replace("://+", "://localhost")
                       .Replace("://0.0.0.0", "://localhost")
                       .Replace("://[::]", "://localhost")
                       .TrimEnd('/');
        }
        return "http://localhost:8080";
    }

    // ---- Recuperacion de archivos fisicos (wwwroot) ----

    /// <summary>Lee un archivo servido desde wwwroot (ruta relativa como
    /// "/uploads/...") y lo devuelve como PDF: si ya es PDF lo pasa tal cual; si es
    /// imagen la envuelve en un PDF. Devuelve null si no existe o no se pudo leer.</summary>
    /// <summary>True si el archivo servido desde wwwroot (ruta relativa) existe en disco.
    /// Mismo calculo de ruta que <see cref="LeerArchivoComoPdf"/>, para que el conteo
    /// cuadre con lo que el ZIP realmente puede incluir.</summary>
    private bool ArchivoExisteEnDisco(string urlRelativa)
    {
        try
        {
            var rel = urlRelativa.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.Combine(_env.WebRootPath ?? "", rel);
            return File.Exists(full);
        }
        catch { return false; }
    }

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
