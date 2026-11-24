namespace Visal.Application.Tenancy;

/// <summary>
/// Motivo por el que una sesion NO puede aun abrir HC. Devolver null significa
/// "puede abrir libremente". El caller (backend gate en HistoriaClinicaService
/// y UI /atencion) decide como mostrar el bloqueo.
/// </summary>
public sealed record AtencionOrdenBloqueo(
    string Motivo,
    int SessionNoPendiente,
    Guid AsignacionTurnoId,
    Guid SesionAnteriorId);

/// <summary>
/// Valida el orden secuencial de sesiones en el modulo /atencion: dentro de la
/// misma AsignacionTurno, la sesion N solo se puede atender si las sesiones
/// 1..N-1 estan Completadas (tienen al menos una HC vinculada en estado Cerrada).
///
/// Regla de escape: si el usuario tiene el permiso <c>atencion.saltar-orden</c>,
/// o es Owner/Admin del tenant, pasa libre. La responsabilidad final del
/// bloqueo esta en el backend: la UI reflejara el estado pero
/// <see cref="HistoriaClinicaService"/> tambien llama este validador antes de
/// crear la HC (defensa contra clientes que evadan la UI).
/// </summary>
public interface IAtencionOrdenService
{
    Task<AtencionOrdenBloqueo?> ValidarAperturaAsync(Guid sesionId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// IMP-66: misma regla al CERRAR una HC ligada a una sesion. Evita que, tras una
    /// reapertura en bloque, se cierre la sesion N con sesiones anteriores del mismo
    /// profesional aun abiertas (la sesion N quedaria Completada fuera de orden).
    /// Null = puede cerrar (o la HC no esta ligada a una sesion, o el actor tiene escape).
    /// </summary>
    Task<AtencionOrdenBloqueo?> ValidarCierreAsync(Guid historiaClinicaId, Guid actorUserId, CancellationToken ct = default);
}

/// <summary>El cierre de la HC se rechazo por orden secuencial de sesiones (IMP-66).</summary>
public sealed class CierreFueraDeOrdenException(string message) : InvalidOperationException(message);
