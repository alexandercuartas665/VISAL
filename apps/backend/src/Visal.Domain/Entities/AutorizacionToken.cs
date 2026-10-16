using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Estado de un token de autorizacion de fecha.</summary>
public enum AutorizacionTokenEstado
{
    /// <summary>Generado y aun no usado. (Puede estar vencido: se deriva comparando ExpiresAt.)</summary>
    Activo = 0,
    /// <summary>Ya consumido por una operacion (un solo uso).</summary>
    Usado = 1,
}

/// <summary>
/// Token de autorizacion de 6 caracteres que permite SALTAR el control de fecha de
/// atencion (cuando la fecha excede el limite de oportunidad del tipo de servicio).
/// Lo genera un usuario de autorizacion en el modulo /autorizaciones con una
/// observacion de uso; vence a los 30 minutos y es de UN SOLO USO. Al consumirse
/// queda trazado quien lo uso, en que HC/asignacion y con que motivo. Tenant-scoped.
/// </summary>
public class AutorizacionToken : TenantEntity
{
    /// <summary>Codigo legible de 6 caracteres (base32 sin ambiguos). Unico por tenant.</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Concepto/categoria tipificada (snapshot del nombre del AutorizacionConcepto
    /// elegido al generar). Null si no se selecciono ninguno.</summary>
    public string? ConceptoNombre { get; set; }

    /// <summary>Observacion/justificacion que captura el usuario de autorizacion al generarlo.</summary>
    public string Observacion { get; set; } = null!;

    /// <summary>Nombre del usuario de autorizacion que lo genero (CreatedAt/CreatedBy guardan fecha/id).</summary>
    public string? GeneradoPorNombre { get; set; }

    /// <summary>Momento de vencimiento (generacion + 30 min). Tras esto no se puede usar.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Activo / Usado. "Vencido" se deriva (Activo && ExpiresAt &lt; ahora).</summary>
    public AutorizacionTokenEstado Estado { get; set; } = AutorizacionTokenEstado.Activo;

    // ── Trazabilidad de uso (null hasta que se consume) ──────────────────────────
    /// <summary>Momento en que se uso el token.</summary>
    public DateTimeOffset? UsadoEn { get; set; }

    /// <summary>Id (platform user) del operador que lo uso.</summary>
    public Guid? UsadoPorUserId { get; set; }

    /// <summary>Nombre del operador que lo uso.</summary>
    public string? UsadoPorNombre { get; set; }

    /// <summary>Historia clinica que lo consumio.</summary>
    public Guid? HistoriaClinicaId { get; set; }

    /// <summary>Asignacion asociada a la HC que lo consumio.</summary>
    public Guid? AsignacionId { get; set; }

    /// <summary>Motivo/descripcion que el operador escribio al usar el token.</summary>
    public string? MotivoUso { get; set; }
}
