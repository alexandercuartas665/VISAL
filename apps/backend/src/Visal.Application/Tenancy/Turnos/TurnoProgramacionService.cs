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
        // Filtro por sede: la programacion es elegible si tiene esa sede en su lista N:N.
        if (sucursalId is Guid sid)
        {
            q = q.Where(x => x.Sucursales.Any(s => s.SucursalId == sid));
        }
        if (tipoServicioId is Guid tsid) { q = q.Where(x => x.TipoServicioId == tsid); }
        if (anio is int a) { q = q.Where(x => x.Anio == a); }
        if (mes is int m) { q = q.Where(x => x.Mes == m); }
        if (soloActivas) { q = q.Where(x => x.Activa); }

        var rows = await q
            .OrderByDescending(x => x.Anio).ThenByDescending(x => x.Mes).ThenBy(x => x.Nombre)
            .Select(x => new
            {
                x.Id, x.TipoServicioId, x.Nombre, x.Anio, x.Mes,
                x.GridDataJson, x.Activa,
                SucursalIds = x.Sucursales.Select(s => s.SucursalId).ToList()
            })
            .ToListAsync(ct);

        var sucIds = rows.SelectMany(r => r.SucursalIds).Distinct().ToArray();
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
            var nombres = r.SucursalIds
                .Select(id => sucursales.TryGetValue(id, out var n) ? n : "?")
                .OrderBy(n => n).ToList();
            var tsNombre = r.TipoServicioId is Guid t ? (tiposServ.GetValueOrDefault(t)) : null;
            return new TurnoProgramacionDto(
                r.Id, r.SucursalIds, nombres, r.TipoServicioId, tsNombre,
                r.Nombre, r.Anio, r.Mes, grid.Turnos.Count, r.Activa);
        }).ToList();
    }

    public async Task<TurnoProgramacionDetailDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.TurnoProgramaciones.AsNoTracking()
            .Include(x => x.Sucursales)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null
            ? null
            : new TurnoProgramacionDetailDto(
                e.Id,
                e.Sucursales.Select(s => s.SucursalId).ToList(),
                e.TipoServicioId, e.Nombre, e.Anio, e.Mes,
                e.Descripcion, e.GridDataJson, e.Activa);
    }

    public async Task<Guid> CrearAsync(CrearTurnoProgramacionCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var nombre = NormalizarNombre(cmd.Nombre);
        ValidarMesAnio(cmd.Mes, cmd.Anio);
        var sucIds = await ValidarSucursalesAsync(cmd.SucursalIds, tid, ct);

        // Unicidad (tenant, anio, mes, nombre). Case-insensitive.
        var duplicado = await db.TurnoProgramaciones.AsNoTracking()
            .AnyAsync(x => x.Anio == cmd.Anio
                        && x.Mes == cmd.Mes
                        && x.Nombre.ToLower() == nombre.ToLower(), ct);
        if (duplicado)
        {
            throw new InvalidOperationException(
                $"Ya existe una programacion '{nombre}' para {cmd.Mes}/{cmd.Anio} en este tenant.");
        }

        var bloquear = await LeerBloqueoOverloadAsync(ct);
        var grid = GridDataModel.FromJson(cmd.GridDataJson);
        var err = grid.Validate(bloquear, DateTime.DaysInMonth(cmd.Anio, cmd.Mes));
        if (err is not null) { throw new InvalidOperationException(err); }

        var e = new TurnoProgramacion
        {
            TenantId = tid,
            TipoServicioId = cmd.TipoServicioId,
            Nombre = nombre,
            Anio = cmd.Anio,
            Mes = cmd.Mes,
            Descripcion = string.IsNullOrWhiteSpace(cmd.Descripcion) ? null : cmd.Descripcion.Trim(),
            GridDataJson = grid.ToJson(),
            Activa = true
        };
        foreach (var s in sucIds)
        {
            e.Sucursales.Add(new TurnoProgramacionSucursal { TenantId = tid, SucursalId = s });
        }
        db.TurnoProgramaciones.Add(e);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task ActualizarAsync(Guid id, ActualizarTurnoProgramacionCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var e = await db.TurnoProgramaciones
            .Include(x => x.Sucursales)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Programacion no encontrada.");
        var nombre = NormalizarNombre(cmd.Nombre);
        ValidarMesAnio(cmd.Mes, cmd.Anio);
        var sucIds = await ValidarSucursalesAsync(cmd.SucursalIds, tid, ct);

        var duplicado = await db.TurnoProgramaciones.AsNoTracking()
            .AnyAsync(x => x.Id != id
                        && x.Anio == cmd.Anio
                        && x.Mes == cmd.Mes
                        && x.Nombre.ToLower() == nombre.ToLower(), ct);
        if (duplicado)
        {
            throw new InvalidOperationException(
                $"Ya existe otra programacion '{nombre}' para {cmd.Mes}/{cmd.Anio} en este tenant.");
        }

        var bloquear = await LeerBloqueoOverloadAsync(ct);
        var grid = GridDataModel.FromJson(cmd.GridDataJson);
        var err = grid.Validate(bloquear, DateTime.DaysInMonth(cmd.Anio, cmd.Mes));
        if (err is not null) { throw new InvalidOperationException(err); }

        e.TipoServicioId = cmd.TipoServicioId;
        e.Nombre = nombre;
        e.Anio = cmd.Anio;
        e.Mes = cmd.Mes;
        e.Descripcion = string.IsNullOrWhiteSpace(cmd.Descripcion) ? null : cmd.Descripcion.Trim();
        e.GridDataJson = grid.ToJson();
        e.Activa = cmd.Activa;

        // Sincronizar sedes N:N.
        var actuales = e.Sucursales.Select(s => s.SucursalId).ToHashSet();
        var deseadas = sucIds.ToHashSet();
        foreach (var quitar in e.Sucursales.Where(s => !deseadas.Contains(s.SucursalId)).ToList())
        {
            e.Sucursales.Remove(quitar);
        }
        foreach (var agregar in deseadas.Where(s => !actuales.Contains(s)))
        {
            e.Sucursales.Add(new TurnoProgramacionSucursal { TenantId = tid, SucursalId = agregar });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> DuplicarAsync(Guid id, int nuevoAnio, int nuevoMes, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var origen = await db.TurnoProgramaciones.AsNoTracking()
            .Include(x => x.Sucursales)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw new InvalidOperationException("Programacion origen no encontrada.");
        ValidarMesAnio(nuevoMes, nuevoAnio);

        var duplicado = await db.TurnoProgramaciones.AsNoTracking()
            .AnyAsync(x => x.Anio == nuevoAnio
                        && x.Mes == nuevoMes
                        && x.Nombre.ToLower() == origen.Nombre.ToLower(), ct);
        if (duplicado)
        {
            throw new InvalidOperationException(
                $"Ya existe una programacion '{origen.Nombre}' para {nuevoMes}/{nuevoAnio} en este tenant.");
        }

        var copia = new TurnoProgramacion
        {
            TenantId = tid,
            TipoServicioId = origen.TipoServicioId,
            Nombre = origen.Nombre,
            Anio = nuevoAnio,
            Mes = nuevoMes,
            Descripcion = origen.Descripcion,
            GridDataJson = origen.GridDataJson,
            Activa = true
        };
        foreach (var s in origen.Sucursales)
        {
            copia.Sucursales.Add(new TurnoProgramacionSucursal { TenantId = tid, SucursalId = s.SucursalId });
        }
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

    /// <summary>Regla dura: al menos 1 sede vinculada, todas del tenant y activas.</summary>
    private async Task<List<Guid>> ValidarSucursalesAsync(
        IReadOnlyList<Guid> sucursalIds, Guid tid, CancellationToken ct)
    {
        var ids = (sucursalIds ?? Array.Empty<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("Debes seleccionar al menos una sede.");
        }
        var existen = await db.Sucursales.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.TenantId == tid)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var faltantes = ids.Except(existen).ToList();
        if (faltantes.Count > 0)
        {
            throw new InvalidOperationException("Alguna de las sedes elegidas no existe en el tenant.");
        }
        return ids;
    }
}
