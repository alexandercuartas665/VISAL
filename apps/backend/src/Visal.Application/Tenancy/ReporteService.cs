using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class ReporteService(
    IApplicationDbContext db,
    ITenantContext tenant) : IReporteService
{
    // Palabras clave bloqueadas para prevenir escritura desde el editor de reportes.
    // Solo aceptamos SELECT (WITH ... SELECT) puros.
    private static readonly string[] BloqueadasSql = new[]
    {
        "insert", "update", "delete", "merge", "alter", "drop", "create",
        "truncate", "grant", "revoke", "commit", "rollback", "call", "vacuum",
        "copy", "reindex", "cluster", "listen", "notify", "lock", "do "
    };

    // Tokens whitelisted que se sustituyen por parametros de comando (@_tenantId etc).
    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{tenantId}"] = "@_tenantId",
        ["{sedeId}"]   = "@_sedeId",
        ["{desde}"]    = "@_desde",
        ["{hasta}"]    = "@_hasta"
    };

    // ==================== Runner (usuario del tenant) ====================

    public async Task<IReadOnlyList<ReporteDto>> ListarMisReportesAsync(CancellationToken ct = default)
    {
        var userId = tenant.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) { return []; }

        // Catalogo global (sin filtro) x activaciones/asignaciones del tenant (auto-filtradas).
        var q = from c in db.ReporteCatalogos.AsNoTracking()
                where c.Habilitado
                join a in db.ReporteTenantActivaciones.AsNoTracking() on c.Id equals a.ReporteCatalogoId
                where a.Activo
                let tieneAsign = db.ReporteUsuarios.Any(u => u.ReporteCatalogoId == c.Id)
                let esMio = db.ReporteUsuarios.Any(u => u.ReporteCatalogoId == c.Id && u.PlatformUserId == userId)
                where !tieneAsign || esMio
                orderby c.Orden, c.Nombre
                select new ReporteDto(c.Id, c.Nombre, c.Descripcion, c.Categoria, c.FiltraSede, c.FiltraFechas, c.Orden);

        return await q.ToListAsync(ct);
    }

    // ==================== Galeria (admin del tenant) ====================

    public async Task<IReadOnlyList<ReporteGaleriaDto>> ListarGaleriaAsync(CancellationToken ct = default)
    {
        var cats = await db.ReporteCatalogos.AsNoTracking()
            .Where(c => c.Habilitado)
            .OrderBy(c => c.Orden).ThenBy(c => c.Nombre)
            .ToListAsync(ct);

        // Estas dos estan auto-filtradas por tenant (ITenantScoped).
        var activaciones = await db.ReporteTenantActivaciones.AsNoTracking().ToListAsync(ct);
        var asignaciones = await db.ReporteUsuarios.AsNoTracking().ToListAsync(ct);

        return cats.Select(c => new ReporteGaleriaDto(
            c.Id, c.Nombre, c.Descripcion, c.Categoria, c.FiltraSede, c.FiltraFechas, c.Orden,
            activaciones.Any(a => a.ReporteCatalogoId == c.Id && a.Activo),
            asignaciones.Where(u => u.ReporteCatalogoId == c.Id).Select(u => u.PlatformUserId).ToList()
        )).ToList();
    }

    public async Task SetActivoAsync(Guid catalogoId, bool activo, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        // El catalogo debe existir y estar habilitado globalmente.
        var existe = await db.ReporteCatalogos.AsNoTracking().AnyAsync(c => c.Id == catalogoId && c.Habilitado, ct);
        if (!existe) { throw new InvalidOperationException("Reporte no disponible en el catalogo."); }

        var a = await db.ReporteTenantActivaciones.FirstOrDefaultAsync(x => x.ReporteCatalogoId == catalogoId, ct);
        if (a is null)
        {
            db.ReporteTenantActivaciones.Add(new ReporteTenantActivacion
            {
                TenantId = tid,
                ReporteCatalogoId = catalogoId,
                Activo = activo
            });
        }
        else
        {
            a.Activo = activo;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SetUsuariosAsync(Guid catalogoId, IReadOnlyList<Guid> usuarios, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var actuales = await db.ReporteUsuarios.Where(u => u.ReporteCatalogoId == catalogoId).ToListAsync(ct);
        var nuevoSet = new HashSet<Guid>(usuarios ?? []);
        var actualSet = actuales.Select(a => a.PlatformUserId).ToHashSet();

        foreach (var a in actuales.Where(x => !nuevoSet.Contains(x.PlatformUserId)))
        {
            db.ReporteUsuarios.Remove(a);
        }
        foreach (var uid in nuevoSet.Where(x => !actualSet.Contains(x)))
        {
            db.ReporteUsuarios.Add(new ReporteUsuario
            {
                TenantId = tid,
                ReporteCatalogoId = catalogoId,
                PlatformUserId = uid
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // ==================== Catalogo (Super Admin) ====================

    public async Task<IReadOnlyList<ReporteCatalogoDto>> ListarCatalogoAsync(CancellationToken ct = default)
    {
        return await db.ReporteCatalogos.AsNoTracking()
            .OrderBy(x => x.Orden).ThenBy(x => x.Nombre)
            .Select(c => new ReporteCatalogoDto(c.Id, c.Nombre, c.Descripcion, c.Categoria,
                c.FiltraSede, c.FiltraFechas, c.Habilitado, c.Orden))
            .ToListAsync(ct);
    }

    public async Task<ReporteCatalogoDetalleDto?> ObtenerCatalogoAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.ReporteCatalogos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return null; }
        return new ReporteCatalogoDetalleDto(c.Id, c.Nombre, c.Descripcion, c.Categoria, c.QuerySql,
            c.FiltraSede, c.FiltraFechas, c.Habilitado, c.Orden);
    }

    public async Task<Guid> CrearCatalogoAsync(GuardarCatalogoRequest req, Guid actor, CancellationToken ct = default)
    {
        ValidarSql(req.QuerySql);
        var c = new ReporteCatalogo
        {
            Nombre = req.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim(),
            Categoria = string.IsNullOrWhiteSpace(req.Categoria) ? null : req.Categoria.Trim(),
            QuerySql = req.QuerySql.Trim(),
            FiltraSede = req.FiltraSede,
            FiltraFechas = req.FiltraFechas,
            Habilitado = req.Habilitado,
            Orden = req.Orden
        };
        db.ReporteCatalogos.Add(c);
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task<bool> ActualizarCatalogoAsync(Guid id, GuardarCatalogoRequest req, Guid actor, CancellationToken ct = default)
    {
        var c = await db.ReporteCatalogos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return false; }
        ValidarSql(req.QuerySql);
        c.Nombre = req.Nombre.Trim();
        c.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        c.Categoria = string.IsNullOrWhiteSpace(req.Categoria) ? null : req.Categoria.Trim();
        c.QuerySql = req.QuerySql.Trim();
        c.FiltraSede = req.FiltraSede;
        c.FiltraFechas = req.FiltraFechas;
        c.Habilitado = req.Habilitado;
        c.Orden = req.Orden;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EliminarCatalogoAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var c = await db.ReporteCatalogos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return false; }
        // Borra activaciones y asignaciones de TODOS los tenants (ignora el filtro de tenant).
        var acts = await db.ReporteTenantActivaciones.IgnoreQueryFilters()
            .Where(a => a.ReporteCatalogoId == id).ToListAsync(ct);
        var asigs = await db.ReporteUsuarios.IgnoreQueryFilters()
            .Where(u => u.ReporteCatalogoId == id).ToListAsync(ct);
        db.ReporteTenantActivaciones.RemoveRange(acts);
        db.ReporteUsuarios.RemoveRange(asigs);
        db.ReporteCatalogos.Remove(c);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ==================== Ejecucion ====================

    public async Task<ReporteResultado> EjecutarAsync(Guid catalogoId, EjecutarReporteRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var userId = tenant.UserId ?? Guid.Empty;

        var c = await db.ReporteCatalogos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == catalogoId, ct);
        if (c is null || !c.Habilitado) { throw new InvalidOperationException("Reporte no encontrado o deshabilitado."); }

        // El tenant debe tenerlo activo.
        var activo = await db.ReporteTenantActivaciones.AsNoTracking()
            .AnyAsync(a => a.ReporteCatalogoId == catalogoId && a.Activo, ct);
        if (!activo) { throw new InvalidOperationException("El reporte no esta activo para esta agencia."); }

        // Si hay asignaciones, el usuario debe estar en la lista.
        var tieneAsign = await db.ReporteUsuarios.AsNoTracking().AnyAsync(u => u.ReporteCatalogoId == catalogoId, ct);
        if (tieneAsign)
        {
            var permitido = await db.ReporteUsuarios.AsNoTracking()
                .AnyAsync(u => u.ReporteCatalogoId == catalogoId && u.PlatformUserId == userId, ct);
            if (!permitido) { throw new InvalidOperationException("No tiene permiso para ver este reporte."); }
        }

        ValidarSql(c.QuerySql);

        // Sustituye tokens por placeholders parametrizados.
        var sql = c.QuerySql;
        foreach (var (token, param) in TokenMap)
        {
            sql = Regex.Replace(sql, Regex.Escape(token), param, RegexOptions.IgnoreCase);
        }

        var ctx = (DbContext)db;
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) { await conn.OpenAsync(ct); }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 30;

        AddParam(cmd, "_tenantId", tid);
        AddParam(cmd, "_sedeId",   req.SedeId as object ?? DBNull.Value);
        AddParam(cmd, "_desde",    req.Desde.HasValue ? req.Desde.Value.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        AddParam(cmd, "_hasta",    req.Hasta.HasValue ? req.Hasta.Value.ToDateTime(TimeOnly.MaxValue) : (object)DBNull.Value);

        var columnas = new List<string>();
        var filas = new List<IReadOnlyList<object?>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        for (var i = 0; i < rd.FieldCount; i++) { columnas.Add(rd.GetName(i)); }
        while (await rd.ReadAsync(ct))
        {
            var row = new object?[rd.FieldCount];
            for (var i = 0; i < rd.FieldCount; i++)
            {
                var v = rd.GetValue(i);
                row[i] = v is DBNull ? null : v;
            }
            filas.Add(row);
            if (filas.Count >= 10000) { break; } // salvaguarda
        }
        return new ReporteResultado(columnas, filas, filas.Count);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void ValidarSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) { throw new InvalidOperationException("El SQL del reporte no puede estar vacio."); }
        var lower = sql.ToLowerInvariant();
        foreach (var pal in BloqueadasSql)
        {
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(pal.Trim())}\b"))
            {
                throw new InvalidOperationException($"El SQL no puede contener la palabra clave '{pal.Trim()}'. Solo se permiten SELECT.");
            }
        }
        if (!Regex.IsMatch(lower, @"^\s*(with|select)\b"))
        {
            throw new InvalidOperationException("El SQL debe iniciar con SELECT o WITH ... SELECT.");
        }
        if (sql.Contains(';'))
        {
            throw new InvalidOperationException("El SQL no debe contener ';' (una sola sentencia por reporte).");
        }
        // Hardening multi-tenant: el SQL DEBE aislar por tenant con el token {tenantId}.
        if (!sql.Contains("{tenantId}", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El SQL debe incluir el token {tenantId} para aislar los datos por agencia (multi-tenant).");
        }
    }
}
