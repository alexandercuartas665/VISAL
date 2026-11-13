using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.SuperAdmin.Infra;

/// <summary>
/// Implementacion sobre el sistema de archivos. La carpeta de copias se configura con
/// "Backups:Path" (default "/app/backups", que en los servidores es el bind-mount de
/// /opt/visal/backups). La carpeta de uploads es wwwroot/uploads (volumen persistente).
/// </summary>
public sealed class BackupsService : IBackupsService
{
    // Extensiones que consideramos "copia" para rotar (el .snar es un indice de uploads,
    // no una copia de la base; se lista pero NO se rota/borra por rotacion).
    private static readonly string[] ExtCopias = { ".dump", ".sql", ".gz", ".backup", ".tar", ".zip" };

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;
    private readonly IApplicationDbContextFactory _dbFactory;

    public BackupsService(IWebHostEnvironment env, IConfiguration cfg, IApplicationDbContextFactory dbFactory)
    {
        _env = env;
        _cfg = cfg;
        _dbFactory = dbFactory;
    }

    private string CarpetaCopias()
    {
        var p = _cfg["Backups:Path"];
        return string.IsNullOrWhiteSpace(p) ? "/app/backups" : p.Trim();
    }

    private string CarpetaUploads() => Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads");

    public IReadOnlyList<BackupFileInfo> Listar()
    {
        var dir = CarpetaCopias();
        var res = new List<BackupFileInfo>();
        try
        {
            if (!Directory.Exists(dir)) { return res; }
            foreach (var ruta in Directory.EnumerateFiles(dir))
            {
                try
                {
                    var fi = new FileInfo(ruta);
                    res.Add(new BackupFileInfo(fi.Name, fi.Length, fi.LastWriteTime, ClasificarTipo(fi.Name)));
                }
                catch { /* un archivo ilegible no tumba el listado */ }
            }
        }
        catch { /* carpeta inaccesible: devolvemos lo que haya */ }
        return res.OrderByDescending(x => x.Fecha).ToList();
    }

    public BackupResumen Resumen()
    {
        var dir = CarpetaCopias();
        var existe = Directory.Exists(dir);
        var copias = Listar();
        var totalCopias = copias.Sum(x => x.Bytes);
        var uploadsDir = CarpetaUploads();
        var uploads = TamanoCarpeta(uploadsDir);
        var (total, libre) = EspacioDisco(existe ? dir : (Path.GetDirectoryName(dir) ?? dir));
        return new BackupResumen(copias.Count, totalCopias, uploads, total, libre, dir, uploadsDir, existe);
    }

    public string? RutaSegura(string nombre)
    {
        var dir = CarpetaCopias();
        if (string.IsNullOrWhiteSpace(nombre)) { return null; }
        // Solo basename: corta cualquier intento de "../" o ruta absoluta.
        if (!string.Equals(Path.GetFileName(nombre), nombre, StringComparison.Ordinal)) { return null; }
        var full = Path.GetFullPath(Path.Combine(dir, nombre));
        var dirFull = Path.GetFullPath(dir);
        var prefijo = dirFull.EndsWith(Path.DirectorySeparatorChar) ? dirFull : dirFull + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefijo, StringComparison.Ordinal)) { return null; }
        return File.Exists(full) ? full : null;
    }

    public async Task<(bool ok, string? error)> EliminarAsync(string nombre, Guid actor, Guid? tenantId, CancellationToken ct = default)
    {
        var ruta = RutaSegura(nombre);
        if (ruta is null) { return (false, "Nombre de archivo invalido o inexistente."); }
        long bytes = 0;
        try { bytes = new FileInfo(ruta).Length; } catch { }
        try { File.Delete(ruta); }
        catch (Exception ex) { return (false, $"No se pudo borrar: {ex.Message}"); }
        await AuditarAsync("BackupEliminar", actor, tenantId, new { nombre, bytes }, ct);
        return (true, null);
    }

    public async Task<(int borradas, long bytesLiberados)> RotarAsync(int conservar, Guid actor, Guid? tenantId, CancellationToken ct = default)
    {
        if (conservar < 1) { conservar = 1; }
        var copias = Listar()
            .Where(x => ExtCopias.Contains(Path.GetExtension(x.Nombre).ToLowerInvariant()))
            .OrderByDescending(x => x.Fecha)
            .ToList();
        var aBorrar = copias.Skip(conservar).ToList();
        int n = 0; long bytes = 0;
        foreach (var c in aBorrar)
        {
            var ruta = RutaSegura(c.Nombre);
            if (ruta is null) { continue; }
            try { File.Delete(ruta); n++; bytes += c.Bytes; }
            catch { /* si una no se deja borrar, seguimos con las demas */ }
        }
        if (n > 0)
        {
            await AuditarAsync("BackupRotar", actor, tenantId, new { conservar, borradas = n, bytesLiberados = bytes }, ct);
        }
        return (n, bytes);
    }

    private async Task AuditarAsync(string accion, Guid actor, Guid? tenantId, object detalle, CancellationToken ct)
    {
        try
        {
            using var db = _dbFactory.Create();
            db.SuperAdminAuditLogs.Add(new SuperAdminAuditLog
            {
                ActorUserId = actor,
                ActorType = AuditActorType.Human,
                ActionName = accion,
                EntityName = "Backup",
                TenantId = tenantId,
                NewValue = JsonSerializer.Serialize(detalle)
            });
            await db.SaveChangesAsync(ct);
        }
        catch { /* la auditoria no debe tumbar la accion ya realizada */ }
    }

    private static string ClasificarTipo(string nombre)
    {
        var n = nombre.ToLowerInvariant();
        if (n.EndsWith(".snar")) { return "Indice de uploads"; }
        if (n.StartsWith("pre_deploy")) { return "Pre-deploy (manual)"; }
        if (n.StartsWith("pre_update")) { return "Pre-update (automatico)"; }
        if (n.StartsWith("pre_repair")) { return "Pre-reparacion"; }
        if (n.StartsWith("pre_refresh")) { return "Pre-refresh"; }
        return "Otro";
    }

    private static long TamanoCarpeta(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) { return 0; }
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    // Elige el drive/mount cuyo root es el prefijo mas largo de la ruta dada
    // (en Linux "/" matchea todo; un mount especifico de la carpeta matchea mas largo).
    private static (long total, long libre) EspacioDisco(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            DriveInfo? best = null; int bestLen = -1;
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) { continue; }
                    var root = d.RootDirectory.FullName;
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && root.Length > bestLen)
                    {
                        best = d; bestLen = root.Length;
                    }
                }
                catch { }
            }
            if (best is null) { return (0, 0); }
            return (best.TotalSize, best.AvailableFreeSpace);
        }
        catch { return (0, 0); }
    }
}
