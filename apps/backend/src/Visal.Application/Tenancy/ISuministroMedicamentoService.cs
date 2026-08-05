namespace Visal.Application.Tenancy;

/// <summary>Fila del registro de medicamentos suministrados al paciente.</summary>
public sealed record SuministroMedicamentoItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    DateTimeOffset FechaHora,
    string Presentacion,
    string? Dosis,
    string? Cantidad,
    string? Via,
    Guid? UsuarioCreacionId,
    string? UsuarioCreacionNombre,
    int Orden);

public sealed record AgregarSuministroMedicamentoRequest(
    DateTimeOffset FechaHora,
    string Presentacion,
    string? Dosis,
    string? Cantidad,
    string? Via);

public sealed record ActualizarSuministroMedicamentoRequest(
    DateTimeOffset FechaHora,
    string Presentacion,
    string? Dosis,
    string? Cantidad,
    string? Via);

public interface ISuministroMedicamentoService
{
    Task<IReadOnlyList<SuministroMedicamentoItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<SuministroMedicamentoItemDto> AgregarAsync(
        Guid historiaId, AgregarSuministroMedicamentoRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(
        Guid itemId, ActualizarSuministroMedicamentoRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}
