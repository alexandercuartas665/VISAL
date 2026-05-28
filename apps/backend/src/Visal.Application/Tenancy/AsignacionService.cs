using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class AsignacionService(IApplicationDbContext db, ITenantContext tenant) : IAsignacionService
{
    public async Task<PacienteAsignacionDto?> GetPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        var p = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null) { return null; }

        // Sede del paciente (nombre)
        string? sedeNombre = null;
        if (p.SedeAtencionId is Guid sid)
        {
            sedeNombre = await db.Sucursales.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Nombre).FirstOrDefaultAsync(ct);
        }

        // Contratos: si tiene aseguradora, sus contratos. Si no, vacio.
        var contratos = new List<ContratoMiniDto>();
        if (p.AseguradoraId is Guid aid)
        {
            contratos = await db.ContratosAseguradora.AsNoTracking()
                .Where(c => c.AseguradoraId == aid)
                .Join(db.Aseguradoras.AsNoTracking(), c => c.AseguradoraId, a => a.Id,
                    (c, a) => new ContratoMiniDto(c.Id, a.Id, a.Nombre, c.CodigoContrato, c.Estado))
                .ToListAsync(ct);
        }

        return new PacienteAsignacionDto(p.Id, p.NumeroDocumento, p.TipoDocumento, p.NombreCompleto,
            sedeNombre, p.Ciudad, contratos);
    }

    public async Task<IReadOnlyList<PacienteAsignacionDto>> BuscarPacientesAsync(string? texto, Guid? contratoId, CancellationToken ct = default)
    {
        var q = db.Pacientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var f = texto.Trim().ToLower();
            q = q.Where(p => p.NumeroDocumento.ToLower().Contains(f) || p.NombreCompleto.ToLower().Contains(f) || (p.Telefono != null && p.Telefono.Contains(f)));
        }
        if (contratoId is Guid cid)
        {
            // Filtrar por aseguradora del contrato.
            var aseguradoraId = await db.ContratosAseguradora.AsNoTracking()
                .Where(c => c.Id == cid)
                .Select(c => (Guid?)c.AseguradoraId)
                .FirstOrDefaultAsync(ct);
            if (aseguradoraId is Guid a)
            {
                q = q.Where(p => p.AseguradoraId == a);
            }
        }
        var lista = await q.OrderBy(p => p.NombreCompleto).Take(50).ToListAsync(ct);
        var result = new List<PacienteAsignacionDto>(lista.Count);
        foreach (var p in lista) { result.Add((await GetPacienteAsync(p.Id, ct))!); }
        return result;
    }

    public async Task<IReadOnlyList<string>> TiposServicioPorContratoAsync(Guid contratoId, CancellationToken ct = default)
    {
        return await db.ServiciosContrato.AsNoTracking()
            .Where(s => s.ContratoId == contratoId && s.Modulo != null && s.Modulo != "")
            .Select(s => s.Modulo!)
            .Distinct()
            .OrderBy(m => m)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ServicioCatalogoDto>> ServiciosPorContratoAsync(Guid contratoId, string? tipo, CancellationToken ct = default)
    {
        var q = db.ServiciosContrato.AsNoTracking().Where(s => s.ContratoId == contratoId);
        if (!string.IsNullOrWhiteSpace(tipo)) { q = q.Where(s => s.Modulo == tipo); }
        return await q.OrderBy(s => s.Descripcion)
            .Select(s => new ServicioCatalogoDto(s.Id, s.CodigoServicio, s.Descripcion ?? s.CodigoServicio ?? "(sin descripcion)", s.Modulo, s.Especialidad, s.Tarifa))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AsignacionMiniDto>> UltimasAsignacionesAsync(Guid pacienteId, int n, CancellationToken ct = default)
    {
        if (n <= 0) { n = 10; }
        return await db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == pacienteId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(n)
            .Select(a => new AsignacionMiniDto(a.Id, a.NombreServicio, a.TipoServicio, a.Cantidad,
                a.FechaInicio, a.FechaFinal, a.Estado.ToString(), a.ContratoCodigo, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<LoteCreadoDto> CrearLoteAsync(CrearLoteRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (req.Items is null || req.Items.Count == 0) { throw new InvalidOperationException("El lote no tiene servicios."); }
        foreach (var it in req.Items)
        {
            if (it.Cantidad <= 0) { throw new InvalidOperationException("La cantidad debe ser mayor a cero."); }
            if (it.MesVigencia < 1 || it.MesVigencia > 12) { throw new InvalidOperationException("Mes de vigencia invalido."); }
            if (it.MesFinal is short mf && (mf < 1 || mf > 12)) { throw new InvalidOperationException("Mes final invalido."); }
        }
        // Validar que el paciente exista en el tenant.
        var paciente = await db.Pacientes.FirstOrDefaultAsync(p => p.Id == req.PacienteId, ct)
            ?? throw new InvalidOperationException("Paciente no encontrado en el tenant activo.");

        var lote = new AsignacionLote
        {
            TenantId = tid,
            PacienteId = paciente.Id,
            Sucursal = req.Sucursal,
            ContratoCodigo = req.ContratoCodigo
        };
        db.AsignacionLotes.Add(lote);
        foreach (var it in req.Items)
        {
            db.Asignaciones.Add(new Asignacion
            {
                TenantId = tid,
                Lote = lote,
                PacienteId = paciente.Id,
                Sucursal = req.Sucursal,
                ServicioId = it.ServicioId,
                NombreServicio = it.NombreServicio,
                TipoServicio = it.TipoServicio,
                Modulo = it.Modulo,
                Cantidad = it.Cantidad,
                ContratoCodigo = req.ContratoCodigo,
                CodigoAutorizacion = it.CodigoAutorizacion,
                AnioServicio = it.AnioServicio,
                MesVigencia = it.MesVigencia,
                MesFinal = it.MesFinal,
                FechaInicio = it.FechaInicio,
                FechaFinal = it.FechaFinal,
                Observaciones = it.Observaciones,
                FormatoHistoria = it.FormatoHistoria,
                Estado = AsignacionEstado.Pendiente
            });
        }
        await db.SaveChangesAsync(ct);
        return new LoteCreadoDto(lote.Id, req.Items.Count);
    }

    public async Task<bool> EliminarAsignacionAsync(Guid asignacionId, Guid actor, CancellationToken ct = default)
    {
        var a = await db.Asignaciones.FirstOrDefaultAsync(x => x.Id == asignacionId, ct);
        if (a is null) { return false; }
        db.Asignaciones.Remove(a);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
