using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Regla configurable de alerta sobre asignaciones/servicios de un tenant. El
/// worker de alertas evalua las reglas activas cada dia: si "hoy" cae en el
/// disparo y la asignacion cumple la condicion, envia la alerta al destinatario
/// por el canal configurado (correo o WhatsApp HSM) y registra el envio en
/// <see cref="AlertaEnvio"/> para no repetirlo en el mismo periodo. Tenant-scoped.
/// </summary>
public class AlertaRegla : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public bool Activa { get; set; } = true;
    public int Orden { get; set; }

    /// <summary>Condicion sobre la asignacion (sesion pendiente / atenciones terminadas).</summary>
    public AlertaCondicion Condicion { get; set; }

    /// <summary>Filtro opcional por tipo de servicio (Modulo del contrato). Null = todos.</summary>
    public string? FiltroModulo { get; set; }

    // ---------------- Disparo ----------------
    public AlertaDisparoTipo DisparoTipo { get; set; }

    /// <summary>Dias del mes separados por coma (ej. "15,16,17"). Solo si DisparoTipo=DiasDelMes.</summary>
    public string? DiasDelMes { get; set; }

    /// <summary>Cantidad de meses despues del ancla. Solo si DisparoTipo=MesesDespues.</summary>
    public int? MesesDespues { get; set; }

    /// <summary>Ancla del disparo relativo. Solo si DisparoTipo=MesesDespues.</summary>
    public AlertaAnclaRelativa? AnclaRelativa { get; set; }

    // ---------------- Destinatario ----------------
    public AlertaDestinatario Destinatario { get; set; }

    /// <summary>TenantUser destino cuando Destinatario=UsuarioSistema.</summary>
    public Guid? UsuarioSistemaId { get; set; }

    // ---------------- Canal + contenido ----------------
    public AlertaCanal Canal { get; set; }

    /// <summary>Asunto del correo (admite tokens). Solo Canal=Correo.</summary>
    public string? Asunto { get; set; }

    /// <summary>Cuerpo del correo (admite tokens; se envia como HTML simple). Solo Canal=Correo.</summary>
    public string? Cuerpo { get; set; }

    // ---------------- WhatsApp HSM (Canal=WhatsApp) ----------------
    /// <summary>Linea Gupshup desde la que se envia la plantilla HSM.</summary>
    public Guid? HsmLineId { get; set; }

    /// <summary>UUID de la plantilla HSM en Gupshup.</summary>
    public string? HsmTemplateId { get; set; }

    /// <summary>element_name de la plantilla (informativo para la UI).</summary>
    public string? HsmTemplateName { get; set; }

    /// <summary>Cantidad de parametros {{n}} que exige la plantilla.</summary>
    public int HsmParameterCount { get; set; }

    /// <summary>Tokens de cada parametro posicional, en orden, como JSON array de strings
    /// (ej. ["{paciente}","{servicio}","{fecha}"]). Se renderizan con los datos de la
    /// asignacion antes de enviar. Su longitud debe igualar <see cref="HsmParameterCount"/>.</summary>
    public string? HsmParametrosJson { get; set; }
}
