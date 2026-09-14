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
    private readonly Email.INotificacionEmailSender _notiEmail;
    private readonly IHsmTemplateService _hsm;
    private readonly IInformeTerapiasService _informe;
    private readonly ILogger<AlertaService> _log;

    public AlertaService(
        IApplicationDbContext db, ITenantContext tenant,
        IEmailSender email, Email.INotificacionEmailSender notiEmail,
        IHsmTemplateService hsm, IInformeTerapiasService informe, ILogger<AlertaService> log)
    {
        _db = db;
        _tenant = tenant;
        _email = email;
        _notiEmail = notiEmail;
        _hsm = hsm;
        _informe = informe;
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
        entity.FiltroModulo = NormalizarModulos(req.FiltroModulo);
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
        entity.HsmHeaderUrl = req.Canal == AlertaCanal.WhatsApp ? Vacio(req.HsmHeaderUrl) : null;

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

    public async Task<IReadOnlyList<AlertaServicioDto>> ListServiciosContratoAsync(CancellationToken ct = default)
    {
        // Servicios de los contratos del tenant. Un mismo codigo puede repetirse en
        // varios contratos; agrupamos por codigo base (sin sufijo d/f) para presentar
        // una lista limpia y guardar el codigo base en la regla.
        var filas = await _db.ServiciosContrato.AsNoTracking()
            .Where(s => s.CodigoServicio != null && s.CodigoServicio != "")
            .Select(s => new { s.CodigoServicio, s.Descripcion })
            .ToListAsync(ct);

        return filas
            .Select(s => new { Codigo = ServicioCodigo.Base(s.CodigoServicio) ?? "", s.Descripcion })
            .Where(s => !string.IsNullOrWhiteSpace(s.Codigo))
            .GroupBy(s => s.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AlertaServicioDto(
                g.Key,
                g.Select(x => x.Descripcion).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? ""))
            .OrderBy(s => s.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ======================== Evaluacion ========================

    public async Task<AlertaEvaluacionResult> EvaluarYDispararAsync(DateOnly hoy, bool forzar, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return new(0, 0, 0, Array.Empty<string>()); }

        var reglas = await _db.AlertaReglas.AsNoTracking()
            .Where(r => r.Activa)
            .OrderBy(r => r.Orden)
            .ToListAsync(ct);
        if (reglas.Count == 0) { return new(0, 0, 0, Array.Empty<string>()); }

        // Las reglas de control (InformeNoLeido) son agregadas (un resumen a gerencia),
        // no per-paciente: se procesan aparte del nucleo por-candidato.
        var reglasNormales = reglas.Where(r => r.Condicion != AlertaCondicion.InformeNoLeido).ToList();
        var nucleo = await EvaluarNucleoAsync(tid, reglasNormales, hoy, forzar, dryRun: false, telefonoOverride: null, forzarReenvio: false, actor, baseUri: null, soloAsignaciones: null, ct);

        var (agEnv, agErr, agMsgs) = await DispararInformeNoLeidoAsync(tid, reglas, hoy, forzar, ct);

        var mensajes = new List<string>(nucleo.Mensajes);
        mensajes.AddRange(agMsgs);
        _log.LogInformation("Alertas evaluadas tenant {Tenant}: {Env} enviadas, {Skip} saltadas, {Err} errores (control gerencia: {Ag} enviadas, {AgErr} errores).",
            tid, nucleo.Enviadas, nucleo.Saltadas, nucleo.Errores, agEnv, agErr);
        return new(nucleo.Enviadas + agEnv, nucleo.Saltadas, nucleo.Errores + agErr, mensajes);
    }

    /// <summary>Alerta agregada de control a gerencia: por cada regla InformeNoLeido que
    /// dispare hoy, arma el resumen de doctores a los que se les envio la alerta del informe
    /// en el periodo y NO abrieron su enlace, y lo envia por correo al usuario de gerencia.
    /// Deduplicado por (regla, periodo) con una fila de outbox de asignacion vacia.</summary>
    private async Task<(int Enviadas, int Errores, List<string> Msgs)> DispararInformeNoLeidoAsync(
        Guid tid, List<AlertaRegla> reglas, DateOnly hoy, bool forzar, CancellationToken ct)
    {
        var msgs = new List<string>();
        int enviadas = 0, errores = 0;
        var candidatas = reglas.Where(r => r.Condicion == AlertaCondicion.InformeNoLeido).ToList();
        if (candidatas.Count == 0) { return (0, 0, msgs); }

        var periodo = hoy.ToString("yyyy-MM");
        foreach (var regla in candidatas)
        {
            bool dispara = forzar
                || (regla.DisparoTipo == AlertaDisparoTipo.DiasDelMes && ParseDias(regla.DiasDelMes).Contains(hoy.Day));
            if (!dispara) { continue; }

            if (regla.UsuarioSistemaId is not Guid uid)
            {
                errores++; msgs.Add($"{regla.Nombre}: sin usuario de gerencia configurado."); continue;
            }
            var user = await _db.TenantUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
            var correo = Vacio(user?.Email);
            if (correo is null)
            {
                errores++; msgs.Add($"{regla.Nombre}: el usuario de gerencia no tiene correo."); continue;
            }

            // Dedup por (regla, periodo): fila de outbox con asignacion vacia.
            var previo = await _db.AlertaEnvios
                .FirstOrDefaultAsync(e => e.ReglaId == regla.Id && e.AsignacionId == Guid.Empty && e.Periodo == periodo, ct);
            if (previo is not null && previo.Exito && !forzar) { continue; }

            var datos = await ControlLecturaCoreAsync(tid, periodo, ct);
            var noLey = datos.Where(d => d.Aperturas == 0).OrderBy(d => d.Nombre, StringComparer.OrdinalIgnoreCase).ToList();

            var asunto = string.IsNullOrWhiteSpace(regla.Asunto)
                ? $"Control de lectura de informes {periodo}"
                : regla.Asunto!;

            bool ok; string? err;
            if (await _notiEmail.TieneCuentaAsync(tid, ct))
            {
                var r = await _notiEmail.SendAsync(tid, correo, asunto, BuildNoLeidoTexto(periodo, datos.Count, noLey), ct);
                ok = r.Ok; err = r.Error;
            }
            else
            {
                var r = await _email.SendAsync(correo, asunto, BuildNoLeidoHtml(periodo, datos.Count, noLey), ct);
                ok = r.Ok; err = r.Error;
            }

            if (previo is null)
            {
                previo = new AlertaEnvio
                {
                    TenantId = tid, ReglaId = regla.Id, AsignacionId = Guid.Empty, PacienteId = Guid.Empty,
                    Periodo = periodo, Canal = AlertaCanal.Correo, Destinatario = AlertaDestinatario.UsuarioSistema,
                };
                _db.AlertaEnvios.Add(previo);
            }
            previo.Contacto = correo;
            previo.FechaEnvio = DateTimeOffset.UtcNow;
            previo.Exito = ok;
            previo.Error = err;
            await _db.SaveChangesAsync(ct);

            if (ok) { enviadas++; } else { errores++; msgs.Add($"{regla.Nombre}: {err}"); }
        }
        return (enviadas, errores, msgs);
    }

    private static string BuildNoLeidoTexto(string periodo, int totalDoctores, List<ControlLecturaDoctorDto> noLey)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Control de lectura de informes - periodo ").Append(periodo).Append('\n');
        sb.Append($"Doctores notificados: {totalDoctores}. No leyeron su informe: {noLey.Count}.\n\n");
        if (noLey.Count == 0) { sb.Append("Todos los doctores notificados abrieron su enlace. Sin pendientes.\n"); return sb.ToString(); }
        sb.Append("Doctores que NO abrieron su enlace:\n");
        var i = 1;
        foreach (var d in noLey)
        {
            sb.Append(i++).Append(". ").Append(d.Nombre);
            if (!string.IsNullOrWhiteSpace(d.Celular)) { sb.Append(" (").Append(d.Celular).Append(')'); }
            sb.Append(" - envios: ").Append(d.Enviados);
            if (d.UltimoEnvio is { } ue) { sb.Append(", ultimo envio: ").Append(ue.ToLocalTime().ToString("dd/MM/yyyy")); }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string BuildNoLeidoHtml(string periodo, int totalDoctores, List<ControlLecturaDoctorDto> noLey)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<div style=\"font-family:Arial,sans-serif;font-size:14px;color:#0f172a\">");
        sb.Append($"<h2 style=\"margin:0 0 4px\">Control de lectura de informes</h2>");
        sb.Append($"<p style=\"margin:0 0 12px;color:#475569\">Periodo <b>{System.Net.WebUtility.HtmlEncode(periodo)}</b> · Doctores notificados: <b>{totalDoctores}</b> · No leyeron: <b>{noLey.Count}</b></p>");
        if (noLey.Count == 0)
        {
            sb.Append("<p style=\"color:#16a34a\">Todos los doctores notificados abrieron su enlace. Sin pendientes.</p></div>");
            return sb.ToString();
        }
        sb.Append("<table style=\"border-collapse:collapse;font-size:13px\"><thead><tr>");
        sb.Append("<th style=\"text-align:left;border-bottom:2px solid #e2e8f0;padding:6px 10px\">Doctor</th>");
        sb.Append("<th style=\"text-align:left;border-bottom:2px solid #e2e8f0;padding:6px 10px\">Celular</th>");
        sb.Append("<th style=\"text-align:center;border-bottom:2px solid #e2e8f0;padding:6px 10px\">Envios</th>");
        sb.Append("<th style=\"text-align:left;border-bottom:2px solid #e2e8f0;padding:6px 10px\">Ultimo envio</th></tr></thead><tbody>");
        foreach (var d in noLey)
        {
            sb.Append("<tr>");
            sb.Append($"<td style=\"padding:6px 10px;border-bottom:1px solid #eef2f7\"><b>{System.Net.WebUtility.HtmlEncode(d.Nombre)}</b></td>");
            sb.Append($"<td style=\"padding:6px 10px;border-bottom:1px solid #eef2f7\">{System.Net.WebUtility.HtmlEncode(d.Celular ?? "-")}</td>");
            sb.Append($"<td style=\"padding:6px 10px;border-bottom:1px solid #eef2f7;text-align:center\">{d.Enviados}</td>");
            sb.Append($"<td style=\"padding:6px 10px;border-bottom:1px solid #eef2f7\">{(d.UltimoEnvio is { } ue ? ue.ToLocalTime().ToString("dd/MM/yyyy") : "-")}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    public async Task<AlertaSimulacionResult> SimularReglaAsync(AlertaReglaUpsertRequest req, DateOnly fecha, bool emitir, string? telefonoOverride, bool forzarReenvio, Guid actor, string? baseUri = null, IReadOnlyCollection<Guid>? soloAsignaciones = null, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (emitir && req.Id is null) { throw new InvalidOperationException("Guarda la regla antes de emitir la simulacion."); }

        // Regla transitoria construida desde la config del modal (guardada o no).
        var regla = new AlertaRegla
        {
            Id = req.Id ?? Guid.Empty,
            TenantId = tid,
            Nombre = string.IsNullOrWhiteSpace(req.Nombre) ? "(simulacion)" : req.Nombre.Trim(),
            Activa = true,
            Orden = req.Orden,
            Condicion = req.Condicion,
            FiltroModulo = NormalizarModulos(req.FiltroModulo),
            DisparoTipo = req.DisparoTipo,
            DiasDelMes = req.DisparoTipo == AlertaDisparoTipo.DiasDelMes ? NormalizarDias(req.DiasDelMes) : null,
            MesesDespues = req.DisparoTipo == AlertaDisparoTipo.MesesDespues ? req.MesesDespues : null,
            AnclaRelativa = req.DisparoTipo == AlertaDisparoTipo.MesesDespues ? req.AnclaRelativa : null,
            Destinatario = req.Destinatario,
            UsuarioSistemaId = req.Destinatario == AlertaDestinatario.UsuarioSistema ? req.UsuarioSistemaId : null,
            Canal = req.Canal,
            Asunto = req.Canal == AlertaCanal.Correo ? req.Asunto : null,
            Cuerpo = req.Canal == AlertaCanal.Correo ? req.Cuerpo : null,
            HsmLineId = req.Canal == AlertaCanal.WhatsApp ? req.HsmLineId : null,
            HsmTemplateId = req.Canal == AlertaCanal.WhatsApp ? req.HsmTemplateId : null,
            HsmTemplateName = req.Canal == AlertaCanal.WhatsApp ? req.HsmTemplateName : null,
            HsmParameterCount = req.Canal == AlertaCanal.WhatsApp ? req.HsmParameterCount : 0,
            HsmParametrosJson = req.Canal == AlertaCanal.WhatsApp && req.HsmParametros is { Count: > 0 }
                ? JsonSerializer.Serialize(req.HsmParametros) : null,
            HsmHeaderUrl = req.Canal == AlertaCanal.WhatsApp ? Vacio(req.HsmHeaderUrl) : null,
        };

        // Simula la fecha elegida con la logica real de disparo (no forzar).
        var nucleo = await EvaluarNucleoAsync(tid, new List<AlertaRegla> { regla }, fecha, forzar: false, dryRun: !emitir, telefonoOverride, forzarReenvio, actor, baseUri, soloAsignaciones, ct);
        var filas = nucleo.Filas;

        string? aviso = null;
        if (regla.DisparoTipo == AlertaDisparoTipo.DiasDelMes && !ParseDias(regla.DiasDelMes).Contains(fecha.Day))
        {
            aviso = $"Ojo: los dias configurados son {regla.DiasDelMes}. El {fecha:dd/MM} no es uno de ellos, "
                  + "por eso no hay candidatos. Elige un dia configurado para ver el disparo.";
        }
        else if (filas.Count == 0)
        {
            aviso = "Ningun paciente cumple la condicion y el filtro para esta fecha.";
        }

        return new AlertaSimulacionResult(
            fecha, fecha.ToString("yyyy-MM"), emitir,
            Coinciden: filas.Count,
            Emitibles: filas.Count(f => f.Emitible),
            SinContacto: filas.Count(f => f.Emitible == false && string.IsNullOrEmpty(f.ContactoUsado) && f.Estado.StartsWith("Sin", StringComparison.OrdinalIgnoreCase)),
            YaEnviadas: filas.Count(f => f.Estado.StartsWith("Ya", StringComparison.OrdinalIgnoreCase)),
            Enviadas: nucleo.Enviadas, Errores: nucleo.Errores,
            Filas: filas, Aviso: aviso);
    }

    private sealed record NucleoResult(int Enviadas, int Saltadas, int Errores, List<string> Mensajes, List<AlertaSimulacionFila> Filas);

    private async Task<NucleoResult> EvaluarNucleoAsync(Guid tid, List<AlertaRegla> reglas, DateOnly hoy, bool forzar, bool dryRun, string? telefonoOverride, bool forzarReenvio, Guid actor, string? baseUri, IReadOnlyCollection<Guid>? soloAsignaciones, CancellationToken ct)
    {
        var overrideTel = string.IsNullOrWhiteSpace(telefonoOverride) ? null : NormalizarTelefono(telefonoOverride);
        var baseUriInforme = string.IsNullOrWhiteSpace(baseUri) ? null : baseUri!.Trim();
        var filtroAsignaciones = soloAsignaciones is { Count: > 0 } ? new HashSet<Guid>(soloAsignaciones) : null;

        var mensajes = new List<string>();
        var filas = new List<AlertaSimulacionFila>();
        int enviadas = 0, saltadas = 0, errores = 0;

        // Candidatos: asignaciones con turnos + sus agregados de sesiones.
        var candidatos = await CargarCandidatosAsync(ct);
        if (candidatos.Count == 0) { return new(0, 0, 0, mensajes, filas); }

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

        // Colapso de WhatsApp DENTRO de esta corrida: un profesional puede tener varias
        // asignaciones que disparan la misma regla en el mismo periodo; la notificacion
        // se envia UNA sola vez por (contacto, periodo) y las demas quedan "Agrupada"
        // (el informe ya agrega todas sus terapias). No se siembra con historial: la
        // deduplicacion entre corridas ya la maneja el chequeo por-asignacion (previo
        // .Exito && !forzarReenvio); sembrar bloquearia el reenvio forzado.
        var whatsappNotificado = new HashSet<string>();

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
                // Filtro por servicio(s) de contrato: la asignacion debe corresponder a uno
                // de los codigos de servicio seleccionados (comparado por codigo base sin d/f).
                var filtro = ParseModulos(regla.FiltroModulo);
                if (filtro.Count > 0 && !(cand.CodigoBase is not null && filtro.Contains(cand.CodigoBase)))
                {
                    continue;
                }

                // Disparo -> (dispara?, periodo)
                if (!ResolverDisparo(regla, cand, hoy, forzar, out var periodo)) { continue; }

                // Datos de display del destinatario (para la simulacion).
                var (destNombre, destCorreo, destTelefono) = ResolverDisplay(regla, cand, pacientes, profesionales, doctorEmails, reglaUsuarios, reglaUserCelulares);
                var destTipo = regla.Destinatario switch
                {
                    AlertaDestinatario.Paciente => "Paciente",
                    AlertaDestinatario.DoctorAtendio => "Doctor que atendio",
                    AlertaDestinatario.UsuarioSistema => "Usuario del sistema",
                    _ => regla.Destinatario.ToString(),
                };
                var pacInfo = pacientes.TryGetValue(cand.PacienteId, out var pinfo) ? pinfo : null;

                // Seleccion de la simulacion (Paso 2 real): si el usuario marco filas
                // concretas en la tabla, el envio se limita a esas asignaciones. En
                // dry-run (Paso 1) no aplica: se listan todos los candidatos.
                if (!dryRun && filtroAsignaciones is not null && !filtroAsignaciones.Contains(cand.AsignacionId))
                {
                    continue;
                }

                // Enlace del informe acotado al doctor de esta fila (solo para mostrar en
                // la tabla y poder abrirlo/validarlo; no altera lo que se envia).
                string? informeUrl = baseUriInforme is not null && cand.ProfesionalId is Guid infPid
                    ? SafeEnlaceInforme(baseUriInforme, tid, infPid)
                    : null;

                // Dedup: ya enviada con exito este periodo. Con forzarReenvio se ignora
                // (se reenvia y se actualiza el mismo registro del outbox). Con telefono
                // de prueba (override) tambien se ignora: es una prueba que va a mi numero
                // y no debe quedar bloqueada por lo ya enviado a los reales.
                var key = (regla.Id, cand.AsignacionId, periodo);
                enviosIdx.TryGetValue(key, out var previo);
                if (previo is not null && previo.Exito && !forzarReenvio && overrideTel is null)
                {
                    saltadas++;
                    filas.Add(new AlertaSimulacionFila(
                        pacInfo?.NombreCompleto ?? "", pacInfo?.NumeroDocumento ?? "", cand.Servicio, cand.CodigoBase ?? "",
                        destTipo, destNombre, destCorreo, destTelefono, regla.Canal, previo.Contacto,
                        "Ya enviada este periodo", Emitible: false, EnvioOk: null, EnvioError: null,
                        AsignacionId: cand.AsignacionId, ProfesionalId: cand.ProfesionalId, InformeUrl: informeUrl));
                    continue;
                }

                // Contacto que se usaria + contexto para render.
                var (contacto, contactoError) = ResolverContacto(regla, cand, pacientes, profesionales, doctorEmails, reglaUsuarios, reglaUserCelulares);
                var ctx = BuildContexto(cand, pacientes, profesionales, hoy);

                // Override de telefono (solo simulacion, canal WhatsApp): reemplaza el
                // destino resuelto por el numero de prueba indicado en el modal.
                if (overrideTel is not null && regla.Canal == AlertaCanal.WhatsApp)
                {
                    contacto = overrideTel;
                    contactoError = null;
                    destTelefono = overrideTel;
                }

                if (dryRun)
                {
                    // Paso 1 (simulacion): no se envia nada, solo se muestra a quien iria.
                    var emitible = contactoError is null;
                    filas.Add(new AlertaSimulacionFila(
                        pacInfo?.NombreCompleto ?? "", pacInfo?.NumeroDocumento ?? "", cand.Servicio, cand.CodigoBase ?? "",
                        destTipo, destNombre, destCorreo, destTelefono, regla.Canal, emitible ? contacto : null,
                        emitible ? "Se emitiria" : ("Sin contacto: " + contactoError), Emitible: emitible, EnvioOk: null, EnvioError: null,
                        AsignacionId: cand.AsignacionId, ProfesionalId: cand.ProfesionalId, InformeUrl: informeUrl));
                    continue;
                }

                bool ok; string? err; string? extId = null; bool agrupada = false;
                if (contactoError is not null)
                {
                    ok = false; err = contactoError;
                }
                else if (regla.Canal == AlertaCanal.Correo)
                {
                    var asunto = Render(regla.Asunto, ctx);
                    var cuerpo = Render(regla.Cuerpo, ctx);
                    var asuntoFinal = string.IsNullOrWhiteSpace(asunto) ? "Alerta VISAL" : asunto;
                    // Preferir la cuenta de correo (Gmail/PQR) de la agencia; envia texto plano
                    // desde el buzon propio. Si no hay cuenta, cae al SMTP global (HTML).
                    EmailSendResult r = await _notiEmail.TieneCuentaAsync(tid, ct)
                        ? await _notiEmail.SendAsync(tid, contacto!, asuntoFinal, cuerpo, ct)
                        : await _email.SendAsync(contacto!, asuntoFinal, ToHtml(cuerpo), ct);
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
                        var waKey = $"{contacto}|{periodo}";
                        if (whatsappNotificado.Contains(waKey))
                        {
                            // Ya se envio un WhatsApp a este profesional en este periodo:
                            // no reenviamos. Se registra como exitoso (agrupado) para
                            // mantener el tracking y que el informe agregue todo lo suyo.
                            ok = true; err = null; agrupada = true;
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
                                var r = await _hsm.SendTestAsync(lineId, regla.HsmTemplateId!, contacto!, parametros, actor,
                                    headerMediaUrl: regla.HsmHeaderUrl, ct: ct);
                                ok = r.Ok; err = r.Error;
                                if (ok) { whatsappNotificado.Add(waKey); }
                            }
                        }
                    }
                }

                // Registrar/actualizar outbox — SOLO en envio real. Con telefono de prueba
                // (override) es una prueba a mi numero: no se toca la deduplicacion real
                // para no "olvidar" el numero verdadero ni marcar como enviado a los doctores.
                if (overrideTel is null)
                {
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
                    // Guardar el profesional destinatario para el control de lectura del
                    // informe (cruce "a quien se envio" vs "quien abrio el enlace").
                    previo.ProfesionalId = regla.Destinatario == AlertaDestinatario.DoctorAtendio
                        ? cand.ProfesionalId : null;
                }

                if (agrupada) { saltadas++; }
                else if (ok) { enviadas++; }
                else { errores++; mensajes.Add($"{regla.Nombre} / {ctx.PacienteNombre}: {err}"); }
                filas.Add(new AlertaSimulacionFila(
                    pacInfo?.NombreCompleto ?? "", pacInfo?.NumeroDocumento ?? "", cand.Servicio, cand.CodigoBase ?? "",
                    destTipo, destNombre, destCorreo, destTelefono, regla.Canal, contacto,
                    agrupada ? "Agrupada (ya notificado al profesional este periodo)" : (ok ? "Enviada" : ("Error: " + err)),
                    Emitible: ok, EnvioOk: agrupada ? (bool?)null : ok, EnvioError: err,
                    AsignacionId: cand.AsignacionId, ProfesionalId: cand.ProfesionalId, InformeUrl: informeUrl));
            }
        }

        if (!dryRun) { await _db.SaveChangesAsync(ct); }
        return new NucleoResult(enviadas, saltadas, errores, mensajes, filas);
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

    public async Task<ControlLecturaResult> ObtenerControlLecturaAsync(string periodo, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        periodo = string.IsNullOrWhiteSpace(periodo) ? DateTime.Now.ToString("yyyy-MM") : periodo.Trim();

        var datos = await ControlLecturaCoreAsync(tid, periodo, ct);
        var consum = datos.Where(d => d.Aperturas > 0).OrderByDescending(d => d.UltimaApertura).ToList();
        var noLey = datos.Where(d => d.Aperturas == 0).OrderBy(d => d.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
        return new ControlLecturaResult(periodo, datos.Count, consum.Count, noLey.Count, consum, noLey);
    }

    /// <summary>Nucleo del control de lectura: doctores a los que se les envio la alerta en
    /// el periodo con su conteo de envios y de aperturas del enlace. Reusado por el reporte
    /// y por la alerta a gerencia.</summary>
    private async Task<List<ControlLecturaDoctorDto>> ControlLecturaCoreAsync(Guid tid, string periodo, CancellationToken ct)
    {
        var envios = await _db.AlertaEnvios.AsNoTracking()
            .Where(e => e.Destinatario == AlertaDestinatario.DoctorAtendio
                     && e.Exito && e.Periodo == periodo && e.ProfesionalId != null)
            .Select(e => new { ProfId = e.ProfesionalId!.Value, e.FechaEnvio })
            .ToListAsync(ct);
        if (envios.Count == 0) { return new(); }

        var porDoctor = envios.GroupBy(e => e.ProfId)
            .ToDictionary(g => g.Key, g => (Enviados: g.Count(), UltimoEnvio: g.Max(x => x.FechaEnvio)));
        var profIds = porDoctor.Keys.ToList();

        var (ini, fin) = RangoMes(periodo);
        var accesos = await _db.InformeAccesos.AsNoTracking()
            .Where(a => a.ProfesionalId != null && profIds.Contains(a.ProfesionalId.Value)
                     && a.AccedidoEn >= ini && a.AccedidoEn < fin)
            .Select(a => new { ProfId = a.ProfesionalId!.Value, a.AccedidoEn })
            .ToListAsync(ct);
        var aperturas = accesos.GroupBy(a => a.ProfId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Ultima: g.Max(x => x.AccedidoEn)));

        var profs = (await _db.Profesionales.AsNoTracking()
            .Where(p => profIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.Celular })
            .ToListAsync(ct))
            .ToDictionary(p => p.Id, p => (p.NombreCompleto, p.Celular));

        var res = new List<ControlLecturaDoctorDto>(profIds.Count);
        foreach (var pid in profIds)
        {
            var env = porDoctor[pid];
            var tieneAp = aperturas.TryGetValue(pid, out var ap);
            var (nombre, cel) = profs.TryGetValue(pid, out var pn) ? (pn.NombreCompleto, pn.Celular) : ("(profesional desconocido)", (string?)null);
            res.Add(new ControlLecturaDoctorDto(
                pid, nombre, cel, env.Enviados,
                tieneAp ? ap.Count : 0, tieneAp ? ap.Ultima : (DateTimeOffset?)null, env.UltimoEnvio));
        }
        return res;
    }

    /// <summary>Rango [inicio, fin) en UTC del mes "yyyy-MM". Fallback al mes actual.</summary>
    private static (DateTimeOffset Ini, DateTimeOffset Fin) RangoMes(string periodo)
    {
        int y = DateTime.Now.Year, m = DateTime.Now.Month;
        var parts = (periodo ?? "").Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var py) && int.TryParse(parts[1], out var pm)
            && pm is >= 1 and <= 12) { y = py; m = pm; }
        var ini = new DateTimeOffset(new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc));
        return (ini, ini.AddMonths(1));
    }

    // ======================== Internos ========================

    private sealed record PacienteInfo(string NombreCompleto, string NumeroDocumento, string? Email, string? Telefono);

    private sealed record Candidato(
        Guid AsignacionId, Guid PacienteId, string Servicio, string TipoServicio, string? Modulo,
        string? CodigoBase, string Contrato, int Cantidad, DateOnly FechaInicio, DateOnly? FechaFinal,
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
            .Select(a => new { a.Id, a.PacienteId, a.NombreServicio, a.TipoServicio, a.Modulo, a.CodigoRips, a.ContratoCodigo, a.Cantidad, a.FechaInicio, a.FechaFinal })
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

            var codigoBase = string.IsNullOrWhiteSpace(a.CodigoRips) ? null : ServicioCodigo.Base(a.CodigoRips);

            res.Add(new Candidato(a.Id, a.PacienteId, a.NombreServicio, a.TipoServicio, a.Modulo,
                codigoBase, a.ContratoCodigo, a.Cantidad, a.FechaInicio, a.FechaFinal,
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

    /// <summary>Nombre/correo/telefono del destinatario para mostrar en la simulacion
    /// (independiente del canal — muestra ambos datos de contacto disponibles).</summary>
    private (string? Nombre, string? Correo, string? Telefono) ResolverDisplay(
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
                return pacientes.TryGetValue(cand.PacienteId, out var pac)
                    ? (pac.NombreCompleto, Vacio(pac.Email), Vacio(pac.Telefono))
                    : (null, null, null);

            case AlertaDestinatario.DoctorAtendio:
                if (cand.ProfesionalId is not Guid pid) { return (null, null, null); }
                string? dnom = profesionales.TryGetValue(pid, out var pr) ? pr.Nombre : null;
                string? dcel = profesionales.TryGetValue(pid, out var pr2) ? Vacio(pr2.Celular) : null;
                string? dmail = doctorEmails.TryGetValue(pid, out var de) ? Vacio(de) : null;
                return (dnom, dmail, dcel);

            case AlertaDestinatario.UsuarioSistema:
                if (regla.UsuarioSistemaId is not Guid uid || !reglaUsuarios.TryGetValue(uid, out var us))
                {
                    return (null, null, null);
                }
                string? ucel = us.ProfId is Guid upid && reglaUserCelulares.TryGetValue(upid, out var c) ? Vacio(c) : null;
                return (us.Email, Vacio(us.Email), ucel);

            default:
                return (null, null, null);
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

    /// <summary>Genera el enlace del informe acotado a un profesional para mostrarlo en la
    /// tabla de simulacion. Best-effort: si algo falla (sin base, sin tenant) devuelve null
    /// para no romper la simulacion.</summary>
    private string? SafeEnlaceInforme(string baseUri, Guid tenantId, Guid profesionalId)
    {
        try { return _informe.GenerarEnlace(baseUri, tenantId, profesionalId); }
        catch { return null; }
    }

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

    /// <summary>Parsea la CSV de tipos de servicio a un set case-insensitive de codigos.</summary>
    private static HashSet<string> ParseModulos(string? csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) { return set; }
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }
        return set;
    }

    /// <summary>Normaliza la CSV de tipos de servicio (dedupe, orden estable). Null si vacia.</summary>
    private static string? NormalizarModulos(string? csv)
    {
        var codigos = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(csv))
        {
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (vistos.Add(part)) { codigos.Add(part); }
            }
        }
        return codigos.Count == 0 ? null : string.Join(",", codigos);
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
        // Control a gerencia (InformeNoLeido): es agregado y por correo; el cuerpo se
        // genera automaticamente. Solo exige un usuario de gerencia como destinatario.
        if (req.Condicion == AlertaCondicion.InformeNoLeido)
        {
            if (req.Destinatario != AlertaDestinatario.UsuarioSistema || req.UsuarioSistemaId is null)
            {
                throw new InvalidOperationException("El control a gerencia debe dirigirse a un usuario del sistema (gerencia).");
            }
            return;
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
            r.HsmLineId, r.HsmTemplateId, r.HsmTemplateName, r.HsmParameterCount, parametros,
            r.HsmHeaderUrl);
    }
}
