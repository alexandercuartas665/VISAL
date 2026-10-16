using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>Concepto/categoria configurable para los tokens.</summary>
public sealed record AutorizacionConceptoDto(Guid Id, string Nombre, int Orden, bool Activo);

/// <summary>Fila para el listado del modulo /autorizaciones.</summary>
public sealed record AutorizacionTokenDto(
    Guid Id,
    string Codigo,
    string? ConceptoNombre,
    string Observacion,
    string? GeneradoPorNombre,
    DateTimeOffset GeneradoEn,
    DateTimeOffset ExpiresAt,
    AutorizacionTokenEstado Estado,
    bool Vencido,
    DateTimeOffset? UsadoEn,
    string? UsadoPorNombre,
    Guid? HistoriaClinicaId,
    long? HistoriaClinicaConsecutivo,
    Guid? AsignacionId,
    long? AsignacionConsecutivo,
    string? MotivoUso);

/// <summary>Resultado de validar/consumir un token.</summary>
public sealed record UsarTokenResult(bool Ok, string? Mensaje, Guid? TokenId = null);

/// <summary>
/// Tokens de autorizacion de 6 caracteres (un solo uso, 30 min de vigencia) que permiten
/// saltar el control de fecha de atencion. Los genera un usuario de autorizacion en el
/// modulo /autorizaciones; se consumen desde el flujo de Atencion al capturar una fecha
/// fuera del limite del tipo de servicio.
/// </summary>
public interface IAutorizacionTokenService
{
    /// <summary>Lista los tokens del tenant (recientes primero) con su estado y datos de uso.</summary>
    Task<IReadOnlyList<AutorizacionTokenDto>> ListarAsync(int max = 200, CancellationToken ct = default);

    /// <summary>Genera un token nuevo con un concepto (opcional) y una observacion obligatoria.
    /// Devuelve el codigo de 6 chars.</summary>
    Task<string> GenerarAsync(string? conceptoNombre, string observacion, string? generadoPorNombre, Guid actor, CancellationToken ct = default);

    // ── Conceptos configurables ──────────────────────────────────────────────────
    /// <summary>Lista los conceptos del tenant (ordenados). Siembra ejemplos la primera vez.</summary>
    Task<IReadOnlyList<AutorizacionConceptoDto>> ListarConceptosAsync(bool incluirInactivos = false, CancellationToken ct = default);

    /// <summary>Crea (Id null) o actualiza un concepto. Nombre unico por tenant.</summary>
    Task<AutorizacionConceptoDto> GuardarConceptoAsync(Guid? id, string nombre, int orden, bool activo, Guid actor, CancellationToken ct = default);

    /// <summary>Elimina un concepto. Los tokens historicos conservan su nombre (snapshot).</summary>
    Task<bool> EliminarConceptoAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Valida un token (Activo, no vencido, no usado, del tenant) y, si es valido, lo MARCA como
    /// usado registrando HC/asignacion/usuario/motivo. NO hace SaveChanges: la persistencia la hace
    /// el orquestador (HistoriaClinicaService) en la misma transaccion que la HC, para atomicidad.
    /// </summary>
    Task<UsarTokenResult> ValidarYMarcarUsoAsync(
        string codigo, Guid historiaClinicaId, Guid? asignacionId,
        string? usadoPorNombre, Guid actor, string? motivo, CancellationToken ct = default);
}
