using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion de la "Cuenta medica" (paquete de soporte) exigida por una
/// aseguradora para pagar/auditar por paciente. Singleton por aseguradora
/// (UNIQUE en AseguradoraId + TenantId).
///
/// Los items concretos que componen el informe viven en
/// <see cref="AseguradoraInformeItem"/>, cada uno resuelto por
/// <see cref="Visal.Domain.Enums.OrigenInformeItem"/>.
///
/// Los campos de portada/indice se capturan hoy pero NO se renderizan hasta
/// que exista el generador (fase 2 del modulo).
/// </summary>
public class AseguradoraCuentaMedicaConfig : TenantEntity
{
    public Guid AseguradoraId { get; set; }

    // ===== Portada =====
    public bool PortadaHabilitada { get; set; }
    /// <summary>Ruta web al logo de la EPS (subido a /uploads/aseguradoras/logos/).</summary>
    public string? PortadaLogoUrl { get; set; }
    public string? PortadaTitulo { get; set; }
    public string? PortadaSubtitulo { get; set; }
    /// <summary>Texto legal / declaracion / footer que aparece en la portada.</summary>
    public string? PortadaTextoLegal { get; set; }

    // ===== Indice =====
    public bool IndiceHabilitado { get; set; }

    // ===== Nombre de archivo =====
    /// <summary>Patron fallback si el item no define uno propio. Tokens
    /// disponibles: {sigla} {cedula} {nombre} {fecha:yyyyMMdd} {consecutivo}
    /// {mes} {codigo_hc} {tipo_servicio}. Ej: "{sigla}_{cedula}_{fecha:yyyyMMdd}".</summary>
    public string? PatronNombreDefault { get; set; }
}
