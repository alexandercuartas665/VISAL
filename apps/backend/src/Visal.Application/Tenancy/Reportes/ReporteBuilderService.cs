using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Reportes;

public sealed class ReporteBuilderService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IReportSemanticModel semantic,
    IReportePackService pack) : IReporteBuilderService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ==================== modelo semantico ====================

    public IReadOnlyList<EntidadReportableDto> ListarEntidades()
    {
        return semantic.Entities.Select(e => new EntidadReportableDto(
            e.Key,
            e.Label,
            e.Fields.Select(f => new CampoReportableDto(f.Key, f.Label, f.Kind.ToString())).ToList(),
            e.Relations.Select(r =>
            {
                var target = semantic.Find(r.TargetEntityKey);
                var campos = target?.Fields.Select(f => new CampoReportableDto(f.Key, f.Label, f.Kind.ToString())).ToList()
                             ?? [];
                return new RelacionReportableDto(r.Key, r.Label, r.TargetEntityKey, target?.Label ?? r.TargetEntityKey, campos);
            }).ToList()
        )).ToList();
    }

    // ==================== CRUD plantillas ====================

    public async Task<IReadOnlyList<ReportePlantillaDto>> ListarAsync(CancellationToken ct = default)
    {
        return await db.ReportePlantillas.AsNoTracking()
            .OrderBy(p => p.Orden).ThenBy(p => p.Nombre)
            .Select(p => new ReportePlantillaDto(
                p.Id, p.Nombre, p.Descripcion, p.Categoria, p.Orden, p.Activo,
                db.ReporteHojas.Count(h => h.ReportePlantillaId == p.Id)))
            .ToListAsync(ct);
    }

    public async Task<ReportePlantillaDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.ReportePlantillas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return null; }

        var hojas = await db.ReporteHojas.AsNoTracking()
            .Where(h => h.ReportePlantillaId == id)
            .OrderBy(h => h.Orden)
            .ToListAsync(ct);

        var defs = hojas.Select(DeserializarHoja).ToList();
        return new ReportePlantillaDetalleDto(
            p.Id, p.Nombre, p.Descripcion, p.Categoria, p.Orden, p.Activo, defs,
            p.MostrarEncabezado, p.Codigo, p.Version, p.Subtitulo);
    }

    public async Task<Guid> CrearCensoAsync(CancellationToken ct = default)
    {
        var req = CensoBlueprint.Crear();
        // Idempotente: si el tenant ya tiene el CENSO, no lo dupliques (el query filter de
        // tenant ya limita la busqueda al tenant actual).
        var existente = await db.ReportePlantillas.AsNoTracking()
            .Where(p => p.Nombre == req.Nombre)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        return existente ?? await GuardarAsync(null, req, ct);
    }

    public async Task<Guid> GuardarAsync(Guid? id, GuardarPlantillaRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }
        if (string.IsNullOrWhiteSpace(req.Nombre))
        {
            throw new InvalidOperationException("El reporte necesita un nombre.");
        }

        ReportePlantilla plantilla;
        if (id is Guid pid)
        {
            plantilla = await db.ReportePlantillas.FirstOrDefaultAsync(x => x.Id == pid, ct)
                ?? throw new InvalidOperationException("Reporte no encontrado.");
            // Reemplaza las hojas existentes (upsert simple del pack completo).
            var viejas = await db.ReporteHojas.Where(h => h.ReportePlantillaId == pid).ToListAsync(ct);
            db.ReporteHojas.RemoveRange(viejas);
        }
        else
        {
            plantilla = new ReportePlantilla { TenantId = tid };
            db.ReportePlantillas.Add(plantilla);
        }

        plantilla.Nombre = req.Nombre.Trim();
        plantilla.Descripcion = Limpio(req.Descripcion);
        plantilla.Categoria = Limpio(req.Categoria);
        plantilla.Orden = req.Orden;
        plantilla.Activo = req.Activo;
        plantilla.MostrarEncabezado = req.MostrarEncabezado;
        plantilla.Codigo = Limpio(req.Codigo);
        plantilla.Version = Limpio(req.Version);
        plantilla.Subtitulo = Limpio(req.Subtitulo);

        var orden = 0;
        foreach (var hoja in req.Hojas)
        {
            db.ReporteHojas.Add(new ReporteHoja
            {
                TenantId = tid,
                ReportePlantillaId = plantilla.Id,
                Orden = orden++,
                Nombre = string.IsNullOrWhiteSpace(hoja.Nombre) ? $"Hoja {orden}" : hoja.Nombre.Trim(),
                EntidadKey = hoja.EntidadKey,
                Modo = hoja.Modo.ToString(),
                DefinicionJson = JsonSerializer.Serialize(hoja, Json),
            });
        }

        await db.SaveChangesAsync(ct);
        return plantilla.Id;
    }

    public async Task<bool> EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.ReportePlantillas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return false; }
        // Las hojas caen por FK cascade.
        db.ReportePlantillas.Remove(p);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ==================== preview / export ====================

    public async Task<ReporteHojaResultado> PreviewAsync(ReporteHojaDef hoja, int limite = 50, CancellationToken ct = default)
    {
        var acotada = hoja with { LimiteFilas = Math.Clamp(limite, 1, 500) };
        var res = await pack.EjecutarAsync(new ReportePackDef("preview", [acotada]), ct);
        return res.Hojas[0];
    }

    public async Task<byte[]> GenerarExcelAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.ReportePlantillas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Reporte no encontrado.");
        var hojas = await db.ReporteHojas.AsNoTracking()
            .Where(h => h.ReportePlantillaId == id)
            .OrderBy(h => h.Orden)
            .ToListAsync(ct);
        if (hojas.Count == 0)
        {
            throw new InvalidOperationException("El reporte no tiene hojas.");
        }

        var defs = hojas.Select(DeserializarHoja).ToList();
        var resultado = await pack.EjecutarAsync(new ReportePackDef(p.Nombre, defs), ct);

        if (p.MostrarEncabezado)
        {
            var agencia = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == p.TenantId)
                .Select(t => t.LegalName ?? t.Name)
                .FirstOrDefaultAsync(ct) ?? p.Nombre;
            var enc = new ReporteEncabezado(agencia, p.Nombre, p.Subtitulo, p.Codigo, p.Version, DateTime.Now);
            resultado = resultado with { Encabezado = enc };
        }

        return pack.ExportarExcel(resultado);
    }

    // ==================== helpers ====================

    private static ReporteHojaDef DeserializarHoja(ReporteHoja h)
    {
        var def = JsonSerializer.Deserialize<ReporteHojaDef>(h.DefinicionJson, Json);
        // Si el JSON viejo no trae algo, caemos a los campos columna. Nombre/entidad/modo
        // mandan desde la fila para no depender solo del blob.
        if (def is null)
        {
            return ReporteHojaDef.Detalle(h.Nombre, h.EntidadKey, []);
        }
        return def with
        {
            Nombre = h.Nombre,
            EntidadKey = h.EntidadKey,
        };
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
