using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Tenancy.WhatsApp;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Alertas;

public sealed class AlertaService : IAlertaService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEmailSender _email;
    private readonly IHsmTemplateService _hsm;
    private readonly ILogger<AlertaService> _log;

    public AlertaService(
        IApplicationDbContext db, ITenantContext tenant,
        IEmailSender email, IHsmTemplateService hsm, ILogger<AlertaService> log)
    {
        _db = db;
        _tenant = tenant;
        _email = email;
        _hsm = hsm;
        _log = log;
    }

    // ============================ CRUD ============================

    public async Task<IReadOnlyList<AlertaReglaDto>> ListAsync(CancellationToken ct = default)
    {
        var reglas = await _db.AlertaReglas.AsNoTracking()
            .OrderBy(r => r.Orden).ThenBy(r => r.Nombre)
            .ToListAsync(ct);
        if (reglas.Count == 0) { return Array.Empty<AlertaReglaDto>(); }

        var userIds = reglas.Where(r => r.UsuarioSistemaId is Guid).Select(r => r.UsuarioSistemaId!.Value).Distinct().ToList();
        var usuarios = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.TenantUsers.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return reglas.Select(r => ToDto(r, r.UsuarioSistemaId is Guid uid && usuarios.TryGetValue(uid, out var e) ? e : null)).ToList();
    }

    public async Task<AlertaReglaDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.AlertaReglas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return null; }
        string? nombre = null;
        if (r.UsuarioSistemaId is Guid uid)
        {
            nombre = await _db.TenantUsers.AsNoTracking().Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct);
        }
        return ToDto(r, nombre);
    }

    public async Task<Guid> UpsertAsync(AlertaReglaUpsertRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Nombre)) { throw new InvalidOperationException("Indica un nombre para la regla."); }

        ValidarRequest(req);

        var paramsJson = req.HsmParametros is { Count: > 0 }
            ? JsonSerializer.Serialize(req.HsmParametros)
            : null;

        AlertaRegla entity;
        if (req.Id is Guid id)
        {
            entity = await _db.AlertaReglas.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("La regla no existe.");
        }
        else
        {
            entity = new AlertaRegla { TenantId = tid };
            _db.AlertaReglas.Add(entity);
        }

        entity.Nombre = req.Nombre.Trim();
        entity.Activa = req.Activa;
        entity.Orden = req.Orden;
        entity.Condicion = req.Condicion;
        entity.FiltroModulo = string.IsNullOrWhiteSpace(req.FiltroModulo) ? null : req.FiltroModulo.Trim();
        entity.DisparoTipo = req.DisparoTipo;
        entity.DiasDelMes = req.DisparoTipo == AlertaDisparoTipo.DiasDelMes ? NormalizarDias(req.DiasDelMes) : null;
        entity.MesesDespues = req.DisparoTipo == AlertaDisparoTipo.MesesDespues ? req.MesesDespues : null;
        entity.AnclaRelativa = req.DisparoTipo == AlertaDisparoTipo.MesesDespues ? req.AnclaRelativa : null;
        entity.Destinatario = req.Destinatario;
        entity.UsuarioSistemaId = req.Destinatario == AlertaDestinatario.UsuarioSistema ? req.UsuarioSistemaId : null;
        entity.Canal = req.Canal;
        entity.Asunto = req.Canal == AlertaCanal.Correo ? req.Asunto : null;
        entity.Cuerpo = req.Canal == AlertaCanal.Correo ? req.Cuerpo : null;
        entity.HsmLineId = req.Canal == AlertaCanal.WhatsApp ? req.HsmLineId : null;
        entity.HsmTemplateId = req.Canal == AlertaCanal.WhatsApp ? req.HsmTemplateId : null;
        entity.HsmTemplateName = req.Canal == AlertaCanal.WhatsApp ? req.HsmTemplateName : null;
        entity.HsmParameterCount = req.Canal == AlertaCanal.WhatsApp ? req.HsmParameterCount : 0;
        entity.HsmParametrosJson = req.Canal == AlertaCanal.WhatsApp ? paramsJson : null;

        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var r = await _db.AlertaReglas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return false; }
        _db.AlertaReglas.Remove(r);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ToggleActivaAsync(Guid id, bool activa, Guid actor, CancellationToken ct = default)
    {
        var r = await _db.AlertaReglas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return false; }
        r.Activa = activa;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AlertaLineaDto>> ListLineasGupshupAsync(CancellationToken ct = default)
    {
        return await _db.WhatsAppLines.AsNoTracking()
            .Where(l => l.Provider == WhatsAppProvider.Gupshup)
            .OrderBy(l => l.InstanceName)
            .Select(l => new AlertaLineaDto(l.Id, l.InstanceName + (l.PhoneNumber != null ? " (" + l.PhoneNumber + ")" : "")))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AlertaUsuarioDto>> ListUsuariosAsync(CancellationToken ct = default)
    {
        return await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Status == PlatformUserStatus.Active)
            .OrderBy(u => u.Email)
            .Select(u => new AlertaUsuarioDto(u.Id, u.Email, u.Email))
            .ToListAsync(ct);
    }

    // ======================== Evaluacion ========================

    public async Task<AlertaEvaluacionResult> EvaluarYDispararAsync(DateOnly hoy, bool forzar, Guid actor, CancellationToken ct = default)
    {
        var mensajes = new List<string>();
        int enviadas = 0, saltadas = 0, errores = 0;
        if (_tenant.TenantId is not Guid tid) { return new(0, 0, 0, mensajes); }

        var reglas = await _db.AlertaReglas.AsNoTracking()
            .Where(r => r.Activa)
            .OrderBy(r => r.Orden)
            .ToListAsync(ct);
        if (reglas.Count == 0) { return new(0, 0, 0, mensajes); }

        // Candidatos: asignaciones con turnos + sus agregados de sesiones.
        var candidatos = await CargarCandidatosAsync(ct);
        if (candidatos.Count == 0) { return new(0, 0, 0, mensajes); }

        // Lookups de contacto.
        var pacienteIds = candidatos.Select(c => c.PacienteId).Distinct().ToList();
        var pacientes = (await _db.Pacientes.AsNoTracking()
            .Where(p => pacienteIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.NumeroDocumento, p.Email, p.Telefono })
            .ToListAsync(ct))
            .ToDictionary(p => p.Id, p => new PacienteInfo(p.NombreCompleto, p.NumeroDocumento, p.Email, p.Telefono));

        var profIds = candidatos.Where(c => c.ProfesionalId is Guid).Select(c => c.ProfesionalId!.Value).Distinct().ToList();
        var profesionales = profIds.Count == 0
            ? new Dictionary<Guid, (string Nombre, string? Celular)>()
            : await _db.Profesionales.AsNoTracking()
                .Where(p => profIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => new ValueTuple<string, string?>(p.NombreCompleto, p.Celular), ct);

        // Correo del doctor: via TenantUser vinculado al profesional.
        var doctorEmails = profIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.TenantUsers.AsNoTracking()
                .Where(u => u.ProfesionalId != null && profIds.Contains(u.ProfesionalId!.Value) && u.Email != null)
                .GroupBy(u => u.ProfesionalId!.Value)
                .Select(g => new { ProfId = g.Key, Email = g.Max(x => x.Email) })
                .ToDictionaryAsync(x => x.ProfId, x => x.Email!, ct);

        // Usuarios del sistema referenciados por reglas.
        var reglaUserIds = reglas.Where(r => r.UsuarioSistemaId is Guid).Select(r => r.UsuarioSistemaId!.Value).Distinct().ToList();
        var reglaUsuarios = reglaUserIds.Count == 0
            ? new Dictionary<Guid, (string Email, Guid? ProfId)>()
            : (await _db.TenantUsers.AsNoTracking()
                .Where(u => reglaUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.ProfesionalId })
                .ToListAsync(ct))
                .ToDictionary(u => u.Id, u => (Email: u.Email, ProfId: u.ProfesionalId));
        // Celular del profesional vinculado a esos usuarios (para WhatsApp a usuario del sistema).
        var reglaUserProfIds = reglaUsuarios.Values.Where(v => v.ProfId is Guid).Select(v => v.ProfId!.Value).Distinct().ToList();
        var reglaUserCelulares = reglaUserProfIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _db.Profesionales.AsNoTracking()
                .Where(p => reglaUserProfIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Celular, ct);

        // Outbox previo (para dedup) de estas reglas.
        var reglaIds = reglas.Select(r => r.Id).ToList();
        var enviosPrevios = await _db.AlertaEnvios
            .Where(e => reglaIds.Contains(e.ReglaId))
            .ToListAsync(ct);
        var enviosIdx = enviosPrevios.ToDictionary(e => (e.ReglaId, e.AsignacionId, e.Periodo));

        foreach (var regla in reglas)
        {
            foreach (var cand in candidatos)
            {
                // Condicion.
                var cumple = regla.Condicion switch
                {
                    AlertaCondicion.SesionPendiente => cand.HasPending,
                    AlertaCondicion.AtencionesTerminadas => cand.AllFinished,
                    _ => false,
                };
                if (!cumple) { continue; }
                if (!string.IsNullOrWhiteSpace(regla.FiltroModulo)
                    && !string.Equals(cand.Modulo, regla.FiltroModulo, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(cand.TipoServicio, regla.FiltroModulo, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Disparo -> (dispara?, periodo)
                if (!ResolverDisparo(regla, cand, hoy, forzar, out var periodo)) { continue; }

                // Dedup.
                var key = (regla.Id, cand.AsignacionId, periodo);
                if (enviosIdx.TryGetValue(key, out var previo) && previo.Exito)
                {
                    saltadas++;
                    continue;
                }

                // Contacto.
                var (contacto, contactoError) = ResolverContacto(regla, cand, pacientes, profesionales, doctorEmails, reglaUsuarios, reglaUserCelulares);
                var ctx = BuildContexto(cand, pacientes, profesionales, hoy);

                bool ok; string? err; string? extId = null;
                if (contactoError is not null)
                {
                    ok = false; err = contactoError;
                }
                else if (regla.Canal == AlertaCanal.Correo)
                {
                    var asunto = Render(regla.Asunto, ctx);
                    var cuerpo = Render(regla.Cuerpo, ctx);
                    var r = await _email.SendAsync(contacto!, string.IsNullOrWhiteSpace(asunto) ? "Alerta VISAL" : asunto, ToHtml(cuerpo), ct);
                    ok = r.Ok; err = r.Error;
                }
                else // WhatsApp HSM
                {
                    if (regla.HsmLineId is not Guid lineId || string.IsNullOrWhiteSpace(regla.HsmTemplateId))
                    {
                        ok = false; err = "Regla WhatsApp sin linea o plantilla configurada.";
                    }
                    else
                    {
                        var parametros = RenderParametros(regla, ctx);
                        if (parametros.Count != regla.HsmParameterCount)
                        {
                            ok = false; err = $"La plantilla exige {regla.HsmParameterCount} parametros y la regla tiene {parametros.Count}.";
                        }
                        else
                        {
                            var r = await _hsm.SendTestAsync(lineId, regla.HsmTemplateId!, contacto!, parametros, actor, ct);
                            ok = r.Ok; err = r.Error;
                        }
                    }
                }

                // Registrar/actualizar outbox.
                if (previo is null)
                {
                    previo = new AlertaEnvio
                    {
                        TenantId = tid,
                        ReglaId = regla.Id,
                        AsignacionId = cand.AsignacionId,
                        PacienteId = cand.PacienteId,
                        Periodo = periodo,
                        Canal = regla.Canal,
                        Destinatario = regla.Destinatario,
                    };
                    _db.AlertaEnvios.Add(previo);
                    enviosIdx[key] = previo;
                }
                previo.Contacto = contacto;
                previo.FechaEnvio = DateTimeOffset.UtcNow;
                previo.Exito = ok;
                previo.Error = err;
                previo.ExternalId = extId;

                if (ok) { enviadas++; }
                else { errores++; mensajes.Add($"{regla.Nombre} / {ctx.PacienteNombre}: {err}"); }
            }
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Alertas evaluadas tenant {Tenant}: {Env} enviadas, {Skip} saltadas, {Err} errores.",
            tid, enviadas, saltadas, errores);
        return new(enviadas, saltadas, errores, mensajes);
    }

    public async Task<IReadOnlyList<AlertaEnvioDto>> ListEnviosRecientesAsync(int max = 200, CancellationToken ct = default)
    {
        if (max <= 0) { max = 200; }
        var envios = await _db.AlertaEnvios.AsNoTracking()
            .OrderByDescending(e => e.FechaEnvio)
            .Take(max)
            .ToListAsync(ct);
        if (envios.Count == 0) { return Array.Empty<AlertaEnvioDto>(); }

        var reglaIds = envios.Select(e => e.ReglaId).Distinct().ToList();
        var reglas = await _db.AlertaReglas.AsNoTracking()
            .Where(r => reglaIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);
        var pacIds = envios.Select(e => e.PacienteId).Distinct().ToList();
        var pacientes = await _db.Pacientes.AsNoTracking()
            .Where(p => pacIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NombreCompleto, ct);

        return envios.Select(e => new AlertaEnvioDto(
            e.Id,
            reglas.TryGetValue(e.ReglaId, out var rn) ? rn : "(regla eliminada)",
            pacientes.TryGetValue(e.PacienteId, out var pn) ? pn : "(paciente)",
            e.Contacto, e.Canal, e.Destinatario, e.FechaEnvio, e.Exito, e.Error, e.EstadoGestion, e.Periodo))
            .ToList();
    }

    public async Task<bool> MarcarGestionAsync(Guid envioId, AlertaGestion estado, Guid actor, CancellationToken ct = default)
    {
        var e = await _db.AlertaEnvios.FirstOrDefaultAsync(x => x.Id == envioId, ct);
        if (e is null) { return false; }
        e.EstadoGestion = estado;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ======================== Internos ========================

    private sealed record PacienteInfo(string NombreCompleto, string NumeroDocumento, string? Email, string? Telefono);

    private sealed record Candidato(
        Guid AsignacionId, Guid PacienteId, string Servicio, string TipoServicio, string? Modulo,
        string Contrato, int Cantidad, DateOnly FechaInicio, DateOnly? FechaFinal,
        bool HasPending, bool AllFinished, DateOnly? UltimaAtencion, Guid? ProfesionalId);

    private async Task<List<Candidato>> CargarCandidatosAsync(CancellationToken ct)
    {
        // Sesiones con su asignacion + profesional (via turno).
        var sesiones = await (from s in _db.AsignacionTurnoSesiones.AsNoTracking()
                              join t in _db.AsignacionTurnos.AsNoTracking() on s.AsignacionTurnoId equals t.Id
                              select new { t.AsignacionId, t.ProfesionalId, s.FechaAtencion, s.Completado })
                             .ToListAsync(ct);
        var turnos = await _db.AsignacionTurnos.AsNoTracking()
            .Select(t => new { t.AsignacionId, t.ProfesionalId, t.Cantidad })
            .ToListAsync(ct);
        if (turnos.Count == 0) { return new(); }

        var turnosPorAsig = turnos.GroupBy(t => t.AsignacionId)
            .ToDictionary(g => g.Key, g => new { Cantidad = g.Sum(x => x.Cantidad), PrimerProf = g.First().ProfesionalId });
        var sesPorAsig = sesiones.GroupBy(s => s.AsignacionId).ToDictionary(g => g.Key, g => g.ToList());

        var asigIds = turnosPorAsig.Keys.ToList();
        var asigs = await _db.Asignaciones.AsNoTracking()
            .Where(a => asigIds.Contains(a.Id))
            .Select(a => new { a.Id, a.PacienteId, a.NombreServicio, a.TipoServicio, a.Modulo, a.ContratoCodigo, a.Cantidad, a.FechaInicio, a.FechaFinal })
            .ToListAsync(ct);

        var res = new List<Candidato>(asigs.Count);
        foreach (var a in asigs)
        {
            var tinfo = turnosPorAsig[a.Id];
            var ses = sesPorAsig.TryGetValue(a.Id, out var lst) ? lst : new();
            var sesTotal = ses.Count;
            var completadas = ses.Count(x => x.Completado);
            var totalEsperado = sesTotal > 0 ? sesTotal : tinfo.Cantidad;
            var allFinished = totalEsperado > 0 && completadas >= totalEsperado;
            var hasPending = !allFinished && (sesTotal - completadas) > 0;

            DateOnly? ultima = ses.Where(x => x.Completado).Select(x => (DateOnly?)x.FechaAtencion).DefaultIfEmpty(null).Max();
            var profId = ses.Where(x => x.Completado).OrderByDescending(x => x.FechaAtencion)
                .Select(x => (Guid?)x.ProfesionalId).FirstOrDefault() ?? tinfo.PrimerProf;

            res.Add(new Candidato(a.Id, a.PacienteId, a.NombreServicio, a.TipoServicio, a.Modulo,
                a.ContratoCodigo, a.Cantidad, a.FechaInicio, a.FechaFinal,
                hasPending, allFinished, ultima, profId));
        }
        return res;
    }

    /// <summary>Decide si la regla dispara hoy para el candidato y calcula la clave de periodo.</summary>
    private static bool ResolverDisparo(AlertaRegla regla, Candidato cand, DateOnly hoy, bool forzar, out string periodo)
    {
        periodo = "";
        if (regla.DisparoTipo == AlertaDisparoTipo.DiasDelMes)
        {
            periodo = hoy.ToString("yyyy-MM");
            if (forzar) { return true; }
            var dias = ParseDias(regla.DiasDelMes);
            return dias.Contains(hoy.Day);
        }
        // MesesDespues
        var ancla = regla.AnclaRelativa == AlertaAnclaRelativa.UltimaAtencion ? cand.UltimaAtencion : cand.FechaFinal;
        if (ancla is not DateOnly a || regla.MesesDespues is not int n) { return false; }
        var target = a.AddMonths(n);
        periodo = target.ToString("yyyy-MM");
        if (forzar) { return true; }
        return hoy >= target;
    }

    private (string? Contacto, string? Error) ResolverContacto(
        AlertaRegla regla, Candidato cand,
        IReadOnlyDictionary<Guid, PacienteInfo> pacientes,
        IReadOnlyDictionary<Guid, (string Nombre, string? Celular)> profesionales,
        IReadOnlyDictionary<Guid, string> doctorEmails,
        IReadOnlyDictionary<Guid, (string Email, Guid? ProfId)> reglaUsuarios,
        IReadOnlyDictionary<Guid, string?> reglaUserCelulares)
    {
        switch (regla.Destinatario)
        {
            case AlertaDestinatario.Paciente:
                if (!pacientes.TryGetValue(cand.PacienteId, out var pac)) { return (null, "Paciente no encontrado."); }
                return regla.Canal == AlertaCanal.Correo
                    ? (Vacio(pac.Email) is string em ? (em, null) : (null, "El paciente no tiene correo."))
                    : (NormalizarTelefono(pac.Telefono) is string tel ? (tel, null) : (null, "El paciente no tiene telefono."));

            case AlertaDestinatario.DoctorAtendio:
                if (cand.ProfesionalId is not Guid pid) { return (null, "No se pudo identificar el profesional."); }
                if (regla.Canal == AlertaCanal.Correo)
                {
                    return doctorEmails.TryGetValue(pid, out var de) && !string.IsNullOrWhiteSpace(de)
                        ? (de, null)
                        : (null, "El doctor no tiene correo (usuario del sistema vinculado).");
                }
                return profesionales.TryGetValue(pid, out var pr) && NormalizarTelefono(pr.Celular) is string cel
                    ? (cel, null)
                    : (null, "El doctor no tiene celular.");

            case AlertaDestinatario.UsuarioSistema:
                if (regla.UsuarioSistemaId is not Guid uid || !reglaUsuarios.TryGetValue(uid, out var us))
                {
                    return (null, "Regla sin usuario del sistema valido.");
                }
                if (regla.Canal == AlertaCanal.Correo)
                {
                    return !string.IsNullOrWhiteSpace(us.Email) ? (us.Email, null) : (null, "El usuario no tiene correo.");
                }
                if (us.ProfId is Guid upid && reglaUserCelulares.TryGetValue(upid, out var ucel) && NormalizarTelefono(ucel) is string uc)
                {
                    return (uc, null);
                }
                return (null, "El usuario del sistema no tiene un profesional con celular para WhatsApp.");

            default:
                return (null, "Destinatario no soportado.");
        }
    }

    private static Ctx BuildContexto(Candidato cand, IReadOnlyDictionary<Guid, PacienteInfo> pacientes,
        IReadOnlyDictionary<Guid, (string Nombre, string? Celular)> profesionales, DateOnly hoy)
    {
        string pacNombre = "", pacDoc = "";
        if (pacientes.TryGetValue(cand.PacienteId, out var p)) { pacNombre = p.NombreCompleto; pacDoc = p.NumeroDocumento; }
        string doctor = cand.ProfesionalId is Guid pid && profesionales.TryGetValue(pid, out var pr) ? pr.Nombre : "";
        return new Ctx(pacNombre, pacDoc, cand.Servicio, cand.Contrato, cand.Cantidad,
            hoy, cand.FechaInicio, cand.FechaFinal, doctor, cand.UltimaAtencion);
    }

    private sealed record Ctx(
        string PacienteNombre, string PacienteDocumento, string Servicio, string Contrato, int Cantidad,
        DateOnly Hoy, DateOnly FechaInicio, DateOnly? FechaFinal, string Doctor, DateOnly? UltimaAtencion);

    private static string Render(string? tpl, Ctx c)
    {
        if (string.IsNullOrEmpty(tpl)) { return ""; }
        return tpl
            .Replace("{paciente}", c.PacienteNombre, StringComparison.OrdinalIgnoreCase)
            .Replace("{documento}", c.PacienteDocumento, StringComparison.OrdinalIgnoreCase)
            .Replace("{servicio}", c.Servicio, StringComparison.OrdinalIgnoreCase)
            .Replace("{contrato}", c.Contrato, StringComparison.OrdinalIgnoreCase)
            .Replace("{cantidad}", c.Cantidad.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{fecha}", c.Hoy.ToString("dd/MM/yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{fecha_inicio}", c.FechaInicio.ToString("dd/MM/yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{fecha_fin}", c.FechaFinal?.ToString("dd/MM/yyyy") ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{doctor}", c.Doctor, StringComparison.OrdinalIgnoreCase)
            .Replace("{ultima_atencion}", c.UltimaAtencion?.ToString("dd/MM/yyyy") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> RenderParametros(AlertaRegla regla, Ctx ctx)
    {
        if (string.IsNullOrWhiteSpace(regla.HsmParametrosJson)) { return new(); }
        List<string>? tokens;
        try { tokens = JsonSerializer.Deserialize<List<string>>(regla.HsmParametrosJson!); }
        catch { tokens = null; }
        if (tokens is null) { return new(); }
        return tokens.Select(t => Render(t, ctx)).ToList();
    }

    private static string ToHtml(string texto)
        => string.IsNullOrEmpty(texto) ? "" : "<div style=\"font-family:Arial,sans-serif;font-size:14px;color:#0f172a;white-space:pre-wrap\">"
            + System.Net.WebUtility.HtmlEncode(texto).Replace("\n", "<br/>") + "</div>";

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Normaliza a solo digitos y antepone 57 si son 10 (celular CO). Null si vacio.</summary>
    private static string? NormalizarTelefono(string? raw)
    {
        var telefono = PacienteTelefonoHelper.Principal(raw);
        if (string.IsNullOrWhiteSpace(telefono)) { return null; }
        var digits = new string(telefono.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return null; }
        if (digits.Length == 10) { digits = "57" + digits; }
        return digits;
    }

    private static HashSet<int> ParseDias(string? csv)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(csv)) { return set; }
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var d) && d >= 1 && d <= 31) { set.Add(d); }
        }
        return set;
    }

    private static string? NormalizarDias(string? csv)
    {
        var dias = ParseDias(csv).OrderBy(x => x).ToList();
        return dias.Count == 0 ? null : string.Join(",", dias);
    }

    private static void ValidarRequest(AlertaReglaUpsertRequest req)
    {
        if (req.DisparoTipo == AlertaDisparoTipo.DiasDelMes)
        {
            if (ParseDias(req.DiasDelMes).Count == 0)
            {
                throw new InvalidOperationException("Indica al menos un dia del mes (ej. 15,16,17).");
            }
        }
        else
        {
            if (req.MesesDespues is not int n || n < 0) { throw new InvalidOperationException("Indica los meses despues (>= 0)."); }
            if (req.AnclaRelativa is null) { throw new InvalidOperationException("Elige el ancla del disparo relativo."); }
        }
        if (req.Destinatario == AlertaDestinatario.UsuarioSistema && req.UsuarioSistemaId is null)
        {
            throw new InvalidOperationException("Elige el usuario del sistema destinatario.");
        }
        if (req.Canal == AlertaCanal.Correo)
        {
            if (string.IsNullOrWhiteSpace(req.Cuerpo)) { throw new InvalidOperationException("Escribe el cuerpo del correo."); }
        }
        else
        {
            if (req.HsmLineId is null || string.IsNullOrWhiteSpace(req.HsmTemplateId))
            {
                throw new InvalidOperationException("Elige la linea y la plantilla HSM de WhatsApp.");
            }
            var count = req.HsmParametros?.Count ?? 0;
            if (count != req.HsmParameterCount)
            {
                throw new InvalidOperationException($"La plantilla exige {req.HsmParameterCount} parametros; definiste {count}.");
            }
        }
    }

    private static AlertaReglaDto ToDto(AlertaRegla r, string? usuarioNombre)
    {
        List<string> parametros = new();
        if (!string.IsNullOrWhiteSpace(r.HsmParametrosJson))
        {
            try { parametros = JsonSerializer.Deserialize<List<string>>(r.HsmParametrosJson!) ?? new(); }
            catch { parametros = new(); }
        }
        return new AlertaReglaDto(
            r.Id, r.Nombre, r.Activa, r.Orden,
            r.Condicion, r.FiltroModulo,
            r.DisparoTipo, r.DiasDelMes, r.MesesDespues, r.AnclaRelativa,
            r.Destinatario, r.UsuarioSistemaId, usuarioNombre,
            r.Canal, r.Asunto, r.Cuerpo,
            r.HsmLineId, r.HsmTemplateId, r.HsmTemplateName, r.HsmParameterCount, parametros);
    }
}
