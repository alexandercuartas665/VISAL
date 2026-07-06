using System.Security.Cryptography;
using Visal.Application.Common;
using Visal.Application.Tenancy.WhatsApp;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Visal.Application.Tenancy;

public sealed class FirmaRemotaService : IFirmaRemotaService
{
    /// <summary>Duracion del link de firma. 2 horas (acordado con negocio).</summary>
    private static readonly TimeSpan _vigencia = TimeSpan.FromHours(2);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IChatService _chat;
    private readonly Common.IUploadStorage _storage;
    private readonly TimeProvider _time;
    private readonly IWhatsAppTemplateBindingService _bindings;
    private readonly IHsmTemplateService _hsm;
    private readonly ILogger<FirmaRemotaService> _log;

    public FirmaRemotaService(
        IApplicationDbContext db, ITenantContext tenant, IChatService chat,
        Common.IUploadStorage storage, TimeProvider time,
        IWhatsAppTemplateBindingService bindings, IHsmTemplateService hsm,
        ILogger<FirmaRemotaService> log)
    {
        _db = db;
        _tenant = tenant;
        _chat = chat;
        _storage = storage;
        _time = time;
        _bindings = bindings;
        _hsm = hsm;
        _log = log;
    }

    /// <summary>Ofusca un telefono para logs: deja los ultimos 4 digitos.
    /// Cumple la regla del proyecto de no exponer contactos completos en logs.</summary>
    private static string MaskPhone(string? tel)
    {
        var digits = new string((tel ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) { return new string('*', digits.Length); }
        return new string('*', digits.Length - 4) + digits.Substring(digits.Length - 4);
    }

    public async Task<FirmaRequestDto?> CrearOReutilizarAsync(Guid notaMedicaId, Guid pacienteId, string telefono, string? nombreContacto, Guid actorTenantUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var digits = Digits(telefono);
        if (digits.Length == 0) { return null; }

        // Si ya hay una pendiente sin expirar para esta nota, la devolvemos.
        var ahora = _time.GetUtcNow();
        var existente = await _db.FirmaPacienteRequests
            .FirstOrDefaultAsync(r => r.NotaMedicaId == notaMedicaId
                                      && r.Status == FirmaRequestStatus.Pendiente
                                      && r.ExpiresAt > ahora, ct);
        if (existente is not null)
        {
            return Map(existente);
        }

        var req = new FirmaPacienteRequest
        {
            TenantId = tenantId,
            Token = NewToken(),
            PacienteId = pacienteId,
            NotaMedicaId = notaMedicaId,
            Telefono = digits,
            NombreContacto = nombreContacto,
            SolicitadaPorTenantUserId = actorTenantUserId == Guid.Empty ? null : actorTenantUserId,
            CreatedAt = ahora,
            ExpiresAt = ahora + _vigencia,
            Status = FirmaRequestStatus.Pendiente
        };
        _db.FirmaPacienteRequests.Add(req);
        await _db.SaveChangesAsync(ct);
        return Map(req);
    }

    public async Task<ChatSendResult> EnviarPorWhatsAppAsync(Guid solicitudId, Guid lineaId, string urlAbsoluta, Guid actorTenantUserId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.FirstOrDefaultAsync(r => r.Id == solicitudId, ct);
        if (req is null)
        {
            _log.LogWarning("FirmaWA envio solicitud={SolicitudId} linea={LineaId} — solicitud no encontrada", solicitudId, lineaId);
            return new ChatSendResult(false, null, "Solicitud no encontrada.");
        }
        if (req.Status != FirmaRequestStatus.Pendiente)
        {
            _log.LogWarning("FirmaWA envio solicitud={SolicitudId} status={Status} — ya no esta pendiente", solicitudId, req.Status);
            return new ChatSendResult(false, null, "La solicitud ya no esta pendiente.");
        }

        var linea = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineaId, ct);
        if (linea is null)
        {
            _log.LogWarning("FirmaWA envio solicitud={SolicitudId} linea={LineaId} — linea no encontrada", solicitudId, lineaId);
            return new ChatSendResult(false, null, "Linea no encontrada.");
        }

        var maskedPhone = MaskPhone(req.Telefono);
        var saludo = string.IsNullOrWhiteSpace(req.NombreContacto)
            ? "Hola"
            : "Hola " + req.NombreContacto!.Split(' ').FirstOrDefault();
        var textoUrl = $"{saludo}, aqui esta el link para firmar el documento clinico: {urlAbsoluta} El link vence en 2 horas.";

        // Ruta HSM: Gupshup fuera de la ventana 24h no acepta texto libre. Si el
        // admin ya asigno una plantilla al proceso SolicitudFirma, la enviamos
        // primero (abre la ventana de 24h como conversacion iniciada por negocio)
        // y luego mandamos el link como texto de sesion — todo dentro de la
        // misma ventana recien abierta.
        //
        // Optimizacion: si el paciente ya tiene ventana abierta (respondio en las
        // ultimas 23h) NO enviamos el HSM. Meta lo colapsaria visualmente contra
        // la sesion activa y estariamos cobrando una utility redundante. Vamos
        // directo al texto de sesion con el link.
        if (linea.Provider == WhatsAppProvider.Gupshup)
        {
            var ventanaAbierta = await SesionAbiertaAsync(req.Telefono, ct);
            var binding = ventanaAbierta ? null : await _bindings.GetAsync(WhatsAppTemplateRole.SolicitudFirma, ct);
            // LogDebug: flujo feliz. En operacion normal no queremos ruido; si
            // se necesita diagnosticar se sube el nivel a Debug via env var
            // Logging__LogLevel__Visal.Application.Tenancy.FirmaRemotaService=Debug
            _log.LogDebug(
                "FirmaWA envio solicitud={SolicitudId} telefono={Telefono} provider=Gupshup ventanaAbierta={VentanaAbierta} binding={Binding} rutaElegida={Ruta}",
                solicitudId, maskedPhone, ventanaAbierta,
                binding?.TemplateName ?? "(none)",
                binding is not null ? "HSM+texto" : "texto-directo");

            if (binding is not null)
            {
                // Parametros de la plantilla: {{1}}=destinatario, {{2}}=profesional.
                // Si la plantilla exige mas placeholders, pasa cadenas vacias — Meta
                // rechazara y la UI mostrara el mensaje. El operador reasigna.
                var recipient = string.IsNullOrWhiteSpace(req.NombreContacto) ? "paciente" : req.NombreContacto!;
                var profesional = await ResolverNombreProfesionalAsync(actorTenantUserId, ct);
                var parms = BuildTemplateParams(binding.ParameterCount, recipient, profesional);
                var digits = new string(req.Telefono.Where(char.IsDigit).ToArray());
                var hsmRes = await _hsm.SendTestAsync(binding.LineId, binding.TemplateId, digits, parms, actorTenantUserId, ct);
                if (hsmRes.Ok)
                {
                    _log.LogDebug("FirmaWA HSM solicitud={SolicitudId} template={Template} ok=True", solicitudId, binding.TemplateName);
                }
                else
                {
                    _log.LogWarning("FirmaWA HSM solicitud={SolicitudId} template={Template} ok=False error={Error}",
                        solicitudId, binding.TemplateName, hsmRes.Error ?? "(none)");
                    return new ChatSendResult(false, null,
                        $"No se pudo enviar la plantilla '{binding.TemplateName}': {hsmRes.Error ?? "error desconocido"}");
                }
                // La ventana ya esta abierta. Enviamos el link como texto de sesion.
                var conv = await _chat.GetOrCreateByPhoneAsync(req.Telefono, req.NombreContacto, ct);
                if (conv is null)
                {
                    _log.LogWarning("FirmaWA HSM enviado pero no se pudo crear conversacion. solicitud={SolicitudId} telefono={Telefono}", solicitudId, maskedPhone);
                    // Plantilla se envio pero no persistimos chat; para el negocio el
                    // envio fue exitoso.
                    return new ChatSendResult(true, null, null);
                }
                // Dejamos rastro en el chat de que la plantilla HSM salio. El paciente
                // recibio 2 mensajes (HSM + link), el operador debe verlos como uno.
                await _chat.AddNoticeAsync(conv.Id,
                    $"Plantilla HSM enviada: {binding.TemplateName} ({binding.LanguageCode})", ct);
                var textoRes = await _chat.SendViaLineAsync(conv.Id, lineaId, textoUrl, actorTenantUserId, ct);
                if (textoRes.Ok)
                {
                    _log.LogDebug("FirmaWA texto-sesion (post-HSM) solicitud={SolicitudId} ok=True", solicitudId);
                }
                else
                {
                    _log.LogWarning("FirmaWA texto-sesion (post-HSM) solicitud={SolicitudId} ok=False error={Error}",
                        solicitudId, textoRes.Error ?? "(none)");
                }
                return textoRes;
            }
            // Ventana ya abierta (o Gupshup sin binding): mandamos texto directo.
            // En el primer caso ahorramos el HSM redundante; en el segundo caemos
            // al comportamiento clasico y avisamos si Meta rechaza.
        }

        // Evolution o Gupshup sin binding: comportamiento clasico (texto libre).
        var conv2 = await _chat.GetOrCreateByPhoneAsync(req.Telefono, req.NombreContacto, ct);
        if (conv2 is null)
        {
            _log.LogWarning("FirmaWA envio directo solicitud={SolicitudId} telefono={Telefono} — no se pudo crear conversacion", solicitudId, maskedPhone);
            return new ChatSendResult(false, null, "No se pudo crear la conversacion del paciente.");
        }
        var textoCompleto = $"{saludo}, en IPS Visal RT necesitamos su firma para confirmar la atencion recibida. "
                          + $"Por favor abra este link en su celular y firme con el dedo: {urlAbsoluta} "
                          + $"El link vence en 2 horas.";
        var res = await _chat.SendViaLineAsync(conv2.Id, lineaId, textoCompleto, actorTenantUserId, ct);
        if (res.Ok)
        {
            _log.LogDebug("FirmaWA texto-directo solicitud={SolicitudId} telefono={Telefono} provider={Provider} ok=True",
                solicitudId, maskedPhone, linea.Provider);
        }
        else
        {
            _log.LogWarning("FirmaWA texto-directo solicitud={SolicitudId} telefono={Telefono} provider={Provider} ok=False error={Error}",
                solicitudId, maskedPhone, linea.Provider, res.Error ?? "(none)");
        }
        return res;
    }

    /// <summary>La conversacion identificada por <paramref name="telefono"/> tiene
    /// ventana Meta abierta si el cliente respondio en las ultimas 23h. Usamos 23
    /// (no 24) como margen para no chocar con el corte exacto de Meta si el
    /// servidor y Meta desfasan por segundos. Retorna false si no hay conversacion
    /// o si el ultimo Inbound es mas viejo.</summary>
    private async Task<bool> SesionAbiertaAsync(string telefono, CancellationToken ct)
    {
        var digits = new string((telefono ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return false; }
        var corte = _time.GetUtcNow() - TimeSpan.FromHours(23);
        return await _db.Messages.AsNoTracking()
            .Where(m => m.Conversation != null
                        && m.Conversation.ContactPhone == digits
                        && m.Direction == Visal.Domain.Enums.MessageDirection.Inbound
                        && m.SentAt >= corte)
            .AnyAsync(ct);
    }

    /// <summary>Reintervalo minimo entre auto-envios para el mismo telefono. Evita
    /// loops si el paciente presiona el boton 2 veces por accidente o si Meta
    /// reintega el webhook.</summary>
    private static readonly TimeSpan _autoResponseDedupeWindow = TimeSpan.FromSeconds(30);

    public async Task<int> AutoResponderConLinkAsync(
        Guid tenantId,
        string telefonoDigits,
        Guid lineaId,
        string baseUri,
        CancellationToken ct = default)
    {
        var digits = new string((telefonoDigits ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            _log.LogWarning("AutoResponderConLink tenant={TenantId} — telefono vacio", tenantId);
            return 0;
        }

        var ahora = _time.GetUtcNow();
        var pendientes = await _db.FirmaPacienteRequests
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                        && r.Telefono == digits
                        && r.Status == FirmaRequestStatus.Pendiente
                        && r.ExpiresAt > ahora)
            .ToListAsync(ct);

        if (pendientes.Count == 0)
        {
            // Debug: se dispara cada mensaje entrante afirmativo aunque no
            // haya solicitud (paciente responde por otra razon). Muy ruidoso
            // en Information, se queda en Debug.
            _log.LogDebug("AutoResponderConLink telefono={Telefono} — sin solicitudes pendientes",
                MaskPhone(digits));
            return 0;
        }

        // Idempotencia: si acabamos de enviar el link a este telefono hace <30s
        // (por retry del webhook o doble click del cliente), no re-enviamos.
        var corteDedupe = ahora - _autoResponseDedupeWindow;
        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ContactPhone == digits, ct);
        if (conv is not null)
        {
            var recienteOutbound = await _db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conv.Id
                            && m.Direction == MessageDirection.Outbound
                            && m.SentAt >= corteDedupe
                            && m.Body.Contains("/firma/"))
                .AnyAsync(ct);
            if (recienteOutbound)
            {
                _log.LogDebug("AutoResponderConLink telefono={Telefono} — dedupe: link ya enviado en los ultimos {Segundos}s",
                    MaskPhone(digits), _autoResponseDedupeWindow.TotalSeconds);
                return 0;
            }
        }

        // Si no habia conversacion (raro, porque el webhook la crea al ingestar
        // el Inbound previo), la obtenemos o creamos ahora — SendViaLineAsync
        // necesita conversationId valido.
        if (conv is null)
        {
            var chatConv = await _chat.GetOrCreateByPhoneAsync(digits, pendientes[0].NombreContacto, ct);
            if (chatConv is null)
            {
                _log.LogWarning("AutoResponderConLink telefono={Telefono} — no se pudo obtener conversacion", MaskPhone(digits));
                return 0;
            }
            conv = await _db.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == chatConv.Id, ct);
            if (conv is null) { return 0; }
        }

        var enviados = 0;
        var baseTrim = (baseUri ?? "").TrimEnd('/');
        foreach (var req in pendientes)
        {
            var urlAbsoluta = string.IsNullOrEmpty(baseTrim)
                ? $"/firma/{req.Token}"
                : $"{baseTrim}/firma/{req.Token}";
            var saludo = string.IsNullOrWhiteSpace(req.NombreContacto)
                ? "Hola"
                : "Hola " + req.NombreContacto!.Split(' ').FirstOrDefault();
            var texto = $"{saludo}, aqui esta el link para firmar el documento clinico: {urlAbsoluta} El link vence en 2 horas.";
            var res = await _chat.SendViaLineTrustedAsync(tenantId, conv.Id, lineaId, texto, ct);
            if (res.Ok)
            {
                _log.LogDebug("AutoResponderConLink solicitud={SolicitudId} telefono={Telefono} ok=True",
                    req.Id, MaskPhone(digits));
                enviados++;
            }
            else
            {
                _log.LogWarning("AutoResponderConLink solicitud={SolicitudId} telefono={Telefono} ok=False error={Error}",
                    req.Id, MaskPhone(digits), res.Error ?? "(none)");
            }
        }
        return enviados;
    }

    /// <summary>Nombre a mostrar del profesional que solicita la firma. Preferencia:
    /// nombres del Profesional vinculado > DisplayName del PlatformUser > fallback
    /// generico. Cae al fallback si no hay TenantUser (webhook, cron, etc).</summary>
    private async Task<string> ResolverNombreProfesionalAsync(Guid actorTenantUserId, CancellationToken ct)
    {
        if (actorTenantUserId == Guid.Empty) { return "su equipo medico"; }
        var user = await _db.TenantUsers.AsNoTracking()
            .Include(u => u.Profesional)
            .Include(u => u.PlatformUser)
            .FirstOrDefaultAsync(u => u.Id == actorTenantUserId, ct);
        if (user?.Profesional is { } p)
        {
            var nombre = string.Join(" ", new[] { p.PrimerNombre, p.PrimerApellido }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(nombre)) { return nombre; }
        }
        if (!string.IsNullOrWhiteSpace(user?.PlatformUser?.DisplayName)) { return user!.PlatformUser!.DisplayName!; }
        return "su equipo medico";
    }

    /// <summary>Rellena N parametros para la plantilla HSM: los dos primeros
    /// vienen del sistema (destinatario, profesional); el resto quedan como
    /// vacios para que Meta valide y — si sobran o faltan — el operador ajuste
    /// la asignacion en Plantillas WA.</summary>
    private static IReadOnlyList<string> BuildTemplateParams(int expected, string recipient, string profesional)
    {
        if (expected <= 0) { return Array.Empty<string>(); }
        var list = new List<string>(expected) { recipient };
        if (expected >= 2) { list.Add(profesional); }
        while (list.Count < expected) { list.Add(""); }
        return list;
    }

    public async Task<bool> CancelarAsync(Guid solicitudId, Guid actorTenantUserId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.FirstOrDefaultAsync(r => r.Id == solicitudId, ct);
        if (req is null || req.Status != FirmaRequestStatus.Pendiente) { return false; }
        req.Status = FirmaRequestStatus.Cancelada;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FirmaRequestStateDto?> ObtenerEstadoAsync(Guid solicitudId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == solicitudId, ct);
        if (req is null) { return null; }
        // Si esta pendiente pero ya expiro, marcamos antes de devolver (tracking).
        if (req.Status == FirmaRequestStatus.Pendiente && req.ExpiresAt < _time.GetUtcNow())
        {
            var tracked = await _db.FirmaPacienteRequests.FirstAsync(r => r.Id == solicitudId, ct);
            tracked.Status = FirmaRequestStatus.Expirada;
            await _db.SaveChangesAsync(ct);
            req = tracked;
        }
        return new FirmaRequestStateDto(req.Id, req.Status, req.CompletedAt, req.ImageDataUrl);
    }

    public async Task<FirmaRequestDto?> ObtenerActivaPorNotaAsync(Guid notaMedicaId, CancellationToken ct = default)
    {
        var ahora = _time.GetUtcNow();
        var req = await _db.FirmaPacienteRequests.AsNoTracking()
            .Where(r => r.NotaMedicaId == notaMedicaId
                        && (r.Status == FirmaRequestStatus.Pendiente || r.Status == FirmaRequestStatus.Completada))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return req is null ? null : Map(req);
    }

    public async Task<FirmaRequestDto?> CrearLibreParaPacienteAsync(Guid pacienteId, string telefono, string? nombreContacto, Guid actorTenantUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var digits = Digits(telefono);
        if (digits.Length == 0) { return null; }

        // Reutiliza la solicitud "libre" (NotaMedicaId == null) si todavia esta vigente.
        var ahora = _time.GetUtcNow();
        var existente = await _db.FirmaPacienteRequests
            .FirstOrDefaultAsync(r => r.PacienteId == pacienteId
                                      && r.NotaMedicaId == null
                                      && r.Status == FirmaRequestStatus.Pendiente
                                      && r.ExpiresAt > ahora, ct);
        if (existente is not null) { return Map(existente); }

        var req = new FirmaPacienteRequest
        {
            TenantId = tenantId,
            Token = NewToken(),
            PacienteId = pacienteId,
            NotaMedicaId = null,
            Telefono = digits,
            NombreContacto = nombreContacto,
            SolicitadaPorTenantUserId = actorTenantUserId == Guid.Empty ? null : actorTenantUserId,
            CreatedAt = ahora,
            ExpiresAt = ahora + _vigencia,
            Status = FirmaRequestStatus.Pendiente
        };
        _db.FirmaPacienteRequests.Add(req);
        await _db.SaveChangesAsync(ct);
        return Map(req);
    }

    public async Task<FirmaRequestDto?> ObtenerActivaLibrePorPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.AsNoTracking()
            .Where(r => r.PacienteId == pacienteId
                        && r.NotaMedicaId == null
                        && r.ContactoEmergenciaId == null
                        && (r.Status == FirmaRequestStatus.Pendiente || r.Status == FirmaRequestStatus.Completada))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return req is null ? null : Map(req);
    }

    public async Task<IReadOnlyList<FirmaRequestDto>> ListarActivasLibresPorPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        // Traemos la MAS RECIENTE por destinatario (paciente o cada pariente).
        // Asi el modal muestra estado unico por tarjeta aunque el doctor haya
        // pedido firma multiples veces al mismo destinatario.
        var todas = await _db.FirmaPacienteRequests.AsNoTracking()
            .Where(r => r.PacienteId == pacienteId
                        && r.NotaMedicaId == null
                        && (r.Status == FirmaRequestStatus.Pendiente || r.Status == FirmaRequestStatus.Completada))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return todas
            .GroupBy(r => r.ContactoEmergenciaId ?? Guid.Empty)
            .Select(g => Map(g.First()))
            .ToList();
    }

    public async Task<FirmaRequestPublicDto?> ObtenerPorTokenPublicoAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }

        // Token es globalmente unico. IgnoreQueryFilters porque la pagina /firma/{token}
        // se sirve anonimamente (sin claim tenant_id). El tenant lo obtenemos del propio
        // registro de la solicitud, no del usuario.
        var req = await _db.FirmaPacienteRequests
            .IgnoreQueryFilters()
            .Where(r => r.Token == token)
            .Select(r => new
            {
                r.Id, r.Token, r.PacienteId, r.NotaMedicaId, r.TenantId,
                r.ExpiresAt, r.Status, r.SolicitadaPorTenantUserId,
                r.ContactoEmergenciaId, r.NombreContacto
            })
            .FirstOrDefaultAsync(ct);
        if (req is null) { return null; }

        // Si esta pendiente pero ya expiro, marcamos antes de devolver.
        if (req.Status == FirmaRequestStatus.Pendiente && req.ExpiresAt < _time.GetUtcNow())
        {
            var tracked = await _db.FirmaPacienteRequests.IgnoreQueryFilters().FirstAsync(r => r.Id == req.Id, ct);
            tracked.Status = FirmaRequestStatus.Expirada;
            await _db.SaveChangesAsync(ct);
        }

        var paciente = await _db.Pacientes.IgnoreQueryFilters()
            .Where(p => p.Id == req.PacienteId)
            .Select(p => p.NombreCompleto)
            .FirstOrDefaultAsync(ct);

        string? profesional = null;
        if (req.SolicitadaPorTenantUserId is Guid uid)
        {
            profesional = await _db.TenantUsers.IgnoreQueryFilters()
                .Where(u => u.Id == uid)
                .Select(u => u.PlatformUser != null ? u.PlatformUser.DisplayName : null)
                .FirstOrDefaultAsync(ct);
        }

        var tenantName = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == req.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        // Rol y nombre del firmante real. Si es pariente, leemos parentesco + nombre
        // del contacto (con fallback al NombreContacto guardado en el request).
        string? nombreSig = null;
        string? rolSig = null;
        if (req.ContactoEmergenciaId is Guid cid)
        {
            var contacto = await _db.PacienteContactosEmergencia.IgnoreQueryFilters()
                .Where(c => c.Id == cid)
                .Select(c => new { c.Nombre, c.Parentesco })
                .FirstOrDefaultAsync(ct);
            nombreSig = contacto?.Nombre ?? req.NombreContacto;
            rolSig = string.IsNullOrWhiteSpace(contacto?.Parentesco)
                ? "ACOMPANANTE"
                : contacto!.Parentesco!.ToUpperInvariant();
        }
        else
        {
            nombreSig = paciente ?? "Paciente";
            rolSig = "PACIENTE";
        }

        return new FirmaRequestPublicDto(
            req.Id, req.Token,
            paciente ?? "Paciente",
            profesional,
            tenantName,
            req.ExpiresAt,
            req.Status,
            nombreSig,
            rolSig);
    }

    public async Task<bool> GuardarFirmaPorTokenAsync(string token, string imageDataUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(imageDataUrl)) { return false; }
        // Validacion basica del formato data URL.
        if (!imageDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) { return false; }

        var req = await _db.FirmaPacienteRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Token == token, ct);
        if (req is null) { return false; }
        if (req.Status != FirmaRequestStatus.Pendiente) { return false; }
        if (req.ExpiresAt < _time.GetUtcNow())
        {
            req.Status = FirmaRequestStatus.Expirada;
            await _db.SaveChangesAsync(ct);
            return false;
        }

        // Persistir la firma en la solicitud y en la nota medica (campo oficial).
        req.ImageDataUrl = imageDataUrl;
        req.Status = FirmaRequestStatus.Completada;
        req.CompletedAt = _time.GetUtcNow();

        // Si la solicitud esta atada a una nota, replicamos la firma en el campo
        // oficial de la nota y dejamos el doc adjunto. Las solicitudes "libres"
        // (pedidas desde el panel WhatsApp del paciente sin nota especifica)
        // solo dejan la imagen en FirmaPacienteRequest.ImageDataUrl + el
        // NotaMedicaDocumento queda atado al paciente sin NotaMedica.
        var notaIdParaDoc = req.NotaMedicaId;
        if (req.NotaMedicaId is Guid notaId)
        {
            var nota = await _db.NotasMedicas.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.Id == notaId, ct);
            if (nota is not null)
            {
                nota.FirmaPacienteDataUrl = imageDataUrl;
            }
        }

        // Si la solicitud es de un pariente, sobreescribimos su FirmaUrl en la ficha
        // del paciente. Asi los formularios de consentimiento con ruta prefill
        // pacienteContactoEmergenciaN.firmaUrl recogen automaticamente la firma
        // remota mas reciente sin depender de que el admin abra /admision.
        if (req.ContactoEmergenciaId is Guid contactoId)
        {
            var contacto = await _db.PacienteContactosEmergencia.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == contactoId, ct);
            if (contacto is not null)
            {
                contacto.FirmaUrl = imageDataUrl;
            }
        }

        // Adicional: guardar la firma como PNG fisico y crear un NotaMedicaDocumento
        // aunque no haya nota asociada — asi el operador ve la firma en el tab
        // "Documentos" del paciente en /admision sin depender de que exista nota.
        // Ademas la categoria y anotaciones cambian segun sea firma del paciente
        // o firma de un pariente (con parentesco + nombre).
        try
        {
            var bytes = DecodeDataUrl(imageDataUrl);
            if (bytes is not null && bytes.Length > 0)
            {
                var nombre = $"firma-{(req.ContactoEmergenciaId is null ? "paciente" : "acompanante")}-{req.Id:N}.png";
                var rutaWeb = await _storage.GuardarAsync("notas", nombre, bytes, ct);

                string categoria, nombreOriginal, anotaciones;
                if (req.ContactoEmergenciaId is Guid cid2)
                {
                    var info = await _db.PacienteContactosEmergencia.IgnoreQueryFilters()
                        .Where(c => c.Id == cid2)
                        .Select(c => new { c.Nombre, c.Parentesco })
                        .FirstOrDefaultAsync(ct);
                    var rolLbl = string.IsNullOrWhiteSpace(info?.Parentesco) ? "Acompanante" : info!.Parentesco;
                    categoria = "Firma del Acompanante";
                    nombreOriginal = $"Firma remota de {info?.Nombre ?? "acompanante"} ({rolLbl}) - {_time.GetUtcNow().LocalDateTime:dd-MM-yyyy HH:mm}.png";
                    anotaciones = $"Firma capturada remotamente por WhatsApp. Signatario: {rolLbl} - {info?.Nombre}.";
                }
                else
                {
                    categoria = "Firma del Paciente";
                    nombreOriginal = $"Firma remota del paciente - {_time.GetUtcNow().LocalDateTime:dd-MM-yyyy HH:mm}.png";
                    anotaciones = "Firma capturada remotamente por WhatsApp.";
                }

                _db.NotaMedicaDocumentos.Add(new NotaMedicaDocumento
                {
                    TenantId = req.TenantId,
                    NotaMedicaId = notaIdParaDoc, // nullable ahora — null cuando la firma no viene de una nota
                    PacienteId = req.PacienteId,
                    NombreOriginal = nombreOriginal,
                    RutaArchivo = rutaWeb,
                    TipoMime = "image/png",
                    Tamano = bytes.Length,
                    Categoria = categoria,
                    Anotaciones = anotaciones
                });
            }
        }
        catch { /* el doc externo es secundario: si falla, la firma del campo ya quedo */ }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Decodifica el payload base64 de una data URL ("data:image/png;base64,…") a bytes.</summary>
    private static byte[]? DecodeDataUrl(string dataUrl)
    {
        var idx = dataUrl.IndexOf(',');
        if (idx < 0 || idx == dataUrl.Length - 1) { return null; }
        try { return Convert.FromBase64String(dataUrl[(idx + 1)..]); }
        catch { return null; }
    }

    // ===== Helpers =====
    private static string Digits(string s) =>
        new string((s ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>32 caracteres hex aleatorios (~128 bits). Imposible de adivinar.</summary>
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static FirmaRequestDto Map(FirmaPacienteRequest r) =>
        new(r.Id, r.PacienteId, r.NotaMedicaId, r.Token, r.Telefono, r.NombreContacto,
            r.CreatedAt, r.ExpiresAt, r.CompletedAt, r.Status, $"/firma/{r.Token}",
            r.ContactoEmergenciaId);

    public async Task<IReadOnlyList<FirmaRequestDto>> CrearMultipleParaPacienteAsync(
        Guid pacienteId,
        IReadOnlyList<FirmaDestinatarioSpec> destinatarios,
        Guid actorTenantUserId,
        CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return Array.Empty<FirmaRequestDto>(); }
        if (destinatarios is null || destinatarios.Count == 0) { return Array.Empty<FirmaRequestDto>(); }

        var ahora = _time.GetUtcNow();
        var resultado = new List<FirmaRequestDto>(destinatarios.Count);

        foreach (var d in destinatarios)
        {
            var digits = Digits(d.Telefono);
            if (digits.Length == 0) { continue; }

            // Reutilizamos solo si el destinatario coincide EXACTAMENTE (paciente o
            // mismo pariente). Asi cada firma queda desacoplada aunque haya varias
            // solicitudes libres en curso.
            var existente = await _db.FirmaPacienteRequests
                .FirstOrDefaultAsync(r => r.PacienteId == pacienteId
                                          && r.NotaMedicaId == null
                                          && r.ContactoEmergenciaId == d.ContactoEmergenciaId
                                          && r.Status == FirmaRequestStatus.Pendiente
                                          && r.ExpiresAt > ahora, ct);
            if (existente is not null)
            {
                resultado.Add(Map(existente));
                continue;
            }

            var req = new FirmaPacienteRequest
            {
                TenantId = tenantId,
                Token = NewToken(),
                PacienteId = pacienteId,
                NotaMedicaId = null,
                ContactoEmergenciaId = d.ContactoEmergenciaId,
                Telefono = digits,
                NombreContacto = d.NombreContacto,
                SolicitadaPorTenantUserId = actorTenantUserId == Guid.Empty ? null : actorTenantUserId,
                CreatedAt = ahora,
                ExpiresAt = ahora + _vigencia,
                Status = FirmaRequestStatus.Pendiente
            };
            _db.FirmaPacienteRequests.Add(req);
            resultado.Add(Map(req));
        }

        if (resultado.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
        return resultado;
    }
}
