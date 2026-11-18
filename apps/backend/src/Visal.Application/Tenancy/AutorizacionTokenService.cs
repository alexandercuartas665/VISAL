using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = dbFactory.Create();) en vez del
// IApplicationDbContext scoped. Mantiene el overload ValidarYMarcarUsoAsync(db, ...) para que
// HistoriaClinicaService persista token + HC en su MISMO contexto corto (atomico).
public sealed class AutorizacionTokenService(
    IApplicationDbContextFactory dbFactory, ITenantContext tenant, IAuditWriter audit, TimeProvider clock)
    : IAutorizacionTokenService
{
    // Base32 sin caracteres ambiguos (sin 0/O/1/I/L), mismo alfabeto que
    // OrdenMedicamentoPublicaService para codigos legibles por telefono.
    private const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int LargoCodigo = 6;
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    private static string GenerarCodigo()
    {
        var chars = new char[LargoCodigo];
        for (var i = 0; i < LargoCodigo; i++)
        {
            chars[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        }
        return new string(chars);
    }

    private static async Task<string> GenerarCodigoUnicoAsync(IApplicationDbContext db, CancellationToken ct)
    {
        for (var intento = 0; intento < 12; intento++)
        {
            var cand = GenerarCodigo();
            var existe = await db.AutorizacionTokens.AsNoTracking().AnyAsync(t => t.Codigo == cand, ct);
            if (!existe) { return cand; }
        }
        throw new InvalidOperationException("No se pudo generar un codigo de token unico.");
    }

    public async Task<string> GenerarAsync(string? conceptoNombre, string observacion, string? generadoPorNombre, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var obs = (observacion ?? "").Trim();
        if (obs.Length == 0) { throw new InvalidOperationException("La observacion es obligatoria."); }

        using var db = dbFactory.Create();
        var token = new AutorizacionToken
        {
            TenantId = tid,
            Codigo = await GenerarCodigoUnicoAsync(db, ct),
            ConceptoNombre = string.IsNullOrWhiteSpace(conceptoNombre) ? null : conceptoNombre.Trim(),
            Observacion = obs,
            GeneradoPorNombre = string.IsNullOrWhiteSpace(generadoPorNombre) ? null : generadoPorNombre.Trim(),
            ExpiresAt = clock.GetUtcNow().Add(Vigencia),
            Estado = AutorizacionTokenEstado.Activo,
        };
        db.AutorizacionTokens.Add(token);
        audit.Write(db, actor, "autorizacion-token.generar", nameof(AutorizacionToken), token.Id,
            previousValue: null,
            newValue: new { token.Codigo, token.ConceptoNombre, token.Observacion, token.ExpiresAt },
            tenantId: tid);
        await db.SaveChangesAsync(ct);
        return token.Codigo;
    }

    // ── Solicitudes de autorizacion (sin token) ─────────────────────────────────

    public async Task<IReadOnlyList<AutorizacionSolicitudDto>> ListarSolicitudesAsync(bool soloPendientes, int max = 200, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var q = db.AutorizacionSolicitudes.AsNoTracking();
        if (soloPendientes) { q = q.Where(s => s.Estado == AutorizacionSolicitudEstado.Pendiente); }
        var rows = await q
            .OrderBy(s => s.Estado == AutorizacionSolicitudEstado.Pendiente ? 0 : 1)
            .ThenByDescending(s => s.CreatedAt)
            .Take(max)
            .ToListAsync(ct);
        if (rows.Count == 0) { return Array.Empty<AutorizacionSolicitudDto>(); }

        var hcIds = rows.Select(r => r.HistoriaClinicaId).Distinct().ToList();
        var hcs = await db.HistoriasClinicas.AsNoTracking().Where(h => hcIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Consecutivo, h.PacienteId })
            .ToDictionaryAsync(x => x.Id, ct);
        var pacIds = hcs.Values.Select(h => h.PacienteId).Distinct().ToList();
        var pacs = await db.Pacientes.AsNoTracking().Where(p => pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.TipoDocumento, p.NumeroDocumento })
            .ToDictionaryAsync(x => x.Id, ct);
        var asigIds = rows.Where(r => r.AsignacionId != null).Select(r => r.AsignacionId!.Value).Distinct().ToList();
        var asigs = asigIds.Count == 0
            ? new()
            : await db.Asignaciones.AsNoTracking().Where(a => asigIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Consecutivo, a.NombreServicio, a.TipoServicio })
                .ToDictionaryAsync(x => x.Id, ct);

        return rows.Select(r =>
        {
            hcs.TryGetValue(r.HistoriaClinicaId, out var hc);
            var pac = hc is not null && pacs.TryGetValue(hc.PacienteId, out var p) ? p : null;
            var asg = r.AsignacionId is Guid aid && asigs.TryGetValue(aid, out var a) ? a : null;
            return new AutorizacionSolicitudDto(
                r.Id, r.Estado, r.CreatedAt, r.SolicitadoPorNombre,
                r.HistoriaClinicaId, hc?.Consecutivo, r.AsignacionId, asg?.Consecutivo,
                pac?.NombreCompleto, pac is null ? null : $"{pac.TipoDocumento} {pac.NumeroDocumento}",
                asg?.NombreServicio, asg?.TipoServicio,
                r.FechaAtencion, r.Detalle, r.ConceptoNombre, r.Motivo,
                r.ResueltoEn, r.ResueltoPorNombre, r.Respuesta);
        }).ToList();
    }

    public async Task<int> ContarSolicitudesPendientesAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        return await db.AutorizacionSolicitudes.AsNoTracking()
            .CountAsync(s => s.Estado == AutorizacionSolicitudEstado.Pendiente, ct);
    }

    public async Task<UsarTokenResult> AprobarSolicitudAsync(Guid solicitudId, string? observacion, string? actorNombre, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return new UsarTokenResult(false, "Sin tenant activo."); }
        using var db = dbFactory.Create();
        if (!await PermisosUsuario.TieneAsync(db, actor, PermisosUsuario.Autorizaciones, ct))
        {
            return new UsarTokenResult(false, "No tienes permiso para aprobar autorizaciones.");
        }
        var sol = await db.AutorizacionSolicitudes.FirstOrDefaultAsync(s => s.Id == solicitudId, ct);
        if (sol is null) { return new UsarTokenResult(false, "Solicitud no encontrada."); }
        if (sol.Estado != AutorizacionSolicitudEstado.Pendiente) { return new UsarTokenResult(false, "La solicitud ya fue resuelta."); }
        var hc = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == sol.HistoriaClinicaId, ct);
        if (hc is null) { return new UsarTokenResult(false, "La historia clinica ya no existe."); }

        var now = clock.GetUtcNow();
        var obs = string.IsNullOrWhiteSpace(observacion) ? "Aprobada desde solicitud del profesional." : observacion.Trim();
        var nombre = string.IsNullOrWhiteSpace(actorNombre) ? null : actorNombre.Trim();
        // Token "usado" al instante: deja la aprobacion en el MISMO historial que los tokens
        // (quien autorizo, quien lo uso, HC/asignacion, motivo).
        var token = new AutorizacionToken
        {
            TenantId = tid,
            Codigo = await GenerarCodigoUnicoAsync(db, ct),
            ConceptoNombre = sol.ConceptoNombre,
            Observacion = obs,
            GeneradoPorNombre = nombre,
            ExpiresAt = now,
            Estado = AutorizacionTokenEstado.Usado,
            UsadoEn = now,
            UsadoPorUserId = sol.SolicitadoPorUserId,
            UsadoPorNombre = sol.SolicitadoPorNombre,
            HistoriaClinicaId = sol.HistoriaClinicaId,
            AsignacionId = sol.AsignacionId,
            MotivoUso = sol.Motivo,
        };
        db.AutorizacionTokens.Add(token);

        // La HC queda autorizada para la fecha SOLICITADA (si luego cambia la fecha, se re-pide).
        hc.AutorizacionFechaTokenId = token.Id;
        hc.AutorizacionFechaAutorizada = sol.FechaAtencion;

        sol.Estado = AutorizacionSolicitudEstado.Aprobada;
        sol.ResueltoEn = now;
        sol.ResueltoPorUserId = actor;
        sol.ResueltoPorNombre = nombre;
        sol.Respuesta = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        sol.TokenId = token.Id;
        audit.Write(db, actor, "autorizacion-solicitud.aprobar", nameof(AutorizacionSolicitud), sol.Id,
            previousValue: new { estado = "Pendiente" },
            newValue: new { estado = "Aprobada", hcId = sol.HistoriaClinicaId, sol.FechaAtencion, tokenId = token.Id, observacion = sol.Respuesta },
            tenantId: tid);
        await db.SaveChangesAsync(ct); // solicitud + token + HC atomico
        return new UsarTokenResult(true, null, token.Id);
    }

    public async Task<UsarTokenResult> RechazarSolicitudAsync(Guid solicitudId, string observacion, string? actorNombre, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return new UsarTokenResult(false, "Sin tenant activo."); }
        var obs = (observacion ?? "").Trim();
        if (obs.Length == 0) { return new UsarTokenResult(false, "Escribe el motivo del rechazo."); }
        using var db = dbFactory.Create();
        if (!await PermisosUsuario.TieneAsync(db, actor, PermisosUsuario.Autorizaciones, ct))
        {
            return new UsarTokenResult(false, "No tienes permiso para rechazar autorizaciones.");
        }
        var sol = await db.AutorizacionSolicitudes.FirstOrDefaultAsync(s => s.Id == solicitudId, ct);
        if (sol is null) { return new UsarTokenResult(false, "Solicitud no encontrada."); }
        if (sol.Estado != AutorizacionSolicitudEstado.Pendiente) { return new UsarTokenResult(false, "La solicitud ya fue resuelta."); }
        sol.Estado = AutorizacionSolicitudEstado.Rechazada;
        sol.ResueltoEn = clock.GetUtcNow();
        sol.ResueltoPorUserId = actor;
        sol.ResueltoPorNombre = string.IsNullOrWhiteSpace(actorNombre) ? null : actorNombre.Trim();
        sol.Respuesta = obs;
        audit.Write(db, actor, "autorizacion-solicitud.rechazar", nameof(AutorizacionSolicitud), sol.Id,
            previousValue: new { estado = "Pendiente" },
            newValue: new { estado = "Rechazada", hcId = sol.HistoriaClinicaId, observacion = obs },
            tenantId: tid);
        await db.SaveChangesAsync(ct);
        return new UsarTokenResult(true, null);
    }

    // ── Conceptos configurables ──────────────────────────────────────────────────

    private static readonly string[] ConceptosDefault =
    {
        "Registro tardio por conectividad",
        "Error de digitacion de la fecha",
        "Reprogramacion / novedad del paciente",
        "Autorizacion administrativa",
        "Otro",
    };

    public async Task<IReadOnlyList<AutorizacionConceptoDto>> ListarConceptosAsync(bool incluirInactivos = false, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return Array.Empty<AutorizacionConceptoDto>(); }

        using var db = dbFactory.Create();
        // Siembra idempotente de ejemplos la primera vez que el tenant no tiene conceptos.
        var hay = await db.AutorizacionConceptos.AsNoTracking().AnyAsync(ct);
        if (!hay)
        {
            var orden = 1;
            foreach (var nom in ConceptosDefault)
            {
                db.AutorizacionConceptos.Add(new AutorizacionConcepto { TenantId = tid, Nombre = nom, Orden = orden++, Activo = true });
            }
            await db.SaveChangesAsync(ct);
        }

        var q = db.AutorizacionConceptos.AsNoTracking();
        if (!incluirInactivos) { q = q.Where(x => x.Activo); }
        var rows = await q.ToListAsync(ct);
        return rows
            .OrderBy(r => r.Orden).ThenBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(r => new AutorizacionConceptoDto(r.Id, r.Nombre, r.Orden, r.Activo))
            .ToList();
    }

    public async Task<AutorizacionConceptoDto> GuardarConceptoAsync(Guid? id, string nombre, int orden, bool activo, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var nom = (nombre ?? "").Trim();
        if (nom.Length == 0) { throw new InvalidOperationException("El nombre del concepto es obligatorio."); }

        using var db = dbFactory.Create();
        AutorizacionConcepto? entity;
        if (id is Guid gid)
        {
            entity = await db.AutorizacionConceptos.FirstOrDefaultAsync(x => x.Id == gid, ct);
            if (entity is null) { throw new InvalidOperationException("Concepto no encontrado."); }
        }
        else
        {
            var dup = await db.AutorizacionConceptos.AsNoTracking().AnyAsync(x => x.Nombre.ToUpper() == nom.ToUpper(), ct);
            if (dup) { throw new InvalidOperationException($"Ya existe un concepto '{nom}'."); }
            entity = new AutorizacionConcepto { TenantId = tid };
            db.AutorizacionConceptos.Add(entity);
        }
        entity.Nombre = nom;
        entity.Orden = orden;
        entity.Activo = activo;
        await db.SaveChangesAsync(ct);
        return new AutorizacionConceptoDto(entity.Id, entity.Nombre, entity.Orden, entity.Activo);
    }

    public async Task<bool> EliminarConceptoAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var e = await db.AutorizacionConceptos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        db.AutorizacionConceptos.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AutorizacionTokenDto>> ListarAsync(int max = 200, CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        var now = clock.GetUtcNow();
        var rows = await db.AutorizacionTokens.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(max)
            .ToListAsync(ct);

        // Consecutivos legibles (HC-xxxxx / AS-xxxxx) para la tabla de uso.
        var hcIds = rows.Where(r => r.HistoriaClinicaId != null).Select(r => r.HistoriaClinicaId!.Value).Distinct().ToList();
        var asigIds = rows.Where(r => r.AsignacionId != null).Select(r => r.AsignacionId!.Value).Distinct().ToList();
        var hcCons = hcIds.Count == 0
            ? new Dictionary<Guid, long>()
            : await db.HistoriasClinicas.AsNoTracking().Where(h => hcIds.Contains(h.Id))
                .Select(h => new { h.Id, h.Consecutivo }).ToDictionaryAsync(x => x.Id, x => x.Consecutivo, ct);
        var asigCons = asigIds.Count == 0
            ? new Dictionary<Guid, long>()
            : await db.Asignaciones.AsNoTracking().Where(a => asigIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Consecutivo }).ToDictionaryAsync(x => x.Id, x => x.Consecutivo, ct);

        return rows.Select(r => new AutorizacionTokenDto(
            r.Id, r.Codigo, r.ConceptoNombre, r.Observacion, r.GeneradoPorNombre, r.CreatedAt, r.ExpiresAt, r.Estado,
            r.Estado == AutorizacionTokenEstado.Activo && r.ExpiresAt < now,
            r.UsadoEn, r.UsadoPorNombre,
            r.HistoriaClinicaId,
            r.HistoriaClinicaId is Guid hid && hcCons.TryGetValue(hid, out var hc) ? hc : (long?)null,
            r.AsignacionId,
            r.AsignacionId is Guid aid && asigCons.TryGetValue(aid, out var ac) ? ac : (long?)null,
            r.MotivoUso)).ToList();
    }

    public async Task<UsarTokenResult> ValidarYMarcarUsoAsync(
        string codigo, Guid historiaClinicaId, Guid? asignacionId,
        string? usadoPorNombre, Guid actor, string? motivo, CancellationToken ct = default)
    {
        // Ruta autonoma (sin orquestador): contexto corto propio y SaveChanges aqui.
        // La variante con db la usa HistoriaClinicaService para persistir token + HC atomico.
        using var db = dbFactory.Create();
        var res = await ValidarYMarcarUsoAsync(db, codigo, historiaClinicaId, asignacionId, usadoPorNombre, actor, motivo, ct);
        if (res.Ok) { await db.SaveChangesAsync(ct); }
        return res;
    }

    public async Task<UsarTokenResult> ValidarYMarcarUsoAsync(
        IApplicationDbContext db, string codigo, Guid historiaClinicaId, Guid? asignacionId,
        string? usadoPorNombre, Guid actor, string? motivo, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return new UsarTokenResult(false, "Sin tenant activo."); }
        var code = (codigo ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) { return new UsarTokenResult(false, "Ingresa el token de autorizacion."); }
        var mot = (motivo ?? "").Trim();
        if (mot.Length == 0) { return new UsarTokenResult(false, "El motivo de uso es obligatorio."); }

        // Tracked (sin AsNoTracking): la mutacion la persiste el orquestador.
        var token = await db.AutorizacionTokens.FirstOrDefaultAsync(t => t.Codigo == code, ct);
        if (token is null) { return new UsarTokenResult(false, "Token no encontrado."); }
        if (token.Estado == AutorizacionTokenEstado.Usado) { return new UsarTokenResult(false, "Este token ya fue usado."); }
        var now = clock.GetUtcNow();
        if (token.ExpiresAt < now) { return new UsarTokenResult(false, "El token esta vencido (vigencia de 30 minutos)."); }

        token.Estado = AutorizacionTokenEstado.Usado;
        token.UsadoEn = now;
        token.UsadoPorUserId = actor;
        token.UsadoPorNombre = string.IsNullOrWhiteSpace(usadoPorNombre) ? null : usadoPorNombre.Trim();
        token.HistoriaClinicaId = historiaClinicaId;
        token.AsignacionId = asignacionId;
        token.MotivoUso = mot;
        audit.Write(db, actor, "autorizacion-token.usar", nameof(AutorizacionToken), token.Id,
            previousValue: null,
            newValue: new { token.Codigo, historiaClinicaId, asignacionId, motivo = mot, usadoPor = token.UsadoPorNombre },
            tenantId: tid);
        // Sin SaveChanges: lo persiste HistoriaClinicaService junto con la HC (atomico).
        return new UsarTokenResult(true, null, token.Id);
    }
}
