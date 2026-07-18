using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class AseguradoraService : IAseguradoraService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public AseguradoraService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // ── Aseguradoras ──
    public async Task<IReadOnlyList<AseguradoraDto>> ListAseguradorasAsync(CancellationToken ct = default)
    {
        return await _db.Aseguradoras.AsNoTracking()
            .OrderBy(a => a.Nombre)
            .Select(a => new AseguradoraDto(a.Id, a.Codigo, a.Tipo, a.Nombre, a.Nit, a.Regimen,
                _db.ContratosAseguradora.Count(c => c.AseguradoraId == a.Id)))
            .ToListAsync(ct);
    }

    public async Task<AseguradoraDetailDto?> GetAseguradoraAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Aseguradoras.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new AseguradoraDetailDto(a.Id, a.Codigo, a.Tipo, a.Nombre, a.CodigoMovilidad,
                a.Nit, a.Regimen, a.CodInt, a.Descripcion, a.CorreoFacturacion))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AseguradoraDetailDto?> SaveAseguradoraAsync(SaveAseguradoraRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.Codigo ?? "").Trim();
        var nombre = (req.Nombre ?? "").Trim();
        if (codigo.Length == 0 || nombre.Length == 0)
        {
            throw new InvalidOperationException("El codigo y el nombre de la aseguradora son obligatorios.");
        }

        Aseguradora entity;
        if (req.Id is Guid id)
        {
            entity = await _db.Aseguradoras.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new InvalidOperationException("Aseguradora no encontrada.");
            if (await _db.Aseguradoras.AnyAsync(a => a.Codigo == codigo && a.Id != id, ct))
            {
                throw new InvalidOperationException($"Ya existe otra aseguradora con el codigo '{codigo}'.");
            }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.Aseguradoras.AnyAsync(a => a.Codigo == codigo, ct))
            {
                throw new InvalidOperationException($"Ya existe una aseguradora con el codigo '{codigo}'.");
            }
            entity = new Aseguradora { TenantId = tid };
            _db.Aseguradoras.Add(entity);
        }

        entity.Codigo = codigo;
        entity.Tipo = string.IsNullOrWhiteSpace(req.Tipo) ? "EPS" : req.Tipo.Trim();
        entity.Nombre = nombre;
        entity.CodigoMovilidad = req.CodigoMovilidad?.Trim();
        entity.Nit = req.Nit?.Trim();
        entity.Regimen = req.Regimen?.Trim();
        entity.CodInt = req.CodInt?.Trim();
        entity.Descripcion = req.Descripcion?.Trim();
        entity.CorreoFacturacion = req.CorreoFacturacion?.Trim();

        await _db.SaveChangesAsync(ct);
        return new AseguradoraDetailDto(entity.Id, entity.Codigo, entity.Tipo, entity.Nombre,
            entity.CodigoMovilidad, entity.Nit, entity.Regimen, entity.CodInt, entity.Descripcion,
            entity.CorreoFacturacion);
    }

    public async Task<bool> DeleteAseguradoraAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.Aseguradoras.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (e is null) { return false; }
        _db.Aseguradoras.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Contratos ──
    public async Task<IReadOnlyList<ContratoDto>> ListContratosAsync(Guid aseguradoraId, CancellationToken ct = default)
    {
        return await _db.ContratosAseguradora.AsNoTracking()
            .Where(c => c.AseguradoraId == aseguradoraId)
            .OrderBy(c => c.CodigoContrato)
            .Select(c => new ContratoDto(c.Id, c.AseguradoraId, c.CodigoContrato, c.FechaInicial, c.FechaFinal, c.Estado, c.Prorroga, c.RequierePdfAutorizacion))
            .ToListAsync(ct);
    }

    public async Task<ContratoDto?> SaveContratoAsync(SaveContratoRequest req, Guid actor, CancellationToken ct = default)
    {
        var codigo = (req.CodigoContrato ?? "").Trim();
        if (codigo.Length == 0) { throw new InvalidOperationException("El codigo del contrato es obligatorio."); }

        ContratoAseguradora entity;
        if (req.Id is Guid id)
        {
            entity = await _db.ContratosAseguradora.FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new InvalidOperationException("Contrato no encontrado.");
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            // El filtro global garantiza que la aseguradora pertenece al tenant activo.
            if (!await _db.Aseguradoras.AnyAsync(a => a.Id == req.AseguradoraId, ct)) { return null; }
            entity = new ContratoAseguradora { TenantId = tid, AseguradoraId = req.AseguradoraId };
            _db.ContratosAseguradora.Add(entity);
        }

        entity.CodigoContrato = codigo;
        entity.FechaInicial = req.FechaInicial;
        entity.FechaFinal = req.FechaFinal;
        entity.Estado = string.IsNullOrWhiteSpace(req.Estado) ? "ACTIVO" : req.Estado.Trim();
        entity.Prorroga = req.Prorroga;
        entity.RequierePdfAutorizacion = req.RequierePdfAutorizacion;

        await _db.SaveChangesAsync(ct);
        return new ContratoDto(entity.Id, entity.AseguradoraId, entity.CodigoContrato, entity.FechaInicial, entity.FechaFinal, entity.Estado, entity.Prorroga, entity.RequierePdfAutorizacion);
    }

    public async Task<bool> DeleteContratoAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.ContratosAseguradora.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) { return false; }
        _db.ContratosAseguradora.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Servicios ──
    public async Task<IReadOnlyList<ServicioDto>> ListServiciosAsync(Guid contratoId, string? filtro, CancellationToken ct = default)
    {
        var q = _db.ServiciosContrato.AsNoTracking().Where(s => s.ContratoId == contratoId);
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim().ToLower();
            q = q.Where(s =>
                (s.Descripcion != null && s.Descripcion.ToLower().Contains(f)) ||
                (s.CodigoServicio != null && s.CodigoServicio.ToLower().Contains(f)) ||
                (s.CodigoInterno != null && s.CodigoInterno.ToLower().Contains(f)) ||
                (s.Historia != null && s.Historia.ToLower().Contains(f)) ||
                (s.Modulo != null && s.Modulo.ToLower().Contains(f)) ||
                (s.Especialidad != null && s.Especialidad.ToLower().Contains(f)) ||
                (s.Sede != null && s.Sede.ToLower().Contains(f)));
        }
        return await q.OrderBy(s => s.Descripcion)
            .Select(s => new ServicioDto(s.Id, s.ContratoId, s.Sede, s.Historia,
                s.PaqueteId,
                s.PaqueteId != null ? _db.Paquetes.Where(p => p.Id == s.PaqueteId).Select(p => p.Codigo).FirstOrDefault() : null,
                s.CodigoServicio, s.CodigoInterno,
                s.Descripcion, s.Tarifa, s.Modulo, s.Especialidad, s.Modalidad, s.Clasificacion, s.Observaciones,
                s.Finalidad, s.CausaExterna, s.ModalidadAtencion, s.ViaIngreso, s.GrupoServicios, s.Servicios, s.ValorTotal))
            .ToListAsync(ct);
    }

    public async Task<ServicioDto?> SaveServicioAsync(SaveServicioRequest req, Guid actor, CancellationToken ct = default)
    {
        ServicioContrato entity;
        if (req.Id is Guid id)
        {
            entity = await _db.ServiciosContrato.FirstOrDefaultAsync(s => s.Id == id, ct)
                ?? throw new InvalidOperationException("Servicio no encontrado.");
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (!await _db.ContratosAseguradora.AnyAsync(c => c.Id == req.ContratoId, ct)) { return null; }
            entity = new ServicioContrato { TenantId = tid, ContratoId = req.ContratoId };
            _db.ServiciosContrato.Add(entity);
        }

        entity.Sede = req.Sede?.Trim();
        entity.Historia = req.Historia?.Trim();
        entity.PaqueteId = req.PaqueteId;
        entity.CodigoServicio = req.CodigoServicio?.Trim();
        entity.CodigoInterno = req.CodigoInterno?.Trim();
        entity.Descripcion = req.Descripcion?.Trim();
        entity.Tarifa = req.Tarifa;
        entity.Modulo = req.Modulo?.Trim();
        entity.Especialidad = req.Especialidad?.Trim();
        entity.Modalidad = req.Modalidad?.Trim();
        entity.Clasificacion = req.Clasificacion?.Trim();
        entity.Observaciones = req.Observaciones?.Trim();
        // RIPS Res 2275 + ValorTotal (Fase 4 Facturacion).
        entity.Finalidad = req.Finalidad?.Trim();
        entity.CausaExterna = req.CausaExterna?.Trim();
        entity.ModalidadAtencion = req.ModalidadAtencion?.Trim();
        entity.ViaIngreso = req.ViaIngreso?.Trim();
        entity.GrupoServicios = req.GrupoServicios?.Trim();
        entity.Servicios = req.Servicios?.Trim();
        entity.ValorTotal = req.ValorTotal;

        await _db.SaveChangesAsync(ct);
        var paqueteCod = entity.PaqueteId is Guid pid
            ? await _db.Paquetes.Where(p => p.Id == pid).Select(p => p.Codigo).FirstOrDefaultAsync(ct)
            : null;
        return new ServicioDto(entity.Id, entity.ContratoId, entity.Sede, entity.Historia,
            entity.PaqueteId, paqueteCod,
            entity.CodigoServicio, entity.CodigoInterno,
            entity.Descripcion, entity.Tarifa, entity.Modulo, entity.Especialidad, entity.Modalidad, entity.Clasificacion, entity.Observaciones,
            entity.Finalidad, entity.CausaExterna, entity.ModalidadAtencion, entity.ViaIngreso, entity.GrupoServicios, entity.Servicios, entity.ValorTotal);
    }

    public async Task<bool> DeleteServicioAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.ServiciosContrato.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (e is null) { return false; }
        _db.ServiciosContrato.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ImportServiciosAsync(Guid contratoId, IReadOnlyList<ServicioImportRow> rows, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return 0; }
        var contrato = await _db.ContratosAseguradora.FirstOrDefaultAsync(c => c.Id == contratoId, ct);
        if (contrato is null) { return 0; }

        // Precargar mapa codigo -> paqueteId para no golpear la BD en cada fila.
        var codigosPaquete = rows.Select(r => (r.PaqueteCodigo ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mapaPaquetes = codigosPaquete.Count == 0
            ? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            : await _db.Paquetes
                .Where(p => codigosPaquete.Contains(p.Codigo))
                .ToDictionaryAsync(p => p.Codigo, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

        var n = 0;
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.CodigoServicio) && string.IsNullOrWhiteSpace(r.Descripcion)) { continue; }
            Guid? paqueteId = null;
            var codPaq = (r.PaqueteCodigo ?? "").Trim();
            if (codPaq.Length > 0 && mapaPaquetes.TryGetValue(codPaq, out var pid)) { paqueteId = pid; }
            _db.ServiciosContrato.Add(new ServicioContrato
            {
                TenantId = tid,
                ContratoId = contratoId,
                Sede = r.Sede?.Trim(),
                Historia = r.Historia?.Trim(),
                PaqueteId = paqueteId,
                CodigoServicio = r.CodigoServicio?.Trim(),
                CodigoInterno = r.CodigoInterno?.Trim(),
                Descripcion = r.Descripcion?.Trim(),
                Tarifa = r.Tarifa,
                Modulo = r.Modulo?.Trim(),
                Especialidad = r.Especialidad?.Trim(),
                Modalidad = r.Modalidad?.Trim(),
                Clasificacion = r.Clasificacion?.Trim(),
                Observaciones = r.Observaciones?.Trim()
            });
            n++;
        }
        if (n > 0) { await _db.SaveChangesAsync(ct); }
        return n;
    }

    public async Task<int> EliminarServiciosDeContratoAsync(Guid contratoId, Guid actor, CancellationToken ct = default)
    {
        var existentes = await _db.ServiciosContrato
            .Where(s => s.ContratoId == contratoId)
            .ToListAsync(ct);
        if (existentes.Count == 0) { return 0; }
        _db.ServiciosContrato.RemoveRange(existentes);
        await _db.SaveChangesAsync(ct);
        return existentes.Count;
    }

}
