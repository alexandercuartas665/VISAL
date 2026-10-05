using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class HcMarcaErrorService(IApplicationDbContext db, ITenantContext tenant) : IHcMarcaErrorService
{
    public async Task<Guid> MarcarAsync(
        Guid asignacionId,
        string codigoAsignacion,
        string? pacienteNombre,
        string? pacienteDoc,
        string observacion,
        string? marcadoPorNombre,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        observacion = (observacion ?? "").Trim();

        // Si ya hay una marca Pendiente para esta asignacion, actualizamos en vez de
        // duplicar: el usuario puede re-marcar para corregir/ampliar la observacion.
        // Upsert acotado a marcas MANUALES: no debe pisar un auto-registro de
        // auditoria que comparta la misma asignacion.
        var existente = await db.HcMarcasError
            .FirstOrDefaultAsync(m => m.AsignacionId == asignacionId
                                   && m.Estado == HcMarcaErrorEstado.Pendiente
                                   && m.Origen == HcMarcaErrorOrigen.Manual, ct);
        if (existente is not null)
        {
            existente.Observacion = observacion;
            existente.CodigoAsignacion = codigoAsignacion;
            existente.PacienteNombre = pacienteNombre;
            existente.PacienteDoc = pacienteDoc;
            existente.MarcadoPorNombre = marcadoPorNombre;
            await db.SaveChangesAsync(ct);
            return existente.Id;
        }

        var marca = new HcMarcaError
        {
            TenantId = tid,
            AsignacionId = asignacionId,
            Origen = HcMarcaErrorOrigen.Manual,
            CodigoAsignacion = codigoAsignacion,
            PacienteNombre = pacienteNombre,
            PacienteDoc = pacienteDoc,
            Observacion = observacion,
            Estado = HcMarcaErrorEstado.Pendiente,
            MarcadoPorNombre = marcadoPorNombre
        };
        db.HcMarcasError.Add(marca);
        await db.SaveChangesAsync(ct);
        return marca.Id;
    }

    public async Task<IReadOnlyList<HcMarcaErrorDto>> ListarAsync(
        HcMarcaErrorEstado? estado,
        HcMarcaErrorOrigen? origen = null,
        Guid? sedeId = null,
        CancellationToken ct = default)
    {
        var q = db.HcMarcasError.AsNoTracking();
        if (estado is HcMarcaErrorEstado e) { q = q.Where(m => m.Estado == e); }
        if (origen is HcMarcaErrorOrigen o) { q = q.Where(m => m.Origen == o); }
        var marcas = await q.OrderByDescending(m => m.CreatedAt).ToListAsync(ct);

        // Sede en vivo: Asignacion -> Paciente.SedeAtencionId -> Sucursal.Nombre.
        // (La sede vive en el paciente, no en la marca; no se snapshotea.)
        var asigIds = marcas.Select(m => m.AsignacionId).Where(x => x != Guid.Empty).Distinct().ToList();
        var sedePorAsig = new Dictionary<Guid, (Guid? SedeId, string? SedeNombre)>();
        if (asigIds.Count > 0)
        {
            var filas = await (
                from a in db.Asignaciones.AsNoTracking()
                where asigIds.Contains(a.Id)
                join p in db.Pacientes.AsNoTracking() on a.PacienteId equals p.Id
                join s in db.Sucursales.AsNoTracking() on p.SedeAtencionId equals s.Id into sj
                from s in sj.DefaultIfEmpty()
                select new { a.Id, Sede = p.SedeAtencionId, Nombre = s != null ? s.Nombre : null })
              .ToListAsync(ct);
            foreach (var f in filas) { sedePorAsig[f.Id] = (f.Sede, f.Nombre); }
        }

        var result = new List<HcMarcaErrorDto>(marcas.Count);
        foreach (var m in marcas)
        {
            sedePorAsig.TryGetValue(m.AsignacionId, out var sede);
            // Filtro por sede: excluye las que no resuelven a esa sede.
            if (sedeId is Guid sid && sede.SedeId != sid) { continue; }
            result.Add(new HcMarcaErrorDto(
                m.Id, m.AsignacionId, m.CodigoAsignacion,
                m.PacienteNombre, m.PacienteDoc,
                m.Observacion, m.Estado,
                m.MarcadoPorNombre, m.CreatedAt,
                m.ReparadoPorNombre, m.ReparadoEn, m.ObservacionReparacion,
                m.Origen, m.HistoriaClinicaId,
                sede.SedeId, sede.SedeNombre));
        }
        return result;
    }

    // Badge del tab: solo Pendientes MANUALES. Las auto-reparaciones no inflan el badge
    // (son pista de auditoria; se ven con el filtro de origen "Auto"/"Todas").
    public async Task<int> ContarPendientesAsync(CancellationToken ct = default)
        => await db.HcMarcasError.AsNoTracking()
            .CountAsync(m => m.Estado == HcMarcaErrorEstado.Pendiente
                          && m.Origen == HcMarcaErrorOrigen.Manual, ct);

    public async Task<Guid> RegistrarTocadoAsync(
        Guid historiaClinicaId,
        string motivo,
        string? detalle = null,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        // Asignacion por el pivote sesion -> turno -> asignacion. Si la HC no tiene
        // pivote, cae a Guid.Empty y el codigo son los 8 primeros del HcId.
        var asigId = await (
            from pv in db.AsignacionTurnoSesionHcs.AsNoTracking()
            join s in db.AsignacionTurnoSesiones.AsNoTracking() on pv.SesionId equals s.Id
            join t in db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
            where pv.HistoriaClinicaId == historiaClinicaId
            select t.AsignacionId).FirstOrDefaultAsync(ct);
        var codigo = (asigId != Guid.Empty ? asigId : historiaClinicaId)
            .ToString()[..8].ToUpperInvariant();

        // Snapshot del paciente de la HC (para la lista del tab, sin joins extra).
        var pac = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == historiaClinicaId)
            .Join(db.Pacientes.AsNoTracking(), h => h.PacienteId, p => p.Id, (h, p) => new
            {
                Nombre = ((p.PrimerNombre ?? "") + " " + (p.PrimerApellido ?? "")).Trim(),
                Doc = (p.TipoDocumento + " " + p.NumeroDocumento).Trim()
            })
            .FirstOrDefaultAsync(ct);

        var stamp = DateTimeOffset.UtcNow;
        var linea = $"[{stamp:yyyy-MM-dd HH:mm}] {motivo}."
            + (string.IsNullOrWhiteSpace(detalle) ? "" : " " + detalle.Trim());

        // Idempotente por (HistoriaClinicaId, Origen=AutoReparacion): si ya hay una
        // marca auto para esta HC, le anexa la nueva linea; si no, la crea. NUNCA Reparado.
        var existente = await db.HcMarcasError
            .FirstOrDefaultAsync(m => m.HistoriaClinicaId == historiaClinicaId
                                   && m.Origen == HcMarcaErrorOrigen.AutoReparacion, ct);
        if (existente is not null)
        {
            existente.Observacion = (existente.Observacion + "\n" + linea).Trim();
            existente.Estado = HcMarcaErrorEstado.Pendiente; // reabrir si se habia cerrado
            await db.SaveChangesAsync(ct);
            return existente.Id;
        }

        var marca = new HcMarcaError
        {
            TenantId = tid,
            AsignacionId = asigId,
            HistoriaClinicaId = historiaClinicaId,
            Origen = HcMarcaErrorOrigen.AutoReparacion,
            CodigoAsignacion = codigo,
            PacienteNombre = pac?.Nombre,
            PacienteDoc = pac?.Doc,
            Observacion = linea,
            Estado = HcMarcaErrorEstado.Pendiente,
            MarcadoPorNombre = "SISTEMA (auto-reparacion)"
        };
        db.HcMarcasError.Add(marca);
        await db.SaveChangesAsync(ct);
        return marca.Id;
    }

    public async Task<bool> RepararAsync(
        Guid id,
        string? observacionReparacion,
        string? reparadoPorNombre,
        CancellationToken ct = default)
    {
        var marca = await db.HcMarcasError
            .FirstOrDefaultAsync(m => m.Id == id && m.Estado == HcMarcaErrorEstado.Pendiente, ct);
        if (marca is null) { return false; }
        marca.Estado = HcMarcaErrorEstado.Reparado;
        marca.ObservacionReparacion = string.IsNullOrWhiteSpace(observacionReparacion) ? null : observacionReparacion.Trim();
        marca.ReparadoPorNombre = reparadoPorNombre;
        marca.ReparadoEn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
