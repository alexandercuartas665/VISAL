using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Usuario de plataforma (identidad). Puede operar uno o varios tenants via TenantUser
/// y/o ser operador del SaaS via PlatformRole. Entidad global. Ver Notas dev sec.1.5.
/// </summary>
public class PlatformUser : BaseEntity
{
    public string Email { get; set; } = null!;
    public bool EmailVerified { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GoogleSubject { get; set; }
    public string AuthProvider { get; set; } = "local";

    /// <summary>Hash PBKDF2 de la clave para login local. Null si el usuario solo usa proveedor externo.</summary>
    public string? PasswordHash { get; set; }
    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Invited;
    public PlatformRole? PlatformRole { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Usuario global: puede acceder a cualquier empresa/tenant y elegir cual al iniciar sesion.</summary>
    public bool EsGlobal { get; set; }

    /// <summary>Numero de documento (cedula) del usuario. Permite login con cedula ademas del correo.</summary>
    public string? Documento { get; set; }

    // ---------- Datos personales (modulo Administracion de Usuarios) ----------

    /// <summary>Nombre de usuario corto para login (ej. "ANA.TORO"). Opcional, distinto del email.</summary>
    public string? Username { get; set; }

    /// <summary>Componentes del nombre. Si se llenan, DisplayName se debe sincronizar con la concatenacion.</summary>
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }

    /// <summary>Telefono celular del usuario.</summary>
    public string? Celular { get; set; }
    /// <summary>Telefono fijo del usuario.</summary>
    public string? Fijo { get; set; }

    /// <summary>Ciudad de residencia (codigo o nombre, segun catalogo).</summary>
    public string? Ciudad { get; set; }
    public string? Direccion { get; set; }
}
