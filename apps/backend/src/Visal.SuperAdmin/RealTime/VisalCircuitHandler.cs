using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Security.Claims;

namespace Visal.SuperAdmin.RealTime;

/// <summary>
/// CircuitHandler que agrega contexto a los logs cuando el circuito Blazor Server
/// se abre/cierra o pierde conexion. NO captura la excepcion del handler que rompe
/// el circuito (esa la loguea el framework en la categoria
/// Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost a nivel Error), pero
/// SI deja un evento correlacionable justo antes con el CircuitId + usuario, para
/// que al leer el log se pueda emparejar la excepcion del framework con la sesion
/// del usuario que la disparo.
///
/// Registro en Program.cs como Scoped: cada circuito recibe su instancia.
/// </summary>
public sealed class VisalCircuitHandler : CircuitHandler
{
    private readonly ILogger<VisalCircuitHandler> _log;
    private readonly IHttpContextAccessor _http;

    public VisalCircuitHandler(ILogger<VisalCircuitHandler> log, IHttpContextAccessor http)
    {
        _log = log;
        _http = http;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        var (user, tenant, path) = ResolverContexto();
        _log.LogInformation(
            "Circuito Blazor abierto | CircuitId={CircuitId} User={User} Tenant={Tenant} Path={Path}",
            circuit.Id, user, tenant, path);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken ct)
    {
        // Bajada de conexion == se cayo el WebSocket. Puede ser red o excepcion.
        // Solo Info: es evento normal si el usuario cambia de pestana o pierde wifi.
        var (user, tenant, path) = ResolverContexto();
        _log.LogInformation(
            "Circuito Blazor DESCONECTADO (transient) | CircuitId={CircuitId} User={User} Tenant={Tenant} Path={Path}",
            circuit.Id, user, tenant, path);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        // Cierre definitivo del circuito. Si viene precedido de una excepcion en
        // el framework, esta linea permite correlar con el usuario/sesion.
        var (user, tenant, path) = ResolverContexto();
        _log.LogWarning(
            "Circuito Blazor CERRADO | CircuitId={CircuitId} User={User} Tenant={Tenant} Path={Path}",
            circuit.Id, user, tenant, path);
        return Task.CompletedTask;
    }

    /// <summary>Extrae los claims utiles del HttpContext actual. Best-effort:
    /// devuelve "-" cuando no hay HttpContext (ejecucion fuera de request).</summary>
    private (string user, string tenant, string path) ResolverContexto()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) { return ("-", "-", "-"); }
        var user = ctx.User.Identity?.Name
                   ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
                   ?? ctx.User.FindFirst("display_name")?.Value
                   ?? "anonimo";
        var tenant = ctx.User.FindFirst("tenant_id")?.Value ?? "-";
        var path = ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : "-";
        return (user, tenant, path);
    }
}
