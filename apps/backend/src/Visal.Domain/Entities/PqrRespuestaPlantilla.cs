using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Plantilla pregrabada de respuesta a un correo de PQR (por tenant). El cuerpo admite
/// marcadores tipo <c>{{nombre}}</c> que se reemplazan al insertar la plantilla en la respuesta.
/// Estilo de los pregrabados del chat de WhatsApp, pero especifico del flujo PQR (no reutiliza
/// <c>MessageTemplate</c>, que es de WhatsApp con su propia taxonomia de categorias y media).
/// </summary>
public class PqrRespuestaPlantilla : TenantEntity
{
    /// <summary>Nombre corto para elegirla (ej. "Acuse de recibo").</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Asunto sugerido (opcional). Vacio -> se usa "Re: {asunto original}".</summary>
    public string? Asunto { get; set; }

    /// <summary>Cuerpo con marcadores {{...}} (ej. {{nombre}}, {{radicado}}).</summary>
    public string Cuerpo { get; set; } = null!;

    /// <summary>Orden de aparicion (menor primero).</summary>
    public int SortOrder { get; set; }

    /// <summary>Solo se listan las activas.</summary>
    public bool IsActive { get; set; } = true;
}
