using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Registro de cada consulta EXITOSA a la pagina publica de verificacion de una
/// orden de medicamentos. Sirve para trazabilidad (quien/cuando/desde donde se
/// escaneo o tipeo el codigo). No es tenant-scoped a nivel de filtro: la
/// escritura ocurre en un contexto anonimo (sin tenant activo), asi que se
/// modela como entidad global con una columna TenantId de referencia — igual que
/// SqlConsoleLog / SuperAdminAuditLog. Los intentos fallidos (codigo inexistente)
/// NO se persisten aqui; se controlan con rate limiting en memoria.
/// </summary>
public class VerificacionOrdenLog : BaseEntity
{
    /// <summary>Orden verificada.</summary>
    public Guid OrdenMedicamentoPublicaId { get; set; }

    /// <summary>Tenant dueno de la orden (referencia, no filtro).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Codigo tal como se consulto (para correlacionar).</summary>
    public string CodigoConsultado { get; set; } = null!;

    /// <summary>IP del cliente (puede venir del header X-Forwarded-For).</summary>
    public string? Ip { get; set; }

    /// <summary>User-Agent del navegador que consulto.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Momento de la consulta.</summary>
    public DateTimeOffset ConsultadoEn { get; set; }
}
