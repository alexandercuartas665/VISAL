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

        string url;
        try { url = _informe.GenerarEnlace(baseUri, tenantId); }
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
