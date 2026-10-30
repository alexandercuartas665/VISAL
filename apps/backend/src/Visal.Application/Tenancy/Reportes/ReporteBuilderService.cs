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
    IReportePackService pack,
    IReporteCamposDinamicos dinamicos) : IReporteBuilderService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Mismo contrato que Json pero legible: el JSON exportado lo abre/revisa un humano.
    private static readonly JsonSerializerOptions JsonExport = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    // Marca del sobre portable (para rechazar archivos que no son una plantilla de reporte).
    private const string FormatoExport = "visal.reporte.plantilla";

    // ==================== modelo semantico ====================

    private const string EntidadPaciente = "paciente";

    public async Task<IReadOnlyList<EntidadReportableDto>> ListarEntidadesAsync(CancellationToken ct = default)
    {
        // Campos dinamicos del tenant: se ofrecen como columnas/filtros dentro de la entidad
        // paciente (y de cualquier cruce que apunte a paciente).
        var din = await dinamicos.CargarAsync(ct);
        var dinDtos = din.Select(f => new CampoReportableDto(f.Key, f.Label, f.Kind.ToString())).ToList();

        List<CampoReportableDto> CamposDe(ReportableEntityDef e)
        {
            var lista = e.Fields.Select(f => new CampoReportableDto(f.Key, f.Label, f.Kind.ToString())).ToList();
            if (string.Equals(e.Key, EntidadPaciente, StringComparison.OrdinalIgnoreCase)) { lista.AddRange(dinDtos); }
            return lista;
        }

        return semantic.Entities.Select(e => new EntidadReportableDto(
            e.Key,
            e.Label,
            CamposDe(e),
            e.Relations.Select(r =>
            {
                var target = semantic.Find(r.TargetEntityKey);
                var campos = target is null ? new List<CampoReportableDto>() : CamposDe(target);
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
            p.MostrarEncabezado, p.Codigo, p.Version, p.Subtitulo, DeserializarParametros(p.ParametrosJson));
    }

    public async Task<Guid> CrearCensoAsync(CancellationToken ct = default)
    {
        var req = CensoBlueprint.Crear();
        // Upsert por nombre: si el tenant ya tiene el CENSO, se REGENERA en vez de duplicarlo
        // (GuardarAsync con el id reemplaza sus hojas por las del blueprint actual). Antes se
        // devolvia el id sin tocar las hojas, por lo que los cambios del blueprint (columnas o
        // cruces nuevos) nunca aparecian al volver a pulsar "Plantilla CENSO 2026". El query
        // filter de tenant ya limita la busqueda al tenant actual.
        var existente = await db.ReportePlantillas.AsNoTracking()
            .Where(p => p.Nombre == req.Nombre)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        return await GuardarAsync(existente, req, ct);
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
        plantilla.ParametrosJson = (req.Parametros is { Count: > 0 })
            ? JsonSerializer.Serialize(req.Parametros, Json)
            : null;

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

    public async Task<byte[]> GenerarExcelAsync(
        Guid id,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parametros = null,
        CancellationToken ct = default)
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
        // Resuelve los parametros de ejecucion (globales) antes de generar el SQL: los filtros
        // enlazados a un parametro toman el valor pedido al descargar; sin valor, se descartan.
        var packDef = ReporteParametros.Resolver(new ReportePackDef(p.Nombre, defs), parametros);
        var resultado = await pack.EjecutarAsync(packDef, ct);

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

    public async Task<IReadOnlyList<SedeOpcionDto>> ListarSedesAsync(CancellationToken ct = default)
    {
        // El query filter de tenant limita las sucursales al tenant actual.
        return await db.Sucursales.AsNoTracking()
            .OrderBy(s => s.Nombre)
            .Select(s => new SedeOpcionDto(s.Id, s.Nombre))
            .ToListAsync(ct);
    }

    // ==================== export / import de plantillas ====================

    public async Task<string> ExportarAsync(Guid id, CancellationToken ct = default)
    {
        var d = await ObtenerAsync(id, ct)
            ?? throw new InvalidOperationException("Reporte no encontrado.");
        var dto = new ReportePlantillaExportDto(
            FormatoExport, 1, DateTime.UtcNow,
            d.Nombre, d.Descripcion, d.Categoria, d.Orden, d.Activo, d.Hojas,
            d.MostrarEncabezado, d.Codigo, d.Version, d.Subtitulo, d.Parametros);
        return JsonSerializer.Serialize(dto, JsonExport);
    }

    public async Task<ImportarPlantillaResultado> ImportarAsync(string json, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("El archivo esta vacio.");
        }

        ReportePlantillaExportDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReportePlantillaExportDto>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("El archivo no es un JSON valido: " + ex.Message);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Nombre) || dto.Hojas is null)
        {
            throw new InvalidOperationException("El JSON no corresponde a una plantilla de reporte.");
        }
        if (!string.IsNullOrWhiteSpace(dto.Formato) &&
            !string.Equals(dto.Formato, FormatoExport, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Formato de archivo no reconocido ('{dto.Formato}').");
        }

        // Portabilidad: los campos dinamicos de paciente son datos POR TENANT; no viajan en el JSON.
        // Si una hoja referencia un campo dinamico que este entorno no tiene, el SQL se romperia al
        // generar, asi que lo bloqueamos aqui con un mensaje claro.
        var conocidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in semantic.Entities)
        {
            foreach (var f in e.Fields) { conocidas.Add(f.Key); }
        }
        foreach (var f in await dinamicos.CargarAsync(ct)) { conocidas.Add(f.Key); }

        var faltantes = ReferenciasNoResueltas(dto.Hojas, conocidas);
        if (faltantes.Count > 0)
        {
            throw new InvalidOperationException(
                "No se puede importar: el reporte usa campos que no existen en este entorno: " +
                string.Join(", ", faltantes) +
                ". Si son campos dinamicos de la ficha del paciente, crealos en el gestor de campos y vuelve a importar.");
        }

        var req = new GuardarPlantillaRequest(
            dto.Nombre, dto.Descripcion, dto.Categoria, dto.Orden, dto.Activo, dto.Hojas,
            dto.MostrarEncabezado, dto.Codigo, dto.Version, dto.Subtitulo, dto.Parametros);

        // Upsert por nombre (el query filter de tenant limita la busqueda al tenant actual).
        var nombre = dto.Nombre.Trim();
        var existente = await db.ReportePlantillas.AsNoTracking()
            .Where(p => p.Nombre == nombre)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        var id = await GuardarAsync(existente, req, ct);
        return new ImportarPlantillaResultado(id, nombre, existente is not null);
    }

    /// <summary>
    /// Devuelve las claves de campo (hoja a hoja) que NO estan en <paramref name="conocidas"/>
    /// (union de campos del modelo semantico + campos dinamicos del tenant). Una referencia es
    /// "rel.campo" o "campo"; se compara por la clave final. NO se validan las referencias de
    /// ORDEN porque pueden apuntar al titulo/alias de un agregado, no a un campo. Pura (sin BD):
    /// testeable en aislamiento.
    /// </summary>
    public static IReadOnlyList<string> ReferenciasNoResueltas(
        IReadOnlyList<ReporteHojaDef> hojas, ISet<string> conocidas)
    {
        var faltantes = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bare in hojas.SelectMany(RefsDeHoja).Select(BareKey))
        {
            if (string.IsNullOrWhiteSpace(bare)) { continue; }
            if (conocidas.Contains(bare)) { continue; }
            if (vistos.Add(bare)) { faltantes.Add(bare); }
        }
        return faltantes;
    }

    private static IEnumerable<string> RefsDeHoja(ReporteHojaDef h)
    {
        foreach (var c in h.Columnas) { yield return c.CampoRef; }
        foreach (var g in h.GroupBy) { yield return g.CampoRef; }
        foreach (var a in h.Agregados)
        {
            if (!string.IsNullOrWhiteSpace(a.CampoRef)) { yield return a.CampoRef!; }
        }
        foreach (var f in h.Filtros) { yield return f.CampoRef; }
    }

    private static string BareKey(string campoRef)
    {
        if (string.IsNullOrEmpty(campoRef)) { return ""; }
        var i = campoRef.LastIndexOf('.');
        return (i >= 0 ? campoRef[(i + 1)..] : campoRef).Trim();
    }

    // ==================== helpers ====================

    private static IReadOnlyList<ReporteParametroDef> DeserializarParametros(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return []; }
        try
        {
            return JsonSerializer.Deserialize<List<ReporteParametroDef>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

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
