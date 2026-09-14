using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Registro de cada apertura del enlace del informe de terapias pendientes. La
/// pagina del informe es anonima: el token cifrado identifica al profesional
/// (inf2) o al tenant completo (inf1). Con estos registros el modulo de Alertas
/// mide que doctores CONSUMEN (abren) su informe y cuales NO, y alimenta la
/// alerta a gerencia de doctores que no leen. Tenant-scoped.
/// </summary>
public class InformeAcceso : TenantEntity
{
    /// <summary>Profesional al que estaba acotado el enlace (token inf2). Null si el
    /// enlace era general del tenant (inf1).</summary>
    public Guid? ProfesionalId { get; set; }

    /// <summary>Momento en que se abrio el enlace.</summary>
    public DateTimeOffset AccedidoEn { get; set; }

    /// <summary>Tipo de token del enlace: "inf1" (tenant) o "inf2" (profesional).</summary>
    public string TokenTipo { get; set; } = "inf2";
}
