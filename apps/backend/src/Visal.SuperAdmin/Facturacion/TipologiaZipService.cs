using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Visal.Application.Common;
using Visal.Application.Facturacion;
using Visal.Application.Tenancy;
using Visal.Domain.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Pdf;

namespace Visal.SuperAdmin.Facturacion;

/// <inheritdoc />
// Ola 4c (P0): contexto corto por operacion (using var db = _dbFactory.Create();) en vez del IApplicationDbContext scoped.
public sealed class TipologiaZipService : ITipologiaZipService
{
    private readonly IApplicationDbContextFactory _dbFactory;
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

    // v0.153.0: el flag del archivo RotuloSeparadorCup sigue ORGANIZANDO el expediente
    // por CUP (agrupa + ordena; ver ResolverUrlsFormularioPorTipoAsync), pero ya NO se
    // genera la pagina separadora (rotulo) entre CUPs: el usuario no quiere verla.
    // Para reactivar la pagina separadora, poner esta bandera en true (un solo punto).
    private static readonly bool EmitirPaginaRotuloCup = false;

    /// <summary>Una parte del PDF de un paciente: o bytes ya listos (autorizacion/firma,
    /// leidos de disco/BD) via <see cref="Inline"/>, o una referencia a la URL global
    /// <see cref="RenderIdx"/> que se renderiza en lote (formulario por tipo).
    /// <see cref="Cup"/> (CodigoRips), <see cref="CupNombre"/> y <see cref="Sede"/> solo se
    /// llenan en las partes de historia cuando el archivo pide rotulo separador por CUP;
    /// null = la parte no tiene CUP (autorizacion/firma/anexo) y no dispara separador.</summary>
    private readonly record struct PartePlan(
        byte[]? Inline, int RenderIdx, string? Cup = null, string? CupNombre = null, string? Sede = null,
        string? Etiqueta = null, Guid HcId = default);

    /// <summary>URL de un documento de historia a renderizar + su CUP, nombre de servicio y
    /// sede (para el rotulo separador por CUP). Cup null = no se pidio rotulo. <see cref="Fecha"/>
    /// (fecha de atencion) permite ordenar JUNTAS las HC de varios contenidos "Formulario por
    /// tipo" por CUP->fecha, para que el archivo siga el mismo orden plano del snapshot.</summary>
    private readonly record struct UrlCup(string Url, string? Cup, string? CupNombre, string? Sede, DateTimeOffset Fecha, long Consecutivo = 0, Guid HcId = default);

    /// <summary>Fila de HC para ordenar el contenido "Formulario por tipo": id + fecha
    /// de atencion (para orden cronologico) — el CUP se resuelve aparte por lote.</summary>
    private readonly record struct FilaHcOrden(Guid Id, DateTimeOffset Fecha, long Consecutivo = 0);

    /// <summary>Clave de orden para CUPS: primero los que empiezan por digito (orden numerico
    /// ascendente), luego los que empiezan por letra (alfabetico + su parte numerica); vacio
    /// al final. Asi "9" va antes que "10" y "S123" queda despues de todos los numericos.</summary>
    private static (int Grupo, string Alfa, decimal Num) CupSortKey(string? cup)
    {
        var s = (cup ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0) { return (2, "", 0m); }
        if (char.IsDigit(s[0]))
        {
            var digs = new string(s.TakeWhile(char.IsDigit).ToArray());
            decimal.TryParse(digs, out var val);
            return (0, "", val);
        }
        var letras = new string(s.TakeWhile(char.IsLetter).ToArray());
        var resto = s[letras.Length..];
        var digs2 = new string(resto.Where(char.IsDigit).ToArray());
        decimal.TryParse(digs2, out var val2);
        return (1, letras, val2);
    }

    static TipologiaZipService()
    {
        // QuestPDF Community (gratis para este uso). Idempotente.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public TipologiaZipService(
        IApplicationDbContextFactory dbFactory,
        IFacturacionSnapshotService snaps,
        ICuentaMedicaConfigService cuenta,
        IWebHostEnvironment env,
        ILogger<TipologiaZipService> log,
        IQuotePdfRenderer pdfRenderer,
        IHcPrintTokenStore printTokens,
        ITenantContext tenant,
        Microsoft.AspNetCore.Hosting.Server.IServer server)
    {
        _dbFactory = dbFactory;
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
        using var db = _dbFactory.Create();
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

        // NIT del IPS (tenant) para el token {nit} del nombre de archivo. Uno por IPS.
        // Se expone SOLO con digitos: NIT + digito de verificacion PEGADO, sin guion ni
        // puntos (p. ej. "900123456-7" -> "9001234567"), que es como lo piden las cuentas.
        var nitIps = (_tenant.TenantId is Guid tnit
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == tnit).Select(t => t.TaxId).FirstOrDefaultAsync(ct)
            : null) ?? "";
        nitIps = new string(nitIps.Where(char.IsDigit).ToArray());

        // Logo del tenant para el rotulo separador por CUP (solo se lee si el archivo lo
        // pide Y la pagina separadora esta habilitada; hoy esta deshabilitada, ver arriba).
        var logoRotulo = (EmitirPaginaRotuloCup && archivo.RotuloSeparadorCup)
            ? await ObtenerLogoTenantBytesAsync(db, ct) : null;

        var pacientes = await _snaps.ListarPacientesSnapshotAsync(snapshotId, ct);

        // --- MODO "un archivo por autorizacion": un PDF por CADA autorizacion del
        // paciente (no uno fusionado por paciente), nombrado con su numero de autorizacion.
        // Pensado para archivos de solo autorizaciones; ignora otros contenidos. ---
        if (archivo.UnoPorAutorizacion)
        {
            return await GenerarZipPorAutorizacionAsync(db, archivo, cfg, detalle, pacientes, patron, nitIps, logoRotulo, progreso, ct);
        }

        // --- Pase 1: plan por paciente + junta TODAS las URLs de formularios-por-tipo.
        // En vez de lanzar un Chrome por paciente y rendir en serie, juntamos todas las
        // URLs de todos los pacientes para rendirlas de una sola vez (un navegador, en
        // paralelo). Las partes de disco/BD (autorizacion, firma) se resuelven aqui. ---
        // Ventana del snapshot: acota los formularios-por-tipo y sus sub-documentos a su
        // periodo (fecha de atencion), para no colar HC/evoluciones de otro mes (v0.137.0).
        var (pDesde, pHasta) = PeriodoDe(detalle.FiltrosJson);

        var allUrls = new List<string>();
        var planes = new List<(PacienteSnapshotDto pac, List<PartePlan> partes)>();
        foreach (var pac in pacientes)
        {
            ct.ThrowIfCancellationRequested();
            var partes = await ConstruirPlanPacienteAsync(db, archivo, pac, allUrls, pDesde, pHasta, ct);
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

        // --- Pase 2: ensambla el PDF de CADA paciente con sus partes resueltas/renderizadas. ---
        var salidas = new List<(string Name, byte[] Pdf)>();
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pac, partes) in planes)
            {
                ct.ThrowIfCancellationRequested();
                var bytesPartes = EnsamblarPartes(partes, rendered, EmitirPaginaRotuloCup && archivo.RotuloSeparadorCup, logoRotulo);
                // Solo se incluye el paciente si REALMENTE quedo contenido (firma,
                // autorizacion o formularios renderizados). Los vacios se omiten.
                if (bytesPartes.Count == 0) { continue; }

                var pdf = FusionarPdfs(bytesPartes);
                var baseName = SanitizarNombre(ResolverNombre(patron, archivo, pac, nitIps));
                var name = baseName + ".pdf";
                // Evita colisiones de nombre (dos pacientes con mismo patron).
                var n = 2;
                while (!usados.Add(name)) { name = $"{baseName}_{n++}.pdf"; }
                salidas.Add((name, pdf));
            }
        }

        var notaSinContenido =
            $"Archivo: {(string.IsNullOrWhiteSpace(archivo.Descripcion) ? archivo.Alias : archivo.Descripcion)}\n\n" +
            $"Ninguno de los {pacientes.Count} paciente(s) de este snapshot tiene contenido " +
            "disponible para este archivo.";

        // --- PDF UNICO: todos los pacientes concatenados en un solo PDF (config del archivo). ---
        if (archivo.PdfUnico)
        {
            var unico = salidas.Count == 0
                ? PaginaNota(notaSinContenido)
                : FusionarPdfs(salidas.Select(s => s.Pdf).ToList());
            var nombrePdf = SanitizarNombre($"{archivo.Alias}_{detalle.Metadata.Nombre}") + ".pdf";
            return new ArchivoExportado(unico, "application/pdf", nombrePdf);
        }

        // --- ZIP: un PDF por paciente (comportamiento historico). ---
        using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, pdf) in salidas)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(pdf, ct);
            }
            // Si ningun paciente tenia contenido, dejamos una unica nota resumen para
            // que el ZIP no baje totalmente vacio y quede claro el porque.
            if (salidas.Count == 0)
            {
                var entry = zip.CreateEntry("_SIN_CONTENIDO.pdf", CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(PaginaNota(notaSinContenido), ct);
            }
        }

        var nombreZip = SanitizarNombre($"{archivo.Alias}_{detalle.Metadata.Nombre}") + ".zip";
        return new ArchivoExportado(zipMs.ToArray(), "application/zip", nombreZip);
    }

    /// <summary>Genera el ZIP del modo "un archivo por autorizacion": por cada paciente y
    /// cada autorizacion del snapshot, un PDF independiente nombrado con el numero de
    /// autorizacion. El PDF trae el CONTENIDO CONFIGURADO del archivo (firmas, historias
    /// por tipo, adjuntos, la propia autorizacion...) pero filtrado a lo que pertenece a esa
    /// autorizacion (via el mapa HC-&gt;autorizacion). Si el patron no incluye
    /// <c>{autorizacion}</c>, el codigo se anexa al nombre para evitar colisiones.
    ///
    /// Nota: el contenido se atribuye a la autorizacion por la HC de la que cuelga (cadena
    /// sesion-&gt;turno-&gt;asignacion-&gt;codigo). Los adjuntos y firmas "libres" (sin HC) no se
    /// pueden atribuir a una autorizacion y no entran en este modo.</summary>
    private async Task<ArchivoExportado?> GenerarZipPorAutorizacionAsync(
        IApplicationDbContext db, InformeItemDto archivo, CuentaMedicaConfigDto cfg,
        FacturacionSnapshotDetalleDto detalle, IReadOnlyList<PacienteSnapshotDto> pacientes,
        string patron, string nitIps, byte[]? logoRotulo, IProgress<ZipProgreso>? progreso, CancellationToken ct)
    {
        _ = cfg;
        var incluyeTokenAut = patron.Contains("{autorizacion}", StringComparison.OrdinalIgnoreCase);
        var docsRender = CalcularDocsRender(archivo);
        var (pDesde, pHasta) = PeriodoDe(detalle.FiltrosJson);

        // doc -> pacienteId (los del snapshot que existen en el sistema).
        var docs = pacientes.Select(p => p.Documento).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => docs.Contains(p.NumeroDocumento))
            .Select(p => new { p.Id, p.NumeroDocumento })
            .ToListAsync(ct);
        var docToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pacs) { docToId[p.NumeroDocumento] = p.Id; }

        // Mapa HC -> autorizacion (una sola consulta para todos los pacientes del snapshot).
        var hcAut = await ConstruirMapaHcAutorizacionAsync(db, docToId.Values.ToHashSet(), ct);

        // --- Pase 1: una "unidad" por (paciente, autorizacion). Junta todas las URLs de
        // formulario-por-tipo para rendirlas de una sola vez (un navegador, en paralelo). ---
        var allUrls = new List<string>();
        var unidades = new List<(PacienteSnapshotDto Pac, string Codigo, List<PartePlan> Partes)>();
        var i = 0;
        foreach (var pac in pacientes)
        {
            ct.ThrowIfCancellationRequested();
            progreso?.Report(new ZipProgreso("Preparando PDFs", i++, pacientes.Count));
            if (!docToId.TryGetValue(pac.Documento, out var pacienteId)) { continue; }

            var codigos = pac.Autorizaciones
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var codigo in codigos)
            {
                var partes = await ConstruirPlanAutorizacionAsync(db, archivo, pacienteId, codigo, hcAut, docsRender, allUrls, pDesde, pHasta, ct);
                if (partes.Count == 0) { continue; }
                unidades.Add((pac, codigo, partes));
            }
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

        // --- Pase 2: ensambla el PDF de cada (paciente, autorizacion). ---
        var salidas = new List<(string Name, byte[] Pdf)>();
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pac, codigo, partes) in unidades)
        {
            ct.ThrowIfCancellationRequested();
            var bytesPartes = EnsamblarPartes(partes, rendered, EmitirPaginaRotuloCup && archivo.RotuloSeparadorCup, logoRotulo);
            if (bytesPartes.Count == 0) { continue; }

            var pdf = FusionarPdfs(bytesPartes);
            var baseName = SanitizarNombre(ResolverNombre(patron, archivo, pac, nitIps, codigo));
            // Si el patron no trae el token de autorizacion, anexamos el codigo para
            // que dos autorizaciones del mismo paciente no colisionen de nombre.
            if (!incluyeTokenAut)
            {
                baseName = SanitizarNombre(baseName + "_" + codigo);
            }
            var name = baseName + ".pdf";
            var n = 2;
            while (!usados.Add(name)) { name = $"{baseName}_{n++}.pdf"; }
            salidas.Add((name, pdf));
        }

        var notaSinContenido =
            $"Archivo: {(string.IsNullOrWhiteSpace(archivo.Descripcion) ? archivo.Alias : archivo.Descripcion)}\n\n" +
            $"Ninguno de los {pacientes.Count} paciente(s) de este snapshot tiene contenido " +
            "atribuible a una autorizacion para este archivo.";

        // --- PDF UNICO: todas las autorizaciones concatenadas en un solo PDF. ---
        if (archivo.PdfUnico)
        {
            var unico = salidas.Count == 0
                ? PaginaNota(notaSinContenido)
                : FusionarPdfs(salidas.Select(s => s.Pdf).ToList());
            var nombrePdf = SanitizarNombre($"{archivo.Alias}_{detalle.Metadata.Nombre}") + ".pdf";
            return new ArchivoExportado(unico, "application/pdf", nombrePdf);
        }

        using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, pdf) in salidas)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(pdf, ct);
            }
            if (salidas.Count == 0)
            {
                var entry = zip.CreateEntry("_SIN_CONTENIDO.pdf", CompressionLevel.Optimal);
                await using var es = entry.Open();
                await es.WriteAsync(PaginaNota(notaSinContenido), ct);
            }
        }

        var nombreZip = SanitizarNombre($"{archivo.Alias}_{detalle.Metadata.Nombre}") + ".zip";
        return new ArchivoExportado(zipMs.ToArray(), "application/zip", nombreZip);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> ContarConContenidoAsync(
        Guid snapshotId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var lookup = await ConstruirLookupAsync(db, snapshotId, ct);
        if (lookup is null) { return new Dictionary<Guid, int>(); }

        var res = new Dictionary<Guid, int>();
        foreach (var a in lookup.Archivos)
        {
            var n = 0;
            foreach (var pac in lookup.Pacientes)
            {
                if (!lookup.DocToId.TryGetValue(pac.Documento, out var pid)) { continue; }
                if (lookup.Tiene(a, pid)) { n++; }
            }
            res[a.Id] = n;
        }
        return res;
    }

    /// <inheritdoc />
    public async Task<ArchivoExportado?> GenerarExcelFaltantesAsync(
        Guid snapshotId, Guid archivoItemId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var lookup = await ConstruirLookupAsync(db, snapshotId, ct);
        if (lookup is null) { return null; }

        var archivo = lookup.Archivos.FirstOrDefault(a => a.Id == archivoItemId);
        if (archivo is null) { return null; }

        // Pacientes del snapshot para los que ESTE archivo NO se generaria: o no existen en
        // el sistema (no hay con que generar), o existen pero ningun contenido del archivo
        // tiene material para ellos. Misma regla exacta que el contador de la tarjeta, asi
        // "faltantes + con contenido = total del snapshot".
        var faltantes = new List<(PacienteSnapshotDto Pac, string Motivo)>();
        foreach (var pac in lookup.Pacientes)
        {
            if (!lookup.DocToId.TryGetValue(pac.Documento, out var pid))
            {
                faltantes.Add((pac, "Paciente no existe en el sistema"));
            }
            else if (!lookup.Tiene(archivo, pid))
            {
                faltantes.Add((pac, "Sin contenido"));
            }
        }

        var nombreArchivo = string.IsNullOrWhiteSpace(archivo.Descripcion) ? archivo.Alias : archivo.Descripcion!;

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sin archivo");

        // Encabezado contextual (snapshot + archivo).
        ws.Cell(1, 1).Value = "Pacientes sin el archivo";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(2, 1).Value = $"Archivo: {nombreArchivo} ({archivo.Alias})";
        ws.Cell(3, 1).Value = $"Snapshot: {lookup.SnapshotNombre}";
        ws.Cell(4, 1).Value = $"Faltantes: {faltantes.Count} de {lookup.Pacientes.Count} paciente(s) del snapshot";
        ws.Cell(5, 1).Value = $"Generado: {DateTimeOffset.Now.ToLocalTime():yyyy-MM-dd HH:mm}";

        const int hdr = 7;
        string[] cols = { "#", "Paciente", "Tipo doc", "Documento", "Servicios", "Autorizaciones", "Sede", "Motivo" };
        for (var i = 0; i < cols.Length; i++)
        {
            var cell = ws.Cell(hdr, i + 1);
            cell.Value = cols[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var row = hdr + 1;
        var n = 0;
        foreach (var (pac, motivo) in faltantes)
        {
            n++;
            ws.Cell(row, 1).Value = n;
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(pac.Nombre) ? "-" : pac.Nombre;
            ws.Cell(row, 3).Value = pac.TipoDocumento ?? "";
            ws.Cell(row, 4).Value = pac.Documento ?? "";
            ws.Cell(row, 4).Style.NumberFormat.Format = "@"; // documento como texto (no notacion cientifica)
            ws.Cell(row, 5).Value = pac.Servicios;
            ws.Cell(row, 6).Value = string.Join(", ", pac.Autorizaciones.Where(x => !string.IsNullOrWhiteSpace(x)));
            ws.Cell(row, 7).Value = pac.CodigoHabilitacionSede ?? "";
            ws.Cell(row, 8).Value = motivo;
            row++;
        }

        if (faltantes.Count == 0)
        {
            ws.Cell(hdr + 1, 1).Value = "Todos los pacientes del snapshot generaron este archivo.";
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var nombre = SanitizarNombre($"SIN_{archivo.Alias}_{lookup.SnapshotNombre}") + ".xlsx";
        return new ArchivoExportado(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombre);
    }

    /// <summary>Una fila del manifiesto de simulacion: un archivo (PDF) que se generaria, con
    /// su composicion. <see cref="Partes"/> son las etiquetas de cada parte EN EL ORDEN REAL en
    /// que el motor las encadenaria (HC-xxxxxx para historias, [AUT]/[FIRMA]/[ANEXO] para el
    /// resto), incluidas las repeticiones: asi el TXT delata la historia que sale dos veces.</summary>
    private sealed record SimFila(
        string Nombre, string Paciente, string TipoDoc, string Documento, string Autorizacion,
        IReadOnlyList<SimParte> Partes, string Estado, IReadOnlyList<string>? Avisos = null);

    /// <summary>Una parte del PDF en el TXT de simulacion: su etiqueta (HC-xxxxxx / [AUT] / ...)
    /// y, cuando el archivo pide "Ordenar por CUP", el CUP (CodigoRips) y el nombre de servicio
    /// de esa historia -&gt; el TXT inserta una cabecera "CUP {codigo} - {servicio}" al cambiar de
    /// CUP. Cup null = la parte no tiene CUP (autorizacion/firma/anexo) o el archivo no ordena
    /// por CUP, y no dispara cabecera.</summary>
    private sealed record SimParte(string Label, string? Cup = null, string? CupNombre = null);

    /// <inheritdoc />
    public async Task<ArchivoExportado?> SimularGeneracionAsync(
        Guid snapshotId, Guid archivoItemId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var detalle = await _snaps.ObtenerAsync(snapshotId, ct);
        if (detalle is null || detalle.Metadata.AseguradoraId is not Guid aseguradoraId) { return null; }

        var archivos = await _cuenta.ListarItemsAsync(aseguradoraId, ct);
        var archivo = archivos.FirstOrDefault(a => a.Id == archivoItemId);
        if (archivo is null) { return null; }
        var cfg = await _cuenta.GetOrCreateAsync(aseguradoraId, ct);
        _ = cfg;
        var patron = !string.IsNullOrWhiteSpace(archivo.PatronNombre) ? archivo.PatronNombre!
            : !string.IsNullOrWhiteSpace(cfg.PatronNombreDefault) ? cfg.PatronNombreDefault!
            : "{sigla}_{cedula}";

        var nitIps = (_tenant.TenantId is Guid tnit
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == tnit).Select(t => t.TaxId).FirstOrDefaultAsync(ct)
            : null) ?? "";
        nitIps = new string(nitIps.Where(char.IsDigit).ToArray());

        var pacientes = await _snaps.ListarPacientesSnapshotAsync(snapshotId, ct);
        var (pDesde, pHasta) = PeriodoDe(detalle.FiltrosJson);

        // doc -> pacienteId (los del snapshot que existen en el sistema).
        var docs = pacientes.Select(p => p.Documento).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => docs.Contains(p.NumeroDocumento))
            .Select(p => new { p.Id, p.NumeroDocumento })
            .ToListAsync(ct);
        var docToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pacs) { docToId[p.NumeroDocumento] = p.Id; }

        var filas = new List<SimFila>();
        var allUrls = new List<string>();            // acumulador requerido por la planeacion (no se rinde)
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // v0.164.0: el simulador modela TAMBIEN lo que el motor ANIDA al renderizar cada HC
        // base (una base de terapia mete sus evoluciones DENTRO de su hoja), no solo las
        // partes de primer nivel del plan. El render anida evoluciones solo si el 'docs' de
        // /p/hc-form incluye EVO (ver PaqueteHcImprimible L83, gateado por _incluirEvo). Las
        // tipologias usan docs=HC (CalcularDocsRender) -> hoy NO anidan. ExpandirYDiagnosticar
        // refleja ese anidado 1:1 con el PDF y, aparte, AVISA de "doble fuente" (una base de
        // terapia + sus evoluciones tambien sueltas) que se duplicaria si el render anidara,
        // para cazar el patron aunque cambie el gate.
        var incluyeEvo = CalcularDocsRender(archivo).Contains("EVO", StringComparison.OrdinalIgnoreCase);

        if (archivo.UnoPorAutorizacion)
        {
            var incluyeTokenAut = patron.Contains("{autorizacion}", StringComparison.OrdinalIgnoreCase);
            var docsRender = CalcularDocsRender(archivo);
            var hcAut = await ConstruirMapaHcAutorizacionAsync(db, docToId.Values.ToHashSet(), ct);

            foreach (var pac in pacientes)
            {
                ct.ThrowIfCancellationRequested();
                if (!docToId.TryGetValue(pac.Documento, out var pacienteId))
                {
                    filas.Add(new SimFila("(no se genera)", pac.Nombre ?? "", pac.TipoDocumento ?? "",
                        pac.Documento ?? "", "", Array.Empty<SimParte>(), "Paciente no existe en el sistema"));
                    continue;
                }

                var codigos = pac.Autorizaciones
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var codigo in codigos)
                {
                    var partes = await ConstruirPlanAutorizacionAsync(
                        db, archivo, pacienteId, codigo, hcAut, docsRender, allUrls, pDesde, pHasta, ct);

                    if (partes.Count == 0)
                    {
                        // El generador real omite las unidades vacias (no produce archivo).
                        filas.Add(new SimFila("(no se genera)", pac.Nombre ?? "", pac.TipoDocumento ?? "",
                            pac.Documento ?? "", codigo, Array.Empty<SimParte>(), "Vacio: sin contenido para esta autorizacion"));
                        continue;
                    }

                    var baseName = SanitizarNombre(ResolverNombre(patron, archivo, pac, nitIps, codigo));
                    if (!incluyeTokenAut) { baseName = SanitizarNombre(baseName + "_" + codigo); }
                    var name = baseName + ".pdf";
                    var n = 2;
                    while (!usados.Add(name)) { name = $"{baseName}_{n++}.pdf"; }

                    var exp = await ExpandirYDiagnosticarAsync(db, partes, incluyeEvo, pDesde, pHasta, ct);
                    filas.Add(new SimFila(name, pac.Nombre ?? "", pac.TipoDocumento ?? "",
                        pac.Documento ?? "", codigo, exp.Partes, "OK", exp.Avisos));
                }
            }
        }
        else
        {
            foreach (var pac in pacientes)
            {
                ct.ThrowIfCancellationRequested();
                var partes = await ConstruirPlanPacienteAsync(db, archivo, pac, allUrls, pDesde, pHasta, ct);
                if (partes is null)
                {
                    filas.Add(new SimFila("(no se genera)", pac.Nombre ?? "", pac.TipoDocumento ?? "",
                        pac.Documento ?? "", "", Array.Empty<SimParte>(), "Paciente no existe en el sistema"));
                    continue;
                }

                if (partes.Count == 0)
                {
                    filas.Add(new SimFila("(no se genera)", pac.Nombre ?? "", pac.TipoDocumento ?? "",
                        pac.Documento ?? "", "", Array.Empty<SimParte>(), "Vacio: sin contenido para este paciente"));
                    continue;
                }

                var baseName = SanitizarNombre(ResolverNombre(patron, archivo, pac, nitIps));
                var name = baseName + ".pdf";
                var n = 2;
                while (!usados.Add(name)) { name = $"{baseName}_{n++}.pdf"; }

                var exp = await ExpandirYDiagnosticarAsync(db, partes, incluyeEvo, pDesde, pHasta, ct);
                filas.Add(new SimFila(name, pac.Nombre ?? "", pac.TipoDocumento ?? "",
                    pac.Documento ?? "", string.Join(", ", pac.Autorizaciones.Where(x => !string.IsNullOrWhiteSpace(x))),
                    exp.Partes, "OK", exp.Avisos));
            }
        }

        return ConstruirTxtSimulacion(detalle.Metadata.Nombre, archivo, filas);
    }

    /// <summary>Resultado de expandir las partes de un PDF: la lista de etiquetas EN ORDEN REAL
    /// (ya con las evoluciones que el motor anidaria bajo cada HC base, si <c>incluyeEvo</c>) y los
    /// avisos de "doble fuente" encontrados.</summary>
    private sealed record SimExpand(List<SimParte> Partes, List<string> Avisos);

    /// <summary>Expande las partes de primer nivel del plan agregando lo que el RENDER anida
    /// dentro de cada HC base (evoluciones de terapia), modelando el motor de impresion sin
    /// renderizar. Si una base de terapia tiene evoluciones que TAMBIEN van como documento
    /// suelto en el mismo PDF, lo reporta como aviso de "doble fuente" (el patron que duplicaba
    /// historias), aunque el gate de EVO este apagado.</summary>
    private async Task<SimExpand> ExpandirYDiagnosticarAsync(
        IApplicationDbContext db, List<PartePlan> partes, bool incluyeEvo, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var res = new List<SimParte>(partes.Count);
        var avisos = new List<string>();

        // Consecutivos de HC que van como documento SUELTO (parte de primer nivel) en este PDF.
        var sueltos = new HashSet<long>();
        foreach (var p in partes)
        {
            if (p.HcId != Guid.Empty && TryConsecutivoDeEtiqueta(p.Etiqueta, out var cs)) { sueltos.Add(cs); }
        }

        foreach (var p in partes)
        {
            var etq = string.IsNullOrWhiteSpace(p.Etiqueta) ? "[?]" : p.Etiqueta!;
            res.Add(new SimParte(etq, p.Cup, p.CupNombre));
            if (p.HcId == Guid.Empty) { continue; }

            var anidadas = await ConsultarEvolucionesAnidadasAsync(db, p.HcId, desde, hasta, ct);
            if (anidadas.Count == 0) { continue; }

            // Refleja el render: las evoluciones salen DENTRO de la hoja base solo si docs incluye EVO.
            // Heredan el CUP de su hoja base para quedar bajo la misma cabecera de CUP en el TXT.
            if (incluyeEvo)
            {
                foreach (var c in anidadas) { res.Add(new SimParte($"{CodigoPublico.Hc(c)} (anidada en {etq})", p.Cup, p.CupNombre)); }
            }

            // Diagnostico independiente del gate: evoluciones de esta base que ADEMAS van sueltas.
            var doble = anidadas.Where(sueltos.Contains).Select(CodigoPublico.Hc).ToList();
            if (doble.Count > 0)
            {
                avisos.Add(
                    $"{etq} es base de terapia; sus evoluciones {string.Join(", ", doble)} tambien van " +
                    "como documento suelto (doble fuente). " +
                    (incluyeEvo
                        ? "docs incluye EVO -> el render las ANIDA y SALEN DUPLICADAS."
                        : "docs=HC -> hoy NO se anidan (no se duplican); vigilar si se activa EVO."));
            }
        }

        return new SimExpand(res, avisos);
    }

    /// <summary>"HC-004270" -> 4270. False si la etiqueta no es un consecutivo HC-NNNNNN.</summary>
    private static bool TryConsecutivoDeEtiqueta(string? etiqueta, out long consecutivo)
    {
        consecutivo = 0;
        if (string.IsNullOrWhiteSpace(etiqueta)) { return false; }
        var e = etiqueta.Trim();
        if (!e.StartsWith("HC-", StringComparison.OrdinalIgnoreCase)) { return false; }
        return long.TryParse(e.AsSpan(3), out consecutivo);
    }

    /// <summary>Evoluciones de terapia que el motor ANIDA bajo una HC base, como consecutivos
    /// ordenados por numero_sesion rigido y fecha. Misma logica que
    /// HistoriaClinicaService.GetEvolucionesLigadasAsync (base con FormatoEvolucionCodigo ->
    /// HC del mismo formato de evolucion ligadas a la misma asignacion, Cerradas), acotada al
    /// periodo por fecha de atencion igual que el render. Vacia si la HC no es base de terapia.</summary>
    private async Task<List<long>> ConsultarEvolucionesAnidadasAsync(
        IApplicationDbContext db, Guid baseHcId, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var evoCod = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == baseHcId)
            .Join(db.FormDefinitions.AsNoTracking(),
                  h => h.FormDefinitionId, f => f.Id, (h, f) => f.FormatoEvolucionCodigo)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(evoCod)) { return new(); }

        var asigId = await db.AsignacionTurnoSesionHcs.AsNoTracking()
            .Where(p => p.HistoriaClinicaId == baseHcId)
            .Join(db.AsignacionTurnoSesiones.AsNoTracking(),
                  p => p.SesionId, s => s.Id, (p, s) => s.AsignacionTurnoId)
            .Join(db.AsignacionTurnos.AsNoTracking(),
                  turnoId => turnoId, t => t.Id, (turnoId, t) => t.AsignacionId)
            .FirstOrDefaultAsync(ct);
        if (asigId == Guid.Empty) { return new(); }

        var filas = await (
            from p in db.AsignacionTurnoSesionHcs.AsNoTracking()
            join s in db.AsignacionTurnoSesiones.AsNoTracking() on p.SesionId equals s.Id
            join t in db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
            join h in db.HistoriasClinicas.AsNoTracking() on p.HistoriaClinicaId equals h.Id
            join f in db.FormDefinitions.AsNoTracking() on h.FormDefinitionId equals f.Id
            where t.AsignacionId == asigId
                  && f.Codigo == evoCod
                  && h.Estado == HistoriaClinicaEstado.Cerrada
                  && h.Id != baseHcId
            select new
            {
                h.Id,
                h.Consecutivo,
                Sesion = t.NumeroSesion,
                Fecha = h.FechaAtencion ?? h.FechaCierre ?? h.FechaApertura
            })
            .ToListAsync(ct);

        return filas
            .Where(x => EnRango(x.Fecha, desde, hasta))
            .GroupBy(x => x.Id).Select(g => g.First())
            .OrderBy(x => x.Sesion ?? int.MaxValue)
            .ThenBy(x => x.Fecha)
            .Select(x => x.Consecutivo)
            .ToList();
    }

    /// <summary>Arma el .txt del manifiesto de simulacion: por cada PDF que se generaria, lista
    /// sus partes EN EL ORDEN REAL en que el motor las encadenaria (HC-xxxxxx para historias,
    /// [AUT]/[FIRMA]/[ANEXO] para el resto), marcando la historia que se repite dentro del mismo
    /// archivo. No renderiza nada: usa el mismo plan que el generador, solo que emite el nombre.</summary>
    private static ArchivoExportado ConstruirTxtSimulacion(
        string snapshotNombre, InformeItemDto archivo, IReadOnlyList<SimFila> filas)
    {
        var nombreArchivo = string.IsNullOrWhiteSpace(archivo.Descripcion) ? archivo.Alias : archivo.Descripcion!;
        var modo = archivo.UnoPorAutorizacion ? "Un archivo por autorizacion" : "Un archivo por paciente";
        var ok = filas.Where(f => f.Estado == "OK").ToList();
        var omitidos = filas.Count - ok.Count;

        // Historias (HC-...) que aparecen 2+ veces dentro del MISMO archivo = el duplicado buscado.
        static List<string> HistoriasRepetidas(SimFila f)
        {
            var cuenta = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in f.Partes)
            {
                if (!e.Label.StartsWith("HC-", StringComparison.OrdinalIgnoreCase)) { continue; }
                cuenta[e.Label] = cuenta.TryGetValue(e.Label, out var v) ? v + 1 : 1;
            }
            return cuenta.Where(kv => kv.Value > 1).Select(kv => $"{kv.Key} x{kv.Value}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var conRepetidas = ok.Where(f => HistoriasRepetidas(f).Count > 0).ToList();
        var conAvisos = ok.Where(f => f.Avisos is { Count: > 0 }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("====================================================================");
        sb.AppendLine(" SIMULACION DE GENERACION (dry-run, sin renderizar PDFs)");
        sb.AppendLine("====================================================================");
        sb.AppendLine($" Archivo  : {nombreArchivo} ({archivo.Alias})");
        sb.AppendLine($" Modo     : {modo}");
        sb.AppendLine($" Snapshot : {snapshotNombre}");
        sb.AppendLine($" Generado : {DateTimeOffset.Now.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine($" PDFs que saldrian : {ok.Count}   |   Omitidos/vacios : {omitidos}");
        sb.AppendLine($" PDFs con HISTORIA REPETIDA : {conRepetidas.Count}");
        sb.AppendLine($" PDFs con AVISO DE DOBLE FUENTE : {conAvisos.Count}");
        sb.AppendLine();
        sb.AppendLine(" Cada PDF lista sus partes en el ORDEN REAL en que el motor las encadena,");
        sb.AppendLine(" incluidas las evoluciones que una HC base de terapia ANIDA al renderizar.");
        sb.AppendLine(" Historias = HC-xxxxxx ; otras partes = [AUT]/[FIRMA]/[ANEXO].");
        sb.AppendLine(" '(anidada en HC-...)' = evolucion que el render mete DENTRO de la hoja base.");
        sb.AppendLine(" '<-- REPETIDA' marca una historia que ya habia salido en el mismo PDF.");
        sb.AppendLine(" 'AVISO' = base de terapia con evoluciones que tambien van sueltas (doble");
        sb.AppendLine("          fuente): se duplican si el render anida evoluciones (docs con EVO).");
        sb.AppendLine();

        if (conRepetidas.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------");
            sb.AppendLine($" RESUMEN DE REPETIDAS ({conRepetidas.Count} archivo(s)):");
            foreach (var f in conRepetidas)
            {
                sb.AppendLine($"   * {f.Nombre}  ->  {string.Join(", ", HistoriasRepetidas(f))}");
            }
            sb.AppendLine("--------------------------------------------------------------------");
            sb.AppendLine();
        }

        if (conAvisos.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------");
            sb.AppendLine($" RESUMEN DE DOBLE FUENTE ({conAvisos.Count} archivo(s)):");
            foreach (var f in conAvisos)
            {
                sb.AppendLine($"   * {f.Nombre}");
                foreach (var a in f.Avisos!) { sb.AppendLine($"       - {a}"); }
            }
            sb.AppendLine("--------------------------------------------------------------------");
            sb.AppendLine();
        }

        var idx = 0;
        foreach (var f in filas)
        {
            idx++;
            sb.AppendLine("====================================================================");
            var flag = f.Estado == "OK" && HistoriasRepetidas(f).Count > 0 ? "   [TIENE HISTORIA REPETIDA]" : "";
            sb.AppendLine($" #{idx}  {f.Nombre}{flag}");
            var pacLinea = string.IsNullOrWhiteSpace(f.Paciente) ? "-" : f.Paciente;
            var docLinea = string.IsNullOrWhiteSpace(f.Documento) ? "" : $" ({f.TipoDoc} {f.Documento})";
            sb.AppendLine($"     Paciente: {pacLinea}{docLinea}");
            if (!string.IsNullOrWhiteSpace(f.Autorizacion)) { sb.AppendLine($"     Autorizacion: {f.Autorizacion}"); }
            if (f.Estado != "OK")
            {
                sb.AppendLine($"     -> {f.Estado}");
                continue;
            }
            sb.AppendLine($"     Partes en orden ({f.Partes.Count}):");
            var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Solo si el archivo esta marcado "Ordenar por CUP": las partes ya vienen agrupadas
            // por CUP y se inserta una cabecera "CUP {codigo} - {servicio}" al cambiar de CUP.
            var agrupaCup = archivo.RotuloSeparadorCup;
            string? cupActual = null;
            var huboCup = false;
            var n = 0;
            foreach (var e in f.Partes)
            {
                if (agrupaCup && !string.IsNullOrWhiteSpace(e.Cup)
                    && !string.Equals(e.Cup, cupActual, StringComparison.OrdinalIgnoreCase))
                {
                    cupActual = e.Cup;
                    huboCup = true;
                    var nom = string.IsNullOrWhiteSpace(e.CupNombre) ? "" : $" - {e.CupNombre!.Trim()}";
                    sb.AppendLine($"       CUP {e.Cup!.Trim()}{nom}");
                }
                n++;
                var esHc = e.Label.StartsWith("HC-", StringComparison.OrdinalIgnoreCase);
                var marca = esHc && !vistas.Add(e.Label) ? "   <-- REPETIDA" : "";
                var sangria = (agrupaCup && huboCup) ? "         " : "       ";
                sb.AppendLine($"{sangria}{n,3}. {e.Label}{marca}");
            }
            if (f.Avisos is { Count: > 0 })
            {
                sb.AppendLine("     AVISO (doble fuente):");
                foreach (var a in f.Avisos) { sb.AppendLine($"       - {a}"); }
            }
        }

        if (filas.Count == 0)
        {
            sb.AppendLine("Este archivo no generaria ningun PDF para el snapshot.");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var nombre = SanitizarNombre($"SIM_{archivo.Alias}_{snapshotNombre}") + ".txt";
        return new ArchivoExportado(bytes, "text/plain; charset=utf-8", nombre);
    }

    /// <summary>Material precalculado para decidir, por archivo y paciente del snapshot, si
    /// hay contenido real (la misma regla que usan el contador de la tarjeta y el ZIP). Se
    /// construye una sola vez por snapshot con <see cref="ConstruirLookupAsync"/>.</summary>
    private sealed class ContenidoLookup
    {
        public required string SnapshotNombre { get; init; }
        public required IReadOnlyList<InformeItemDto> Archivos { get; init; }
        public required IReadOnlyList<PacienteSnapshotDto> Pacientes { get; init; }
        /// <summary>documento del paciente -> pacienteId (solo los del snapshot que existen en el sistema).</summary>
        public required Dictionary<string, Guid> DocToId { get; init; }
        public required HashSet<Guid> ConAut { get; init; }
        public required HashSet<Guid> ConFirma { get; init; }
        public required Dictionary<string, HashSet<Guid>> ConFormTipo { get; init; }
        public required Dictionary<Guid, List<(string? Cat, bool Hc, bool Nota)>> DocsExternos { get; init; }

        /// <summary>True si ALGUN contenido del archivo tiene material para el paciente.</summary>
        public bool Tiene(InformeItemDto a, Guid pid) =>
            a.Contenidos.Any(c =>
                (c.Origen == OrigenInformeItem.AutorizacionAsignacion && ConAut.Contains(pid)) ||
                (c.Origen == OrigenInformeItem.FirmaPaciente && ConFirma.Contains(pid)) ||
                (c.Origen == OrigenInformeItem.FormularioPorTipo
                    && c.FormularioTipo is string ft
                    && ConFormTipo.TryGetValue(ft, out var set) && set.Contains(pid)) ||
                (EsOrigenDocExterno(c.Origen)
                    && DocsExternos.TryGetValue(pid, out var dl)
                    && dl.Any(d => DocExternoCoincide(c.Origen, c.TipologiaNombre, d.Cat, d.Hc, d.Nota))));
    }

    /// <summary>
    /// Precarga, una sola vez por snapshot, todo lo necesario para decidir "¿tiene contenido
    /// este paciente para este archivo?" (autorizaciones con PDF en disco, firmas, HC cerradas
    /// por tipo, documentos externos renderables por tipologia). Lo comparten el contador de la
    /// tarjeta y el Excel de faltantes para que nunca se desalineen. Devuelve null si el
    /// snapshot no existe, no tiene aseguradora o la aseguradora no tiene archivos configurados.
    /// </summary>
    private async Task<ContenidoLookup?> ConstruirLookupAsync(IApplicationDbContext db, Guid snapshotId, CancellationToken ct)
    {
        var detalle = await _snaps.ObtenerAsync(snapshotId, ct);
        if (detalle is null || detalle.Metadata.AseguradoraId is not Guid aseguradoraId) { return null; }
        // Mismo recorte de periodo que el ZIP, para que el contador de la tarjeta y el
        // reporte de faltantes cuadren con lo que realmente se genera.
        var (pDesde, pHasta) = PeriodoDe(detalle.FiltrosJson);

        var archivos = await _cuenta.ListarItemsAsync(aseguradoraId, ct);
        if (archivos.Count == 0) { return null; }

        var pacientes = await _snaps.ListarPacientesSnapshotAsync(snapshotId, ct);
        var docs = pacientes.Select(p => p.Documento).Distinct().ToList();

        // doc -> pacienteId (los del snapshot que existen en el sistema).
        var pacs = await db.Pacientes.AsNoTracking()
            .Where(p => docs.Contains(p.NumeroDocumento))
            .Select(p => new { p.Id, p.NumeroDocumento })
            .ToListAsync(ct);
        var docToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pacs) { docToId[p.NumeroDocumento] = p.Id; }
        var idSet = docToId.Values.ToHashSet();

        // Codigos de autorizacion que aparecen en ESTE snapshot, por paciente. Solo esas
        // autorizaciones cuentan/se adjuntan — no todas las del paciente (bug: se adjuntaban
        // autorizaciones de otras asignaciones del paciente ajenas al snapshot).
        var autCodesPorPid = new Dictionary<Guid, HashSet<string>>();
        foreach (var pac in pacientes)
        {
            if (!docToId.TryGetValue(pac.Documento, out var pidPac)) { continue; }
            if (!autCodesPorPid.TryGetValue(pidPac, out var set))
            {
                autCodesPorPid[pidPac] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            foreach (var a in pac.Autorizaciones)
            {
                if (!string.IsNullOrWhiteSpace(a)) { set.Add(a.Trim()); }
            }
        }

        // Precarga: pacientes (del snapshot) con autorizacion y con firma.
        // Autorizacion: el ZIP lee el PDF fisico de wwwroot, asi que aqui contamos solo
        // los que TIENEN el archivo en disco — de lo contrario el conteo diria "18" pero
        // el ZIP saldria vacio (p. ej. en local, donde no estan los archivos de prod).
        // Ademas, solo cuentan las asignaciones cuya autorizacion esta en el snapshot.
        var conAut = new HashSet<Guid>();
        if (idSet.Count > 0)
        {
            var autRows = await db.Asignaciones.AsNoTracking()
                .Where(a => idSet.Contains(a.PacienteId) && a.PdfAutorizacionUrl != null && a.CodigoAutorizacion != null)
                .Select(a => new { a.PacienteId, a.PdfAutorizacionUrl, a.CodigoAutorizacion })
                .ToListAsync(ct);
            foreach (var r in autRows)
            {
                if (conAut.Contains(r.PacienteId)) { continue; }
                if (!autCodesPorPid.TryGetValue(r.PacienteId, out var codes)
                    || !codes.Contains(r.CodigoAutorizacion!.Trim())) { continue; }
                if (ArchivoExisteEnDisco(r.PdfAutorizacionUrl!)) { conAut.Add(r.PacienteId); }
            }
        }
        // Firma: data URL en BD (dos fuentes: notas y solicitudes de firma remota).
        var conFirma = new HashSet<Guid>();
        if (idSet.Count > 0)
        {
            foreach (var pid in await db.NotasMedicas.AsNoTracking()
                .Where(n => idSet.Contains(n.PacienteId) && n.FirmaPacienteDataUrl != null)
                .Select(n => n.PacienteId).Distinct().ToListAsync(ct)) { conFirma.Add(pid); }
            foreach (var pid in await db.FirmaPacienteRequests.AsNoTracking()
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
            var filas = await db.HistoriasClinicas.AsNoTracking()
                .Where(h => idSet.Contains(h.PacienteId) && h.Estado == HistoriaClinicaEstado.Cerrada)
                .Join(db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id,
                      (h, f) => new { h.PacienteId, f.Tipo, Fecha = h.FechaAtencion ?? h.FechaCierre ?? h.CreatedAt })
                .Where(x => x.Tipo != null && tiposReferenciados.Contains(x.Tipo))
                .ToListAsync(ct);
            foreach (var x in filas)
            {
                // Mismo recorte por fecha de atencion que el ZIP (sin rango -> todo).
                if (!EnRango(x.Fecha, pDesde, pHasta)) { continue; }
                if (!conFormTipo.TryGetValue(x.Tipo!, out var set)) { conFormTipo[x.Tipo!] = set = new(); }
                set.Add(x.PacienteId);
            }
        }

        // Documentos externos por tipologia (origenes DocumentoHc / DocumentoPacienteLibre /
        // DocumentoNota). Viven en nota_medica_documentos (binario en wwwroot/uploads/notas).
        // Igual que autorizacion: contamos solo los que EXISTEN en disco y son renderables
        // (pdf/imagen), para que el conteo cuadre con lo que el ZIP realmente incluye. Lookup
        // paciente -> lista de (categoria, tieneHc, tieneNota) de sus documentos renderables.
        var usaDocExterno = archivos.SelectMany(a => a.Contenidos).Any(c => EsOrigenDocExterno(c.Origen));
        var docsExternos = new Dictionary<Guid, List<(string? Cat, bool Hc, bool Nota)>>();
        if (idSet.Count > 0 && usaDocExterno)
        {
            var rows = await db.NotaMedicaDocumentos.AsNoTracking()
                .Where(d => idSet.Contains(d.PacienteId))
                .Select(d => new
                {
                    d.PacienteId,
                    d.Categoria,
                    d.RutaArchivo,
                    Hc = d.HistoriaClinicaId != null,
                    Nota = d.NotaMedicaId != null
                })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.RutaArchivo)
                    || !EsDocRenderable(r.RutaArchivo)
                    || !ArchivoExisteEnDisco(r.RutaArchivo)) { continue; }
                if (!docsExternos.TryGetValue(r.PacienteId, out var list)) { docsExternos[r.PacienteId] = list = new(); }
                list.Add((r.Categoria, r.Hc, r.Nota));
            }
        }

        return new ContenidoLookup
        {
            SnapshotNombre = detalle.Metadata.Nombre,
            Archivos = archivos,
            Pacientes = pacientes,
            DocToId = docToId,
            ConAut = conAut,
            ConFirma = conFirma,
            ConFormTipo = conFormTipo,
            DocsExternos = docsExternos,
        };
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
        IApplicationDbContext db, InformeItemDto archivo, PacienteSnapshotDto pac, List<string> allUrls,
        DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var pid = await db.Pacientes.AsNoTracking()
            .Where(p => p.NumeroDocumento == pac.Documento)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (pid is not Guid pacienteId) { return null; }

        // Modelo plano (v0.155.0): cada HC base renderiza solo su hoja (docsRender=HC), sin
        // anidar sub-documentos. Los distintos tipos de formato se renderizan como lineas y se
        // ordenan JUNTAS por CUP->fecha (ver abajo), igual que el detalle del snapshot.
        var docsRender = CalcularDocsRender(archivo);

        var partes = new List<PartePlan>();
        // Acumula las HC de TODOS los contenidos "Formulario por tipo" para ordenarlas juntas.
        var formTipoUrls = new List<UrlCup>();
        int? ftInsertIdx = null;
        foreach (var c in archivo.Contenidos)
        {
            ct.ThrowIfCancellationRequested();
            switch (c.Origen)
            {
                case OrigenInformeItem.AutorizacionAsignacion:
                    foreach (var b in await CargarAutorizacionesAsync(db, pacienteId, pac.Autorizaciones, c.SoloUltimo, ct))
                    {
                        partes.Add(new PartePlan(b, -1, Etiqueta: "[AUT]"));
                    }
                    break;
                case OrigenInformeItem.FirmaPaciente:
                    foreach (var b in await CargarFirmasAsync(db, pacienteId, c.SoloUltimo, ct))
                    {
                        partes.Add(new PartePlan(b, -1, Etiqueta: "[FIRMA]"));
                    }
                    break;
                case OrigenInformeItem.FormularioPorTipo:
                    ftInsertIdx ??= partes.Count;
                    formTipoUrls.AddRange(await ResolverUrlsFormularioPorTipoAsync(
                        db, pacienteId, c.FormularioTipo, c.SoloUltimo, archivo.OrdenNiveles, docsRender, ct,
                        desde: desde, hasta: hasta, rotuloPorCup: archivo.RotuloSeparadorCup));
                    break;
                case OrigenInformeItem.DocumentoHc:
                case OrigenInformeItem.DocumentoPacienteLibre:
                case OrigenInformeItem.DocumentoNota:
                    foreach (var d in await CargarDocumentosExternosAsync(db, pacienteId, c.Origen, c.TipologiaNombre, c.SoloUltimo, ct, membrete: archivo.MembreteHcCup, rotuloPorCup: archivo.RotuloSeparadorCup))
                    {
                        partes.Add(new PartePlan(d.Bytes, -1, Cup: d.Cup, CupNombre: d.CupNombre, Etiqueta: "[ANEXO]"));
                    }
                    break;
                default:
                    // Origenes aun no soportados (HistoriaClinicaPdf, Consentimiento,
                    // Evolucion, Escala): pendientes de una ola posterior. No producen contenido.
                    break;
            }
        }
        InsertarFormTipoUnificado(partes, formTipoUrls, ftInsertIdx, allUrls, archivo);
        return partes;
    }

    /// <summary>Ordena las HC acumuladas de los contenidos "Formulario por tipo" por CUP->fecha
    /// (modelo plano) y las inserta como un bloque unico en la posicion del PRIMER contenido de
    /// ese tipo, preservando el resto de partes (autorizacion/firma/anexos) en su lugar.</summary>
    private static void InsertarFormTipoUnificado(
        List<PartePlan> partes, List<UrlCup> formTipoUrls, int? insertIdx,
        List<string> allUrls, InformeItemDto archivo)
    {
        if (formTipoUrls.Count == 0) { return; }
        var ordenadas = OrdenarUrlCupsUnificado(formTipoUrls, archivo.OrdenNiveles, archivo.RotuloSeparadorCup);
        var bloque = new List<PartePlan>(ordenadas.Count);
        foreach (var u in ordenadas)
        {
            var etq = u.Consecutivo > 0 ? CodigoPublico.Hc(u.Consecutivo) : "HC (sin consecutivo)";
            bloque.Add(new PartePlan(null, allUrls.Count, u.Cup, u.CupNombre, u.Sede, Etiqueta: etq, HcId: u.HcId));
            allUrls.Add(u.Url);
        }
        partes.InsertRange(Math.Clamp(insertIdx ?? partes.Count, 0, partes.Count), bloque);
    }

    /// <summary>
    /// Carga los PDF de autorizacion de un paciente, PERO solo los de las autorizaciones
    /// que aparecen en el snapshot (<paramref name="codigosSnapshot"/> = codigos de la
    /// columna "Autorizacion" para ese paciente). Antes traia TODAS las autorizaciones del
    /// paciente, adjuntando documentos de otras asignaciones ajenas al snapshot. Una
    /// autorizacion = un PDF: se agrupa por codigo y se toma el mas reciente de cada uno;
    /// <paramref name="soloUltimo"/> reduce a la ultima autorizacion.
    /// </summary>
    private async Task<List<byte[]>> CargarAutorizacionesAsync(
        IApplicationDbContext db, Guid pacienteId, IReadOnlyList<string> codigosSnapshot, bool soloUltimo, CancellationToken ct)
    {
        var codSet = codigosSnapshot
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codSet.Count == 0) { return new List<byte[]>(); }

        // Cargamos las asignaciones del paciente con PDF (son pocas) y filtramos en
        // memoria por codigo con trim en ambos lados: el snapshot guarda el codigo
        // trimmeado (NormalizarAutorizacion) y en BD puede tener espacios.
        var filas = (await db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == pacienteId
                        && a.PdfAutorizacionUrl != null
                        && a.CodigoAutorizacion != null)
            .Select(a => new { a.CodigoAutorizacion, a.PdfAutorizacionUrl, a.CreatedAt })
            .ToListAsync(ct))
            .Where(x => codSet.Contains(x.CodigoAutorizacion!.Trim()))
            .ToList();

        // Una autorizacion = un PDF: el mas reciente por codigo. Orden cronologico estable;
        // soloUltimo deja solo la autorizacion mas reciente.
        var porCodigo = filas
            .GroupBy(x => x.CodigoAutorizacion!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First());
        var ordenadas = soloUltimo
            ? porCodigo.OrderByDescending(x => x.CreatedAt).Take(1).ToList()
            : porCodigo.OrderBy(x => x.CreatedAt).ToList();

        var res = new List<byte[]>();
        var urlsVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ordenadas)
        {
            if (!urlsVistas.Add(f.PdfAutorizacionUrl!)) { continue; }  // mismo PDF compartido por 2 codigos
            var pdf = LeerArchivoComoPdf(f.PdfAutorizacionUrl!);
            if (pdf is not null) { res.Add(pdf); }
        }
        return res;
    }

    private async Task<List<byte[]>> CargarFirmasAsync(
        IApplicationDbContext db, Guid pacienteId, bool soloUltimo, CancellationToken ct,
        string? filtroAutorizacion = null, IReadOnlyDictionary<Guid, HashSet<string>>? hcAut = null)
    {
        // La firma del paciente puede estar en dos lugares:
        //  1) NotaMedica.FirmaPacienteDataUrl (cuando la firma se pidio ligada a una nota).
        //  2) FirmaPacienteRequest.ImageDataUrl (firmas "libres" pedidas desde HC/paciente/
        //     WhatsApp; NO actualizan la nota). La mayoria de firmas del paciente viven aqui.
        // Unimos ambas fuentes, ordenadas por fecha, y aplicamos SoloUltimo sobre el total.
        var deNotas = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.PacienteId == pacienteId && n.FirmaPacienteDataUrl != null)
            .Select(n => new { Fecha = n.CreatedAt, Url = n.FirmaPacienteDataUrl!, Hc = n.HistoriaClinicaId })
            .ToListAsync(ct);

        var todas = new List<(DateTimeOffset Fecha, string Url)>();
        if (filtroAutorizacion is string fa && hcAut is not null)
        {
            // Modo "un archivo por autorizacion": solo firmas de notas atadas a una HC de
            // esta autorizacion. Las firmas "libres" (requests, sin HC) no se atribuyen a
            // una autorizacion, asi que no entran en el split.
            foreach (var n in deNotas)
            {
                if (n.Hc is Guid hid && hcAut.TryGetValue(hid, out var set) && set.Contains(fa))
                {
                    todas.Add((n.Fecha, n.Url));
                }
            }
        }
        else
        {
            foreach (var n in deNotas) { todas.Add((n.Fecha, n.Url)); }
            var deRequests = await db.FirmaPacienteRequests.AsNoTracking()
                .Where(r => r.PacienteId == pacienteId && r.ImageDataUrl != null)
                .Select(r => new { Fecha = r.CompletedAt ?? r.CreatedAt, Url = r.ImageDataUrl! })
                .ToListAsync(ct);
            foreach (var r in deRequests) { todas.Add((r.Fecha, r.Url)); }
        }

        todas = todas.OrderByDescending(x => x.Fecha).ToList();
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

    /// <summary>Construye, para los pacientes del snapshot, el mapa
    /// <c>HistoriaClinicaId -&gt; codigos de autorizacion</c> siguiendo la cadena
    /// pivote sesion-&gt;turno-&gt;asignacion (misma cadena que usa el orden por CUP). Lo
    /// usa el modo "un archivo por autorizacion" para atribuir cada HC/adjunto/firma a su
    /// autorizacion. Una HC puede colgar de mas de una autorizacion (set).</summary>
    private async Task<Dictionary<Guid, HashSet<string>>> ConstruirMapaHcAutorizacionAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> pacienteIds, CancellationToken ct)
    {
        var map = new Dictionary<Guid, HashSet<string>>();
        if (pacienteIds.Count == 0) { return map; }

        var filas = await (
            from p in db.AsignacionTurnoSesionHcs.AsNoTracking()
            join s in db.AsignacionTurnoSesiones.AsNoTracking() on p.SesionId equals s.Id
            join t in db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
            join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
            where pacienteIds.Contains(a.PacienteId) && a.CodigoAutorizacion != null
            select new { p.HistoriaClinicaId, a.CodigoAutorizacion })
            .ToListAsync(ct);

        foreach (var r in filas)
        {
            var code = r.CodigoAutorizacion!.Trim();
            if (code.Length == 0) { continue; }
            if (!map.TryGetValue(r.HistoriaClinicaId, out var set))
            {
                map[r.HistoriaClinicaId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            set.Add(code);
        }
        return map;
    }

    /// <summary>Pase 1 del modo "un archivo por autorizacion": arma el plan de partes de
    /// UNA autorizacion de un paciente, recorriendo los contenidos configurados del archivo
    /// pero filtrando cada origen a lo que pertenece a esa autorizacion. Las URLs de
    /// formulario-por-tipo se agregan al lote global para rendirse juntas.</summary>
    private async Task<List<PartePlan>> ConstruirPlanAutorizacionAsync(
        IApplicationDbContext db, InformeItemDto archivo, Guid pacienteId, string codigo,
        IReadOnlyDictionary<Guid, HashSet<string>> hcAut, string docsRender,
        List<string> allUrls, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var unoCodigo = new[] { codigo };
        var partes = new List<PartePlan>();
        // Modelo plano (v0.155.0): las HC de todos los contenidos "Formulario por tipo" se juntan
        // y ordenan por CUP->fecha (igual que el detalle del snapshot); sin anidar sub-documentos.
        var formTipoUrls = new List<UrlCup>();
        int? ftInsertIdx = null;
        foreach (var c in archivo.Contenidos)
        {
            ct.ThrowIfCancellationRequested();
            switch (c.Origen)
            {
                case OrigenInformeItem.AutorizacionAsignacion:
                    // Solo el PDF de ESTA autorizacion.
                    foreach (var b in await CargarAutorizacionesAsync(db, pacienteId, unoCodigo, c.SoloUltimo, ct))
                    {
                        partes.Add(new PartePlan(b, -1, Etiqueta: $"[AUT {codigo}]"));
                    }
                    break;
                case OrigenInformeItem.FirmaPaciente:
                    foreach (var b in await CargarFirmasAsync(db, pacienteId, c.SoloUltimo, ct, codigo, hcAut))
                    {
                        partes.Add(new PartePlan(b, -1, Etiqueta: "[FIRMA]"));
                    }
                    break;
                case OrigenInformeItem.FormularioPorTipo:
                    ftInsertIdx ??= partes.Count;
                    formTipoUrls.AddRange(await ResolverUrlsFormularioPorTipoAsync(
                        db, pacienteId, c.FormularioTipo, c.SoloUltimo, archivo.OrdenNiveles, docsRender, ct,
                        codigo, hcAut, desde, hasta, archivo.RotuloSeparadorCup));
                    break;
                case OrigenInformeItem.DocumentoHc:
                case OrigenInformeItem.DocumentoPacienteLibre:
                case OrigenInformeItem.DocumentoNota:
                    foreach (var d in await CargarDocumentosExternosAsync(
                        db, pacienteId, c.Origen, c.TipologiaNombre, c.SoloUltimo, ct, codigo, hcAut, archivo.MembreteHcCup, archivo.RotuloSeparadorCup))
                    {
                        partes.Add(new PartePlan(d.Bytes, -1, Cup: d.Cup, CupNombre: d.CupNombre, Etiqueta: "[ANEXO]"));
                    }
                    break;
                default:
                    break;
            }
        }
        InsertarFormTipoUnificado(partes, formTipoUrls, ftInsertIdx, allUrls, archivo);
        return partes;
    }

    /// <summary>Origenes de documentos externos (DocumentoHc / DocumentoPacienteLibre /
    /// DocumentoNota): recupera los adjuntos del paciente en <c>nota_medica_documentos</c>
    /// (binario en wwwroot/uploads/notas), filtrando por "ambito" segun el origen y por
    /// categoria = tipologia elegida (si no se eligio, cualquiera). PDFs pasan tal cual;
    /// imagenes se envuelven en una pagina. SoloUltimo = solo el adjunto mas reciente.</summary>
    private async Task<List<(byte[] Bytes, string? Cup, string? CupNombre)>> CargarDocumentosExternosAsync(
        IApplicationDbContext db, Guid pacienteId, OrigenInformeItem origen, string? tipologiaNombre, bool soloUltimo, CancellationToken ct,
        string? filtroAutorizacion = null, IReadOnlyDictionary<Guid, HashSet<string>>? hcAut = null,
        bool membrete = false, bool rotuloPorCup = false)
    {
        var q = db.NotaMedicaDocumentos.AsNoTracking()
            .Where(d => d.PacienteId == pacienteId);

        // Ambito segun el origen seleccionado.
        //   DocumentoHc   -> adjuntos colgados de una HC.
        //   DocumentoNota -> adjuntos colgados de una nota medica.
        //   DocumentoPacienteLibre -> cualquier adjunto del paciente (el mas amplio).
        q = origen switch
        {
            OrigenInformeItem.DocumentoHc => q.Where(d => d.HistoriaClinicaId != null),
            OrigenInformeItem.DocumentoNota => q.Where(d => d.NotaMedicaId != null),
            _ => q,
        };

        // Filtro por tipologia (la categoria del documento). Si no se eligio, cualquiera cuenta.
        if (!string.IsNullOrWhiteSpace(tipologiaNombre))
        {
            var cat = tipologiaNombre.Trim().ToLower();
            q = q.Where(d => d.Categoria != null && d.Categoria.ToLower() == cat);
        }

        var rows = await q
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.RutaArchivo, d.HistoriaClinicaId })
            .ToListAsync(ct);

        // Modo "un archivo por autorizacion": solo los adjuntos atados a una HC cuya
        // asignacion pertenece a ESTA autorizacion. Los adjuntos libres (sin HC) no se
        // pueden atribuir a una autorizacion, asi que se omiten en el split.
        if (filtroAutorizacion is string fa && hcAut is not null)
        {
            rows = rows.Where(r => r.HistoriaClinicaId is Guid hid
                                   && hcAut.TryGetValue(hid, out var set) && set.Contains(fa)).ToList();
        }
        if (rows.Count == 0) { return new(); }

        // SoloUltimo = el mas reciente; si no, todos en orden cronologico ascendente.
        var seleccion = (soloUltimo ? rows.Take(1) : rows.AsEnumerable().Reverse()).ToList();

        // Resuelve HC-xxxxxx + CUP + servicio de la historia de la que cuelga cada anexo (una sola
        // consulta) si hace falta: para estampar el membrete (membrete) y/o para ordenar por CUP
        // y poner la cabecera "CUP ..." (rotuloPorCup).
        var info = (membrete || rotuloPorCup)
            ? await ResolverMembretePorHcAsync(db,
                seleccion.Where(r => r.HistoriaClinicaId is Guid)
                         .Select(r => r.HistoriaClinicaId!.Value).Distinct().ToList(), ct)
            : new Dictionary<Guid, (string Hc, string Cup, string Serv)>();

        string? CupDe(Guid? hid) => hid is Guid id && info.TryGetValue(id, out var m) && !string.IsNullOrWhiteSpace(m.Cup) ? m.Cup : null;
        string? ServDe(Guid? hid) => hid is Guid id && info.TryGetValue(id, out var m) && !string.IsNullOrWhiteSpace(m.Serv) ? m.Serv : null;

        // "Ordenar por CUP": agrupa los anexos por el CUP de su historia (numericos primero, los
        // que empiezan por letra despues; los anexos sin CUP al final), respetando el orden
        // cronologico dentro de cada CUP (OrderBy es estable). Sin el flag -> orden cronologico.
        if (rotuloPorCup)
        {
            seleccion = seleccion
                .OrderBy(r => CupSortKey(CupDe(r.HistoriaClinicaId)).Grupo)
                .ThenBy(r => CupSortKey(CupDe(r.HistoriaClinicaId)).Alfa, StringComparer.Ordinal)
                .ThenBy(r => CupSortKey(CupDe(r.HistoriaClinicaId)).Num)
                .ToList();
        }

        var res = new List<(byte[] Bytes, string? Cup, string? CupNombre)>();
        foreach (var r in seleccion)
        {
            var ruta = r.RutaArchivo;
            if (string.IsNullOrWhiteSpace(ruta) || !EsDocRenderable(ruta)) { continue; }
            var pdf = LeerArchivoComoPdf(ruta);
            if (pdf is null) { continue; }
            if (membrete)
            {
                var texto = ConstruirTextoMembrete(r.HistoriaClinicaId, info);
                pdf = EstamparMembrete(pdf, texto) ?? pdf;
            }
            res.Add((pdf, CupDe(r.HistoriaClinicaId), ServDe(r.HistoriaClinicaId)));
        }
        return res;
    }

    /// <summary>Resuelve, para cada HC, su codigo publico (HC-xxxxxx), su CUP (CodigoRips de la
    /// primera asignacion via cadena de sesiones) y el nombre del servicio, con el mismo
    /// fallback que RelacionFacturas (si la asignacion no estampo CodigoRips, se deriva del
    /// ServicioContrato vivo: codigo base sin sufijo d/f). Una sola consulta por lote de HC.</summary>
    private async Task<Dictionary<Guid, (string Hc, string Cup, string Serv)>> ResolverMembretePorHcAsync(
        IApplicationDbContext db, List<Guid> hcIds, CancellationToken ct)
    {
        var res = new Dictionary<Guid, (string Hc, string Cup, string Serv)>();
        if (hcIds.Count == 0) { return res; }

        var consec = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => hcIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Consecutivo })
            .ToListAsync(ct);
        var hcCode = consec.ToDictionary(x => x.Id, x => CodigoPublico.Hc(x.Consecutivo));

        // Primera asignacion por HC (via pivote sesion->turno->asignacion), con su servicio.
        var cups = await (
            from p in db.AsignacionTurnoSesionHcs.AsNoTracking()
            where hcIds.Contains(p.HistoriaClinicaId)
            join s in db.AsignacionTurnoSesiones.AsNoTracking() on p.SesionId equals s.Id
            join t in db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
            join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
            select new { p.HistoriaClinicaId, a.CodigoRips, a.NombreServicio, a.ServicioId })
            .ToListAsync(ct);
        var asigPorHc = new Dictionary<Guid, (string? Cup, string? Nombre, string? ServicioId)>();
        foreach (var r in cups)
        {
            if (!asigPorHc.ContainsKey(r.HistoriaClinicaId))
            {
                asigPorHc[r.HistoriaClinicaId] = (r.CodigoRips, r.NombreServicio, r.ServicioId);
            }
        }

        // Fallback de CUP/nombre desde el ServicioContrato vivo cuando la asignacion es legacy.
        var servIds = asigPorHc.Values
            .Where(v => string.IsNullOrWhiteSpace(v.Cup) && Guid.TryParse(v.ServicioId, out _))
            .Select(v => Guid.Parse(v.ServicioId!))
            .Distinct().ToList();
        var servInfo = servIds.Count == 0
            ? new Dictionary<Guid, (string? Codigo, string? Descripcion)>()
            : await db.ServiciosContrato.AsNoTracking()
                .Where(s => servIds.Contains(s.Id))
                .Select(s => new { s.Id, s.CodigoServicio, s.Descripcion })
                .ToDictionaryAsync(s => s.Id, s => ((string?)s.CodigoServicio, (string?)s.Descripcion), ct);

        foreach (var id in hcIds)
        {
            var hc = hcCode.TryGetValue(id, out var h) ? h : "";
            string cup = "", serv = "";
            if (asigPorHc.TryGetValue(id, out var a))
            {
                cup = a.Cup?.Trim() ?? "";
                serv = a.Nombre?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(cup)
                    && Guid.TryParse(a.ServicioId, out var sid)
                    && servInfo.TryGetValue(sid, out var sc))
                {
                    cup = ServicioCodigo.Base(sc.Item1) ?? "";
                    if (string.IsNullOrWhiteSpace(serv)) { serv = sc.Item2?.Trim() ?? ""; }
                }
            }
            res[id] = (hc, cup, serv);
        }
        return res;
    }

    /// <summary>Texto del membrete de un anexo: "HC-xxxxxx  |  CUP {codigo} - {servicio}".
    /// Sin HC resoluble -> "(documento sin historia clinica)"; sin CUP -> solo el HC-.</summary>
    private static string ConstruirTextoMembrete(
        Guid? hcId, IReadOnlyDictionary<Guid, (string Hc, string Cup, string Serv)> map)
    {
        if (hcId is not Guid id || !map.TryGetValue(id, out var m) || string.IsNullOrWhiteSpace(m.Hc))
        {
            return "(documento sin historia clinica)";
        }
        var sb = new StringBuilder(m.Hc);
        if (!string.IsNullOrWhiteSpace(m.Cup))
        {
            sb.Append("   |   CUP ").Append(m.Cup);
            if (!string.IsNullOrWhiteSpace(m.Serv)) { sb.Append(" - ").Append(m.Serv); }
        }
        return sb.ToString();
    }

    /// <summary>Estampa el membrete (franja superior con el texto) en la PRIMERA pagina del PDF
    /// via <see cref="MembreteEstampador"/>. Si el PDF no se pudo modificar (cifrado/corrupto),
    /// el helper devuelve null y el anexo sale sin membrete en vez de romper el archivo.</summary>
    private byte[]? EstamparMembrete(byte[] pdf, string texto)
    {
        var res = MembreteEstampador.Estampar(pdf, texto);
        if (res is null) { _log.LogWarning("Tipologia ZIP: no se pudo estampar el membrete de un anexo"); }
        return res;
    }

    /// <summary>True si el origen corresponde a documentos externos (adjuntos del paciente
    /// en nota_medica_documentos).</summary>
    private static bool EsOrigenDocExterno(OrigenInformeItem o) =>
        o is OrigenInformeItem.DocumentoHc
            or OrigenInformeItem.DocumentoPacienteLibre
            or OrigenInformeItem.DocumentoNota;

    /// <summary>True si la ruta apunta a un tipo renderable (PDF o imagen). Otros tipos
    /// (p. ej. .pptx) se omiten para no meter paginas basura en el PDF del paciente.</summary>
    private static bool EsDocRenderable(string ruta)
    {
        var ext = Path.GetExtension(ruta).ToLowerInvariant();
        return ext is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp";
    }

    /// <summary>Evalua si un documento externo (con su categoria/ambito) satisface un
    /// contenido configurado: ambito segun el origen + categoria = tipologia elegida.
    /// Espejo en memoria de los filtros de <see cref="CargarDocumentosExternosAsync"/>,
    /// usado por el contador para que el "con contenido" cuadre con el ZIP.</summary>
    private static bool DocExternoCoincide(
        OrigenInformeItem origen, string? tipologiaNombre, string? categoria, bool tieneHc, bool tieneNota)
    {
        var ambitoOk = origen switch
        {
            OrigenInformeItem.DocumentoHc => tieneHc,
            OrigenInformeItem.DocumentoNota => tieneNota,
            _ => true, // DocumentoPacienteLibre: cualquier documento del paciente
        };
        if (!ambitoOk) { return false; }
        if (string.IsNullOrWhiteSpace(tipologiaNombre)) { return true; }
        return string.Equals((categoria ?? "").Trim(), tipologiaNombre.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Origen "Formulario por tipo": devuelve las URLs publicas (con token de un
    /// solo uso) de cada HC Cerrada del paciente cuyo FormDefinition.Tipo =
    /// <paramref name="tipo"/>. No renderiza aqui: las URLs se juntan con las de los demas
    /// pacientes y se rinden en lote (un solo navegador, en paralelo). SoloUltimo = solo la
    /// HC mas reciente (por fecha de atencion). <paramref name="ordenNiveles"/> define el
    /// orden multinivel (cup/fecha); vacio = cronologico ascendente (historico).</summary>
    /// <summary>Calcula que "docs" debe renderizar <c>/p/hc-form</c> para este archivo, segun
    /// los tipos de formulario configurados (content-driven). Siempre incluye el formulario
    /// base (<c>HC</c>); agrega adjuntos SOLO si el archivo tiene un contenido de ese tipo, para
    /// que "Formulario por tipo" no arrastre escalas/consentimientos que el usuario no pidio.
    /// Mapeo: ESCALAS-&gt;ESC, CONSENTIMIENTO-&gt;CON, EVOLUCION-&gt;EVO.</summary>
    /// <summary>Modelo PLANO (v0.155.0): cada HC base renderiza SOLO su propia hoja, sin
    /// anidar sub-documentos (evoluciones/escalas/consentimientos) debajo. Cada tipo de
    /// formato (HISTORIA CLINICA / EVOLUCION / NOTAS...) se renderiza por su propio contenido
    /// "Formulario por tipo" como una linea mas, y las lineas se ordenan JUNTAS por CUP->fecha
    /// (ver <see cref="OrdenarUrlCupsUnificado"/>). Asi el archivo = exactamente las lineas del
    /// snapshot, en su mismo orden. Reemplaza el modelo anidado anterior (y su dedup).</summary>
    private static string CalcularDocsRender(InformeItemDto archivo) => "HC";

    private async Task<List<UrlCup>> ResolverUrlsFormularioPorTipoAsync(
        IApplicationDbContext db, Guid pacienteId, string? tipo, bool soloUltimo,
        IReadOnlyList<InformeOrdenNivelDto>? ordenNiveles, string docsRender, CancellationToken ct,
        string? filtroAutorizacion = null, IReadOnlyDictionary<Guid, HashSet<string>>? hcAut = null,
        DateOnly? desde = null, DateOnly? hasta = null, bool rotuloPorCup = false)
    {
        if (string.IsNullOrWhiteSpace(tipo) || _tenant.TenantId is not Guid tid) { return new(); }

        var filas = (await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId && h.Estado == HistoriaClinicaEstado.Cerrada)
            .Join(db.FormDefinitions.AsNoTracking(),
                  h => h.FormDefinitionId, f => f.Id,
                  (h, f) => new { h.Id, h.Consecutivo, h.FechaCierre, h.FechaAtencion, h.CreatedAt, f.Tipo })
            .Where(x => x.Tipo == tipo)
            .Select(x => new { x.Id, x.Consecutivo, Fecha = x.FechaAtencion ?? x.FechaCierre ?? x.CreatedAt })
            .ToListAsync(ct))
            .Select(x => new FilaHcOrden(x.Id, x.Fecha, x.Consecutivo))
            .ToList();
        if (filas.Count == 0) { return new(); }

        // Acota al periodo del snapshot por FECHA DE ATENCION (dia Bogota), para no colar
        // HC/evoluciones de otro mes. Sin rango (null) -> no filtra (expediente completo).
        if (desde is not null || hasta is not null)
        {
            filas = filas.Where(f => EnRango(f.Fecha, desde, hasta)).ToList();
            if (filas.Count == 0) { return new(); }
        }

        // Modo "un archivo por autorizacion": solo las HC cuya asignacion pertenece a
        // ESTA autorizacion (mapa HC -> codigos de autorizacion precalculado). Las HC sin
        // autorizacion resoluble no entran en el split por autorizacion.
        if (filtroAutorizacion is string fa && hcAut is not null)
        {
            filas = filas.Where(f => hcAut.TryGetValue(f.Id, out var set) && set.Contains(fa)).ToList();
            if (filas.Count == 0) { return new(); }
        }

        // CUP (CodigoRips) + nombre de servicio + sede por HC, via pivote sesion->turno->
        // asignacion. Se consulta si algun nivel ordena por "cup" O si hay que insertar el
        // rotulo separador por CUP (que ademas necesita nombre de servicio y sede).
        var cupInfo = new Dictionary<Guid, (string Cup, string Nombre, string Sede)>();
        var needCup = rotuloPorCup || (ordenNiveles?.Any(n => n.Campo == "cup") ?? false);
        if (needCup)
        {
            var ids = filas.Select(f => f.Id).ToList();
            var cups = await (
                from p in db.AsignacionTurnoSesionHcs.AsNoTracking()
                where ids.Contains(p.HistoriaClinicaId)
                join s in db.AsignacionTurnoSesiones.AsNoTracking() on p.SesionId equals s.Id
                join t in db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
                join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
                select new { p.HistoriaClinicaId, a.CodigoRips, a.NombreServicio, a.Sucursal, a.ServicioId })
                .ToListAsync(ct);

            // Primera asignacion por HC (via cadena de sesiones), con su servicio para el
            // fallback de CUP.
            var asigPorHc = new Dictionary<Guid, (string? Cup, string? Nombre, string? Sede, string? ServicioId)>();
            foreach (var r in cups)
            {
                if (!asigPorHc.ContainsKey(r.HistoriaClinicaId))
                {
                    asigPorHc[r.HistoriaClinicaId] = (r.CodigoRips, r.NombreServicio, r.Sucursal, r.ServicioId);
                }
            }

            // Fallback como en RelacionFacturas: si la asignacion no estampo CodigoRips
            // (legacy), el CUP y el nombre se derivan del ServicioContrato vivo
            // (codigo base sin sufijo d/f). Asi no quedan "(sin CUP)" cuando el snapshot
            // si muestra el CUP.
            var servIds = asigPorHc.Values
                .Where(v => string.IsNullOrWhiteSpace(v.Cup) && Guid.TryParse(v.ServicioId, out _))
                .Select(v => Guid.Parse(v.ServicioId!))
                .Distinct().ToList();
            var servInfo = servIds.Count == 0
                ? new Dictionary<Guid, (string? Codigo, string? Descripcion)>()
                : await db.ServiciosContrato.AsNoTracking()
                    .Where(s => servIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.CodigoServicio, s.Descripcion })
                    .ToDictionaryAsync(s => s.Id, s => ((string?)s.CodigoServicio, (string?)s.Descripcion), ct);

            foreach (var kv in asigPorHc)
            {
                var (cup, nombre, sede, servicioId) = kv.Value;
                if (string.IsNullOrWhiteSpace(cup)
                    && Guid.TryParse(servicioId, out var sid)
                    && servInfo.TryGetValue(sid, out var sc))
                {
                    cup = ServicioCodigo.Base(sc.Item1);
                    if (string.IsNullOrWhiteSpace(nombre)) { nombre = sc.Item2; }
                }
                if (!string.IsNullOrWhiteSpace(cup))
                {
                    cupInfo[kv.Key] = (cup!.Trim(), nombre ?? "", sede ?? "");
                }
            }
        }
        string CupOf(Guid id) => cupInfo.TryGetValue(id, out var ci) ? ci.Cup : "";

        if (soloUltimo)
        {
            // SoloUltimo = la HC mas reciente por fecha, independiente del orden configurado.
            filas = new List<FilaHcOrden> { filas.OrderByDescending(f => f.Fecha).First() };
        }
        else if (rotuloPorCup)
        {
            // El CUP manda: agrupa por CUP (numericos ascendentes; los que empiezan por letra
            // al final) y, dentro de cada CUP, respeta el orden configurado por fecha (o
            // cronologico si no hay). Garantiza un solo separador por CUP.
            var clave = filas.ToDictionary(f => f.Id, f => CupSortKey(CupOf(f.Id)));
            var ord = filas
                .OrderBy(f => clave[f.Id].Grupo)
                .ThenBy(f => clave[f.Id].Alfa, StringComparer.Ordinal)
                .ThenBy(f => clave[f.Id].Num);
            var internos = (ordenNiveles ?? Array.Empty<InformeOrdenNivelDto>())
                .Where(n => n.Campo == "fecha").ToList();
            if (internos.Count == 0) { internos = new List<InformeOrdenNivelDto> { new("fecha", false) }; }
            foreach (var n in internos)
            {
                Func<FilaHcOrden, IComparable> key = f => f.Fecha;
                ord = n.Desc ? ord.ThenByDescending(key) : ord.ThenBy(key);
            }
            filas = ord.ToList();
        }
        else
        {
            // Orden multinivel: si no hay config, cronologico ascendente (historico).
            var niveles = (ordenNiveles is { Count: > 0 })
                ? ordenNiveles
                : new List<InformeOrdenNivelDto> { new("fecha", false) };

            IOrderedEnumerable<FilaHcOrden>? ordered = null;
            foreach (var n in niveles)
            {
                Func<FilaHcOrden, IComparable> key = n.Campo == "cup"
                    ? (f => CupOf(f.Id))
                    : (f => f.Fecha);
                ordered = ordered is null
                    ? (n.Desc ? filas.OrderByDescending(key) : filas.OrderBy(key))
                    : (n.Desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
            }
            filas = (ordered ?? filas.OrderBy(f => f.Fecha)).ToList();
        }

        var baseUrl = BaseUrlInterna();
        // Pasa el rango a /p/hc-form para que PaqueteHcImprimible acote tambien los
        // SUB-documentos anexados (evoluciones/escalas/consentimientos) por su fecha propia.
        var rango = "";
        if (desde is DateOnly dr) { rango += $"&desde={dr:yyyy-MM-dd}"; }
        if (hasta is DateOnly hr) { rango += $"&hasta={hr:yyyy-MM-dd}"; }
        return filas.Select(f =>
        {
            var url = $"{baseUrl}/p/hc-form/{f.Id}?t={_printTokens.Mint(f.Id, tid)}&docs={docsRender}{rango}";
            if (!rotuloPorCup) { return new UrlCup(url, null, null, null, f.Fecha, f.Consecutivo, f.Id); }
            var ci = cupInfo.TryGetValue(f.Id, out var c) ? c : ("", "", "");
            return new UrlCup(url, ci.Item1, ci.Item2, ci.Item3, f.Fecha, f.Consecutivo, f.Id);
        }).ToList();
    }

    /// <summary>Ordena JUNTAS las HC de varios contenidos "Formulario por tipo" (HISTORIA
    /// CLINICA + EVOLUCION + NOTAS...) en una sola secuencia, para que el archivo siga el
    /// mismo orden PLANO del snapshot (cada HC base = una linea), en vez de agrupar por tipo
    /// de formato. Con rotulo por CUP: el CUP manda (numericos primero, los que empiezan por
    /// letra al final) y la fecha ordena dentro de cada CUP. Sin rotulo: respeta OrdenNiveles
    /// (o cronologico ascendente por defecto).</summary>
    private static List<UrlCup> OrdenarUrlCupsUnificado(
        List<UrlCup> urls, IReadOnlyList<InformeOrdenNivelDto>? ordenNiveles, bool rotuloPorCup)
    {
        if (urls.Count <= 1) { return urls; }

        List<InformeOrdenNivelDto> niveles;
        if (rotuloPorCup)
        {
            niveles = new List<InformeOrdenNivelDto> { new("cup", false) };
            var fechas = (ordenNiveles ?? Array.Empty<InformeOrdenNivelDto>())
                .Where(n => n.Campo == "fecha").ToList();
            niveles.AddRange(fechas.Count > 0 ? fechas : new List<InformeOrdenNivelDto> { new("fecha", false) });
        }
        else
        {
            niveles = (ordenNiveles is { Count: > 0 })
                ? ordenNiveles.ToList()
                : new List<InformeOrdenNivelDto> { new("fecha", false) };
        }

        IOrderedEnumerable<UrlCup>? ordered = null;
        foreach (var n in niveles)
        {
            if (n.Campo == "cup")
            {
                Func<UrlCup, int> kG = u => CupSortKey(u.Cup).Grupo;
                Func<UrlCup, string> kA = u => CupSortKey(u.Cup).Alfa;
                Func<UrlCup, decimal> kN = u => CupSortKey(u.Cup).Num;
                if (ordered is null)
                {
                    ordered = n.Desc ? urls.OrderByDescending(kG) : urls.OrderBy(kG);
                }
                else
                {
                    ordered = n.Desc ? ordered.ThenByDescending(kG) : ordered.ThenBy(kG);
                }
                ordered = n.Desc
                    ? ordered.ThenByDescending(kA, StringComparer.Ordinal).ThenByDescending(kN)
                    : ordered.ThenBy(kA, StringComparer.Ordinal).ThenBy(kN);
            }
            else
            {
                Func<UrlCup, IComparable> key = u => u.Fecha;
                ordered = ordered is null
                    ? (n.Desc ? urls.OrderByDescending(key) : urls.OrderBy(key))
                    : (n.Desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
            }
        }
        return (ordered ?? urls.OrderBy(u => u.Fecha)).ToList();
    }

    /// <summary>Periodo (fechaInicio/fechaFin) del snapshot desde su FiltrosJson. Se usa
    /// para acotar los formularios-por-tipo y sus sub-documentos a la ventana del snapshot
    /// (mismo criterio que el "Imprimir listado" de Ordenes, v0.137.0). Si el snapshot no
    /// tiene rango, devuelve (null, null) = sin filtro (compat: expediente completo).</summary>
    private static (DateOnly? Desde, DateOnly? Hasta) PeriodoDe(string? filtrosJson)
    {
        if (string.IsNullOrWhiteSpace(filtrosJson)) { return (null, null); }
        try
        {
            using var doc = JsonDocument.Parse(filtrosJson);
            var root = doc.RootElement;
            DateOnly? d = null, h = null;
            if (root.TryGetProperty("fechaInicio", out var di) && di.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(di.GetString(), out var dd)) { d = dd; }
            if (root.TryGetProperty("fechaFin", out var hf) && hf.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(hf.GetString(), out var hh)) { h = hh; }
            return (d, h);
        }
        catch { return (null, null); }
    }

    /// <summary>True si la fecha efectiva cae dentro del rango, con el MISMO criterio y
    /// frontera (dia local Bogota UTC-5) que PaqueteHcImprimible.EnRangoImpresion. Sin rango
    /// (null, null) o fecha null => incluye.</summary>
    private static bool EnRango(DateTimeOffset? efectiva, DateOnly? desde, DateOnly? hasta)
    {
        if (desde is null && hasta is null) { return true; }
        if (efectiva is not DateTimeOffset fa) { return true; }
        var bogota = TimeSpan.FromHours(-5);
        if (desde is DateOnly d)
        {
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), bogota).ToUniversalTime();
            if (fa < dStart) { return false; }
        }
        if (hasta is DateOnly h)
        {
            var dEnd = new DateTimeOffset(h.ToDateTime(TimeOnly.MaxValue), bogota).ToUniversalTime();
            if (fa > dEnd) { return false; }
        }
        return true;
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

    /// <summary>Convierte el plan de partes de un paciente/autorizacion en la lista de PDFs a
    /// fusionar. Si <paramref name="rotulo"/> esta activo, inserta una pagina separadora
    /// (rotulo con logo del tenant) cada vez que empieza un CUP distinto entre los documentos
    /// de historia; las partes sin CUP (autorizacion/firma/anexo) no disparan separador y
    /// quedan en su lugar. El conteo de CUP arranca de cero en cada paciente.</summary>
    private List<byte[]> EnsamblarPartes(
        IReadOnlyList<PartePlan> partes, IReadOnlyList<byte[]> rendered, bool rotulo, byte[]? logo)
    {
        var bytes = new List<byte[]>(partes.Count);
        string? ultimoCup = null;
        var iniciado = false;
        foreach (var p in partes)
        {
            byte[]? b = p.Inline is { Length: > 0 } ? p.Inline
                : (p.RenderIdx >= 0 && p.RenderIdx < rendered.Count
                   && rendered[p.RenderIdx] is { Length: > 0 } rb ? rb : null);
            if (b is null) { continue; }

            if (rotulo && p.Cup is not null
                && (!iniciado || !string.Equals(p.Cup, ultimoCup, StringComparison.OrdinalIgnoreCase)))
            {
                bytes.Add(PaginaRotuloCup(logo, p.Cup, p.CupNombre, p.Sede));
                ultimoCup = p.Cup;
                iniciado = true;
            }
            bytes.Add(b);
        }
        return bytes;
    }

    /// <summary>Pagina "rotulo" separadora de 1/4 de carta (4.25 x 5.5 in): tarjeta con el
    /// logo del tenant, el codigo CUP, el nombre del servicio y la sede. Separa cada grupo de
    /// CUP dentro del PDF del paciente.</summary>
    private static byte[] PaginaRotuloCup(byte[]? logo, string? cup, string? nombre, string? sede)
    {
        var codigo = string.IsNullOrWhiteSpace(cup) ? "(sin CUP)" : cup!.Trim();
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                // Franja horizontal: ancho de carta (8.5") y alto de 1/4 de carta (2.75").
                page.Size(8.5f, 2.75f, Unit.Inch);
                page.Margin(14);
                page.Content().Border(1.5f).BorderColor("#1565C0").Padding(16).Row(row =>
                {
                    row.Spacing(18);
                    if (logo is { Length: > 0 })
                    {
                        try { row.ConstantItem(150).AlignMiddle().Height(70).Image(logo).FitArea(); }
                        catch { /* logo ilegible */ }
                    }
                    row.RelativeItem().AlignMiddle().Column(col =>
                    {
                        col.Spacing(5);
                        col.Item().AlignCenter().Text("SEPARADOR DE SERVICIO").FontSize(9).SemiBold().FontColor("#64748B");
                        col.Item().AlignCenter().Text(codigo).FontSize(26).Bold().FontColor("#0F172A");
                        if (!string.IsNullOrWhiteSpace(nombre))
                        {
                            col.Item().AlignCenter().Text(nombre!.Trim()).FontSize(12).FontColor("#0F172A");
                        }
                        if (!string.IsNullOrWhiteSpace(sede))
                        {
                            col.Item().AlignCenter().Text($"Sede: {sede!.Trim()}").FontSize(10).FontColor("#64748B");
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    /// <summary>Bytes del logo del tenant activo (<see cref="Tenant.LogoUrl"/>, servido desde
    /// wwwroot) para el rotulo separador. Null si no hay tenant, no hay logo o no existe en
    /// disco. No lanza.</summary>
    private async Task<byte[]?> ObtenerLogoTenantBytesAsync(IApplicationDbContext db, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var url = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tid).Select(t => t.LogoUrl).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(url)) { return null; }
        try
        {
            var rel = url.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.Combine(_env.WebRootPath ?? "", rel);
            return File.Exists(full) ? await File.ReadAllBytesAsync(full, ct) : null;
        }
        catch { return null; }
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

    private static string ResolverNombre(string patron, InformeItemDto a, PacienteSnapshotDto p, string? nitIps = null, string? codigoAutorizacion = null)
    {
        var hoy = DateTimeOffset.Now.ToLocalTime();
        // En modo "un archivo por autorizacion" el codigo viene explicito (cada archivo es
        // de UNA autorizacion); si no, se cae al primero del paciente (historico).
        var aut = !string.IsNullOrWhiteSpace(codigoAutorizacion)
            ? codigoAutorizacion!
            : (p.Autorizaciones.Count > 0 ? p.Autorizaciones[0] : "");
        var nombre = (p.Nombre ?? "").Replace(' ', '_');
        var res = patron
            .Replace("{sigla}", a.Alias ?? "")
            .Replace("{cedula}", p.Documento ?? "")
            .Replace("{autorizacion}", aut)
            .Replace("{nombre}", nombre)
            // Tipo de documento del paciente (codigo: CC, RC, TI, CE...).
            .Replace("{tipo_doc}", p.TipoDocumento ?? "")
            // "id de la sede" = codigo de habilitacion de la sede que atendio.
            .Replace("{sede}", p.CodigoHabilitacionSede ?? "")
            // NIT del IPS (tenant). Uno por IPS, igual para todas las sedes.
            .Replace("{nit}", nitIps ?? "")
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
