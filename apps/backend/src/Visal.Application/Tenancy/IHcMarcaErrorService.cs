using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>Una marca de error (triage de calidad) sobre una asignacion.</summary>
public sealed record HcMarcaErrorDto(
    Guid Id,
    Guid AsignacionId,
    string CodigoAsignacion,
    string? PacienteNombre,
    string? PacienteDoc,
    string Observacion,
    HcMarcaErrorEstado Estado,
    string? MarcadoPorNombre,
    DateTimeOffset MarcadoEn,
    string? ReparadoPorNombre,
    DateTimeOffset? ReparadoEn,
    string? ObservacionReparacion,
    HcMarcaErrorOrigen Origen = HcMarcaErrorOrigen.Manual,
    Guid? HistoriaClinicaId = null);

public interface IHcMarcaErrorService
{
    /// <summary>Marca una asignacion con error. Si ya hay una marca Pendiente para
    /// esa asignacion, actualiza su observacion (no duplica). Devuelve el Id.</summary>
    Task<Guid> MarcarAsync(
        Guid asignacionId,
        string codigoAsignacion,
        string? pacienteNombre,
        string? pacienteDoc,
        string observacion,
        string? marcadoPorNombre,
        CancellationToken ct = default);

    /// <summary>Lista las marcas del tenant, opcionalmente filtradas por estado y/o
    /// origen (manual vs auto-reparacion), mas recientes primero.</summary>
    Task<IReadOnlyList<HcMarcaErrorDto>> ListarAsync(
        HcMarcaErrorEstado? estado,
        HcMarcaErrorOrigen? origen = null,
        CancellationToken ct = default);

    /// <summary>Cuenta las marcas Pendientes MANUALES (para el badge del tab). Las
    /// auto-reparaciones NO inflan el badge; se ven con el filtro de origen.</summary>
    Task<int> ContarPendientesAsync(CancellationToken ct = default);

    /// <summary>Pista de auditoria: registra que una reparacion de datos TOCO esta HC.
    /// Crea (o amplia) una marca Pendiente con Origen=AutoReparacion, idempotente por
    /// (HistoriaClinicaId, Origen). NUNCA marca Reparado. Debe llamarse dentro de la
    /// misma transaccion del arreglo. Devuelve el Id de la marca.</summary>
    Task<Guid> RegistrarTocadoAsync(
        Guid historiaClinicaId,
        string motivo,
        string? detalle = null,
        CancellationToken ct = default);

    /// <summary>Marca una marca como Reparado. Devuelve false si no existe o ya estaba.</summary>
    Task<bool> RepararAsync(
        Guid id,
        string? observacionReparacion,
        string? reparadoPorNombre,
        CancellationToken ct = default);
}
