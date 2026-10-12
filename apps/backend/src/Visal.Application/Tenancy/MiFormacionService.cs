using System.Text.Json;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

/// <summary>
/// Lado alumno del modulo de capacitaciones. El usuario esta logueado, asi que el
/// filtro global por tenant ya aplica; todo se acota ademas al usuario actual
/// (platform_user_id) para su progreso e intentos.
/// </summary>
public sealed class MiFormacionService : IMiFormacionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public MiFormacionService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<MiCursoDto>> ListarMisCursosAsync(CancellationToken ct = default)
    {
        if (_tenant.UserId is not Guid uid) { return Array.Empty<MiCursoDto>(); }

        var cursos = await _db.Cursos.AsNoTracking().Where(c => c.Activo).OrderBy(c => c.Titulo)
            .Select(c => new
            {
                c.Id, c.Titulo, c.Descripcion, c.CuestionarioId,
                Modulos = _db.CursoModulos.Count(m => m.CursoId == c.Id),
                Lecciones = _db.CursoLecciones.Count(l => _db.CursoModulos.Any(m => m.CursoId == c.Id && m.Id == l.CursoModuloId)),
            })
            .ToListAsync(ct);

        var cursoIds = cursos.Select(c => c.Id).ToList();
        var progreso = await _db.CursoProgresos.AsNoTracking()
            .Where(p => p.UsuarioId == uid && cursoIds.Contains(p.CursoId))
            .Select(p => new { p.CursoId, p.FechaInicio, p.Aprobado, p.MejorNota })
            .ToListAsync(ct);
        var porCurso = progreso.ToDictionary(p => p.CursoId);

        return cursos.Select(c =>
        {
            porCurso.TryGetValue(c.Id, out var p);
            return new MiCursoDto(
                c.Id, c.Titulo, c.Descripcion, c.Modulos, c.Lecciones,
                c.CuestionarioId != null,
                p is not null && p.FechaInicio != null,
                p?.Aprobado ?? false,
                p?.MejorNota ?? 0);
        }).ToList();
    }

    public async Task<CursoDetalleDto?> CursoAsync(Guid cursoId, CancellationToken ct = default)
    {
        var curso = await _db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId && c.Activo, ct);
        if (curso is null) { return null; }

        var modulos = await _db.CursoModulos.AsNoTracking()
            .Where(m => m.CursoId == cursoId).OrderBy(m => m.Orden)
            .Select(m => new { m.Id, m.Titulo, m.Descripcion, m.Orden })
            .ToListAsync(ct);

        var moduloIds = modulos.Select(m => m.Id).ToList();
        var lecciones = await _db.CursoLecciones.AsNoTracking()
            .Where(l => moduloIds.Contains(l.CursoModuloId)).OrderBy(l => l.Orden)
            .Select(l => new CursoLeccionDto(l.Id, l.CursoModuloId, l.Titulo, l.Descripcion, l.Orden,
                l.Tipo, l.RutaArchivo, l.Mime, l.TamanoBytes, l.Contenido, l.DescripcionHtml))
            .ToListAsync(ct);

        return new CursoDetalleDto(curso.Id, curso.Titulo, curso.Descripcion, curso.Activo, curso.CuestionarioId,
            modulos.Select(m => new CursoModuloConLeccionesDto(m.Id, m.Titulo, m.Descripcion, m.Orden,
                lecciones.Where(l => l.CursoModuloId == m.Id).ToList())).ToList());
    }

    public async Task RegistrarInicioAsync(Guid cursoId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid || _tenant.UserId is not Guid uid) { return; }
        if (!await _db.Cursos.AnyAsync(c => c.Id == cursoId && c.Activo, ct)) { return; }

        var prog = await _db.CursoProgresos.FirstOrDefaultAsync(p => p.CursoId == cursoId && p.UsuarioId == uid, ct);
        if (prog is null)
        {
            prog = new CursoProgreso { TenantId = tid, CursoId = cursoId, UsuarioId = uid, FechaInicio = DateTimeOffset.UtcNow };
            _db.CursoProgresos.Add(prog);
            await _db.SaveChangesAsync(ct);
        }
        else if (prog.FechaInicio is null)
        {
            prog.FechaInicio = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<EvaluacionAlumnoDto?> EvaluacionAsync(Guid cursoId, CancellationToken ct = default)
    {
        var cuest = await CuestionarioDelCursoAsync(cursoId, ct);
        if (cuest is null) { return null; }

        var preguntas = await _db.CuestionarioPreguntas.AsNoTracking()
            .Where(p => p.CuestionarioId == cuest.Id).OrderBy(p => p.Orden)
            .Select(p => new { p.Id, p.Orden, p.Enunciado, p.OpcionesJson })
            .ToListAsync(ct);

        // El indice correcto nunca sale del servidor: solo enunciado y opciones.
        return new EvaluacionAlumnoDto(cuest.Id, cuest.Titulo, cuest.Descripcion, cuest.PuntajeMinimo,
            preguntas.Select(p => new PreguntaAlumnoDto(p.Id, p.Orden, p.Enunciado, Opciones(p.OpcionesJson))).ToList());
    }

    public async Task<EstadoEvaluacionDto> EstadoAsync(Guid cursoId, CancellationToken ct = default)
    {
        var cuest = await CuestionarioDelCursoAsync(cursoId, ct);
        if (cuest is null || _tenant.UserId is not Guid uid) { return new EstadoEvaluacionDto(false, 0, 0, cuest is not null); }

        var intentos = await _db.CuestionarioIntentos.AsNoTracking()
            .Where(i => i.CuestionarioId == cuest.Id && i.UsuarioId == uid)
            .Select(i => new { i.Puntaje, i.Aprobado }).ToListAsync(ct);

        return new EstadoEvaluacionDto(
            intentos.Any(i => i.Aprobado),
            intentos.Count,
            intentos.Count == 0 ? 0 : intentos.Max(i => i.Puntaje),
            true);
    }

    public async Task<ResultadoIntentoDto?> ResponderAsync(Guid cursoId, IReadOnlyList<int> respuestas, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid || _tenant.UserId is not Guid uid) { throw new InvalidOperationException("Sesion invalida."); }

        var cuest = await CuestionarioDelCursoAsync(cursoId, ct);
        if (cuest is null) { return null; }

        var preguntas = await _db.CuestionarioPreguntas.AsNoTracking()
            .Where(p => p.CuestionarioId == cuest.Id).OrderBy(p => p.Orden)
            .Select(p => new { p.IndiceCorrecto, p.Retroalimentacion })
            .ToListAsync(ct);
        if (preguntas.Count == 0) { return null; }

        var correctas = 0;
        var retro = new List<string>();
        for (var i = 0; i < preguntas.Count; i++)
        {
            var marcada = i < respuestas.Count ? respuestas[i] : -1;
            if (marcada == preguntas[i].IndiceCorrecto) { correctas++; }
            else if (!string.IsNullOrWhiteSpace(preguntas[i].Retroalimentacion)) { retro.Add(preguntas[i].Retroalimentacion!); }
        }

        var puntaje = (int)Math.Round(correctas * 100d / preguntas.Count);
        var aprobado = puntaje >= cuest.PuntajeMinimo;
        var ahora = DateTimeOffset.UtcNow;

        _db.CuestionarioIntentos.Add(new CuestionarioIntento
        {
            TenantId = tid,
            CuestionarioId = cuest.Id,
            UsuarioId = uid,
            Puntaje = puntaje,
            Aprobado = aprobado,
            RespuestasJson = JsonSerializer.Serialize(respuestas),
            FechaIntento = ahora,
        });

        // Consolida el avance del curso para el usuario.
        var prog = await _db.CursoProgresos.FirstOrDefaultAsync(p => p.CursoId == cursoId && p.UsuarioId == uid, ct);
        if (prog is null)
        {
            prog = new CursoProgreso { TenantId = tid, CursoId = cursoId, UsuarioId = uid, FechaInicio = ahora };
            _db.CursoProgresos.Add(prog);
        }
        prog.Intentos += 1;
        if (puntaje > prog.MejorNota) { prog.MejorNota = puntaje; }
        if (aprobado && !prog.Aprobado) { prog.Aprobado = true; prog.FechaAprobacion = ahora; }
        await _db.SaveChangesAsync(ct);

        return new ResultadoIntentoDto(puntaje, aprobado, correctas, preguntas.Count, retro);
    }

    // ---------- Helpers ----------

    /// <summary>Cuestionario (evaluacion) activo del curso activo, o null.</summary>
    private async Task<CuestionarioCapacitacion?> CuestionarioDelCursoAsync(Guid cursoId, CancellationToken ct)
    {
        var qid = await _db.Cursos.AsNoTracking()
            .Where(c => c.Id == cursoId && c.Activo).Select(c => c.CuestionarioId).FirstOrDefaultAsync(ct);
        if (qid is not Guid id) { return null; }
        return await _db.Cuestionarios.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.Activo, ct);
    }

    private static IReadOnlyList<string> Opciones(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new List<string>(); }
    }
}
