namespace Visal.Application.Tenancy;

/// <summary>Sugerencia del autocompletado al buscar en el catalogo de medicamentos.</summary>
public sealed record MedicamentoSugerenciaDto(
    Guid Id,
    string Producto,
    string? PrincipioActivo,
    string? Concentracion,
    string? FormaFarmaceutica,
    string? RegistroSanitario,
    string? Ium);

/// <summary>Fila de la orden de medicamentos de una HC.</summary>
public sealed record OrdenMedicamentoItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid? MedicamentoId,
    string? CodigoMedicamento,
    string NombreMedicamento,
    string? Cantidad,
    string? Frecuencia,
    string? Dias,
    string? Posologia,
    string? Observacion,
    int Orden,
    string? MipresUrl);

public sealed record AgregarMedicamentoRequest(
    Guid? MedicamentoId,
    string? CodigoMedicamento,
    string NombreMedicamento,
    string? Cantidad,
    string? Frecuencia,
    string? Dias,
    string? Posologia,
    string? Observacion,
    string? MipresUrl = null);

public sealed record ActualizarMedicamentoRequest(
    string? Cantidad,
    string? Frecuencia,
    string? Dias,
    string? Posologia,
    string? Observacion,
    string? MipresUrl = null);

public interface IOrdenMedicamentoService
{
    /// <summary>
    /// Busqueda case-insensitive contra producto + principio activo + IUM +
    /// registro sanitario para alimentar el autocompletado del input.
    /// </summary>
    Task<IReadOnlyList<MedicamentoSugerenciaDto>> BuscarSugerenciasAsync(
        string termino, int take = 12, CancellationToken ct = default);

    /// <summary>Items actuales de la orden de la historia (ordenados por Orden).</summary>
    Task<IReadOnlyList<OrdenMedicamentoItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<OrdenMedicamentoItemDto> AgregarAsync(
        Guid historiaId, AgregarMedicamentoRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(
        Guid itemId, ActualizarMedicamentoRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    /// <summary>Conteo rapido para el badge en la pestana del modulo.</summary>
    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}
