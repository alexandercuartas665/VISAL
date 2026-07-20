namespace Visal.Application.Revision;

/// <summary>
/// Ola 8 RC8c — notifica al profesional autor de la HC cuando su revision fue
/// rechazada. Best-effort: si algo falla (sin telefono, sin linea WA activa,
/// sin binding HSM, error de red) el rechazo NO se aborta, solo se loguea el
/// warning. Se dispara SOLO cuando <c>RevisionPolicy.NotificarRechazoWhatsApp</c>
/// esta activo para el tenant.
/// </summary>
public interface IRevisionRechazoNotifier
{
    /// <summary>Envia el mensaje al profesional autor si el flag esta activo. Nunca lanza.</summary>
    Task NotificarAsync(Guid historiaClinicaId, string motivo, Guid actorUserId, CancellationToken ct = default);
}
