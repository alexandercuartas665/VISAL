using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Turnos;

public sealed class TurnoProgramacionService(
    IApplicationDbContext db,
    ITenantContext tenant) : ITurnoProgramacionService
{
    private const string KeyBloquearOverload = "turnos.bloquear_overload";

    /// <summary>Lee del catalogo TenantConfiguration si el tenant activo bloquea
    /// el guardado ante overload (>24h/dia). Default false = solo warning en UI.</summary>
    private async Task<bool> LeerBloqueoOverloadAsync(CancellationToken ct)
    {
        var cfg = await db.TenantConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == KeyBloquearOverload, ct);
        return cfg is not null
            && bool.TryParse(cfg.ConfigValue, out var v)
            && v;
    }

    public async Task<IReadOnlyList<TurnoProgramacionDto>> ListarAsync(
        Guid? sucursalId, Guid? tipoServicioId, int? anio, int? mes, bool soloActivas,
        CancellationToken ct = default)
    {
        var q = db.TurnoProgramaciones.AsNoTracking();
        if (sucursalId is Guid sid) { q = q.Where(x => x.SucursalId == sid); }
        if (tipoServicioId is Guid tsid) { q = q.Where(x => x.TipoServicioId == tsid); }
        if (anio is int a) { q = q.Where(x => x.Anio == a); }
        if (mes is int m) { q = q.Where(x => x.Mes == m); }
        if (soloActivas) { q = q.Where(x => x.Activa); }

        var rows = await q
            .OrderByDescending(x => x.Anio).ThenByDescending(x => x.Mes).ThenBy(x => x.Nombre)
            .Select(x => new
            {
                x.Id, x.SucursalId, x.TipoServicioId, x.Nombre, x.Anio, x.Mes,
                x.GridDataJson, x.Activa
            })
            .ToListAsync(ct);

        // Sucursales + tipos servicio para pintar nombres. Se cargan aparte para no
        // atarse a includes que traen columnas grandes (json_schema, etc).
        var sucIds = rows.Where(r => r.SucursalId is not null).Select(r => r.SucursalId!.Value).Distinct().ToArray();
        var sucursales = sucIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Sucursales.AsNoTracking()
                .Where(s => sucIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Nombre, ct);

        var tsIds = rows.Where(r => r.TipoServicioId is not null).Select(r => r.TipoServicioId!.Value).Distinct().ToArray();
        var tiposServ = tsIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.CatalogosTipoServicio.AsNoTracking()
                .Where(t => tsIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        return rows.Select(r =>
        {
            var grid = GridDataModel.FromJson(r.GridDataJson);
            var sucNombre = r.SucursalId is Guid s ? (sucursales.GetValueOrDefault(s)) : null;
            var tsNombre = r.TipoServicioId is Guid t ? (tiposServ.GetValueOrDefault(t)) : null;
            return new TurnoProgramacionDto(
                r.Id, r.SucursalId, sucNombre, r.TipoServicioId, tsNombre,
                r.Nombre, r.Anio, r.Mes, grid.Turnos.Count, r.Activa);
        }).ToList();
    }

    public async Task<TurnoProgramacionDetailDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.TurnoProgramaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null
            ? null
            : new TurnoProgramacionDetailDto(
                e.Id, e.SucursalId, e.TipoServicioId, e.Nombre, e.Anio, e.Mes,
                e.Descripcion, e.GridDataJson, e.Activa);
    }

    public async Task<Guid> CrearAsync(CrearTurnoProgramacionCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var nombre = NormalizarNombre(cmd.Nombre);
        ValidarMesAnio(cmd.Mes, cmd.Anio);

        // Unicidad (tenant, sede, anio, mes, nombre). Case-insensitive para evitar
        // "Rotacion A" vs "rotacion a".
        var duplicado = await db.TurnoProgramaciones.AsNoTracking()
            .AnyAsync(x => x.SucursalId == cmd.SucursalId
                        && x.Anio == cmd.Anio
                        && x.Mes == cmd.Mes
                        && x.Nombre.ToLower() == nombre.ToLower(), ct);
        if (duplicado)
        {
            throw new InvalidOperationException(
                $"Ya existe una programacion '{nombre}' para ese periodo en esa sede.");
        }

        var bloquear = await LeerBloqueoOverloadAsync(ct);
        var grid = GridDataModel.FromJson(cmd.GridDataJson);
        var err = grid.Validate(bloquear, DateTime.DaysInMonth(cmd.Anio, cmd.Mes));
        if (err is not null) { throw new InvalidOperationException(err); }

        var e = new TurnoProgramacion
        {
            TenantId = tid,
            SucursalId = cmd.SucursalId,
            TipoServicioId = cmd.TipoServicioId,
            Nombre = nombre,
            Anio = cmd.Anio,
            Mes = cmd.Mes,
            Descripcion = string.IsNullOrWhiteSpace(cmd.Descripcion) ? null : cmd.Descripcion.Trim(),
            GridDataJson = grid.ToJson(),
            Activa = true
        };
        db.TurnoProgramaciones.Add(e);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task ActualizarAsync(Guid id, ActualizarTurnoProgramacionCmd cmd, Guid actor, CancellationToken ct = default)
    {
        var e = await db.TurnoProgramaciones.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Programacion no encontrada.");
        var nombre = NormalizarNombre(cmd.Nombre);
        ValidarMesAnio(cmd.Mes, cmd.Anio);

        var duplicado = await db.TurnoProgramaciones.AsNoTracking()
            .AnyAsync(x => x.Id != id
                        && x.SucursalId == cmd.SucursalId
                        && x.Anio == cmd.Anio
                        && x.Mes == cmd.Mes
                        && x.Nombre.ToLower() == nombre.ToLower(), ct);
        if (duplicado)
        {
            throw new InvalidOperationException(
                $"Ya existe otra programacion '{nombre}' para ese periodo en esa sede.");
        }

        var bloquear = await LeerBloqueoOverloadAsync(ct);
        var grid = GridDataModel.FromJson(cmd.GridDataJson);
        var err = grid.Validate(bloquear, DateTime.DaysInMonth(cmd.Anio, cmd.Mes));
        if (err is not null) { throw new InvalidOperationException(err); }

        e.SucursalId = cmd.SucursalId;
        e.TipoServicioId = cmd.TipoServicioId;
        e.Nombre = nombre;
        e.Anio = cmd.Anio;
        e.Mes = cmd.Mes;
        e.Descripcion = string.IsNullOrWhiteSpace(cmd.Descripcion) ? null : cmd.Descripcion.Trim();
        e.GridDataJson = grid.ToJson();
        e.Activa = cmd.Activa;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> DuplicarAsync(Guid id, int nuevoAnio, int nuevoMes, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var origen = await db.TurnoProgramaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw new InvalidOperationException("Programacion origen no encontrada.");
        ValidarMesAnio(nuevoMes, nuevoAnio);

        var duplicado = await db.TurnoProgramaciones.AsNoTracking()
            .AnyAsync(x => x.SucursalId == origen.SucursalId
                        && x.Anio == nuevoAnio
                        && x.Mes == nuevoMes
                        && x.Nombre.ToLower() == origen.Nombre.ToLower(), ct);
        if (duplicado)
        {
            throw new InvalidOperationException(
                $"Ya existe una programacion '{origen.Nombre}' para {nuevoMes}/{nuevoAnio} en esa sede.");
        }

        var copia = new TurnoProgramacion
        {
            TenantId = tid,
            SucursalId = origen.SucursalId,
            TipoServicioId = origen.TipoServicioId,
            Nombre = origen.Nombre,
            Anio = nuevoAnio,
            Mes = nuevoMes,
            Descripcion = origen.Descripcion,
            GridDataJson = origen.GridDataJson,
            Activa = true
        };
        db.TurnoProgramaciones.Add(copia);
        await db.SaveChangesAsync(ct);
        return copia.Id;
    }

    public async Task DesactivarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.TurnoProgramaciones.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Programacion no encontrada.");
        if (!e.Activa) { return; }
        e.Activa = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.TurnoProgramaciones.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        // Fase futura: chequear que no haya TurnoAsignacionProfesional apuntando a este id.
        db.TurnoProgramaciones.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string NormalizarNombre(string? nombre)
    {
        var n = (nombre ?? "").Trim();
        if (n.Length == 0) { throw new InvalidOperationException("El nombre es obligatorio."); }
        if (n.Length > 120) { throw new InvalidOperationException("El nombre no puede superar 120 caracteres."); }
        return n;
    }

    private static void ValidarMesAnio(int mes, int anio)
    {
        if (mes < 1 || mes > 12) { throw new InvalidOperationException("Mes debe estar entre 1 y 12."); }
        if (anio < 2000 || anio > 2100) { throw new InvalidOperationException("Anio fuera de rango (2000..2100)."); }
    }
}
