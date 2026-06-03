namespace Visal.Application.Tenancy;

public sealed record RelacionFormularioDto(
    Guid Id,
    Guid OrigenId,
    string OrigenCodigo,
    string OrigenNombre,
    string? OrigenTipo,
    Guid DestinoId,
    string DestinoCodigo,
    string DestinoNombre,
    string? DestinoTipo,
    bool Activo,
    string? Observacion);

public sealed record SaveRelacionFormularioRequest(
    Guid? Id,
    Guid OrigenId,
    Guid DestinoId,
    bool Activo,
    string? Observacion);

public sealed record OpcionFormularioDto(Guid Id, string Codigo, string Nombre, string? Tipo, bool Activo);

public interface IRelacionFormularioService
{
    Task<IReadOnlyList<RelacionFormularioDto>> ListarAsync(CancellationToken ct = default);

    /// <summary>Catalogo plano de formularios activos del tenant para popular los dropdowns origen/destino.</summary>
    Task<IReadOnlyList<OpcionFormularioDto>> ListarOpcionesAsync(CancellationToken ct = default);

    Task<RelacionFormularioDto> GuardarAsync(SaveRelacionFormularioRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<bool> SetActivoAsync(Guid id, bool activo, Guid actor, CancellationToken ct = default);
}
