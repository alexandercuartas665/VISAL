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
}
