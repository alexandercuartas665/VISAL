using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

// ============================================================================
// DTOs - "Cuenta medica" configurada por aseguradora
// ============================================================================

public sealed record CuentaMedicaConfigDto(
    Guid Id,
    Guid AseguradoraId,
    bool PortadaHabilitada,
    string? PortadaLogoUrl,
    string? PortadaTitulo,
    string? PortadaSubtitulo,
    string? PortadaTextoLegal,
    bool IndiceHabilitado,
    string? PatronNombreDefault);

public sealed record GuardarPortadaRequest(
    Guid AseguradoraId,
    bool PortadaHabilitada,
    string? PortadaLogoUrl,
    string? PortadaTitulo,
    string? PortadaSubtitulo,
    string? PortadaTextoLegal,
    bool IndiceHabilitado,
    string? PatronNombreDefault);

public sealed record InformeItemDto(
    Guid Id,
    Guid ConfigId,
    int Orden,
    string? Seccion,
    OrigenInformeItem Origen,
    Guid? TipologiaArchivoId,
    string? TipologiaNombre,     // enriquecido en lectura para pintar la tabla
    string Alias,
    string? Descripcion,
    string? PatronNombre,
    bool Obligatorio,
    bool SoloUltimo);

public sealed record GuardarItemRequest(
    Guid? Id,                    // null = crear
    Guid AseguradoraId,          // el service resuelve/crea la config
    string? Seccion,
    OrigenInformeItem Origen,
    Guid? TipologiaArchivoId,
    string Alias,
    string? Descripcion,
    string? PatronNombre,
    bool Obligatorio,
    bool SoloUltimo);

/// <summary>Fila del selector "Copiar de ..." — solo aseguradoras que ya tienen
/// config con al menos 1 item.</summary>
public sealed record AseguradoraConConfigDto(
    Guid AseguradoraId,
    string Nombre,
    int ItemsCount);

/// <summary>
/// Configuracion "Cuenta medica" por aseguradora (fase 1: solo config, sin
/// generador). Singleton implicita: si no existe se crea vacia al primer
/// acceso. Todo tenant-scoped via los filtros globales de EF Core.
/// </summary>
public interface ICuentaMedicaConfigService
{
    /// <summary>Obtiene la config o la crea vacia si no existe.</summary>
    Task<CuentaMedicaConfigDto> GetOrCreateAsync(
        Guid aseguradoraId, CancellationToken ct = default);

    Task<CuentaMedicaConfigDto> GuardarPortadaAsync(
        GuardarPortadaRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<InformeItemDto>> ListarItemsAsync(
        Guid aseguradoraId, CancellationToken ct = default);

    Task<InformeItemDto> GuardarItemAsync(
        GuardarItemRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> EliminarItemAsync(
        Guid itemId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Reordena los items segun el orden del array (indice = Orden).</summary>
    Task ReordenarItemsAsync(
        Guid aseguradoraId, IReadOnlyList<Guid> itemIdsEnOrden,
        Guid actorUserId, CancellationToken ct = default);

    /// <summary>Aseguradoras del tenant que ya tienen al menos 1 item — usadas
    /// para el selector "Copiar de ..." (excluye la destino).</summary>
    Task<IReadOnlyList<AseguradoraConConfigDto>> ListarAseguradorasConConfigAsync(
        Guid excluirAseguradoraId, CancellationToken ct = default);

    /// <summary>Clona portada + items desde <paramref name="origenAseguradoraId"/>
    /// hacia <paramref name="destinoAseguradoraId"/>. Si destino ya tiene items
    /// los reemplaza (borra + inserta).</summary>
    Task<CuentaMedicaConfigDto> CopiarDeAsync(
        Guid origenAseguradoraId, Guid destinoAseguradoraId,
        Guid actorUserId, CancellationToken ct = default);
}
