namespace Visal.Application.Tenancy;

public sealed record DocumentoHcItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid FormDefinitionId,
    string Tipo,
    string FormatoCodigo,
    string FormatoNombre,
    string Estado,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string? EspecialistaNombre);

public sealed record DocumentoHcDetailDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid FormDefinitionId,
    string Tipo,
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

public sealed record DocumentoHcFormatoDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    string? Observacion);

public sealed record IniciarDocumentoHcRequest(
    Guid HistoriaClinicaId,
    Guid FormDefinitionId,
    string Tipo,
    string ValoresJson,
    string? EspecialistaNombre);

/// <summary>
/// Servicio comun para documentos secundarios de la HC: EVOLUCIONES y
/// CONSENTIMIENTOS. La eleccion del formato no viene del catalogo libre, sino
/// de la tabla relaciones_formulario filtrada por origen = FormDefinitionId de
/// la HC padre y TipoRelacion = <c>tipo</c>.
/// </summary>
public interface IDocumentoHcService
{
    /// <summary>
    /// Formatos disponibles para una HC y un tipo de documento dados. Se
    /// resuelven desde <c>relaciones_formulario</c> donde origen sea el
    /// FormDefinitionId de la HC y TipoRelacion sea <paramref name="tipo"/>.
    /// Solo se incluyen formatos destino activos y relaciones activas.
    /// </summary>
    Task<IReadOnlyList<DocumentoHcFormatoDto>> ListarFormatosDisponiblesAsync(
        Guid historiaId, string tipo, CancellationToken ct = default);

    /// <summary>
    /// Formatos disponibles por TipoFormulario global (FormDefinition.Tipo), sin
    /// pasar por relaciones_formulario. Usado por la pestana Atenciones, que
    /// lista TODOS los formatos con Tipo=ATENCION del tenant, no solo los
    /// enlazados al formato de la HC.
    /// </summary>
    Task<IReadOnlyList<DocumentoHcFormatoDto>> ListarFormatosPorTipoFormularioAsync(
        string tipoFormulario, CancellationToken ct = default);

    /// <summary>Documentos iniciados para una HC y un tipo, ordenados cronologicamente.</summary>
    Task<IReadOnlyList<DocumentoHcItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, string tipo, CancellationToken ct = default);

    Task<DocumentoHcDetailDto> IniciarAsync(IniciarDocumentoHcRequest req, Guid actor, CancellationToken ct = default);

    Task<DocumentoHcDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default);

    Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, string tipo, CancellationToken ct = default);
}
