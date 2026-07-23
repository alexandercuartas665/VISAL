namespace Visal.Application.Tenancy;

/// <summary>
/// Gestion de miembros invitados a un tablero.
///
/// Solo el owner del tablero (OwnerPlatformUserId) puede invitar o quitar
/// miembros. Todas las operaciones validan ownership antes de mutar.
/// </summary>
public interface ITaskBoardMemberService
{
    /// <summary>Lista todos los miembros del tablero incluyendo al owner
    /// como primera fila (con IsOwner=true). Requiere que el actor sea
    /// owner o miembro para leer.</summary>
    Task<IReadOnlyList<TaskBoardMemberDto>> ListMembersAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);

    /// <summary>PlatformUsers del tenant que aun NO son miembros del tablero
    /// — sirve para popular el selector "Invitar". Filtro opcional por
    /// texto (busca en DisplayName y Email).</summary>
    Task<IReadOnlyList<TaskBoardInviteCandidateDto>> ListInviteCandidatesAsync(
        Guid boardId, Guid actorPlatformUserId, string? search = null, CancellationToken ct = default);

    /// <summary>Agrega un usuario como miembro. Solo el owner. Idempotente
    /// (si el usuario ya es miembro, devuelve la fila existente).</summary>
    Task<TaskBoardMemberDto?> InviteMemberAsync(InviteMemberRequest request, Guid actorPlatformUserId, CancellationToken ct = default);

    /// <summary>Quita un miembro del tablero. Solo el owner. NO se puede
    /// quitar al owner (devuelve false).</summary>
    Task<bool> RemoveMemberAsync(Guid boardId, Guid platformUserIdToRemove, Guid actorPlatformUserId, CancellationToken ct = default);
}
