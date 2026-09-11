using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Alertas;

public sealed class AlertaInformeAutoResponder : IAlertaInformeAutoResponder
{
    private readonly IApplicationDbContext _db;
    private readonly IChatService _chat;
    private readonly IInformeTerapiasService _informe;
    private readonly ILogger<AlertaInformeAutoResponder> _log;

    // Ventana para considerar que el telefono es un destinatario "de alerta" activo.
    private static readonly TimeSpan Ventana = TimeSpan.FromDays(14);
    // Anti-doble-envio si el webhook reintenta o el usuario responde varias veces.
    private static readonly TimeSpan Dedupe = TimeSpan.FromSeconds(30);

    public AlertaInformeAutoResponder(
        IApplicationDbContext db, IChatService chat,
        IInformeTerapiasService informe, ILogger<AlertaInformeAutoResponder> log)
    {
        _db = db;
        _chat = chat;
        _informe = informe;
        _log = log;
    }

    public async Task<int> ResponderInformeSiAplicaAsync(Guid tenantId, string contactPhone, Guid lineId, string baseUri, CancellationToken ct = default)
    {
        var digits = Normalizar(contactPhone);
        if (digits is null) { return 0; }

        var ahora = DateTimeOffset.UtcNow;
        var corte = ahora - Ventana;

        // ¿Este telefono recibio una alerta por WhatsApp reciente (con exito)? Si no, no aplica.
        var esDestinatarioAlerta = await _db.AlertaEnvios.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId
                           && e.Canal == AlertaCanal.WhatsApp
                           && e.Exito
                           && e.Contacto == digits
                           && e.FechaEnvio >= corte, ct);
        if (!esDestinatarioAlerta) { return 0; }

        // Dedupe: si ya mandamos un enlace de informe a este telefono hace <30s, no repetir.
        var conv = await _db.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ContactPhone == digits, ct);
        if (conv is not null)
        {
            var corteDedupe = ahora - Dedupe;
            var yaEnviado = await _db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conv.Id
                            && m.Direction == MessageDirection.Outbound
                            && m.SentAt >= corteDedupe
                            && m.Body.Contains("/informe/"))
                .AnyAsync(ct);
            if (yaEnviado) { return 0; }
        }

        if (conv is null)
        {
            var chatConv = await _chat.GetOrCreateByPhoneAsync(digits, null, ct);
            if (chatConv is null) { return 0; }
            conv = await _db.Conversations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == chatConv.Id, ct);
            if (conv is null) { return 0; }
        }

        // Acota el informe al profesional dueño de este celular: el enlace que se
        // manda al profesional debe traer SOLO sus terapias pendientes, no las de
        // todos. Se resuelve por coincidencia de los ultimos 10 digitos del celular
        // (el Celular en ficha puede venir con o sin indicativo/espacios). Si no hay
        // match (ej. el numero no es de un profesional), el enlace queda tenant-wide.
        var profesionalId = await ResolverProfesionalPorTelefonoAsync(tenantId, digits, ct);

        string url;
        try { url = _informe.GenerarEnlace(baseUri, tenantId, profesionalId); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AutoResponderInforme tenant={Tenant}: no se pudo generar el enlace", tenantId);
            return 0;
        }

        var texto = $"Aqui esta el informe de pacientes pendientes por terapias: {url}";
        var res = await _chat.SendViaLineTrustedAsync(tenantId, conv.Id, lineId, texto, ct);
        if (res.Ok)
        {
            _log.LogInformation("AutoResponderInforme tenant={Tenant} telefono={Tel}: enlace enviado", tenantId, Mask(digits));
            return 1;
        }
        _log.LogWarning("AutoResponderInforme tenant={Tenant} telefono={Tel}: fallo el envio ({Err})", tenantId, Mask(digits), res.Error);
        return 0;
    }

    /// <summary>Resuelve el profesional al que se acota el informe para este telefono.
    /// 1) El profesional de la asignacion del AlertaEnvio WhatsApp mas reciente a ese
    ///    numero (lo mas fiable: es el alerta que realmente se le mando, funciona
    ///    incluso con el telefono de prueba del simulador). 2) Fallback: el profesional
    ///    cuyo celular coincide (ultimos 10 digitos). Null si nada coincide (informe
    ///    tenant-wide).</summary>
    private async Task<Guid?> ResolverProfesionalPorTelefonoAsync(Guid tenantId, string digits, CancellationToken ct)
    {
        var corte = DateTimeOffset.UtcNow - Ventana;

        // 1) Desde el AlertaEnvio mas reciente a este numero -> su asignacion -> profesional del turno.
        var asigId = await _db.AlertaEnvios.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.Canal == AlertaCanal.WhatsApp && e.Exito
                        && e.Contacto == digits && e.FechaEnvio >= corte)
            .OrderByDescending(e => e.FechaEnvio)
            .Select(e => (Guid?)e.AsignacionId)
            .FirstOrDefaultAsync(ct);
        if (asigId is Guid aid)
        {
            var profId = await _db.AsignacionTurnos.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.AsignacionId == aid && t.ProfesionalId != Guid.Empty)
                .Select(t => (Guid?)t.ProfesionalId)
                .FirstOrDefaultAsync(ct);
            if (profId is Guid pOk) { return pOk; }
        }

        // 2) Fallback: por celular (ultimos 10 digitos, tolera indicativo/formato).
        var local = digits.Length >= 10 ? digits[^10..] : digits;
        if (local.Length < 7) { return null; }
        var profs = await _db.Profesionales.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.Celular != null && p.Celular != "")
            .Select(p => new { p.Id, p.Celular })
            .ToListAsync(ct);
        foreach (var p in profs)
        {
            var cd = new string((p.Celular ?? "").Where(char.IsDigit).ToArray());
            if (cd.Length == 0) { continue; }
            var cdLocal = cd.Length >= 10 ? cd[^10..] : cd;
            if (cdLocal == local) { return p.Id; }
        }
        return null;
    }

    /// <summary>Solo digitos; antepone 57 si son 10 (celular CO). Null si vacio.</summary>
    private static string? Normalizar(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return null; }
        if (digits.Length == 10) { digits = "57" + digits; }
        return digits;
    }

    private static string Mask(string phone)
        => phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];
}
