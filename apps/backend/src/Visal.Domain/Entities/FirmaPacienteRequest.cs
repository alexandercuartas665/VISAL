using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Solicitud de firma remota del paciente. El profesional la crea desde el modulo de
/// Notas Medicas; el sistema genera un token unico y envia al WhatsApp del paciente
/// un link publico (sin auth) donde firma con el dedo en su celular. Cuando completa
/// la firma se persiste en NotaMedica.FirmaPacienteDataUrl y este registro queda
/// como historial / auditoria de la solicitud.
/// </summary>
public class FirmaPacienteRequest : TenantEntity
{
    /// <summary>Token publico unico para la URL /firma/{token}. Sin signos, solo digitos hex.</summary>
    public string Token { get; set; } = null!;

    public Guid PacienteId { get; set; }
    public Guid NotaMedicaId { get; set; }

    /// <summary>Telefono al que se envio la solicitud (solo digitos). Lo guardamos para auditoria
    /// porque el paciente podria cambiar de numero entre la solicitud y la firma.</summary>
    public string Telefono { get; set; } = null!;

    public string? NombreContacto { get; set; }

    /// <summary>Tenant user (profesional) que solicito la firma.</summary>
    public Guid? SolicitadaPorTenantUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Data URL del PNG firmado (data:image/png;base64,…). Solo se llena cuando el
    /// paciente termina la firma desde su celular. Mismo formato que NotaMedica.FirmaPacienteDataUrl.</summary>
    public string? ImageDataUrl { get; set; }

    public FirmaRequestStatus Status { get; set; } = FirmaRequestStatus.Pendiente;
}
