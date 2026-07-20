using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Visal.Application.Tenancy;

public sealed class AiUsageService : IAiUsageService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IEmailSender? _email;
    private readonly IAuditWriter? _audit;
    private readonly ILogger<AiUsageService>? _log;

    public AiUsageService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Ola 9 RC9d — overload con email + audit para disparar alertas de cupo.
    /// Los tests que no usan alerta pueden seguir con el ctor de 2 args.
    /// </summary>
    public AiUsageService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IEmailSender email,
        IAuditWriter audit,
        ILogger<AiUsageService> log)
        : this(db, tenantContext)
    {
        _email = email;
        _audit = audit;
        _log = log;
    }

    public async Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return; }

        var total = inputTokens + outputTokens;
        var log = new AiUsageLog
        {
            TenantId = tenantId,
            AgentId = agentId,
            Provider = provider,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = total,
            EstimatedCostUsd = AiCostEstimator.Estimate(provider, inputTokens, outputTokens),
            Source = string.IsNullOrWhiteSpace(source) ? "chat" : source,
            Success = success
        };
        _db.AiUsageLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        // Ola 9 RC9d — evalua alerta cupo IA y despacha email al Owner si
        // cruzo 90 o 100. Best-effort: cualquier fallo se loguea y el
        // RecordAsync sigue exitoso.
        await TryDispatchQuotaAlertAsync(tenantId, cancellationToken);
    }

    private async Task TryDispatchQuotaAlertAsync(Guid tenantId, CancellationToken ct)
    {
        // Sin email/audit inyectados (ctor de 2 args) — no hay nada que despachar.
        if (_email is null || _audit is null) { return; }
        try
        {
            var quota = await GetQuotaAsync(ct);
            if (!quota.HasLimit) { return; }
            var pct = quota.UsedPct;
            var threshold = pct >= 100 ? 100 : (pct >= 90 ? 90 : 0);
            if (threshold == 0) { return; }

            var action = threshold == 100 ? "ai-quota-alert-critico" : "ai-quota-alert-warning";
            var now = DateTimeOffset.UtcNow;
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

            // Rate limit: max 1 alerta de este threshold por tenant por mes.
            // Reusamos SuperAdminAuditLog como fuente autoritativa; el AuditWriter
            // ya inserta filas con tenantId + actionName + createdAt.
            var yaEnviada = await _db.SuperAdminAuditLogs.AsNoTracking()
                .Where(a => a.TenantId == tenantId
                         && a.ActionName == action
                         && a.CreatedAt >= monthStart)
                .AnyAsync(ct);
            if (yaEnviada) { return; }

            // Emails de los Owners del tenant.
            var emails = await _db.TenantUsers.AsNoTracking()
                .Where(tu => tu.TenantId == tenantId && tu.TenantRole == TenantRole.Owner)
                .Join(_db.PlatformUsers.AsNoTracking(),
                    tu => tu.PlatformUserId,
                    pu => pu.Id,
                    (tu, pu) => pu.Email)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct()
                .ToListAsync(ct);

            if (emails.Count == 0)
            {
                _log?.LogInformation("RC9d alerta cupo IA tenant={TenantId} threshold={Threshold}% — sin Owners con email, no se envia.",
                    tenantId, threshold);
                return;
            }

            var subject = threshold == 100
                ? $"Cupo mensual de tokens IA agotado ({quota.UsedPct}%)"
                : $"Cupo mensual de tokens IA al {quota.UsedPct}%";
            var body = $@"
                <p>Hola,</p>
                <p>El consumo de tokens de IA de tu tenant esta en <b>{quota.UsedPct}%</b> del cupo mensual del plan.</p>
                <p>Usado: {quota.MonthlyUsedTokens:N0} tokens<br/>
                Limite: {quota.MonthlyLimitTokens:N0} tokens<br/>
                Restante: {quota.Remaining:N0} tokens</p>
                {(threshold == 100
                    ? "<p><b>Los agentes de IA no ejecutaran hasta que se libere el cupo el proximo mes o se aumente el plan.</b></p>"
                    : "<p>Todavia hay margen, pero conviene revisar el consumo antes de que se agote.</p>")}
                <p>Ver detalle en: <a href=""/config/revision-policy"">Politica de revision</a> y <a href=""/admin/ai-usage"">Auditoria de uso IA</a>.</p>
                <hr/>
                <p style=""font-size:12px;color:#777;"">Mensaje automatico de Visal. No respondas a este correo.</p>";

            var envios = 0;
            foreach (var e in emails)
            {
                var res = await _email.SendAsync(e, subject, body, ct);
                if (res.Ok) { envios++; }
                else
                {
                    _log?.LogWarning("RC9d alerta cupo IA fallo enviar a {Email}: {Err}", e, res.Error);
                }
            }

            // Registramos la alerta despachada para que el rate-limit no reenvie
            // en el mismo mes. Guid.Empty como actor porque es sistema.
            _audit.Write(
                actorUserId: Guid.Empty,
                actionName: action,
                entityName: "AiQuota",
                entityId: null,
                previousValue: null,
                newValue: new { threshold, usedPct = quota.UsedPct, emailsSent = envios, emailsAttempted = emails.Count },
                tenantId: tenantId,
                actorType: AuditActorType.System);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "RC9d TryDispatchQuotaAlertAsync tenant={TenantId} lanzo excepcion (ignorada).", tenantId);
        }
    }

    public async Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AiUsageLogs.AsNoTracking()
            .Select(l => new { l.AgentId, l.InputTokens, l.OutputTokens, l.TotalTokens, l.EstimatedCostUsd })
            .ToListAsync(cancellationToken);

        var byAgent = rows
            .GroupBy(r => r.AgentId)
            .Select(g => new AgentUsageDto(
                g.Key,
                g.Count(),
                g.Sum(x => (long)x.InputTokens),
                g.Sum(x => (long)x.OutputTokens),
                g.Sum(x => (long)x.TotalTokens),
                g.Sum(x => x.EstimatedCostUsd)))
            .ToList();

        return new AiUsageSummaryDto(
            rows.Count,
            rows.Sum(x => (long)x.TotalTokens),
            rows.Sum(x => (long)x.InputTokens),
            rows.Sum(x => (long)x.OutputTokens),
            rows.Sum(x => x.EstimatedCostUsd),
            byAgent);
    }

    public async Task<AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new AiQuotaDto(0, 0, true); }

        // Consumo del mes en curso (UTC). AiUsageLogs ya esta filtrado por tenant.
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var used = await _db.AiUsageLogs.AsNoTracking()
            .Where(l => l.CreatedAt >= monthStart)
            .SumAsync(l => (long?)l.TotalTokens, cancellationToken) ?? 0;

        // Limite del plan vigente del tenant (TenantSubscription no es ITenantScoped: filtro explicito).
        var planId = await _db.TenantSubscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.StartsAt)
            .Select(s => (Guid?)s.PlanId)
            .FirstOrDefaultAsync(cancellationToken);

        long limit = 0;
        var hard = true;
        if (planId is Guid pid)
        {
            var lim = await _db.SaasPlanLimits.AsNoTracking()
                .Where(l => l.PlanId == pid && l.LimitKey == IAiUsageService.MonthlyTokenLimitKey)
                .Select(l => new { l.LimitValue, l.EnforcementMode })
                .FirstOrDefaultAsync(cancellationToken);
            if (lim is not null)
            {
                limit = lim.LimitValue;
                hard = lim.EnforcementMode == LimitEnforcementMode.Hard;
            }
        }

        return new AiQuotaDto(limit, used, hard);
    }

    public async Task<byte[]> ExportarCsvAsync(AiUsageExportFiltro filtro, CancellationToken cancellationToken = default)
    {
        // Ola 9 RC9a — misma proyeccion del panel /admin/ai-usage con los
        // mismos filtros. Global query filter por tenant sigue aplicando via
        // IApplicationDbContext.
        var q = _db.AiUsageLogs.AsNoTracking().AsQueryable();
        if (filtro.AgentId is Guid aid)
        {
            q = q.Where(l => l.AgentId == aid);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Source))
        {
            var src = filtro.Source.Trim();
            q = q.Where(l => l.Source == src);
        }
        if (filtro.Desde is DateTimeOffset d)
        {
            q = q.Where(l => l.CreatedAt >= d);
        }
        if (filtro.Hasta is DateTimeOffset h)
        {
            q = q.Where(l => l.CreatedAt < h);
        }
        if (filtro.Success is bool ok)
        {
            q = q.Where(l => l.Success == ok);
        }

        // LEFT OUTER JOIN sobre AiAgents (GroupJoin + DefaultIfEmpty). Asi las
        // filas con AgentId NULL (uso sin agente asignado) tambien aparecen y
        // no hay que hacer un segundo pass.
        var rows = await q
            .GroupJoin(
                _db.AiAgents.AsNoTracking(),
                l => l.AgentId,
                a => (Guid?)a.Id,
                (l, ags) => new { l, ags })
            .SelectMany(
                x => x.ags.DefaultIfEmpty(),
                (x, a) => new
                {
                    x.l.CreatedAt,
                    AgenteNombre = a != null ? a.Name : null,
                    x.l.Provider,
                    x.l.Model,
                    x.l.Source,
                    x.l.InputTokens,
                    x.l.OutputTokens,
                    x.l.TotalTokens,
                    x.l.EstimatedCostUsd,
                    x.l.Success,
                })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Fecha,Agente,Proveedor,Modelo,Origen,Tok in,Tok out,Tok total,Costo USD,Exito");
        foreach (var r in rows)
        {
            sb.Append(Csv(r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append(',');
            sb.Append(Csv(r.AgenteNombre ?? "")).Append(',');
            sb.Append(Csv(r.Provider.ToString())).Append(',');
            sb.Append(Csv(r.Model)).Append(',');
            sb.Append(Csv(r.Source)).Append(',');
            sb.Append(r.InputTokens).Append(',');
            sb.Append(r.OutputTokens).Append(',');
            sb.Append(r.TotalTokens).Append(',');
            sb.Append(r.EstimatedCostUsd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.AppendLine(r.Success ? "OK" : "ERROR");
        }
        return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) { return ""; }
        var needsQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!needsQuote) { return s; }
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
