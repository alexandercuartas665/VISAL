using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class CursoService : ICursoService
{
    private static readonly string[] TiposLeccion = ["VIDEO", "IMAGEN", "PDF", "TEXTO"];

    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;
    private readonly IUploadStorage _uploads;

    public CursoService(IApplicationDbContextFactory dbFactory, ITenantContext tenant, IUploadStorage uploads)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
        _uploads = uploads;
    }

    // ---------- Cursos ----------

    public async Task<IReadOnlyList<CursoDto>> ListarCursosAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await db.Cursos.AsNoTracking().OrderBy(c => c.Titulo)
            .Select(c => new CursoDto(
                c.Id, c.Titulo, c.Descripcion, c.Activo,
                c.CuestionarioId,
                c.CuestionarioId == null ? null : db.Cuestionarios.Where(q => q.Id == c.CuestionarioId).Select(q => q.Titulo).FirstOrDefault(),
                db.CursoModulos.Count(m => m.CursoId == c.Id),
                db.CursoLecciones.Count(l => db.CursoModulos.Any(m => m.CursoId == c.Id && m.Id == l.CursoModuloId))))
            .ToListAsync(ct);
    }

    public async Task<CursoDetalleDto?> DetalleAsync(Guid cursoId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var curso = await db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return null; }

        var modulos = await db.CursoModulos.AsNoTracking()
            .Where(m => m.CursoId == cursoId).OrderBy(m => m.Orden)
            .Select(m => new { m.Id, m.Titulo, m.Descripcion, m.Orden })
            .ToListAsync(ct);

        var moduloIds = modulos.Select(m => m.Id).ToList();
        var lecciones = await db.CursoLecciones.AsNoTracking()
            .Where(l => moduloIds.Contains(l.CursoModuloId)).OrderBy(l => l.Orden)
            .Select(l => new CursoLeccionDto(l.Id, l.CursoModuloId, l.Titulo, l.Descripcion, l.Orden,
                l.Tipo, l.RutaArchivo, l.Mime, l.TamanoBytes, l.Contenido, l.DescripcionHtml))
            .ToListAsync(ct);

        return new CursoDetalleDto(curso.Id, curso.Titulo, curso.Descripcion, curso.Activo, curso.CuestionarioId,
            modulos.Select(m => new CursoModuloConLeccionesDto(m.Id, m.Titulo, m.Descripcion, m.Orden,
                lecciones.Where(l => l.CursoModuloId == m.Id).ToList())).ToList());
    }

    public async Task<Guid?> GuardarCursoAsync(GuardarCursoRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { throw new InvalidOperationException("El titulo del curso es obligatorio."); }

        Curso curso;
        if (req.Id is Guid id)
        {
            curso = await db.Cursos.FirstOrDefaultAsync(c => c.Id == id, ct)
                    ?? throw new InvalidOperationException("El curso ya no existe.");
        }
        else
        {
            curso = new Curso { TenantId = tenantId };
            db.Cursos.Add(curso);
        }
        curso.Titulo = titulo;
        curso.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        curso.Activo = req.Activo;
        curso.CuestionarioId = req.CuestionarioId;
        await db.SaveChangesAsync(ct);
        return curso.Id;
    }

    public async Task<bool> EliminarCursoAsync(Guid cursoId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var curso = await db.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return false; }
        // La cascada de BD borra modulos y lecciones. Los archivos fisicos en
        // wwwroot/uploads/cursos quedan huerfanos (limpieza pendiente de fase 2).
        db.Cursos.Remove(curso);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Modulos ----------

    public async Task<Guid?> GuardarModuloAsync(GuardarModuloRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { throw new InvalidOperationException("El titulo del modulo es obligatorio."); }

        CursoModulo modulo;
        if (req.Id is Guid id)
        {
            modulo = await db.CursoModulos.FirstOrDefaultAsync(m => m.Id == id, ct)
                     ?? throw new InvalidOperationException("El modulo ya no existe.");
        }
        else
        {
            if (!await db.Cursos.AnyAsync(c => c.Id == req.CursoId, ct)) { throw new InvalidOperationException("El curso no existe."); }
            var maxOrden = await db.CursoModulos.Where(m => m.CursoId == req.CursoId).MaxAsync(m => (int?)m.Orden, ct) ?? 0;
            modulo = new CursoModulo { TenantId = tenantId, CursoId = req.CursoId, Orden = maxOrden + 1 };
            db.CursoModulos.Add(modulo);
        }
        modulo.Titulo = titulo;
        modulo.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        await db.SaveChangesAsync(ct);
        return modulo.Id;
    }

    public async Task<bool> EliminarModuloAsync(Guid moduloId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var modulo = await db.CursoModulos.FirstOrDefaultAsync(m => m.Id == moduloId, ct);
        if (modulo is null) { return false; }
        db.CursoModulos.Remove(modulo);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Lecciones ----------

    public async Task<Guid?> GuardarLeccionAsync(GuardarLeccionRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { throw new InvalidOperationException("El titulo de la leccion es obligatorio."); }
        var tipo = (req.Tipo ?? "").Trim().ToUpperInvariant();
        if (!TiposLeccion.Contains(tipo)) { throw new InvalidOperationException("Tipo de leccion invalido."); }

        CursoLeccion leccion;
        if (req.Id is Guid id)
        {
            leccion = await db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == id, ct)
                      ?? throw new InvalidOperationException("La leccion ya no existe.");
        }
        else
        {
            if (!await db.CursoModulos.AnyAsync(m => m.Id == req.CursoModuloId, ct)) { throw new InvalidOperationException("El modulo no existe."); }
            var maxOrden = await db.CursoLecciones.Where(l => l.CursoModuloId == req.CursoModuloId).MaxAsync(l => (int?)l.Orden, ct) ?? 0;
            leccion = new CursoLeccion { TenantId = tenantId, CursoModuloId = req.CursoModuloId, Orden = maxOrden + 1 };
            db.CursoLecciones.Add(leccion);
        }
        leccion.Titulo = titulo;
        leccion.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        leccion.Tipo = tipo;
        leccion.Contenido = tipo == "TEXTO" ? req.Contenido : leccion.Contenido;
        await db.SaveChangesAsync(ct);
        return leccion.Id;
    }

    public async Task<bool> SubirRecursoAsync(Guid leccionId, byte[] contenido, string nombreArchivo, string mime, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid) { return false; }
        var leccion = await db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { throw new InvalidOperationException("La leccion ya no existe."); }
        if (leccion.Tipo == "TEXTO") { throw new InvalidOperationException("Una leccion de texto no lleva archivo."); }

        var ext = ResolverExtension(nombreArchivo, mime);
        var nombre = $"{leccion.Id:N}{ext}";
        var ruta = await _uploads.GuardarAsync("cursos", nombre, contenido, ct);

        leccion.RutaArchivo = ruta;
        leccion.Mime = mime;
        leccion.TamanoBytes = contenido.LongLength;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> GuardarDescripcionHtmlAsync(Guid leccionId, string? html, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid) { return false; }
        var leccion = await db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { throw new InvalidOperationException("La leccion ya no existe."); }
        leccion.DescripcionHtml = LeccionHtmlSanitizer.Clean(html);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EliminarLeccionAsync(Guid leccionId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var leccion = await db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { return false; }
        db.CursoLecciones.Remove(leccion);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Catalogo de cuestionarios ----------

    public async Task<IReadOnlyList<(Guid Id, string Titulo)>> CuestionariosAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return (await db.Cuestionarios.AsNoTracking().Where(q => q.Activo).OrderBy(q => q.Titulo)
            .Select(q => new { q.Id, q.Titulo }).ToListAsync(ct))
            .Select(q => (q.Id, q.Titulo)).ToList();
    }

    // ---------- Editor de la evaluacion (cuestionario + preguntas) ----------

    public async Task<IReadOnlyList<CuestionarioAdminDto>> CuestionariosAdminAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await db.Cuestionarios.AsNoTracking().OrderBy(q => q.Titulo)
            .Select(q => new CuestionarioAdminDto(q.Id, q.Modulo, q.Titulo, q.Descripcion, q.PuntajeMinimo, q.Activo,
                db.CuestionarioPreguntas.Count(p => p.CuestionarioId == q.Id)))
            .ToListAsync(ct);
    }

    public async Task<CuestionarioDetalleAdminDto?> CuestionarioAsync(Guid id, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var q = await db.Cuestionarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) { return null; }
        var preguntas = await db.CuestionarioPreguntas.AsNoTracking()
            .Where(p => p.CuestionarioId == id).OrderBy(p => p.Orden).ToListAsync(ct);
        var cab = new CuestionarioAdminDto(q.Id, q.Modulo, q.Titulo, q.Descripcion, q.PuntajeMinimo, q.Activo, preguntas.Count);
        var lista = preguntas.Select(p => new PreguntaAdminDto(
            p.Id, p.Orden, p.Enunciado, ParseOpciones(p.OpcionesJson), p.IndiceCorrecto, p.Retroalimentacion)).ToList();
        return new CuestionarioDetalleAdminDto(cab, lista);
    }

    public async Task<Guid?> GuardarCuestionarioAsync(GuardarCuestionarioRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { throw new InvalidOperationException("El titulo de la evaluacion es obligatorio."); }
        CuestionarioCapacitacion q;
        if (req.Id is Guid id)
        {
            q = await db.Cuestionarios.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException("La evaluacion ya no existe.");
        }
        else
        {
            q = new CuestionarioCapacitacion { TenantId = tenantId, Modulo = string.IsNullOrWhiteSpace(req.Modulo) ? "CAPACITACION" : req.Modulo.Trim() };
            db.Cuestionarios.Add(q);
        }
        q.Titulo = titulo;
        q.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        q.PuntajeMinimo = Math.Clamp(req.PuntajeMinimo, 0, 100);
        q.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return q.Id;
    }

    public async Task<bool> EliminarCuestionarioAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var q = await db.Cuestionarios.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) { return false; }
        var preguntas = await db.CuestionarioPreguntas.Where(p => p.CuestionarioId == id).ToListAsync(ct);
        db.CuestionarioPreguntas.RemoveRange(preguntas);
        db.Cuestionarios.Remove(q);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid?> GuardarPreguntaAsync(GuardarPreguntaRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var enunciado = (req.Enunciado ?? "").Trim();
        var opciones = (req.Opciones ?? new()).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).ToList();
        if (enunciado.Length == 0) { throw new InvalidOperationException("El enunciado es obligatorio."); }
        if (opciones.Count < 2) { throw new InvalidOperationException("La pregunta necesita al menos 2 opciones."); }
        if (req.IndiceCorrecto < 0 || req.IndiceCorrecto >= opciones.Count) { throw new InvalidOperationException("Marca cual de las opciones es la respuesta correcta."); }
        if (!await db.Cuestionarios.AnyAsync(x => x.Id == req.CuestionarioId, ct)) { throw new InvalidOperationException("La evaluacion no existe."); }

        CuestionarioPregunta p;
        if (req.Id is Guid id)
        {
            p = await db.CuestionarioPreguntas.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException("La pregunta ya no existe.");
        }
        else
        {
            var maxOrden = await db.CuestionarioPreguntas.Where(x => x.CuestionarioId == req.CuestionarioId).Select(x => (int?)x.Orden).MaxAsync(ct) ?? 0;
            p = new CuestionarioPregunta { TenantId = tenantId, CuestionarioId = req.CuestionarioId, Orden = maxOrden + 1 };
            db.CuestionarioPreguntas.Add(p);
        }
        p.Enunciado = enunciado;
        p.OpcionesJson = System.Text.Json.JsonSerializer.Serialize(opciones);
        p.IndiceCorrecto = req.IndiceCorrecto;
        p.Retroalimentacion = string.IsNullOrWhiteSpace(req.Retroalimentacion) ? null : req.Retroalimentacion.Trim();
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task<bool> EliminarPreguntaAsync(Guid preguntaId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var p = await db.CuestionarioPreguntas.FirstOrDefaultAsync(x => x.Id == preguntaId, ct);
        if (p is null) { return false; }
        db.CuestionarioPreguntas.Remove(p);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> MoverPreguntaAsync(Guid preguntaId, bool arriba, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var p = await db.CuestionarioPreguntas.FirstOrDefaultAsync(x => x.Id == preguntaId, ct);
        if (p is null) { return false; }
        var vecina = arriba
            ? await db.CuestionarioPreguntas.Where(x => x.CuestionarioId == p.CuestionarioId && x.Orden < p.Orden).OrderByDescending(x => x.Orden).FirstOrDefaultAsync(ct)
            : await db.CuestionarioPreguntas.Where(x => x.CuestionarioId == p.CuestionarioId && x.Orden > p.Orden).OrderBy(x => x.Orden).FirstOrDefaultAsync(ct);
        if (vecina is null) { return false; }
        (p.Orden, vecina.Orden) = (vecina.Orden, p.Orden);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Helpers ----------

    private static IReadOnlyList<string> ParseOpciones(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Extension del archivo subido: la del nombre original si la trae, si no
    /// una derivada del mime (para que el navegador lo sirva con el tipo correcto).</summary>
    private static string ResolverExtension(string? nombreArchivo, string? mime)
    {
        var ext = Path.GetExtension(nombreArchivo ?? "");
        if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 6) { return ext.ToLowerInvariant(); }
        return (mime ?? "").ToLowerInvariant() switch
        {
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/ogg" => ".ogv",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".bin",
        };
    }
}
