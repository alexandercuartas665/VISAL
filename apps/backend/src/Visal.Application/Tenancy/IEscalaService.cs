namespace Visal.Application.Tenancy;

public sealed record EscalaItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid FormDefinitionId,
    string FormatoCodigo,
    string FormatoNombre,
    string Estado,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string? EspecialistaNombre);

public sealed record EscalaDetailDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid FormDefinitionId,
    string FormatoCodigo,
    string FormatoNombre,
    string? FormatoVersion,
    string SchemaJson,
    string? PrefillRoutesJson,
    string ValoresJson,
    string Estado,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string? EspecialistaNombre);

public sealed record EscalaFormatoDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo);

public sealed record IniciarEscalaRequest(
    Guid HistoriaClinicaId,
    Guid FormDefinitionId,
    string ValoresJson,
    string? EspecialistaNombre);

public interface IEscalaService
{
    /// <summary>
    /// Formatos de escala disponibles para una HC dada. Se resuelven desde
    /// <c>relaciones_formulario</c> donde origen = FormDefinitionId de la HC y
    /// TipoRelacion = "ESCALA". Solo formatos destino activos y relaciones activas.
    /// </summary>
    Task<IReadOnlyList<EscalaFormatoDto>> ListarFormatosDisponiblesAsync(
        Guid historiaId, CancellationToken ct = default);

    /// <summary>Escalas iniciadas para una HC, ordenadas cronologicamente.</summary>
    Task<IReadOnlyList<EscalaItemDto>> ListarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);

    /// <summary>Inicia una escala atada a la HC. La HC debe existir y no estar inactiva.</summary>
    Task<EscalaDetailDto> IniciarAsync(IniciarEscalaRequest req, Guid actor, CancellationToken ct = default);

    Task<EscalaDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sobrescribe los valores diligenciados (no cambia el estado).</summary>
    Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default);

    /// <summary>Marca la escala como cerrada.</summary>
    Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}
