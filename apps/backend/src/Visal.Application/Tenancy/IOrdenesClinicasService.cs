using Visal.Domain.Entities;

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
    int ConsentimientosCount = 0,
    // ── Capa 08: Revision Clinica (Ola 2) ────────────────────────────────
    /// <summary>Id de la revision viva cuando existe; null si la HC nunca entro al ciclo.</summary>
    Guid? RevisionId = null,
    /// <summary>Estado agregado de la revision. Null cuando no hay revision (HC abierta o cerrada sin solicitud).</summary>
    RevisionEstadoAgregado? RevisionEstado = null,
    /// <summary>Ultimo veredicto emitido por el agente IA. Null si el agente aun no corrio.</summary>
    RevisionResultado? RevisionAgente = null,
    /// <summary>Iteracion actual del ciclo. 1 al inicio; aumenta por cada reenvio tras rechazo.</summary>
    int? RevisionIteracion = null,
    /// <summary>Ultima nota o motivo del agente — resumen para el tooltip del chip pre-revision.</summary>
    string? RevisionAgenteResumen = null,
    /// <summary>Aseguradora (EPS) del contrato principal del paciente bajo el cual se
    /// ejecuto la atencion. Resuelta via Paciente.Contrato1Id -> Contrato.AseguradoraId.
    /// Null cuando el paciente no tiene Contrato1 configurado o esta huerfano.</summary>
    string? AseguradoraNombre = null,
    Guid? AseguradoraId = null);

public sealed record OrdenesClinicasFiltro(
    string? PacienteTexto = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    string? Especialista = null,
    // Por defecto ahora traemos todas (abiertas + cerradas). El listado marca
    // el estado con un tag y la UI bloquea Consultar/Imprimir para las abiertas.
    bool SoloCerradas = false,
    /// <summary>Filtra HCs cuya EPS (via Contrato1 del paciente) sea esta aseguradora.</summary>
    Guid? AseguradoraId = null);

public sealed record AseguradoraOpcionDto(Guid Id, string Nombre);

public interface IOrdenesClinicasService
{
    Task<IReadOnlyList<OrdenClinicaItemDto>> BuscarAsync(
        OrdenesClinicasFiltro filtro, CancellationToken ct = default);

    /// <summary>Lista de nombres distintos de especialistas para popular el dropdown.</summary>
    Task<IReadOnlyList<string>> ListarEspecialistasAsync(CancellationToken ct = default);

    /// <summary>Lista de aseguradoras que aparecen realmente en /ordenes (via Contrato1
    /// de los pacientes que tienen HCs). Ordenadas por nombre. Sirve para el dropdown
    /// de filtro EPS.</summary>
    Task<IReadOnlyList<AseguradoraOpcionDto>> ListarAseguradorasAsync(CancellationToken ct = default);
}
