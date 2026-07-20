namespace Visal.Application.Common;

/// <summary>
/// Ambient asincrono para pasar el tenant activo en flujos que NO son requests
/// HTTP (workers, tareas de fondo, jobs). El <see cref="CookieUserContext"/> lee
/// primero del HttpContext y, si no hay, recae en el ambient. Introducido en
/// Ola 8 RC8e para que el <c>PreRevisionIaWorker</c> pueda ejecutar el
/// orquestador dentro del scope tenant correcto sin cambiar la firma de los
/// servicios.
///
/// USO: envolver la seccion critica del worker con <c>using var _ = TenantAmbient.Scope(tenantId, userId)</c>.
/// El AsyncLocal se restablece automaticamente al liberar el token.
/// </summary>
public static class TenantAmbient
{
    private static readonly AsyncLocal<State?> _current = new();

    public static Guid? TenantId => _current.Value?.TenantId;
    public static Guid? UserId => _current.Value?.UserId;
    public static Guid? SucursalId => _current.Value?.SucursalId;

    /// <summary>Empuja un tenant al scope actual; el token se libera restaurando el previo.</summary>
    public static IDisposable Scope(Guid tenantId, Guid? userId = null, Guid? sucursalId = null)
    {
        var prev = _current.Value;
        _current.Value = new State(tenantId, userId, sucursalId);
        return new Popper(prev);
    }

    private sealed record State(Guid TenantId, Guid? UserId, Guid? SucursalId);

    private sealed class Popper : IDisposable
    {
        private readonly State? _prev;
        public Popper(State? prev) { _prev = prev; }
        public void Dispose() { _current.Value = _prev; }
    }
}
