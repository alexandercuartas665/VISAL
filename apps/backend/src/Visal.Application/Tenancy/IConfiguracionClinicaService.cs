namespace Visal.Application.Tenancy;

/// <summary>
/// Configuracion clinica del tenant (modulo Configuracion de Empresa).
/// Vive como pares clave/valor en TenantConfiguration.
/// </summary>
public interface IConfiguracionClinicaService
{
    /// <summary>
    /// Meses de validez de una historia clinica antes de exigir una nueva.
    /// Default 3 si no esta configurado. Usado por el modulo del profesional
    /// para validar antes de permitir registrar una nueva nota.
    /// </summary>
    Task<int> GetMesesValidezHistoriaClinicaAsync(CancellationToken ct = default);

    Task SetMesesValidezHistoriaClinicaAsync(int meses, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Si el tenant tiene el toggle activo, TurnoProgramacionService rechaza el
    /// guardado de una programacion cuando algun dia suma mas de 24h entre todos
    /// los turnos apilados. Default false = solo warning visual, no bloquea.
    /// </summary>
    Task<bool> GetBloquearOverloadTurnosAsync(CancellationToken ct = default);

    Task SetBloquearOverloadTurnosAsync(bool bloquear, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Etapa del embudo a la que se enruta cada formulario web (webhook /webhooks/formularios).
    /// tipo: "pqrs" o "contacto". Devuelve null si no esta configurado (el webhook cae a "PQRS").
    /// </summary>
    Task<string?> GetEtapaFormularioWebAsync(string tipo, CancellationToken ct = default);

    Task SetEtapaFormularioWebAsync(string tipo, string? etapa, Guid actor, CancellationToken ct = default);

    /// <summary>Nombres de las etapas del embudo del tenant, para poblar los selects de la config.</summary>
    Task<IReadOnlyList<string>> ListEtapasEmbudoAsync(CancellationToken ct = default);
}
