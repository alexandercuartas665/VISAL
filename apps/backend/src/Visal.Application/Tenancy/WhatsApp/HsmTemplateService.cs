using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>
/// Implementa HSM template ops resolviendo la App Gupshup detras de una
/// linea (TenantGupshupConfig por line.GupshupAppId), descifrando la
/// apikey y delegando en IGupshupApiClient. Ninguna operacion pasa por el
/// WhatsAppConnectorService — no es necesario, y evita mezclar la ruta
/// especifica de HSM con auditoria de envios normales.
///
/// Los envios de prueba SI van por el connector (reusa auditoria y humaniza
/// errores igual que texto/media).
/// </summary>
internal sealed class HsmTemplateService : IHsmTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IGupshupApiClient _client;
    private readonly IAuditWriter _audit;

    public HsmTemplateService(
        IApplicationDbContext db,
        ISecretProtector secretProtector,
        IGupshupApiClient client,
        IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _audit = audit;
    }

    public async Task<HsmTemplateListResult> ListByLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var creds = await ResolveAsync(lineId, ct);
        if (creds is null) { return NotConfigured(); }
        var r = await _client.ListTemplatesAsync(
            creds.Value.ApiKey, creds.Value.AppName, creds.Value.GupshupAppId,
            creds.Value.PartnerToken, ct);
        return new HsmTemplateListResult(r.Ok, r.Error, r.Templates);
    }

    public async Task<HsmCreateResult> CreateByLineAsync(Guid lineId, HsmCreateRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var creds = await ResolveAsync(lineId, ct);
        if (creds is null) { return new HsmCreateResult(false, "Linea sin App Gupshup configurada.", null, null); }
        var payload = new GupshupCreateTemplateRequest(
            req.ElementName, req.LanguageCode, req.Category,
            req.TemplateType, req.Content, req.Example,
            req.ExampleHeader, req.Header, req.Footer, req.Buttons);
        var r = await _client.CreateTemplateAsync(
            creds.Value.ApiKey, creds.Value.AppName, creds.Value.GupshupAppId,
            creds.Value.PartnerToken, payload, ct);
        _audit.Write(actorUserId, "gupshup.template.create", "TenantGupshupConfig", creds.Value.ConfigId,
            previousValue: null,
            newValue: new { req.ElementName, req.LanguageCode, req.Category, ok = r.Ok, id = r.Id },
            tenantId: creds.Value.TenantId);
        return new HsmCreateResult(r.Ok, r.Error, r.Id, r.Status);
    }

    public async Task<HsmSendResult> SendTestAsync(Guid lineId, string templateId, string phone,
        IReadOnlyList<string> parameters, Guid actorUserId, CancellationToken ct = default)
    {
        var creds = await ResolveAsync(lineId, ct);
        if (creds is null) { return new HsmSendResult(false, "Linea sin App Gupshup configurada."); }
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) { return new HsmSendResult(false, "Indica el numero destino."); }
        var r = await _client.SendTemplateAsync(creds.Value.ApiKey, creds.Value.Source, digits, templateId, parameters, ct);
        _audit.Write(actorUserId, "gupshup.template.send-test", "WhatsAppLine", lineId,
            previousValue: null,
            newValue: new { to = digits, templateId, ok = r.Ok, messageId = r.MessageId },
            tenantId: creds.Value.TenantId);
        return new HsmSendResult(r.Ok, r.Error);
    }

    /// <summary>Devuelve credenciales listas para invocar Gupshup o null si
    /// falta configuracion. Todas las lecturas ignoran query filters porque
    /// algunas llamadas vienen sin tenant scope activo (webhook, jobs).</summary>
    private async Task<(Guid TenantId, Guid ConfigId, string AppName, Guid GupshupAppId, string ApiKey, string? PartnerToken, string Source)?> ResolveAsync(Guid lineId, CancellationToken ct)
    {
        var line = await _db.WhatsAppLines
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null || line.Provider != WhatsAppProvider.Gupshup || line.GupshupAppId is not Guid appPk) { return null; }
        var cfg = await _db.TenantGupshupConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == appPk, ct);
        if (cfg is null || !cfg.IsActive || string.IsNullOrWhiteSpace(cfg.PhoneNumber)) { return null; }
        var apikey = _secretProtector.Unprotect(cfg.ApiKeyEncrypted);
        string? partnerToken = null;
        if (!string.IsNullOrWhiteSpace(cfg.PartnerTokenEncrypted))
        {
            try { partnerToken = _secretProtector.Unprotect(cfg.PartnerTokenEncrypted!); }
            catch { /* si esta corrupto queda null y la UI pide re-ingresar */ }
        }
        var source = new string(cfg.PhoneNumber!.Where(char.IsDigit).ToArray());
        // cfg.AppId es el GUID que Gupshup asigna a la App (no el pk de nuestra fila).
        return (line.TenantId, cfg.Id, cfg.AppName, cfg.AppId, apikey, partnerToken, source);
    }

    private static HsmTemplateListResult NotConfigured() =>
        new(false, "La linea no es Gupshup o no tiene App configurada.", Array.Empty<GupshupTemplateInfo>());
}
