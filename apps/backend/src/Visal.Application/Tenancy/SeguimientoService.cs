using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class SeguimientoService(
    IApplicationDbContext db,
    ITenantContext tenant) : ISeguimientoService
{
    private static readonly string[] EstadosValidos = ["Pendiente", "Realizada", "NoContactado"];

    public async Task<IReadOnlyList<SeguimientoEncuestaDto>> ListarPorMesAsync(int mes, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return []; }

        // Auto-materializar: cualquier paciente con HC creada en el mes recibe un registro Pendiente
        // si no existe. Regla simple para primer roll-out.
        var (desde, hasta) = MesToRango(mes);
        var pacientesActividad = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.TenantId == tid
                && h.FechaApertura >= desde
                && h.FechaApertura < hasta)
            .Select(h => h.PacienteId)
            .Distinct()
            .ToListAsync(ct);

        if (pacientesActividad.Count > 0)
        {
            var yaExistentes = await db.SeguimientoEncuestas.AsNoTracking()
                .Where(x => x.Mes == mes && pacientesActividad.Contains(x.PacienteId))
                .Select(x => x.PacienteId)
                .ToListAsync(ct);
            var faltantes = pacientesActividad.Except(yaExistentes).ToList();
            foreach (var pid in faltantes)
            {
                db.SeguimientoEncuestas.Add(new SeguimientoEncuesta
                {
                    TenantId = tid,
                    PacienteId = pid,
                    Mes = mes,
                    Estado = "Pendiente"
                });
            }
            if (faltantes.Count > 0) { await db.SaveChangesAsync(ct); }
        }

        // OrderBy dentro del Select no lo traduce EF; ordenamos client-side despues.
        var filas = await db.SeguimientoEncuestas.AsNoTracking()
            .Where(x => x.Mes == mes)
            .Join(db.Pacientes.AsNoTracking(),
                s => s.PacienteId,
                p => p.Id,
                (s, p) => new SeguimientoEncuestaDto(
                    s.Id, s.PacienteId,
                    p.NombreCompleto,
                    p.TipoDocumento, p.NumeroDocumento,
                    null, null,
                    s.Mes, s.Estado, s.FechaLlamada,
                    s.ResponsableLlamadaId, s.ResponsableLlamadaNombre,
                    s.Pregunta1, s.Pregunta2, s.Pregunta3, s.Pregunta4, s.Pregunta5,
                    s.PersonaAtiende, s.Observaciones))
            .ToListAsync(ct);

        return filas
            .OrderBy(x => x.Estado == "Pendiente" ? 0 : x.Estado == "NoContactado" ? 1 : 2)
            .ThenBy(x => x.PacienteNombre)
            .ToList();
    }

    public async Task<bool> GuardarEncuestaAsync(Guid id, GuardarEncuestaRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.SeguimientoEncuestas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }

        // Postgres timestamp with time zone requiere DateTimeKind.Utc con Npgsql;
        // la UI puede mandar Kind=Local (DateTime.Now) y romper el SaveChanges.
        var fecha = req.FechaLlamada ?? entity.FechaLlamada;
        if (fecha is DateTime f && f.Kind != DateTimeKind.Utc)
        {
            fecha = f.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(f, DateTimeKind.Utc)
                : f.ToUniversalTime();
        }
        entity.FechaLlamada = fecha;
        entity.Pregunta1 = ClampScore(req.Pregunta1);
        entity.Pregunta2 = ClampScore(req.Pregunta2);
        entity.Pregunta3 = ClampScore(req.Pregunta3);
        entity.Pregunta4 = ClampScore(req.Pregunta4);
        entity.Pregunta5 = ClampScore(req.Pregunta5);
        entity.PersonaAtiende = string.IsNullOrWhiteSpace(req.PersonaAtiende) ? null : req.PersonaAtiende.Trim();
        entity.Observaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim();

        var actorId = tenant.UserId ?? actor;
        if (actorId != Guid.Empty)
        {
            entity.ResponsableLlamadaId = actorId;
            var usr = await db.PlatformUsers.AsNoTracking()
                .Where(u => u.Id == actorId)
                .Select(u => new { u.DisplayName, u.PrimerNombre, u.SegundoNombre, u.Username })
                .FirstOrDefaultAsync(ct);
            entity.ResponsableLlamadaNombre = ComponerNombre(usr?.DisplayName, usr?.PrimerNombre, usr?.SegundoNombre, usr?.Username);
        }

        // Realizada si al menos una respuesta fue dada.
        var tieneRespuestas =
            entity.Pregunta1.HasValue || entity.Pregunta2.HasValue ||
            entity.Pregunta3.HasValue || entity.Pregunta4.HasValue ||
            entity.Pregunta5.HasValue || !string.IsNullOrWhiteSpace(entity.Observaciones);
        if (tieneRespuestas) { entity.Estado = "Realizada"; }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CambiarEstadoAsync(Guid id, string estado, Guid actor, CancellationToken ct = default)
    {
        if (!EstadosValidos.Contains(estado)) { throw new InvalidOperationException($"Estado invalido: {estado}"); }
        var entity = await db.SeguimientoEncuestas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        entity.Estado = estado;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static (DateTime desde, DateTime hasta) MesToRango(int mes)
    {
        var y = mes / 100;
        var m = mes % 100;
        var desde = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddMonths(1);
        return (desde, hasta);
    }

    private static int? ClampScore(int? v) => v is null ? null : Math.Clamp(v.Value, 1, 5);

    private static string? ComponerNombre(string? display, string? p1, string? p2, string? user)
    {
        if (!string.IsNullOrWhiteSpace(display)) { return display.Trim(); }
        var partes = new[] { p1, p2 }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim());
        var compuesto = string.Join(' ', partes);
        return !string.IsNullOrWhiteSpace(compuesto) ? compuesto : (string.IsNullOrWhiteSpace(user) ? null : user!.Trim());
    }
}
