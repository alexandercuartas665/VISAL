using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class RdaReintentoService(
    IApplicationDbContext db,
    IIhceSenderService sender,
    ILogger<RdaReintentoService> log) : IRdaReintentoService
{
    public async Task<int> ReintentarPendientesAsync(CancellationToken ct = default)
    {
        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.ReintentoActivo) { return 0; }

        var maxIntentos = cfg.ReintentoMaxIntentos <= 0 ? 20 : cfg.ReintentoMaxIntentos;
        var intervalo = TimeSpan.FromMinutes(cfg.ReintentoIntervaloMin <= 0 ? 15 : cfg.ReintentoIntervaloMin);
        var corte = DateTimeOffset.UtcNow - intervalo;

        // Eventos fallidos, por debajo del maximo de intentos, cuyo ultimo intento ya
        // supero el intervalo (o nunca se intentaron).
        var candidatos = await db.RdaEventos
            .Where(e => (e.Estado == EstadoRdaEvento.Error || e.Estado == EstadoRdaEvento.Rechazado)
                     && e.Intentos < maxIntentos
                     && (e.UltimoIntento == null || e.UltimoIntento <= corte))
            .OrderBy(e => e.UltimoIntento)
            .Take(50)
            .ToListAsync(ct);

        int reintentados = 0;
        foreach (var e in candidatos)
        {
            ct.ThrowIfCancellationRequested();
            // Solo fallas transitorias: un rechazo estructural (bundle mal formado) no se
            // arregla solo, no tiene sentido reintentarlo.
            if (!EsFallaTransitoria(e.Estado, e.ErroresJson)) { continue; }
            try
            {
                var r = await sender.EnviarRdaAsync(e.Id, Guid.Empty, automatico: true, ct: ct);
                reintentados++;
                if (r.NuevoEstado == EstadoRdaEvento.Aceptado)
                {
                    log.LogInformation("Reintento RDA {Id}: ACEPTADO (intento {N}).", e.Id, e.Intentos + 1);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Reintento RDA {Id} fallo (ignorado).", e.Id);
            }
        }

        // Envio INICIAL de RDAs generados automaticamente (p. ej. al aprobar la revision
        // clinica de la HC): Borrador marcados EnvioAutomatico que nunca se intentaron.
        // Se envian una vez; si fallan caen a Error/Rechazado y entran al flujo de
        // reintento de arriba en los siguientes ciclos. Los Borrador generados a mano
        // desde la consola (EnvioAutomatico == false) NO se tocan: se envian con el boton.
        var autoPendientes = await db.RdaEventos
            .Where(e => e.Estado == EstadoRdaEvento.Borrador && e.EnvioAutomatico && e.Intentos == 0)
            .OrderBy(e => e.FechaGeneracion)
            .Take(50)
            .ToListAsync(ct);
        foreach (var e in autoPendientes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await sender.EnviarRdaAsync(e.Id, Guid.Empty, automatico: true, ct: ct);
                reintentados++;
                if (r.NuevoEstado == EstadoRdaEvento.Aceptado)
                {
                    log.LogInformation("Envio automatico RDA {Id}: ACEPTADO.", e.Id);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Envio automatico RDA {Id} fallo (ignorado).", e.Id);
            }
        }

        return reintentados;
    }

    /// <summary>Clasifica si el fallo es transitorio (reintentable). Error = red/5xx/timeout
    /// (siempre transitorio). Rechazado = solo si el mensaje indica servicio indisponible /
    /// timeout / no se pudo validar (EVOL). Rechazos estructurales NO se reintentan.</summary>
    private static bool EsFallaTransitoria(EstadoRdaEvento estado, string? erroresJson)
    {
        if (estado == EstadoRdaEvento.Error) { return true; }   // red / 5xx / timeout
        if (estado != EstadoRdaEvento.Rechazado) { return false; }
        if (string.IsNullOrWhiteSpace(erroresJson)) { return false; }

        // Codigos HTTP de servicio/gateway (no rechazo estructural del bundle): el sandbox
        // de MinSalud alterna 504/404/429 cuando su infra esta inestable -> reintentar.
        var http = ExtraerHttpStatus(erroresJson);
        if (http is 404 or 408 or 425 or 429 or (>= 500 and <= 599)) { return true; }

        // Rechazos por servicio de validacion indisponible (EVOL) o similares.
        var t = erroresJson.ToLowerInvariant();
        return t.Contains("indisponible")
            || t.Contains("no fue posible validarlo")
            || t.Contains("timeout") || t.Contains("time out")
            || t.Contains("temporal") || t.Contains("unavailable")
            || t.Contains("intente");
    }

    private static int? ExtraerHttpStatus(string erroresJson)
    {
        try
        {
            using var d = JsonDocument.Parse(erroresJson);
            if (d.RootElement.TryGetProperty("httpStatus", out var h) && h.TryGetInt32(out var v)) { return v; }
        }
        catch { /* errores_json no JSON */ }
        return null;
    }
}
