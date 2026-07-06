namespace Visal.Application.Tenancy;

/// <summary>Item del listado de "Ordenes Clinicas" — una HC con su paciente y conteos de ordenes.</summary>
public sealed record OrdenClinicaItemDto(
    Guid HistoriaClinicaId,
    Guid PacienteId,
    string PacienteNombre,
    string PacienteTipoDoc,
    string PacienteDoc,
    string Estado,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string FormatoNombre,
    string? EspecialistaNombre,
    int MedicamentosCount,
    int ServiciosCount,
    int RemisionesCount,
    int IncapacidadesCount,
    int CertificacionesCount,
    int InsumosCount = 0,
    int RxImagCount = 0,
    int LabExtCount = 0,
    int InsExtCount = 0,
    int EscalasCount = 0,
    int EvolucionesCount = 0,
    int ConsentimientosCount = 0);

public sealed record OrdenesClinicasFiltro(
    string? PacienteTexto = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    string? Especialista = null,
    // Por defecto ahora traemos todas (abiertas + cerradas). El listado marca
    // el estado con un tag y la UI bloquea Consultar/Imprimir para las abiertas.
    bool SoloCerradas = false);

public interface IOrdenesClinicasService
{
    Task<IReadOnlyList<OrdenClinicaItemDto>> BuscarAsync(
        OrdenesClinicasFiltro filtro, CancellationToken ct = default);

    /// <summary>Lista de nombres distintos de especialistas para popular el dropdown.</summary>
    Task<IReadOnlyList<string>> ListarEspecialistasAsync(CancellationToken ct = default);
}
