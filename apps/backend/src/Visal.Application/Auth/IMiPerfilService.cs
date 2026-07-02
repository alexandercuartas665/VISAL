namespace Visal.Application.Auth;

/// <summary>Datos del usuario logueado + snapshot del profesional vinculado
/// (readonly, solo para mostrar). El campo Profesional viene con datos
/// cuando el TenantUser esta atado a un Profesional en la sede activa; null
/// para usuarios administrativos.</summary>
public sealed record MiPerfilDto(
    Guid Id, string Email, string? Username, string? Documento,
    string? DisplayName, string? PrimerNombre, string? SegundoNombre,
    string? PrimerApellido, string? SegundoApellido,
    string? Celular, string? Fijo, string? Ciudad, string? Direccion,
    string? AvatarUrl,
    ProfesionalVinculadoDto? Profesional);

/// <summary>Datos del profesional vinculado. Solo lectura desde el modulo
/// autogestion (los cambios los hace el admin en /profesionales).</summary>
public sealed record ProfesionalVinculadoDto(
    Guid Id, string NumeroDocumento, string TipoDocumento, string NombreCompleto,
    string? TipoProfesional, string? Registro, string? Ciudad, string? Celular,
    string? FirmaUrl, IReadOnlyList<string> Subcategorias, IReadOnlyList<string> Sedes);

public sealed record ActualizarMiPerfilRequest(
    string? Email, string? PrimerNombre, string? SegundoNombre,
    string? PrimerApellido, string? SegundoApellido,
    string? Celular, string? Fijo, string? Ciudad, string? Direccion,
    string? AvatarUrl);

/// <summary>Modulo de autogestion del usuario logueado. Le deja editar sus
/// datos personales, cambiar avatar, cambiar la firma del profesional
/// vinculado (si tiene) y cambiar su propia clave. NO le deja editar los
/// datos maestros del profesional (esos se hacen desde /profesionales).</summary>
public interface IMiPerfilService
{
    Task<MiPerfilDto?> GetAsync(Guid platformUserId, CancellationToken ct = default);

    Task<MiPerfilDto?> ActualizarPerfilAsync(
        Guid platformUserId, ActualizarMiPerfilRequest req, CancellationToken ct = default);

    /// <summary>Cambia la firma del profesional vinculado al platform user.
    /// Devuelve false si el usuario no tiene profesional vinculado.</summary>
    Task<bool> ActualizarFirmaProfesionalAsync(
        Guid platformUserId, string firmaDataUrl, CancellationToken ct = default);
}
