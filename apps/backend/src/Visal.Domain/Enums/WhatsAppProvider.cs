namespace Visal.Domain.Enums;

/// <summary>
/// Proveedor real detras de una linea WhatsApp del tenant. Cada linea
/// puede correr contra su propio proveedor: el chat del sistema hace
/// dispatch en base a este valor (ver IWhatsAppProviderResolver).
/// </summary>
public enum WhatsAppProvider
{
    /// <summary>
    /// Evolution API (self-hosted o master). Es el proveedor historico y
    /// sigue siendo default para lineas creadas antes de la integracion
    /// Gupshup.
    /// </summary>
    Evolution = 0,

    /// <summary>
    /// Gupshup (BSP oficial de Meta). Requiere App creada en el dashboard
    /// Gupshup + apikey + WABA verificada. Ver TenantGupshupConfig.
    /// </summary>
    Gupshup = 1
}
