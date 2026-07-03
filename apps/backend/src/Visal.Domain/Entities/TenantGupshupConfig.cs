using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion de una App Gupshup a nivel tenant. Tenemos una fila por
/// cada App creada en el dashboard Gupshup que la agencia use como origen
/// de mensajes WhatsApp (una App = un numero WABA verificado). Multiples
/// lineas del tenant pueden compartir la misma App si usan el mismo
/// numero, o cada linea puede apuntar a una App distinta.
///
/// ApiKeyEncrypted se guarda cifrada con ASP.NET Data Protection (mismo
/// patron que InteroperabilidadCredencialSede para RDA). El texto plano
/// NUNCA se persiste ni se logea.
/// </summary>
public class TenantGupshupConfig : TenantEntity
{
    /// <summary>
    /// AppId (GUID) de la App en Gupshup. Ej: 7bdba1f4-d014-48da-b6a8-6a5c7a5030d4.
    /// Es el identificador tecnico usado por endpoints Partner API.
    /// </summary>
    public Guid AppId { get; set; }

    /// <summary>
    /// AppName legible que aparece en dashboard Gupshup. Requerido por
    /// algunos endpoints v1 (ej: users/{appName}/templates).
    /// </summary>
    public string AppName { get; set; } = null!;

    /// <summary>WABA ID (Meta) asociado al numero. Puramente informativo.</summary>
    public string? WabaId { get; set; }

    /// <summary>Numero del telefono (E.164 sin '+', ej: 15559799304). Para display.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Nombre visible de negocio (Meta). Puramente informativo.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// apikey de la App Gupshup cifrada. Necesaria para todas las llamadas
    /// send. Se ingresa por UI y se cifra antes de persistir.
    /// </summary>
    public string ApiKeyEncrypted { get; set; } = null!;

    /// <summary>
    /// Partner Token cifrado (opcional). Necesario para operaciones de
    /// dashboard: listar/crear plantillas HSM, gestionar apps. Se pide
    /// aparte porque tiene otro ciclo de vida que la apikey.
    /// </summary>
    public string? PartnerTokenEncrypted { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastValidatedAt { get; set; }
}
