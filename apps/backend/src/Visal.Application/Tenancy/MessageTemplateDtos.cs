using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record MessageTemplateDto(
    Guid Id,
    string Category,
    string Body,
    MessageMediaType MediaType,
    string? MediaUrl,
    string? MediaMimeType,
    int SortOrder);

public sealed record CreateMessageTemplateRequest(
    string Category,
    string Body,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null);

public sealed record UpdateMessageTemplateRequest(
    string Category,
    string Body,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null);

/// <summary>Categorias estandar de pregrabados clinicos (clave + etiqueta para la UI).</summary>
public static class MessageTemplateCategories
{
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        ("saludo", "Saludo"),
        ("recordatorio", "Recordatorio"),
        ("info", "Pedir info"),
        ("seguimiento", "Seguimiento"),
        ("alta", "Alta / Cierre")
    };

    public static string Label(string key) =>
        All.FirstOrDefault(c => c.Key == key).Label ?? key;
}
