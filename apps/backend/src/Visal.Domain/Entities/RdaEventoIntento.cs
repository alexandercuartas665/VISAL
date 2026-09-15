using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Traza de un intento de envio de un <see cref="RdaEvento"/> al IHCE. Se registra en
/// CADA envio (manual desde la consola o automatico por el worker de reintentos), con el
/// resultado (estado, HTTP, mensaje). Permite auditar el historial completo de intentos
/// de un RDA, no solo el ultimo. Tenant-scoped.
/// </summary>
public class RdaEventoIntento : TenantEntity
{
    public Guid RdaEventoId { get; set; }

    /// <summary>Numero de intento (= RdaEvento.Intentos tras este envio).</summary>
    public int Numero { get; set; }

    public DateTimeOffset Fecha { get; set; }

    /// <summary>Estado resultante de este intento (Aceptado / Rechazado / Error).</summary>
    public EstadoRdaEvento EstadoResultado { get; set; }

    /// <summary>Codigo HTTP de la respuesta del IHCE (null si no hubo respuesta).</summary>
    public int? HttpStatus { get; set; }

    /// <summary>Resumen corto del resultado o del error (para la traza en la consola).</summary>
    public string? Mensaje { get; set; }

    /// <summary>True si lo disparo el worker de reintento automatico; false si fue manual.</summary>
    public bool Automatico { get; set; }
}
