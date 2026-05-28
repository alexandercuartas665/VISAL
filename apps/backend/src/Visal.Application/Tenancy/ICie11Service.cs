namespace Visal.Application.Tenancy;

/// <summary>Configuracion del cliente WHO ICD-11 API (claves OAuth2 + endpoints).</summary>
public sealed record Cie11ConfigDto(string? TokenUrl, string? ClientId, string? ClientSecret, string? SearchUrl, string? MmsUrlBase, bool Activo);

/// <summary>Item devuelto por /entity/search del WHO ICD-11.</summary>
public sealed record Cie11SearchItem(string EntityId, string Title);

/// <summary>Detalle de una entidad: codigo CIE-11 + titulo limpio en espanol.</summary>
public sealed record Cie11Detail(string Code, string Title);

public interface ICie11Service
{
    /// <summary>Obtiene la configuracion del tenant activo (sin secretos enmascarados; solo Super Admin/operador).</summary>
    Task<Cie11ConfigDto?> GetConfigAsync(CancellationToken ct = default);

    /// <summary>Guarda/actualiza la configuracion del tenant activo.</summary>
    Task<Cie11ConfigDto?> SaveConfigAsync(Cie11ConfigDto req, Guid actor, CancellationToken ct = default);

    /// <summary>Busca terminos en WHO ICD-11 (devuelve hasta N coincidencias).</summary>
    Task<IReadOnlyList<Cie11SearchItem>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>Obtiene codigo + titulo de un entityId (URL completa o solo el id final).</summary>
    Task<Cie11Detail?> GetDetailAsync(string entityIdOrUrl, CancellationToken ct = default);
}
