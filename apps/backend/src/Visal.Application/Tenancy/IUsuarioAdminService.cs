namespace Visal.Application.Tenancy;

public sealed record UsuarioDto(
    Guid Id, Guid PlatformUserId, string Email, string? DisplayName,
    Guid? RolId, string? RolNombre,
    IReadOnlyList<Guid> SucursalIds, IReadOnlyList<string> SucursalNombres,
    string Estado, bool EsGlobal,
    // Datos personales del PlatformUser.
    string? Documento, string? Username,
    string? PrimerNombre, string? SegundoNombre, string? PrimerApellido, string? SegundoApellido,
    string? Celular, string? Fijo, string? Ciudad, string? Direccion,
    // Permisos de coordinacion (tenant-scoped, viven en TenantUser).
    bool CoordinaTerapias, bool CoordinaEnfermeria, bool CoordinaConsultas, bool CoordinaEquipos);

public sealed record CrearUsuarioRequest(
    string Email, string? DisplayName, string Password, Guid? RolId,
    IReadOnlyList<Guid> SucursalIds, bool EsGlobal);

/// <summary>Payload para actualizar perfil completo del usuario (campos personales).</summary>
public sealed record ActualizarPerfilUsuarioRequest(
    string? DisplayName, string? Username, string? Documento,
    string? PrimerNombre, string? SegundoNombre, string? PrimerApellido, string? SegundoApellido,
    string? Celular, string? Fijo, string? Ciudad, string? Direccion,
    bool Inhabilitado);

/// <summary>Payload para actualizar los 4 flags de permisos de coordinacion del usuario en este tenant.</summary>
public sealed record ActualizarPermisosCoordinacionRequest(
    bool CoordinaTerapias, bool CoordinaEnfermeria, bool CoordinaConsultas, bool CoordinaEquipos);

public interface IUsuarioAdminService
{
    Task<IReadOnlyList<UsuarioDto>> ListAsync(CancellationToken ct = default);
    Task<UsuarioDto?> CrearAsync(CrearUsuarioRequest req, Guid actor, CancellationToken ct = default);
    Task<UsuarioDto?> AsignarAsync(Guid tenantUserId, Guid? rolId, IReadOnlyList<Guid> sucursalIds, bool esGlobal, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid tenantUserId, Guid actor, CancellationToken ct = default);

    /// <summary>Reinicia la clave del PlatformUser asociado al TenantUser. Devuelve true si tuvo exito.</summary>
    Task<bool> ResetPasswordAsync(Guid tenantUserId, string nuevaClave, Guid actor, CancellationToken ct = default);

    /// <summary>Actualiza datos personales del usuario (campos editables en /cfg-usuarios).</summary>
    Task<UsuarioDto?> ActualizarPerfilAsync(Guid tenantUserId, ActualizarPerfilUsuarioRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Actualiza los flags de permisos de coordinacion del usuario en este tenant.</summary>
    Task<UsuarioDto?> ActualizarPermisosCoordinacionAsync(Guid tenantUserId, ActualizarPermisosCoordinacionRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los modulos que el usuario logueado puede coordinar (TERAPIAS, ENFERMERIA,
    /// CONSULTAS, EQUIPOS). Si el usuario no tiene TenantUser en este tenant, devuelve lista vacia.
    /// Es el feed del dropdown "Seleccione tipo asignacion" en /coordinacion.
    /// </summary>
    Task<IReadOnlyList<string>> GetModulosCoordinacionAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Crea un usuario (PlatformUser + TenantUser) a partir de un profesional existente.
    /// Toma documento, nombre completo y celular del profesional. El correo y la clave los pasa el caller.
    /// El TenantUser queda vinculado al profesional via ProfesionalId.
    /// </summary>
    Task<UsuarioDto?> CrearUsuarioDesdeProfesionalAsync(Guid profesionalId, string email, string password, Guid? rolId, Guid actor, CancellationToken ct = default);

    /// <summary>Devuelve el TenantUser vinculado al profesional (via ProfesionalId FK)
    /// o null si aun no tiene usuario. Usado por la UI de Profesionales para saber
    /// si el modal debe mostrarse en modo Crear o Actualizar.</summary>
    Task<UsuarioDto?> GetPorProfesionalIdAsync(Guid profesionalId, CancellationToken ct = default);
}
