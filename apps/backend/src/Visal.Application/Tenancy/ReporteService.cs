using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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
    // Cualquier otro placeholder queda tal cual y provocara error de SQL si no existe columna.
    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{tenantId}"] = "@_tenantId",
        ["{sedeId}"]   = "@_sedeId",
        ["{desde}"]    = "@_desde",
        ["{hasta}"]    = "@_hasta"
    };

    public async Task<IReadOnlyList<ReporteConfigDto>> ListarMisReportesAsync(CancellationToken ct = default)
    {
        var userId = tenant.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) { return []; }

        var q = from r in db.ReporteConfigs.AsNoTracking()
                where r.Habilitado
                let asignados = db.ReporteUsuarios.Any(u => u.ReporteConfigId == r.Id)
                let esMio = db.ReporteUsuarios.Any(u => u.ReporteConfigId == r.Id && u.PlatformUserId == userId)
                where !asignados || esMio
                orderby r.Orden, r.Nombre
                select new ReporteConfigDto(r.Id, r.Nombre, r.Descripcion,
                    r.FiltraSede, r.FiltraFechas, r.Habilitado, r.Orden);

        return await q.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReporteConfigDto>> ListarTodosAsync(CancellationToken ct = default)
    {
        return await db.ReporteConfigs.AsNoTracking()
            .OrderBy(x => x.Orden).ThenBy(x => x.Nombre)
            .Select(r => new ReporteConfigDto(r.Id, r.Nombre, r.Descripcion,
                r.FiltraSede, r.FiltraFechas, r.Habilitado, r.Orden))
            .ToListAsync(ct);
    }

    public async Task<ReporteConfigDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.ReporteConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return null; }
        var usuarios = await db.ReporteUsuarios.AsNoTracking()
            .Where(u => u.ReporteConfigId == id)
            .Select(u => u.PlatformUserId)
            .ToListAsync(ct);
        return new ReporteConfigDetalleDto(r.Id, r.Nombre, r.Descripcion, r.QuerySql,
            r.FiltraSede, r.FiltraFechas, r.Habilitado, r.Orden, usuarios);
    }

    public async Task<Guid> CrearAsync(GuardarReporteRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        ValidarSql(req.QuerySql);
        var e = new ReporteConfig
        {
            TenantId = tid,
            Nombre = req.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim(),
            QuerySql = req.QuerySql.Trim(),
            FiltraSede = req.FiltraSede,
            FiltraFechas = req.FiltraFechas,
            Habilitado = req.Habilitado,
            Orden = req.Orden
        };
        db.ReporteConfigs.Add(e);
        await db.SaveChangesAsync(ct);
        await MergeUsuariosAsync(e.Id, req.UsuariosAsignados, tid, ct);
        return e.Id;
    }

    public async Task<bool> ActualizarAsync(Guid id, GuardarReporteRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var e = await db.ReporteConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        ValidarSql(req.QuerySql);
        e.Nombre = req.Nombre.Trim();
        e.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        e.QuerySql = req.QuerySql.Trim();
        e.FiltraSede = req.FiltraSede;
        e.FiltraFechas = req.FiltraFechas;
        e.Habilitado = req.Habilitado;
        e.Orden = req.Orden;
        await db.SaveChangesAsync(ct);
        await MergeUsuariosAsync(e.Id, req.UsuariosAsignados, tid, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.ReporteConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        var links = db.ReporteUsuarios.Where(u => u.ReporteConfigId == id);
        db.ReporteUsuarios.RemoveRange(links);
        db.ReporteConfigs.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task MergeUsuariosAsync(Guid reporteId, IReadOnlyList<Guid>? nuevos, Guid tid, CancellationToken ct)
    {
        var actuales = await db.ReporteUsuarios.Where(u => u.ReporteConfigId == reporteId).ToListAsync(ct);
        var conjuntoNuevo = new HashSet<Guid>(nuevos ?? []);
        var conjuntoActual = actuales.Select(a => a.PlatformUserId).ToHashSet();

        foreach (var a in actuales.Where(x => !conjuntoNuevo.Contains(x.PlatformUserId)))
        {
            db.ReporteUsuarios.Remove(a);
        }
        foreach (var uid in conjuntoNuevo.Where(x => !conjuntoActual.Contains(x)))
        {
            db.ReporteUsuarios.Add(new ReporteUsuario
            {
                TenantId = tid,
                ReporteConfigId = reporteId,
                PlatformUserId = uid
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReporteResultado> EjecutarAsync(Guid reporteId, EjecutarReporteRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var r = await db.ReporteConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reporteId, ct);
        if (r is null) { throw new InvalidOperationException("Reporte no encontrado."); }
        if (!r.Habilitado) { throw new InvalidOperationException("Reporte deshabilitado."); }
        ValidarSql(r.QuerySql);

        // Sustituye tokens por placeholders parametrizados
        var sql = r.QuerySql;
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
    }
}
